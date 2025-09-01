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
public class TerrainBaker : MonoBehaviour
{
    [Header("Bake Settings")]
    public ComputeShader shader;

    public TerrainSettings settings;
    

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TerrainVertex {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 uv;
    }
    
    private const int GENERATED_VERT_STRIDE = sizeof(float) * (3+3+2);
    private const int GENERATED_INDEX_STRIDE = sizeof(int);

    
    

    void Update()
    {

        // if (Keyboard.current.spaceKey.wasPressedThisFrame)
        // {
        RunAsync();
        // }

        // if (generatedMesh != null)
        // {
        //     // Attach the mesh to the game object
        //     GetComponent<MeshFilter>().mesh = generatedMesh;
        // }
    }
    
    

    public bool Run (out Mesh generatedMesh){
        int totalTriangles = settings.heightmapDims.x * settings.heightmapDims.y * 2;
        int totalVertices = totalTriangles * 3;
        int totalIndices = totalTriangles * 3;

        if (totalTriangles == 0)
        {
            generatedMesh = null;
            return false;
        }


        GraphicsBuffer generatedVertBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVertices, GENERATED_VERT_STRIDE);
        GraphicsBuffer generatedIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalIndices, GENERATED_INDEX_STRIDE);

        int idGrassKernel = shader.FindKernel("Main");

        // Set buffers and variables
        shader.SetBuffer(idGrassKernel, "_GeneratedVertices", generatedVertBuffer);
        shader.SetBuffer(idGrassKernel, "_GeneratedIndices", generatedIndexBuffer);

        Vector3 origin = transform.position - new Vector3(settings.scale.x / 2, 0, settings.scale.z / 2);
        Vector3 scale = settings.scale;
        scale.x /= settings.heightmapDims.x;
        scale.z /= settings.heightmapDims.y;

        // Convert the scale and rotation settings into a transformation matrix
        shader.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.Euler(Vector3.zero), scale));
        shader.SetFloat("_HeightMultiplier", settings.heightMultiplier);
        shader.SetInt("_HeightmapWidth", settings.heightmapDims.x);
        shader.SetInt("_HeightmapHeight", settings.heightmapDims.y);
        shader.SetInt("_NumSourceTriangles", totalTriangles);

        // Set noise offset as two separate floats, since SetFloats expects float[]
        float noiseOffsetX = Random.Range(-1000000f, 1000000f);
        float noiseOffsetY = Random.Range(-1000000f, 1000000f);
        print ("noiseOffset: " + noiseOffsetX + ", " + noiseOffsetY);
        shader.SetFloats("_noiseOffset", new float[] { noiseOffsetX, 0, noiseOffsetY});

        // Find the needed dispatch size, so that each triangle will be run over
        shader.GetKernelThreadGroupSizes(idGrassKernel, out uint threadGroupSize, out _, out _);
        int dispatchSize = Mathf.CeilToInt((float)totalTriangles / threadGroupSize);
        shader.Dispatch(idGrassKernel, dispatchSize, 1, 1);

        // Get the data from the compute shader
        // Unity will wait here until the compute shader is finished
        // Don't do this as runtime. Look into AsyncGPUReadback.
        TerrainVertex[] generatedVertices = new TerrainVertex[totalVertices];
        generatedVertBuffer.GetData(generatedVertices);
        int[] generatedIndices = new int[totalIndices];
        generatedIndexBuffer.GetData(generatedIndices);


        generatedMesh = ComposeMesh(generatedVertices, generatedIndices);

        // Release the buffers, freeing up the GPU memory
        generatedVertBuffer.Release();
        generatedIndexBuffer.Release();

        return true; // No error

    }


    public void RunAsync()
    {
        int totalTriangles = settings.heightmapDims.x * settings.heightmapDims.y * 2;
        int totalVertices = totalTriangles * 3;
        int totalIndices  = totalTriangles * 3;

        if (totalTriangles == 0)
            return;

        GraphicsBuffer generatedVertBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalVertices, GENERATED_VERT_STRIDE);
        GraphicsBuffer generatedIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, totalIndices, GENERATED_INDEX_STRIDE);

        int idKernel = shader.FindKernel("Main");

        shader.SetBuffer(idKernel, "_GeneratedVertices", generatedVertBuffer);
        shader.SetBuffer(idKernel, "_GeneratedIndices", generatedIndexBuffer);

        Vector3 origin = transform.position - new Vector3(settings.scale.x / 2, 0, settings.scale.z / 2);
        Vector3 scale = settings.scale;
        scale.x /= settings.heightmapDims.x;
        scale.z /= settings.heightmapDims.y;

        shader.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.Euler(Vector3.zero), scale));
        shader.SetFloat("_HeightMultiplier", settings.heightMultiplier);
        shader.SetInt("_HeightmapWidth", settings.heightmapDims.x);
        shader.SetInt("_HeightmapHeight", settings.heightmapDims.y);
        shader.SetInt("_NumSourceTriangles", totalTriangles);

        float noiseOffsetX = Random.Range(-1000000f, 1000000f);
        float noiseOffsetY = Random.Range(-1000000f, 1000000f);
        print ("noiseOffset: " + noiseOffsetX + ", " + noiseOffsetY);
        shader.SetFloats("_noiseOffset", new float[] { noiseOffsetX, 0, noiseOffsetY});

        shader.GetKernelThreadGroupSizes(idKernel, out uint threadGroupSize, out _, out _);
        int dispatchSize = Mathf.CeilToInt((float)totalTriangles / threadGroupSize);
        
        float startTime = Time.realtimeSinceStartup;
        shader.Dispatch(idKernel, dispatchSize, 1, 1);

        // Now request async readbacks
        AsyncGPUReadback.Request(generatedVertBuffer, (req) =>
        {
            if (req.hasError) { Debug.LogError("Vertex readback failed"); return; }
            var verts = req.GetData<TerrainVertex>().ToArray();

            AsyncGPUReadback.Request(generatedIndexBuffer, (req2) =>
            {
                if (req2.hasError) { Debug.LogError("Index readback failed"); return; }
                var indices = req2.GetData<int>().ToArray();

                // Compose mesh on CPU once data arrives
                Mesh mesh = ComposeMesh(verts, indices);
                GetComponent<MeshFilter>().mesh = mesh;

                float gpuTime = Time.realtimeSinceStartup - startTime;
                Debug.Log("Time for GPU stuff: " + (gpuTime * 1000f).ToString("F2") + "ms");

                // Release buffers here after we’re done
                generatedVertBuffer.Release();
                generatedIndexBuffer.Release();
            });
        });
    }

    private static Mesh ComposeMesh(TerrainVertex[] verts, int[] indices){
        Mesh mesh = new Mesh();
        mesh.indexFormat = IndexFormat.UInt32;
        Vector3[] vertices = new Vector3[verts.Length];
        Vector3[] normals = new Vector3[verts.Length];
        Vector2[] uvs = new Vector2[verts.Length];
        for(int i = 0; i < verts.Length; i++){
            var v = verts[i];
            vertices[i] = v.position;
            normals[i] = v.normal;
            uvs[i] = v.uv;
        }
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0, true);
        mesh.Optimize();
        return mesh;
    }

}
