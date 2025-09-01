using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;

public class PyramidBakerRuntime : MonoBehaviour
{
    [Header("Bake Settings")]
    public PyramidBakeSettingsRuntime settings;
    public ComputeShader computeShader;
    
    [Header("Runtime Settings")]
    public bool autoBakeOnStart = false;

    public float pyramidHeight = 10.0f;
    private float lastPyramidHeight = -1f;

    
    private bool isBaking = false;
    
    // Buffers
    private GraphicsBuffer sourceVertBuffer;
    private GraphicsBuffer sourceIndexBuffer;
    private GraphicsBuffer generatedVertBuffer;
    private GraphicsBuffer generatedIndexBuffer;
    
    // Data arrays
    private SourceVertex[] sourceVertices;
    private int[] sourceIndices;
    
    // Mesh for direct GPU buffer usage
    private Mesh generatedMesh;
    
    // The structure to send the compute shader
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SourceVertex {
        public Vector3 position;
        public Vector2 uv;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct GeneratedVertex {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 uv;
    }
    
    private const int SOURCE_VERT_STRIDE = sizeof(float) * (3+2);
    private const int SOURCE_INDEX_STRIDE = sizeof(int);
    private const int GENERATED_VERT_STRIDE = sizeof(float) * (3+3+2);
    private const int GENERATED_INDEX_STRIDE = sizeof(int);

    

    void Update()
    {
        if (!Mathf.Approximately(pyramidHeight, lastPyramidHeight))
        {
            lastPyramidHeight = pyramidHeight;
            StartBake();
        }
    }
    
    
    public void StartBake()
    {
        if (isBaking || settings == null || computeShader == null)
        {
            Debug.LogWarning("Cannot start bake: already baking or missing settings/compute shader");
            return;
        }
        
        StartCoroutine(BakeCoroutine());
    }
    
    private IEnumerator BakeCoroutine()
    {
        isBaking = true;
        
        // Step 1: Decompose the source mesh
        DecomposeMesh(settings.sourceMesh, settings.sourceSubMeshIndex, out sourceVertices, out sourceIndices);
        
        int numSourceTriangles = sourceIndices.Length / 3;
        int numGeneratedVertices = numSourceTriangles * 3 * 3;
        int numGeneratedIndices = numGeneratedVertices;
        
        // Step 2: Create and setup buffers
        SetupBuffers(numGeneratedVertices, numGeneratedIndices);
        
        // Step 3: Dispatch compute shader
        DispatchComputeShader(numSourceTriangles);
        
        // Step 4: Create mesh with direct GPU buffers
        CreateMeshWithGPUBuffers(numGeneratedVertices, numGeneratedIndices);
        
        // Step 5: Cleanup
        CleanupBuffers();
        
        isBaking = false;
        
        // Notify completion
        OnBakeComplete();
        
        yield return null;
    }
    
    private void SetupBuffers(int numGeneratedVertices, int numGeneratedIndices)
    {
        sourceVertBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sourceVertices.Length, SOURCE_VERT_STRIDE);
        sourceIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sourceIndices.Length, SOURCE_INDEX_STRIDE);
        generatedVertBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numGeneratedVertices, GENERATED_VERT_STRIDE);
        generatedIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, numGeneratedIndices, GENERATED_INDEX_STRIDE);
        
        // Set data in the buffers
        sourceVertBuffer.SetData(sourceVertices);
        sourceIndexBuffer.SetData(sourceIndices);
    }
    
    private void DispatchComputeShader(int numSourceTriangles)
    {
        int idGrassKernel = computeShader.FindKernel("Main");
        
        // Set buffers and variables
        computeShader.SetBuffer(idGrassKernel, "_SourceVertices", sourceVertBuffer);
        computeShader.SetBuffer(idGrassKernel, "_SourceIndices", sourceIndexBuffer);
        computeShader.SetBuffer(idGrassKernel, "_GeneratedVertices", generatedVertBuffer);
        computeShader.SetBuffer(idGrassKernel, "_GeneratedIndices", generatedIndexBuffer);
        
        // Convert the scale and rotation settings into a transformation matrix
        computeShader.SetMatrix("_Transform", Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(settings.rotation), settings.scale));
        computeShader.SetFloat("_PyramidHeight", pyramidHeight);
        computeShader.SetInt("_NumSourceTriangles", numSourceTriangles);
        
        // Find the needed dispatch size
        computeShader.GetKernelThreadGroupSizes(idGrassKernel, out uint threadGroupSize, out _, out _);
        int dispatchSize = Mathf.CeilToInt((float)numSourceTriangles / threadGroupSize);
        computeShader.Dispatch(idGrassKernel, dispatchSize, 1, 1);
    }
    
    private void CreateMeshWithGPUBuffers(int numGeneratedVertices, int numGeneratedIndices)
    {
        // Read data back from GPU into CPU arrays
        GeneratedVertex[] cpuVerts = new GeneratedVertex[numGeneratedVertices];
        int[] cpuIndices = new int[numGeneratedIndices];

        generatedVertBuffer.GetData(cpuVerts, 0, 0, numGeneratedVertices);
        generatedIndexBuffer.GetData(cpuIndices, 0, 0, numGeneratedIndices);

        // Build mesh on CPU
        generatedMesh = new Mesh();
        Vector3[] positions = new Vector3[numGeneratedVertices];
        Vector3[] normals = new Vector3[numGeneratedVertices];
        Vector2[] uvs = new Vector2[numGeneratedVertices];

        for (int i = 0; i < numGeneratedVertices; i++)
        {
            positions[i] = cpuVerts[i].position;
            normals[i]   = cpuVerts[i].normal;
            uvs[i]       = cpuVerts[i].uv;
        }

        generatedMesh.vertices  = positions;
        generatedMesh.normals   = normals;
        generatedMesh.uv        = uvs;
        generatedMesh.SetIndices(cpuIndices, MeshTopology.Triangles, 0, true);

        // Safety: recalc bounds
        generatedMesh.RecalculateBounds();
    }




    
    private void CleanupBuffers()
    {
        if (sourceVertBuffer != null) sourceVertBuffer.Release();
        if (sourceIndexBuffer != null) sourceIndexBuffer.Release();
        if (generatedVertBuffer != null) generatedVertBuffer.Release();
        if (generatedIndexBuffer != null) generatedIndexBuffer.Release();
        
        sourceVertBuffer = null;
        sourceIndexBuffer = null;
        generatedVertBuffer = null;
        generatedIndexBuffer = null;
    }
    
    private void OnBakeComplete()
    {
        Debug.Log("Pyramid bake completed! Generated mesh has " + generatedMesh.vertexCount + " vertices");
        
        // You can now use the generatedMesh
        // For example, assign it to a MeshFilter:
        var meshFilter = GetComponent<MeshFilter>();
        if (meshFilter != null)
        {
            meshFilter.mesh = generatedMesh;
        }
    }
    
    // Public getter for the generated mesh
    public Mesh GetGeneratedMesh()
    {
        return generatedMesh;
    }
    
    // Check if currently baking
    public bool IsBaking()
    {
        return isBaking;
    }
    
    // Manual cleanup
    void OnDestroy()
    {
        CleanupBuffers();
    }
    
    // Editor helper method
    private void DecomposeMesh(Mesh mesh, int subMeshIndex, out SourceVertex[] verts, out int[] indices)
    {
        var subMesh = mesh.GetSubMesh(subMeshIndex);

        Vector3[] allVertices = mesh.vertices;
        Vector2[] allUVs = mesh.uv;
        int[] allIndices = mesh.triangles;

        verts = new SourceVertex[subMesh.vertexCount];
        indices = new int[subMesh.indexCount];

        for(int i = 0; i < subMesh.vertexCount; i++){
            // Find the index in the whole mesh index buffer
            int wholeMeshIndex = i + subMesh.firstVertex;
            verts[i] = new SourceVertex{
                position = allVertices[wholeMeshIndex],
                uv = allUVs[wholeMeshIndex]
            };
        }

        for(int i = 0; i < subMesh.indexCount; i++){
            // Find the index in the whole mesh index buffer
            indices[i] = allIndices[i+subMesh.indexStart] + subMesh.baseVertex - subMesh.firstVertex;
        }
    }
    
}
