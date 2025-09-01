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
    

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MCVertex {
        public Vector3 position;

    }
    
    private const int VERT_STRIDE = sizeof(float) * (3);

    
    

    void Update()
    {

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
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
        var vertBuffer = new ComputeBuffer(maxVertices, VERT_STRIDE, ComputeBufferType.Append);
        vertBuffer.SetCounterValue(0);

        int idMCKernel = shader.FindKernel("Main");

        // Set buffers and variables
        shader.SetBuffer(idMCKernel, "_GeneratedVertices", vertBuffer);
    
        Vector3 origin = transform.position - new Vector3(settings.scale.x / 2, 0, settings.scale.z / 2);
        Vector3 scale = settings.scale;
        scale.x /= settings.chunkDims.x;
        scale.y /= settings.chunkDims.y;
        scale.z /= settings.chunkDims.z;

        // Convert the scale and rotation settings into a transformation matrix
        shader.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.Euler(Vector3.zero), scale));
        shader.SetFloat("_IsoLevel", settings.isoLevel);
        shader.SetInts("_ChunkDims", settings.chunkDims.x, settings.chunkDims.y, settings.chunkDims.z);


        shader.GetKernelThreadGroupSizes(idMCKernel, out uint tgX, out uint tgY, out uint tgZ);

        // Debug.Log($"ThreadGroupSizes: {tgX},{tgY},{tgZ}");
        int dispatchSizeX = Mathf.CeilToInt((float)settings.chunkDims.x / tgX);
        int dispatchSizeY = Mathf.CeilToInt((float)settings.chunkDims.y / tgY);
        int dispatchSizeZ = Mathf.CeilToInt((float)settings.chunkDims.z / tgZ);
        shader.Dispatch(idMCKernel, dispatchSizeX, dispatchSizeY, dispatchSizeZ);

        // Read back only the appended count
        var countBuf = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
        ComputeBuffer.CopyCount(vertBuffer, countBuf, 0);
        uint[] countArr = { 0 };
        countBuf.GetData(countArr);
        int vertCount = (int)countArr[0];

        var verts = new MCVertex[vertCount];
        if (vertCount > 0) vertBuffer.GetData(verts, 0, 0, vertCount);

        // print the first 12 vertices
        for (int i = 0; i < 12; i++) {
            Debug.Log($"Vertex {i}: {verts[i].position}");
        }

        generatedMesh = ComposeMesh(verts);

        countBuf.Release();
        vertBuffer.Release();
        return true;

    }


    private static Mesh ComposeMesh(MCVertex[] verts) {
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        int n = verts.Length;
        if (n == 0) return mesh;

        var v = new Vector3[n];
        for (int i = 0; i < n; i++) { 
            v[i] = verts[i].position; 
        }

        var indices = new int[n];
        for (int i = 0; i < n; i++) indices[i] = i;

        mesh.SetVertices(v);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0, true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }


}
