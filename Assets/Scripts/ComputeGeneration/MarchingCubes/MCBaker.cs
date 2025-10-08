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
    // public Vector3Int chunkCount;

    // public bool runConstant = true;
    

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MCTriangle {
        public Vector3 position0;
        public Vector3 position1;
        public Vector3 position2;

    }

    private struct Candidate {
        public Vector3 pos;
        public Vector3 n;
    }
    
    private const int TRIANGLE_STRIDE = sizeof(float) * (3*3);

    // private const int CANDIDATE_STRIDE = sizeof(float) * (3*2+1);
    private const int CANDIDATE_STRIDE = sizeof(float) * (3*2);

    
    

    public bool Run(out Mesh generatedMesh, Vector3 position)
    {
        generatedMesh = null;

        ComputeBuffer triangleBuffer = null;
        ComputeBuffer countBuf = null;
        ComputeBuffer minMax = null;

        ComputeBuffer candidateDownBuffer = null;
        ComputeBuffer candidateSideBuffer = null;
        ComputeBuffer candidateUpBuffer = null;


        try
        {
            int maxCells      = settings.chunkDims.x * settings.chunkDims.y * settings.chunkDims.z;
            int maxTriangles  = maxCells * 5;                // MC worst-case
            if (maxTriangles == 0) return false;

            triangleBuffer = new ComputeBuffer(maxTriangles, TRIANGLE_STRIDE, ComputeBufferType.Append);
            triangleBuffer.SetCounterValue(0);

            candidateDownBuffer = new ComputeBuffer(maxTriangles, CANDIDATE_STRIDE, ComputeBufferType.Append);
            candidateDownBuffer.SetCounterValue(0);

            candidateSideBuffer = new ComputeBuffer(maxTriangles, CANDIDATE_STRIDE, ComputeBufferType.Append);
            candidateSideBuffer.SetCounterValue(0);

            candidateUpBuffer = new ComputeBuffer(maxTriangles, CANDIDATE_STRIDE, ComputeBufferType.Append);
            candidateUpBuffer.SetCounterValue(0);

            int id = shader.FindKernel("Main");
            shader.SetBuffer(id, "_GeneratedTriangles", triangleBuffer);

            Vector3 scale = settings.scale;
            Vector3 dims  = settings.chunkDims;
            Vector3 origin = position - new Vector3(scale.x * dims.x / 2f, scale.y * dims.y / 2f, scale.z * dims.z / 2f);

            // Common
            shader.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.identity, scale));
            shader.SetFloat("_IsoLevel", settings.isoLevel);
            shader.SetInts("_ChunkDims", settings.chunkDims.x, settings.chunkDims.y, settings.chunkDims.z);

            // Worley
            shader.SetFloat("_WorleyNoiseScale", settings.noiseScale);
            shader.SetFloat("_WorleyVerticalScale", settings.verticalScale);
            shader.SetFloat("_WorleyCaveHeightFalloff", settings.caveHeightFalloff);
            shader.SetInt("_WorleySeed", settings.seed == 0 ? Random.Range(0, 1000000) : (int)settings.seed);

            // Displacement
            shader.SetFloat("_DisplacementStrength", settings.displacementStrength);
            shader.SetFloat("_DisplacementScale", settings.displacementScale);
            shader.SetInt("_Octaves", settings.octaves);
            shader.SetFloat("_Lacunarity", settings.lacunarity);
            shader.SetFloat("_Persistence", settings.persistence);

            // Candidates
            shader.SetBuffer(id, "_CandidatesDown", candidateDownBuffer);
            shader.SetBuffer(id, "_CandidatesSide", candidateSideBuffer);
            shader.SetBuffer(id, "_CandidatesUp", candidateUpBuffer);
            shader.SetFloat("_CosUp", settings.cosUp);
            shader.SetFloat("_CosDown", settings.cosDown);
            shader.SetFloat("_CosSide", settings.cosSide);
            shader.SetInt("_ThresholdUINT", (int)(settings.foliageDensity * 4294967296.0));
            shader.SetFloats("_Up", 0f, 1f, 0f);

            minMax = new ComputeBuffer(2, sizeof(uint), ComputeBufferType.Structured);
            minMax.SetData(new uint[] { 0x7f7fffff, 0x00000000 });
            shader.SetBuffer(id, "_MinMax", minMax);

            shader.GetKernelThreadGroupSizes(id, out uint tgX, out uint tgY, out uint tgZ);
            int dx = Mathf.CeilToInt((float)settings.chunkDims.x / tgX);
            int dy = Mathf.CeilToInt((float)settings.chunkDims.y / tgY);
            int dz = Mathf.CeilToInt((float)settings.chunkDims.z / tgZ);
            shader.Dispatch(id, dx, dy, dz);

            // optional: this readback stalls; keep if you need it
            var mm = new uint[2];
            minMax.GetData(mm);

            int downCount = GetAppendBufferCount(candidateDownBuffer);
            int sideCount = GetAppendBufferCount(candidateSideBuffer);
            int upCount = GetAppendBufferCount(candidateUpBuffer);
            int triCount = GetAppendBufferCount(triangleBuffer);

            // Print the first 10 test floats of candidateDownBuffer
            // string debugInfo = $"TriCount: {triCount}, DownCount: {downCount}, SideCount: {sideCount}, UpCount: {upCount}";
            
            // print(debugInfo);

            // Get candidateUpBuffer data
            var upData = new Candidate[upCount];
            if (upCount > 0) candidateUpBuffer.GetData(upData, 0, 0, upCount);

            // Instance grass based on the upData!
            

            var tris = triCount > 0 ? new MCTriangle[triCount] : System.Array.Empty<MCTriangle>();
            if (triCount > 0) triangleBuffer.GetData(tris, 0, 0, triCount);

            generatedMesh = ComposeMesh(tris);
            return true;
        }
        finally
        {
            if (countBuf != null) countBuf.Release();
            if (triangleBuffer != null) triangleBuffer.Release();
            if (minMax != null) minMax.Release();
            if (candidateDownBuffer != null) candidateDownBuffer.Release();
            if (candidateSideBuffer != null) candidateSideBuffer.Release();
            if (candidateUpBuffer != null) candidateUpBuffer.Release();
        }
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


    int GetAppendBufferCount(ComputeBuffer buffer)
    {       
        // temp 1-element raw buffer
        using (var countBuf = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw))
        {
            ComputeBuffer.CopyCount(buffer, countBuf, 0);
            uint[] arr = { 0 };
            countBuf.GetData(arr);
            return (int)arr[0];
        }
    }



}
