using System;
using UnityEngine;
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

    public void GenerateAsync(
        Vector3 position,
        Action<float[,,], ComputeBuffer, object> onComplete,
        object userState = null)
    {
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

        float[] biomeOffsets = { 0.0f, -0.2f, -0.31f };
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
        edtShader.SetInt("_Halo", h);
        edtShader.SetBuffer(kSDF, "EDTIn",  edtInBuf);
        edtShader.SetBuffer(kSDF, "EDTOut", edtOutBuf);
        edtShader.SetBuffer(kSDF, "SDFHalos", sdfBuf);
        edtShader.Dispatch(
            kSDF,
            Mathf.CeilToInt(totalCore / (float)tgSDFX),
            1, 1);

        // 4) async readback
        // inside AsyncGPUReadback.Request(...)
        AsyncGPUReadback.Request(sdfBuf, req =>
        {
            // local helper to free temps once
            void FreeTemps()
            {
                SafeRelease(densityBuf);
                SafeRelease(edtOutBuf);
                SafeRelease(edtInBuf);
                SafeRelease(biomeBuffer);
            }

            try
            {
                if (req.hasError)
                {
                    FreeTemps();
                    SafeRelease(sdfBuf);
                    onComplete?.Invoke(null, null, userState);
                    return;
                }

                // if chunk was destroyed while GPU was working
                if (onComplete == null)
                {
                    FreeTemps();
                    SafeRelease(sdfBuf);
                    return;
                }

                var arr = req.GetData<float>();
                var core = settings.chunkDims;
                var data = new float[core.x, core.y, core.z];

                int sx = core.x, sy = core.y, sz = core.z;
                for (int x = 0; x < sx; x++)
                for (int y = 0; y < sy; y++)
                for (int z = 0; z < sz; z++)
                {
                    int i = z + y * sz + x * sz * sy;
                    data[x, y, z] = arr[i];
                }

                // free temps before callback
                FreeTemps();

                onComplete?.Invoke(data, sdfBuf, userState);
            }
            catch
            {
                FreeTemps();
                SafeRelease(sdfBuf);
                onComplete?.Invoke(null, null, userState);
            }
        });

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
