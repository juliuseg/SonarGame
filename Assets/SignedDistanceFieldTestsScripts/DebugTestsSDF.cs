using UnityEngine;
using UnityEngine.Rendering;

public class DebugTestsSDF : MonoBehaviour
{
    public float[,,] data;
    public ComputeShader densityShader;
    public ComputeShader edtShader;
    public MCSettings settings;

    public float colorMultiplier = 1f;

    ComputeBuffer densityBuffer;
    ComputeBuffer edtBuffer;
    ComputeBuffer edtBufferFlipped;
    ComputeBuffer sdfBuffer;
    void Start()
    {
        Vector3Int dims = settings.chunkDims;
        data = new float[dims.x, dims.y, dims.z];

        int total = dims.x * dims.y * dims.z; // We need to acount for the halos
        int totalWithHalo = (dims.x + 2 * settings.halo) * (dims.y + 2 * settings.halo) * (dims.z + 2 * settings.halo);
        densityBuffer = new ComputeBuffer(totalWithHalo, sizeof(float), ComputeBufferType.Structured);
        edtBuffer     = new ComputeBuffer(totalWithHalo, sizeof(float), ComputeBufferType.Structured);
        edtBufferFlipped = new ComputeBuffer(totalWithHalo, sizeof(float), ComputeBufferType.Structured);
        sdfBuffer = new ComputeBuffer(total, sizeof(float), ComputeBufferType.Structured);

        //Run();
    }

    void Update()
    {
        Vector3 position = transform.position;
        Vector3Int dims = settings.chunkDims; 
        Vector3Int dimsWithHalo = new Vector3Int(dims.x + 2 * settings.halo, dims.y + 2 * settings.halo, dims.z + 2 * settings.halo);

        // ---------- DENSITY PASS ----------
        int id = densityShader.FindKernel("Main");
        densityShader.SetBuffer(id, "GeneratedDensity", densityBuffer);

        Vector3 scale = settings.scale;
        Vector3 origin = position - Vector3.Scale(scale, dimsWithHalo) * 0.5f;

        densityShader.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.identity, scale));
        densityShader.SetFloat("_IsoLevel", settings.isoLevel);
        densityShader.SetInts("_ChunkDims", dimsWithHalo.x, dimsWithHalo.y, dimsWithHalo.z);
        densityShader.SetFloat("_WorleyNoiseScale", settings.noiseScale);
        densityShader.SetFloat("_WorleyVerticalScale", settings.verticalScale);
        densityShader.SetFloat("_WorleyCaveHeightFalloff", settings.caveHeightFalloff);
        densityShader.SetInt("_WorleySeed", settings.seed == 0 ? Random.Range(0, 1000000) : (int)settings.seed);
        densityShader.SetFloat("_DisplacementStrength", settings.displacementStrength);
        densityShader.SetFloat("_DisplacementScale", settings.displacementScale);
        densityShader.SetInt("_Octaves", settings.octaves);
        densityShader.SetFloat("_Lacunarity", settings.lacunarity);
        densityShader.SetFloat("_Persistence", settings.persistence);

        densityShader.GetKernelThreadGroupSizes(id, out uint tgX, out uint tgY, out uint tgZ);
        int dx = Mathf.CeilToInt((float)dimsWithHalo.x / tgX);
        int dy = Mathf.CeilToInt((float)dimsWithHalo.y / tgY);
        int dz = Mathf.CeilToInt((float)dimsWithHalo.z / tgZ);
        densityShader.Dispatch(id, dx, dy, dz);


        // EDT Shader
        edtShader.SetInts("_ChunkDims", dimsWithHalo.x, dimsWithHalo.y, dimsWithHalo.z);
        edtShader.SetFloat("_MaxDistance", 1e9f);

        // --- Normal EDT ---
        bool flipDensity = false;
        DispatchEDT("EDT_X", new Vector2Int(dimsWithHalo.y, dimsWithHalo.z), true,  true, flipDensity);
        DispatchEDT("EDT_Y", new Vector2Int(dimsWithHalo.x, dimsWithHalo.z), false, false, flipDensity);
        DispatchEDT("EDT_Z", new Vector2Int(dimsWithHalo.x, dimsWithHalo.y), false, false, flipDensity);

        // // copy out the result before overwriting it
        // int kCopy = edtShader.FindKernel("CopyBuffer");
        // edtShader.SetInt("_Total", dimsWithHalo.x * dimsWithHalo.y * dimsWithHalo.z);
        // edtShader.SetBuffer(kCopy, "BufferSrc", edtBuffer);
        // edtShader.SetBuffer(kCopy, "BufferDst", edtBufferFlipped);
        // edtShader.GetKernelThreadGroupSizes(kCopy, out uint tgx, out uint tgy, out uint tgz);
        // edtShader.Dispatch(kCopy, Mathf.CeilToInt(dimsWithHalo.x*dimsWithHalo.y*dimsWithHalo.z/tgx), 1, 1);


        // --- Flipped EDT ---
        flipDensity = true;
        DispatchEDT("EDT_X", new Vector2Int(dimsWithHalo.y, dimsWithHalo.z), true,  true, flipDensity);
        DispatchEDT("EDT_Y", new Vector2Int(dimsWithHalo.x, dimsWithHalo.z), false, false, flipDensity);
        DispatchEDT("EDT_Z", new Vector2Int(dimsWithHalo.x, dimsWithHalo.y), false, false, flipDensity);

        // Now we can create the SDF
        int kSDF = edtShader.FindKernel("ComputeSDFWithHalos");
        edtShader.SetInts("_ChunkDims", dims.x, dims.y, dims.z);
        edtShader.SetInt("_Halo", settings.halo);
        edtShader.SetBuffer(kSDF, "EDTIn", edtBufferFlipped);
        edtShader.SetBuffer(kSDF, "EDTOut", edtBuffer);
        edtShader.SetBuffer(kSDF, "SDFHalos", sdfBuffer);

        int totalCore = dims.x * dims.y * dims.z;
        edtShader.Dispatch(kSDF, Mathf.CeilToInt(totalCore / 64f), 1, 1);



        // ---------- READBACK ----------
        AsyncGPUReadback.Request(sdfBuffer, OnReadbackComplete);
    }

    void DispatchEDT(
        string kernelName,
        Vector2Int dispatchAxes,
        bool binarize,
        bool useExternal,
        bool flipDensity)
    {
        int k = edtShader.FindKernel(kernelName);
        edtShader.SetInt("_BinarizeInput", binarize ? 1 : 0);
        edtShader.SetInt("_UseExternalInput", useExternal ? 1 : 0);
        edtShader.SetInt("_FlipDensity", flipDensity ? 1 : 0);
        
        // choose correct output target
        ComputeBuffer outBuf = flipDensity ? edtBufferFlipped : edtBuffer;

        // choose input
        ComputeBuffer inBuf = useExternal
            ? densityBuffer
            : outBuf;

        edtShader.SetBuffer(k, "EDTInput", inBuf);
        edtShader.SetBuffer(k, "EDTBuffer", outBuf);

        edtShader.GetKernelThreadGroupSizes(k, out uint tgx, out uint tgy, out uint tgz);
        int gx = Mathf.CeilToInt(dispatchAxes.x / (float)tgx);
        int gy = Mathf.CeilToInt(dispatchAxes.y / (float)tgy);
        edtShader.Dispatch(k, gx, gy, 1);
    }


    void OnReadbackComplete(AsyncGPUReadbackRequest req)
    {
        if (req.hasError) return;

        var arr = req.GetData<float>();
        int sx = data.GetLength(0);
        int sy = data.GetLength(1);
        int sz = data.GetLength(2);

        for (int x = 0; x < sx; x++)
        for (int y = 0; y < sy; y++)
        for (int z = 0; z < sz; z++)
        {
            int i = z + y * sz + x * sz * sy;
            float val = arr[i];
            // normalize for visualization
            Vector3Int dims = settings.chunkDims;
            float maxDims = Mathf.Max(dims.x, dims.y, dims.z);
            data[x,y,z] = Mathf.Clamp(val / maxDims, -1f, 1f);

            

            // print ("Val: " + val);
        }

        Debug.Log("EDT Readback complete");
    }

    void OnDrawGizmos()
    {
        if (data == null) return;

        int sx = data.GetLength(0);
        int sy = data.GetLength(1);
        int sz = data.GetLength(2);
        int minX = -sx / 2;
        int minY = -sy / 2;
        int minZ = -sz / 2;

        Vector3 scale = settings.scale;

        for (int x = minX; x < sx + minX; x++)
        for (int y = minY; y < sy + minY; y++)
        for (int z = minZ; z < sz + minZ; z++)
        {
            float d = data[x - minX, y - minY, z - minZ];
            float f = d * colorMultiplier;
            Gizmos.color = new Color(f, -f, 0f, 0.3f * Mathf.Clamp01(Mathf.Abs(f)));
            Vector3 pos = transform.position + new Vector3(x * scale.x, y * scale.y, z * scale.z);
            Gizmos.DrawCube(pos, Vector3.Scale(Vector3.one, settings.scale)*0.9f);
        }
    }

    void OnDestroy()
    {
        densityBuffer?.Release();
        edtBuffer?.Release();
        edtBufferFlipped?.Release();
        sdfBuffer?.Release();
    }

}
