using UnityEngine;

public static class PyramidBaker
{
    // The structure to send the compute shader
    // This layout kind assures that the data is laid out sequentially
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

    private static void DecomposeMesh(Mesh mesh, int subMeshIndex, out SourceVertex[] verts, out int[] indices){
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

    private static Mesh ComposeMesh(GeneratedVertex[] verts, int[] indices){
        Mesh mesh = new Mesh();
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

    public static bool Run (ComputeShader shader, PyramidBakeSettings settings, out Mesh generatedMesh){
        DecomposeMesh(settings.sourceMesh, settings.sourceSubMeshIndex, out var sourceVerticies, out var sourceIndices);

        int numSourceTriangles = sourceIndices.Length / 3;

        GeneratedVertex[] generatedVertices = new GeneratedVertex[numSourceTriangles * 3 * 3];
        int[] generatedIndices = new int[generatedVertices.Length];

        GraphicsBuffer sourceVertBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sourceVerticies.Length, SOURCE_VERT_STRIDE);
        GraphicsBuffer sourceIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sourceIndices.Length, SOURCE_INDEX_STRIDE);
        GraphicsBuffer generatedVertBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, generatedVertices.Length, GENERATED_VERT_STRIDE);
        GraphicsBuffer generatedIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, generatedIndices.Length, GENERATED_INDEX_STRIDE);

        int idGrassKernel = shader.FindKernel("Main");

        // Set buffers and variables
        shader.SetBuffer(idGrassKernel, "_SourceVertices", sourceVertBuffer);
        shader.SetBuffer(idGrassKernel, "_SourceIndices", sourceIndexBuffer);
        shader.SetBuffer(idGrassKernel, "_GeneratedVertices", generatedVertBuffer);
        shader.SetBuffer(idGrassKernel, "_GeneratedIndices", generatedIndexBuffer);

        // Convert the scale and rotation settings into a transformation matrix
        shader.SetMatrix("_Transform", Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(settings.rotation), settings.scale));
        shader.SetFloat("_PyramidHeight", settings.pyramidHeight);
        shader.SetInt("_NumSourceTriangles", numSourceTriangles);
        // Set data in the buffers
        sourceVertBuffer.SetData(sourceVerticies);
        sourceIndexBuffer.SetData(sourceIndices);

        // Find the needed dispatch size, so that each triangle will be run over
        shader.GetKernelThreadGroupSizes(idGrassKernel, out uint threadGroupSize, out _, out _);
        int dispatchSize = Mathf.CeilToInt((float)numSourceTriangles / threadGroupSize);
        shader.Dispatch(idGrassKernel, dispatchSize, 1, 1);

        // Get the data from the compute shader
        // Unity will wait here until the compute shader is finished
        // Don't do this as runtime. Look into AsyncGPUReadback.
        generatedVertBuffer.GetData(generatedVertices);
        generatedIndexBuffer.GetData(generatedIndices);


        generatedMesh = ComposeMesh(generatedVertices, generatedIndices);

        // Release the buffers, freeing up the GPU memory
        sourceVertBuffer.Release();
        sourceIndexBuffer.Release();
        generatedVertBuffer.Release();
        generatedIndexBuffer.Release();

        return true; // No error

    }

}
