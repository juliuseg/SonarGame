using System;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

public sealed class SDFGpu : IDisposable
{
    readonly ComputeShader densityShader;
    readonly ComputeShader edtShader;
    readonly MCSettings settings;

    // cached kernel ids + tg sizes
    readonly int kDensity;
    readonly int kEDTX, kEDTY, kEDTZ;
    readonly int kSDF;
    readonly uint tgDX, tgDY, tgDZ;
    readonly uint tgXX, tgXY, tgXZ;
    readonly uint tgYX, tgYY, tgYZ;
    readonly uint tgZX, tgZY, tgZZ;
    readonly uint tgSDFX, tgSDFY, tgSDFZ; // 64,1,1 expected

    private const int TERRAFORM_EDIT_STRIDE = sizeof(float) * 3 + sizeof(float) + sizeof(float);

    public SDFGpu(ComputeShader density, ComputeShader edt, MCSettings cfg)
    {
        densityShader = density;
        edtShader = edt;
        settings = cfg;

        kDensity = densityShader.FindKernel("Main");

        kEDTX = edtShader.FindKernel("EDT_X");
        kEDTY = edtShader.FindKernel("EDT_Y");
        kEDTZ = edtShader.FindKernel("EDT_Z");
        kSDF  = edtShader.FindKernel("ComputeSDFWithHalos");

        densityShader.GetKernelThreadGroupSizes(kDensity, out tgDX, out tgDY, out tgDZ);
        edtShader.GetKernelThreadGroupSizes(kEDTX, out tgXX, out tgXY, out tgXZ);
        edtShader.GetKernelThreadGroupSizes(kEDTY, out tgYX, out tgYY, out tgYZ);
        edtShader.GetKernelThreadGroupSizes(kEDTZ, out tgZX, out tgZY, out tgZZ);
        edtShader.GetKernelThreadGroupSizes(kSDF,   out tgSDFX, out tgSDFY, out tgSDFZ);
    }

    public bool GenerateAsync(
        Vector3 position,
        Action<float[], ComputeBuffer, object> onComplete,
        object userState = null,
        Func<bool> tryBeginReadback = null,
        Action endReadback = null,
        Action onReadbackQueued = null,
        Func<bool> isBuildStillValid = null)
    {
        if (tryBeginReadback != null && !tryBeginReadback())
            return false;

        float startTime = Time.realtimeSinceStartup;        
        // dims
        Vector3Int core = settings.chunkDims;
        int h = settings.halo;
        Vector3Int halo = new Vector3Int(core.x + 2*h, core.y + 2*h, core.z + 2*h);

        int totalCore = core.x * core.y * core.z;
        int totalHalo = halo.x * halo.y * halo.z;

        // per-request buffers
        var densityBuf = new ComputeBuffer(totalHalo, sizeof(float), ComputeBufferType.Structured);
        var edtOutBuf  = new ComputeBuffer(totalHalo, sizeof(float), ComputeBufferType.Structured); // outside
        var edtInBuf   = new ComputeBuffer(totalHalo, sizeof(float), ComputeBufferType.Structured); // inside
        var sdfBuf     = new ComputeBuffer(totalCore, sizeof(float), ComputeBufferType.Structured); // no halos

        // 1) density
        densityShader.SetBuffer(kDensity, "GeneratedDensity", densityBuf);

        Vector3 origin = position - Vector3.Scale(settings.scale, halo) * 0.5f;
        densityShader.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.identity, settings.scale));

        densityShader.SetInts("_ChunkDims", halo.x, halo.y, halo.z);

        densityShader.SetFloat("_IsoLevel", settings.isoLevel);

        densityShader.SetFloat("_WorleyNoiseScale", settings.noiseScale);
        densityShader.SetFloat("_WorleyVerticalScale", settings.verticalScale);
        densityShader.SetFloat("_WorleyCaveHeightFalloff", settings.caveHeightFalloff);
        densityShader.SetInt("_WorleySeed", settings.seed == 0 ? UnityEngine.Random.Range(0, 1_000_000) : (int)settings.seed);

        densityShader.SetFloat("_DisplacementStrength", settings.displacementStrength);
        densityShader.SetFloat("_DisplacementScale", settings.displacementScale);
        densityShader.SetInt("_Octaves", settings.octaves);
        densityShader.SetFloat("_Lacunarity", settings.lacunarity);
        densityShader.SetFloat("_Persistence", settings.persistence);

        densityShader.SetFloat("_BiomeScale", settings.biomeScale);
        densityShader.SetFloat("_BiomeBorder", settings.biomeBorder);
        densityShader.SetFloat("_BiomeDisplacementStrength", settings.biomeDisplacementStrength);
        densityShader.SetFloat("_BiomeDisplacementScale", settings.biomeDisplacementScale);

        // Terraform Edits, just set as nothing
        ComputeBuffer terraformEditBuf = new ComputeBuffer(1, TERRAFORM_EDIT_STRIDE, ComputeBufferType.Structured);
        terraformEditBuf.SetData(new TerraformEdit[] { new TerraformEdit { position = Vector3.zero, strength = 0.0f, radius = 0.0f } });
        densityShader.SetBuffer(kDensity, "_TerraformEdits", terraformEditBuf);
        densityShader.SetInt("_TerraformEditsCount", 0);

        int n = (settings.biomeSettings != null && settings.biomeSettings.Length > 0) ? settings.biomeSettings.Length : 1;
        float[] biomeOffsets = new float[n];
        for (int i = 0; i < n; i++)
            biomeOffsets[i] = (settings.biomeSettings != null && i < settings.biomeSettings.Length) ? settings.biomeSettings[i].densityOffset : 0f;
        ComputeBuffer biomeBuffer = new ComputeBuffer(biomeOffsets.Length, sizeof(float));
        biomeBuffer.SetData(biomeOffsets);
        densityShader.SetBuffer(kDensity, "_BiomeDensityOffsets", biomeBuffer);

        densityShader.Dispatch(
            kDensity,
            Mathf.CeilToInt(halo.x / (float)tgDX),
            Mathf.CeilToInt(halo.y / (float)tgDY),
            Mathf.CeilToInt(halo.z / (float)tgDZ));

        // 2) EDT shared params
        edtShader.SetFloat("_MaxDistance", 1e20f);

        // 2a) outside EDT -> edtOutBuf
        edtShader.SetInts("_ChunkDims", halo.x, halo.y, halo.z);
        RunEDT(densityBuf, edtOutBuf, flip:false, halo);

        // 2b) inside EDT (flip) -> edtInBuf
        RunEDT(densityBuf, edtInBuf, flip:true, halo);

        // 3) SDF combine (trim halos)
        edtShader.SetInts("_ChunkDims", core.x, core.y, core.z);
        edtShader.SetInts("_Halo", h);
        edtShader.SetBuffer(kSDF, "EDTIn",  edtInBuf);
        edtShader.SetBuffer(kSDF, "EDTOut", edtOutBuf);
        edtShader.SetBuffer(kSDF, "SDFHalos", sdfBuf);
        edtShader.Dispatch(
            kSDF,
            Mathf.CeilToInt(totalCore / (float)tgSDFX),
            1, 1);

        void FreeTemps()
        {
            SafeRelease(densityBuf);
            SafeRelease(edtOutBuf);
            SafeRelease(edtInBuf);
            SafeRelease(biomeBuffer);
            SafeRelease(terraformEditBuf);
        }

        AsyncGPUReadback.Request(sdfBuf, req =>
        {
            try
            {
                if (req.hasError)
                {
                    FreeTemps();
                    SafeRelease(sdfBuf);
                    onComplete?.Invoke(null, null, userState);
                    return;
                }

                if (onComplete == null)
                {
                    FreeTemps();
                    SafeRelease(sdfBuf);
                    return;
                }

                if (isBuildStillValid != null && !isBuildStillValid())
                {
                    FreeTemps();
                    SafeRelease(sdfBuf);
                    return;
                }

                float[] data;
                Profiler.BeginSample("Chunk.SDF.Readback.Copy");
                try
                {
                    var arr = req.GetData<float>();
                    data = new float[arr.Length];
                    arr.CopyTo(data);
                }
                finally
                {
                    Profiler.EndSample();
                }

                FreeTemps();
                onComplete?.Invoke(data, sdfBuf, userState);
            }
            catch
            {
                FreeTemps();
                SafeRelease(sdfBuf);
                onComplete?.Invoke(null, null, userState);
            }
            finally
            {
                endReadback?.Invoke();
            }
        });

        onReadbackQueued?.Invoke();
        return true;
    }

    void RunEDT(ComputeBuffer densityBuf, ComputeBuffer outBuf, bool flip, Vector3Int halo)
    {
        // X
        edtShader.SetInt("_BinarizeInput", 1);
        edtShader.SetInt("_UseExternalInput", 1);
        edtShader.SetInt("_FlipDensity", flip ? 1 : 0);
        edtShader.SetBuffer(kEDTX, "EDTInput", densityBuf);
        edtShader.SetBuffer(kEDTX, "EDTBuffer", outBuf);
        edtShader.Dispatch(
            kEDTX,
            Mathf.CeilToInt(halo.y / (float)tgXX),
            Mathf.CeilToInt(halo.z / (float)tgYX),
            1);

        // Y
        edtShader.SetInt("_BinarizeInput", 0);
        edtShader.SetInt("_UseExternalInput", 0);
        edtShader.SetBuffer(kEDTY, "EDTInput", outBuf); // bound but unused
        edtShader.SetBuffer(kEDTY, "EDTBuffer", outBuf);
        edtShader.Dispatch(
            kEDTY,
            Mathf.CeilToInt(halo.x / (float)tgYX),
            Mathf.CeilToInt(halo.z / (float)tgYY),
            1);

        // Z
        edtShader.SetInt("_BinarizeInput", 0);
        edtShader.SetInt("_UseExternalInput", 0);
        edtShader.SetBuffer(kEDTZ, "EDTInput", outBuf); // bound but unused
        edtShader.SetBuffer(kEDTZ, "EDTBuffer", outBuf);
        edtShader.Dispatch(
            kEDTZ,
            Mathf.CeilToInt(halo.x / (float)tgZX),
            Mathf.CeilToInt(halo.y / (float)tgYZ),
            1);
    }

    static void SafeRelease(ComputeBuffer b) { if (b != null) { try { b.Release(); } catch {} } }

    public void Dispose() { /* no persistent buffers; nothing to release */ }
}
