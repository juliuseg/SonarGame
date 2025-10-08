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

    
    

    public void RunAsync(Vector3 position, System.Action<Mesh> onComplete)
    {
        ComputeBuffer triBuf = null, cntBuf = null;
        ComputeBuffer candDown = null, candSide = null, candUp = null, minMax = null;

        try
        {
            int maxCells = settings.chunkDims.x * settings.chunkDims.y * settings.chunkDims.z;
            int maxTriangles = maxCells * 5;
            if (maxTriangles == 0) { onComplete?.Invoke(null); return; }

            triBuf = new ComputeBuffer(maxTriangles, TRIANGLE_STRIDE, ComputeBufferType.Append);
            triBuf.SetCounterValue(0);

            // other buffers if you still need them on GPU
            candDown = new ComputeBuffer(maxTriangles, CANDIDATE_STRIDE, ComputeBufferType.Append); candDown.SetCounterValue(0);
            candSide = new ComputeBuffer(maxTriangles, CANDIDATE_STRIDE, ComputeBufferType.Append); candSide.SetCounterValue(0);
            candUp   = new ComputeBuffer(maxTriangles, CANDIDATE_STRIDE, ComputeBufferType.Append); candUp.SetCounterValue(0);
            minMax   = new ComputeBuffer(2, sizeof(uint), ComputeBufferType.Structured);

            var shaderInstance = Instantiate(shader);
            int id = shaderInstance.FindKernel("Main");

            // Use shaderInstance everywhere from here
            shaderInstance.SetBuffer(id, "_GeneratedTriangles", triBuf);
            shaderInstance.SetBuffer(id, "_CandidatesDown", candDown);
            shaderInstance.SetBuffer(id, "_CandidatesSide", candSide);
            shaderInstance.SetBuffer(id, "_CandidatesUp", candUp);
            shaderInstance.SetBuffer(id, "_MinMax", minMax);

            Vector3 scale = settings.scale;
            Vector3 dims  = settings.chunkDims;
            Vector3 origin = position - new Vector3(scale.x * dims.x / 2f, scale.y * dims.y / 2f, scale.z * dims.z / 2f);

            // Common
            shaderInstance.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.identity, scale));
            shaderInstance.SetFloat("_IsoLevel", settings.isoLevel);
            shaderInstance.SetInts("_ChunkDims", settings.chunkDims.x, settings.chunkDims.y, settings.chunkDims.z);

            // Worley
            shaderInstance.SetFloat("_WorleyNoiseScale", settings.noiseScale);
            shaderInstance.SetFloat("_WorleyVerticalScale", settings.verticalScale);
            shaderInstance.SetFloat("_WorleyCaveHeightFalloff", settings.caveHeightFalloff);
            shaderInstance.SetInt("_WorleySeed", settings.seed == 0 ? Random.Range(0, 1000000) : (int)settings.seed);

            // Displacement
            shaderInstance.SetFloat("_DisplacementStrength", settings.displacementStrength);
            shaderInstance.SetFloat("_DisplacementScale", settings.displacementScale);
            shaderInstance.SetInt("_Octaves", settings.octaves);
            shaderInstance.SetFloat("_Lacunarity", settings.lacunarity);
            shaderInstance.SetFloat("_Persistence", settings.persistence);

            // Candidates
            shaderInstance.SetFloat("_CosUp", settings.cosUp);
            shaderInstance.SetFloat("_CosDown", settings.cosDown);
            shaderInstance.SetFloat("_CosSide", settings.cosSide);
            shaderInstance.SetInt("_ThresholdUINT", (int)(settings.foliageDensity * 4294967296.0));
            shaderInstance.SetFloats("_Up", 0f, 1f, 0f);

            // Dispatch
            shaderInstance.GetKernelThreadGroupSizes(id, out uint tgX, out uint tgY, out uint tgZ);
            int dx = Mathf.CeilToInt((float)settings.chunkDims.x / tgX);
            int dy = Mathf.CeilToInt((float)settings.chunkDims.y / tgY);
            int dz = Mathf.CeilToInt((float)settings.chunkDims.z / tgZ);
            shaderInstance.Dispatch(id, dx, dy, dz);


            // read appended count asynchronously
            cntBuf = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
            ComputeBuffer.CopyCount(triBuf, cntBuf, 0);

            AsyncGPUReadback.Request(cntBuf, reqCount =>
            {
                try
                {
                    if (reqCount.hasError) { onComplete?.Invoke(null); return; }
                    uint triCount = reqCount.GetData<uint>()[0];
                    if (triCount == 0) { onComplete?.Invoke(null); return; }

                    int bytes = (int)triCount * TRIANGLE_STRIDE;

                    // now read exactly 'triCount' triangles
                    AsyncGPUReadback.Request(triBuf, bytes, 0, reqTris =>
                    {
                        try
                        {
                            if (reqTris.hasError) { onComplete?.Invoke(null); return; }
                            var tris = reqTris.GetData<MCTriangle>();
                            var mesh = ComposeMesh(tris.ToArray());   // length == triCount
                            onComplete?.Invoke(mesh);
                        }
                        finally
                        {
                            triBuf?.Release();
                            candDown?.Release(); candSide?.Release(); candUp?.Release();
                            minMax?.Release(); cntBuf?.Release();
                        }
                    });
                }
                catch
                {
                    triBuf?.Release();
                    candDown?.Release(); candSide?.Release(); candUp?.Release();
                    minMax?.Release(); cntBuf?.Release();
                    onComplete?.Invoke(null);
                }
            });
        }
        catch
        {
            triBuf?.Release();
            candDown?.Release(); candSide?.Release(); candUp?.Release();
            minMax?.Release(); cntBuf?.Release();
            onComplete?.Invoke(null);
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
