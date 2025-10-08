using UnityEngine;
using UnityEngine.Rendering;

public class DebugTestsEDT : MonoBehaviour
{
    public float[,,] data;
    public ComputeShader densityShader;
    public ComputeShader edtShader;
    public MCSettings settings;

    public float colorMultiplier = 1f;

    ComputeBuffer densityBuffer;
    ComputeBuffer edtBuffer;

    void Start()
    {
        Vector3Int dims = settings.chunkDims;
        data = new float[dims.x, dims.y, dims.z];

        int total = dims.x * dims.y * dims.z;
        densityBuffer = new ComputeBuffer(total, sizeof(float), ComputeBufferType.Structured);
        edtBuffer     = new ComputeBuffer(total, sizeof(float), ComputeBufferType.Structured);

        //Run();
    }

    void Update()
    {
        Vector3 position = transform.position;
        Vector3Int dims = settings.chunkDims;

        // ---------- DENSITY PASS ----------
        int id = densityShader.FindKernel("Main");
        densityShader.SetBuffer(id, "GeneratedDensity", densityBuffer);

        Vector3 scale = settings.scale;
        Vector3 origin = position - Vector3.Scale(scale, dims) * 0.5f;

        densityShader.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.identity, scale));
        densityShader.SetFloat("_IsoLevel", settings.isoLevel);
        densityShader.SetInts("_ChunkDims", dims.x, dims.y, dims.z);
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
        int dx = Mathf.CeilToInt((float)dims.x / tgX);
        int dy = Mathf.CeilToInt((float)dims.y / tgY);
        int dz = Mathf.CeilToInt((float)dims.z / tgZ);
        densityShader.Dispatch(id, dx, dy, dz);


        // EDT Shader
        edtShader.SetInts("_ChunkDims", dims.x, dims.y, dims.z);
        edtShader.SetFloat("_MaxDistance", 1e9f);

        // Dispatch EDT with normal density
        bool flipDensity = false;
        DispatchEDT("EDT_X", dims, new Vector2Int(dims.y, dims.z), true,  true, flipDensity);
        DispatchEDT("EDT_Y", dims, new Vector2Int(dims.x, dims.z), false, false, flipDensity);
        DispatchEDT("EDT_Z", dims, new Vector2Int(dims.x, dims.y), false, false, flipDensity);

        


        // ---------- READBACK ----------
        AsyncGPUReadback.Request(edtBuffer, OnReadbackComplete);
    }

    void DispatchEDT(string kernelName, Vector3Int dims, Vector2Int dispatchAxes, bool binarize, bool useExternal, bool flipDensity)
    {
        int k = edtShader.FindKernel(kernelName);
        edtShader.SetInt("_BinarizeInput", binarize ? 1 : 0);
        edtShader.SetInt("_UseExternalInput", useExternal ? 1 : 0);
        edtShader.SetInt("_FlipDensity", flipDensity ? 1 : 0);

        // always bind both buffers
        edtShader.SetBuffer(k, "EDTInput",  useExternal ? densityBuffer : edtBuffer);
        edtShader.SetBuffer(k, "EDTBuffer", edtBuffer);

        // get group sizes
        edtShader.GetKernelThreadGroupSizes(k, out uint tgx, out uint tgy, out uint tgz);

        // dispatch
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
            float maxSq = (dims.x-1)*(dims.x-1) + (dims.y-1)*(dims.y-1) + (dims.z-1)*(dims.z-1);
            data[x,y,z] = Mathf.Clamp01(val / maxSq);

            

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
            Gizmos.color = new Color(f, 0f, 0f, 0.3f * Mathf.Clamp01(f));
            Vector3 pos = transform.position + new Vector3(x * scale.x, y * scale.y, z * scale.z);
            Gizmos.DrawCube(pos, Vector3.Scale(Vector3.one, settings.scale)*0.9f);
        }
    }

    void OnDestroy()
    {
        densityBuffer?.Release();
        edtBuffer?.Release();
    }
}
