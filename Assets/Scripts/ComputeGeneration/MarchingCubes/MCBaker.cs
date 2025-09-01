// Terrain Baker
// We tell the compute shader the terrain settings
// Triangels of the terrain: heigh*width * 2
// Vertices of the terrain: heigh*width * 2(triangles) * 3(three vertices per triangle)
// Indices of the terrain: heigh*width * 2(triangles) * 3(three indices per triangle)

// We then dispatch a shader to generate the terrain: We dispatch the number of triangles. And make sure its right in respect to the thread group size.



using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter))]
public class MCBaker : MonoBehaviour
{
    [Header("Bake Settings")]
    public ComputeShader shader;

    public MCSettings settings;

    public bool runConstant = true;
    

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MCTriangle {
        public Vector3 position0;
        public Vector3 position1;
        public Vector3 position2;

    }
    
    private const int TRIANGLE_STRIDE = sizeof(float) * (3*3);

    
    

    void Update()
    {

        if (Keyboard.current.spaceKey.wasPressedThisFrame || runConstant)
        {
            Run(out Mesh generatedMesh);

            if (generatedMesh != null)
            {
                // Attach the mesh to the game object
                Debug.Log("Attaching mesh to game object");
                GetComponent<MeshFilter>().mesh = generatedMesh;
            }
        }

        
    }
    
    

    public bool Run (out Mesh generatedMesh){

        int maxTriangles = settings.chunkDims.x * settings.chunkDims.y * settings.chunkDims.z * 5;
        int maxVertices  = maxTriangles * 3;

        if (maxVertices == 0) { generatedMesh = null; return false; }

        // Append buffer + counter
        var triangleBuffer = new ComputeBuffer(maxVertices, TRIANGLE_STRIDE, ComputeBufferType.Append);
        triangleBuffer.SetCounterValue(0);

        int idMCKernel = shader.FindKernel("Main");

        // Set buffers and variables 
        shader.SetBuffer(idMCKernel, "_GeneratedTriangles", triangleBuffer);
    
        Vector3 origin = - new Vector3(
            settings.scale.x*settings.chunkDims.x / 2, 
            settings.scale.y*settings.chunkDims.y / 2, 
            settings.scale.z*settings.chunkDims.z / 2
        );

        Vector3 scale = settings.scale;

        // Convert the scale and rotation settings into a transformation matrix
        shader.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.Euler(Vector3.zero), scale));
        shader.SetFloat("_IsoLevel", settings.isoLevel);
        shader.SetInts("_ChunkDims", settings.chunkDims.x, settings.chunkDims.y, settings.chunkDims.z);
        shader.SetFloats("_NoiseOffset", settings.noiseOffset.x,
                                   settings.noiseOffset.y,
                                   settings.noiseOffset.z);

        shader.SetFloats("_NoiseFrequency", settings.noiseFrequency.x,
                                            settings.noiseFrequency.y,
                                            settings.noiseFrequency.z);

        // Set Worley parameters
        
        shader.SetFloat("_WorleyNoiseScale", settings.noiseScale);
        shader.SetFloat("_WorleyVerticalScale", settings.verticalScale);
        shader.SetFloat("_WorleyCaveHeightFalloff", settings.caveHeightFalloff);

        // If seed is 0, use a random seed
        int seed = settings.seed == 0 ? Random.Range(0, 1000000) : (int)settings.seed;
        shader.SetInt("_WorleySeed", seed);


        var minMax = new ComputeBuffer(2, sizeof(uint), ComputeBufferType.Structured);
        var init = new uint[] { 0x7f7fffff, 0x00000000 }; // +INF, 0
        minMax.SetData(init);
        shader.SetBuffer(idMCKernel, "_MinMax", minMax);




        shader.GetKernelThreadGroupSizes(idMCKernel, out uint tgX, out uint tgY, out uint tgZ);
        int dispatchSizeX = Mathf.CeilToInt((float)settings.chunkDims.x / tgX);
        int dispatchSizeY = Mathf.CeilToInt((float)settings.chunkDims.y / tgY);
        int dispatchSizeZ = Mathf.CeilToInt((float)settings.chunkDims.z / tgZ);
        shader.Dispatch(idMCKernel, dispatchSizeX, dispatchSizeY, dispatchSizeZ);

        // Get the min and max values
        var mm = new uint[2];
        minMax.GetData(mm);
        float fMin = System.BitConverter.ToSingle(System.BitConverter.GetBytes(mm[0]),0);
        float fMax = System.BitConverter.ToSingle(System.BitConverter.GetBytes(mm[1]),0);
        Debug.Log($"Worley F1 range: [{fMin}, {fMax}]");
        minMax.Release();

        // Read back only the appended count
        var countBuf = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
        ComputeBuffer.CopyCount(triangleBuffer, countBuf, 0);
        uint[] countArr = { 0 };
        countBuf.GetData(countArr);
        int triangleCount = (int)countArr[0];

        var triangles = new MCTriangle[triangleCount];
        if (triangleCount > 0) triangleBuffer.GetData(triangles, 0, 0, triangleCount);


        generatedMesh = ComposeMesh(triangles);

        countBuf.Release();
        triangleBuffer.Release();

        


        return true;

    }


    private static Mesh ComposeMesh(MCTriangle[] triangles) {
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        int n = triangles.Length;
        if (n == 0) return mesh;

        var v = new Vector3[n * 3];
        var indices = new int[n * 3];

        for (int i = 0; i < n; i++) {
            int baseIdx = i * 3;
            v[baseIdx]     = triangles[i].position0;
            v[baseIdx + 1] = triangles[i].position1;
            v[baseIdx + 2] = triangles[i].position2;

            indices[baseIdx]     = baseIdx;
            indices[baseIdx + 1] = baseIdx + 1;
            indices[baseIdx + 2] = baseIdx + 2;
        }

        mesh.SetVertices(v);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0, true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }



}
