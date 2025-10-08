using UnityEngine;
using UnityEngine.Rendering;

public class DebugTests : MonoBehaviour
{

    public float[,,] data = new float[16, 16, 16];

    public ComputeShader densityShader;

    public MCSettings settings;

    ComputeBuffer densityBuffer;

    void Start()
    {
        data = new float[settings.chunkDims.x, settings.chunkDims.y, settings.chunkDims.z];
    }

    void Update()
    {
        Vector3 position = transform.position;



        // Start by setting the buffer
        if (densityBuffer == null)
        {
            densityBuffer = new ComputeBuffer(data.Length, sizeof(float), ComputeBufferType.Structured);
        }

        int id = densityShader.FindKernel("Main");

        densityShader.SetBuffer(id, "GeneratedDensity", densityBuffer);

        Vector3 scale = settings.scale;
        Vector3 dims  = settings.chunkDims;
        Vector3 origin = position - new Vector3(scale.x * dims.x / 2f, scale.y * dims.y / 2f, scale.z * dims.z / 2f);


        // Common
        densityShader.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.identity, scale));
        densityShader.SetFloat("_IsoLevel", settings.isoLevel);
        densityShader.SetInts("_ChunkDims", settings.chunkDims.x, settings.chunkDims.y, settings.chunkDims.z);

        // Worley
        densityShader.SetFloat("_WorleyNoiseScale", settings.noiseScale);
        densityShader.SetFloat("_WorleyVerticalScale", settings.verticalScale);
        densityShader.SetFloat("_WorleyCaveHeightFalloff", settings.caveHeightFalloff);
        densityShader.SetInt("_WorleySeed", settings.seed == 0 ? Random.Range(0, 1000000) : (int)settings.seed);

        // Displacement
        densityShader.SetFloat("_DisplacementStrength", settings.displacementStrength);
        densityShader.SetFloat("_DisplacementScale", settings.displacementScale);
        densityShader.SetInt("_Octaves", settings.octaves);
        densityShader.SetFloat("_Lacunarity", settings.lacunarity);
        densityShader.SetFloat("_Persistence", settings.persistence);

        // Dispatch
        densityShader.GetKernelThreadGroupSizes(id, out uint tgX, out uint tgY, out uint tgZ);
        int dx = Mathf.CeilToInt((float)settings.chunkDims.x / tgX);
        int dy = Mathf.CeilToInt((float)settings.chunkDims.y / tgY);
        int dz = Mathf.CeilToInt((float)settings.chunkDims.z / tgZ);

        densityShader.Dispatch(id, dx, dy, dz);

        // Async gettig data

        // Get data
        // densityBuffer.GetData(data);
        // densityBuffer.Release();
        AsyncGPUReadback.Request(densityBuffer, OnReadbackComplete);


        // SetTestData();
    }

    void OnReadbackComplete(AsyncGPUReadbackRequest req)
    {
        if (req.hasError) return;
        var arr = req.GetData<float>();
        int sizeX = data.GetLength(0);
        int sizeY = data.GetLength(1);
        int sizeZ = data.GetLength(2);
        // Copy arr to your 3D array or use directly
        for (int x = 0; x < sizeX; x++)
        for (int y = 0; y < sizeY; y++)
        for (int z = 0; z < sizeZ; z++)
        {
            
            int i = z + y * sizeZ + x * sizeZ * sizeY;
            data[x, y, z] = arr[i];
        }


        Debug.Log("Readback complete");
    }


    private void OnDrawGizmos()
    {
        int sizeX = data.GetLength(0);
        int sizeY = data.GetLength(1);
        int sizeZ = data.GetLength(2);
        int minX = -sizeX / 2;
        int minY = -sizeY / 2;
        int minZ = -sizeZ / 2;
        int maxX = minX + sizeX - 1;
        int maxY = minY + sizeY - 1;
        int maxZ = minZ + sizeZ - 1;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    float d = data[x-minX, y-minY, z-minZ];
                    Gizmos.color = new Color(Mathf.Clamp01(d), 0f, 0f, Mathf.Clamp01(d)*0.5f); // Transparent red
                    Vector3 pos = transform.position + new Vector3(x*settings.scale.x, y*settings.scale.y, z*settings.scale.z);

                    Gizmos.DrawCube(pos, Vector3.Scale(Vector3.one, settings.scale));
                }
            }
        }
    }


    void OnDestroy()
    {
        densityBuffer?.Release();
        densityBuffer = null;
    }
}
