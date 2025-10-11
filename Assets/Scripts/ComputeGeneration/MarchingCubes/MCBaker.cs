using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using Unity.Collections;

[RequireComponent(typeof(MeshFilter))]
public class MCBaker : MonoBehaviour
{
    [Header("Bake Settings")]
    public ComputeShader shader;

    public MCSettings settings;


    

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MCTriangle {
        public Vector3 position0;
        public Vector3 position1;
        public Vector3 position2;
        public Vector3 color;

    }

    
    private const int TRIANGLE_STRIDE = sizeof(float) * (3*3+3);

    private int _kernelMain;
    void Awake() => _kernelMain = shader.FindKernel("Main");
    

    public void RunAsync(Vector3 position, System.Action<Mesh, uint> onComplete)
    {
        ComputeBuffer triBuf = null, cntBuf = null, candDown = null, candSide = null, candUp = null, biomeMask = null, biomeBuffer = null;

        // use the asset directly; no Instantiate (or Destroy it if you insist on instancing)
        var cs = shader;

        void ReleaseAll()
        {
            triBuf?.Release(); triBuf = null;
            cntBuf?.Release(); cntBuf = null;
            candDown?.Release(); candDown = null;
            candSide?.Release(); candSide = null;
            candUp?.Release();   candUp = null;
            biomeMask?.Release(); biomeMask = null;
            biomeBuffer?.Release(); biomeBuffer = null;
        }

        try
        {
            int maxCells = settings.chunkDims.x * settings.chunkDims.y * settings.chunkDims.z;
            int maxTriangles = maxCells * 5;
            if (maxTriangles == 0) { onComplete?.Invoke(null, 0); return; }

            triBuf = new ComputeBuffer(maxTriangles, TRIANGLE_STRIDE, ComputeBufferType.Append);
            triBuf.SetCounterValue(0);



            cs.SetBuffer(_kernelMain, "_GeneratedTriangles", triBuf);

            biomeMask = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
            uint[] zero = { 0 };
            biomeMask.SetData(zero);
            cs.SetBuffer(_kernelMain, "_BiomeMask", biomeMask);

            Vector3 scale = settings.scale;
            Vector3 dims  = settings.chunkDims;
            Vector3 origin = position - new Vector3(scale.x * dims.x / 2f, scale.y * dims.y / 2f, scale.z * dims.z / 2f);

            cs.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.identity, scale));
            cs.SetFloat("_IsoLevel", settings.isoLevel);
            cs.SetInts("_ChunkDims", settings.chunkDims.x, settings.chunkDims.y, settings.chunkDims.z);

            cs.SetFloat("_WorleyNoiseScale", settings.noiseScale);
            cs.SetFloat("_WorleyVerticalScale", settings.verticalScale);
            cs.SetFloat("_WorleyCaveHeightFalloff", settings.caveHeightFalloff);
            cs.SetInt("_WorleySeed", settings.seed == 0 ? Random.Range(0, 1000000) : (int)settings.seed);

            cs.SetFloat("_DisplacementStrength", settings.displacementStrength);
            cs.SetFloat("_DisplacementScale", settings.displacementScale);
            cs.SetInt("_Octaves", settings.octaves);
            cs.SetFloat("_Lacunarity", settings.lacunarity);
            cs.SetFloat("_Persistence", settings.persistence);

            cs.SetFloat("_BiomeScale", settings.biomeScale);
            cs.SetFloat("_BiomeBorder", settings.biomeBorder);
            cs.SetFloat("_BiomeDisplacementStrength", settings.biomeDisplacementStrength);
            cs.SetFloat("_BiomeDisplacementScale", settings.biomeDisplacementScale);

            float[] biomeOffsets = new float[settings.biomeSettings.Length];
            for (int i = 0; i < settings.biomeSettings.Length; i++)
            {
                biomeOffsets[i] = settings.biomeSettings[i].densityOffset;
            }
            
            biomeBuffer = new ComputeBuffer(biomeOffsets.Length, sizeof(float));
            biomeBuffer.SetData(biomeOffsets);
            cs.SetBuffer(_kernelMain, "_BiomeDensityOffsets", biomeBuffer);


            cs.GetKernelThreadGroupSizes(_kernelMain, out uint tgX, out uint tgY, out uint tgZ);
            int dx = Mathf.CeilToInt((float)settings.chunkDims.x / tgX);
            int dy = Mathf.CeilToInt((float)settings.chunkDims.y / tgY);
            int dz = Mathf.CeilToInt((float)settings.chunkDims.z / tgZ);
            cs.Dispatch(_kernelMain, dx, dy, dz);

            cntBuf = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
            ComputeBuffer.CopyCount(triBuf, cntBuf, 0);

            AsyncGPUReadback.Request(cntBuf, reqCount =>
            {
                try
                {
                    if (reqCount.hasError)
                    {
                        ReleaseAll();
                        onComplete?.Invoke(null, 0);
                        return;
                    }

                    uint triCount = reqCount.GetData<uint>()[0];
                    if (triCount == 0)
                    {
                        ReleaseAll();
                        onComplete?.Invoke(null, 0);
                        return;
                    }

                    int bytes = (int)triCount * TRIANGLE_STRIDE;

                    AsyncGPUReadback.Request(triBuf, bytes, 0, reqTris =>
                    {
                        try
                        {
                            if (reqTris.hasError)
                            {
                                ReleaseAll();
                                onComplete?.Invoke(null, 0);
                                return;
                            }

                            var tris = reqTris.GetData<MCTriangle>();
                            var mesh = ComposeMesh(tris);

                            AsyncGPUReadback.Request(biomeMask, reqBiome =>
                            {
                                try
                                {
                                    if (reqBiome.hasError)
                                    {
                                        ReleaseAll();
                                        onComplete?.Invoke(mesh, 0);
                                        return;
                                    }

                                    uint mask = reqBiome.GetData<uint>()[0];
                                    onComplete?.Invoke(mesh, mask);
                                }
                                finally
                                {
                                    ReleaseAll();
                                }
                            });
                        }
                        finally
                        {
                            ReleaseAll();
                        }
                    });
                }
                catch
                {
                    ReleaseAll();
                    onComplete?.Invoke(null, 0);
                }
            });
        }
        catch
        {
            ReleaseAll();
            onComplete?.Invoke(null, 0);
        }
    }

    private Vector3[] _verts;
    private int[] _indices;
    private Color[] _colors;
    

    private Mesh ComposeMesh(NativeArray<MCTriangle> triangles)
    {
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        int n = triangles.Length;
        if (n == 0)
            return mesh;

        int vCount = n * 3;

        // allocate only when capacity is insufficient
        if (_verts == null || _verts.Length < vCount)
        {
            _verts = new Vector3[vCount];
            _indices = new int[vCount];
            _colors = new Color[vCount];
        }

        for (int i = 0; i < n; i++)
        {
            int baseIdx = i * 3;

            _verts[baseIdx]     = triangles[i].position0;
            _verts[baseIdx + 1] = triangles[i].position1;
            _verts[baseIdx + 2] = triangles[i].position2;

            _indices[baseIdx]     = baseIdx;
            _indices[baseIdx + 1] = baseIdx + 1;
            _indices[baseIdx + 2] = baseIdx + 2;

            Color c = new Color(triangles[i].color.x, triangles[i].color.y, triangles[i].color.z);
            _colors[baseIdx] = _colors[baseIdx + 1] = _colors[baseIdx + 2] = c;
        }

        mesh.Clear();
        mesh.SetVertices(_verts, 0, vCount);
        mesh.SetIndices(_indices, 0, vCount, MeshTopology.Triangles, 0, true);
        mesh.SetColors(_colors, 0, vCount);

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
