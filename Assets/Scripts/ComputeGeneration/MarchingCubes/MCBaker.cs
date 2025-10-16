using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;
using Unity.Collections;
using System.Collections.Generic;
using System;

[RequireComponent(typeof(MeshFilter))]
public class MCBaker : MonoBehaviour
{
    [Header("Bake Settings")]
    public ComputeShader terrainGenerationShader;
    public ComputeShader packForReadbackShader;

    public MCSettings settings;


    

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MCTriangle {
        public Vector3 position0;
        public Vector3 position1;
        public Vector3 position2;
        public Vector3 color;

    }

    

    
    private const int TRIANGLE_STRIDE = sizeof(float) * (3*3+3);
    private const int SPAWN_POINT_STRIDE = sizeof(float) * (3*3);
    private const int TERRAFORM_EDIT_STRIDE = sizeof(float) * 3 + sizeof(float) + sizeof(float);

    private int _kernelMain;
    private int _kernelPack;



    void Awake() {

        _kernelMain = terrainGenerationShader.FindKernel("TerrainGeneration");
        _kernelPack = packForReadbackShader.FindKernel("PackForReadback");
    }
    

    public void RunAsync(Vector3 position, List<TerraformEdit> terraformEdits, System.Action<Mesh, uint, List<SpawnPoint>, float> onComplete)
    {
        float startTime = Time.realtimeSinceStartup;

        // Per-job shader instances to prevent shared-state conflicts
        var genShader = Instantiate(terrainGenerationShader);
        var packShader = Instantiate(packForReadbackShader);

        ComputeBuffer headerBuf = null;
        ComputeBuffer triBuf = null;
        ComputeBuffer spawnPointsBuf = null;
        ComputeBuffer biomeBuffer = null;
        ComputeBuffer terraformEditBuf = null;
        ComputeBuffer combinedBuf = null;
        ComputeBuffer triCountBuf = null;

        const int HEADER_BYTES = 16;

        void ReleaseAll()
        {
            headerBuf?.Release(); headerBuf = null;
            triBuf?.Release(); triBuf = null;
            spawnPointsBuf?.Release(); spawnPointsBuf = null;
            biomeBuffer?.Release(); biomeBuffer = null;
            terraformEditBuf?.Release(); terraformEditBuf = null;
            combinedBuf?.Release(); combinedBuf = null;
            triCountBuf?.Release(); triCountBuf = null;

            if (genShader != null) Destroy(genShader);
            if (packShader != null) Destroy(packShader);
        }

        try
        {
            int kMain = genShader.FindKernel("TerrainGeneration");
            int kPack = packShader.FindKernel("PackForReadback");

            int maxCells = settings.chunkDims.x * settings.chunkDims.y * settings.chunkDims.z;
            int maxTriangles = maxCells * 5;
            if (maxTriangles <= 0) { onComplete?.Invoke(null, 0, new List<SpawnPoint>(), 0f); return; }

            // Header buffer
            headerBuf = new ComputeBuffer(1, sizeof(uint) * 2, ComputeBufferType.Structured);

            // Triangle buffer
            triBuf = new ComputeBuffer(maxTriangles, TRIANGLE_STRIDE, ComputeBufferType.Append);
            triBuf.SetCounterValue(0);

            // Spawn points buffer
            spawnPointsBuf = new ComputeBuffer(maxTriangles, SPAWN_POINT_STRIDE, ComputeBufferType.Append);
            spawnPointsBuf.SetCounterValue(0);

            
            // Combined buffer
            int combinedBytes = HEADER_BYTES + maxTriangles * (TRIANGLE_STRIDE + SPAWN_POINT_STRIDE);
            combinedBuf = new ComputeBuffer(Mathf.CeilToInt(combinedBytes / 4f), 4, ComputeBufferType.Raw);
            
            // Biome offsets
            float[] biomeOffsets = new float[settings.biomeSettings.Length];
            for (int i = 0; i < settings.biomeSettings.Length; i++)
                biomeOffsets[i] = settings.biomeSettings[i].densityOffset;

            biomeBuffer = new ComputeBuffer(biomeOffsets.Length, sizeof(float), ComputeBufferType.Structured);
            biomeBuffer.SetData(biomeOffsets);

            // terraform edits
            if (terraformEdits != null && terraformEdits.Count > 0)
            {
                terraformEditBuf = new ComputeBuffer(terraformEdits.Count, TERRAFORM_EDIT_STRIDE, ComputeBufferType.Structured);
                terraformEditBuf.SetData(terraformEdits);
            }
            else
            {
                terraformEditBuf = new ComputeBuffer(1, TERRAFORM_EDIT_STRIDE, ComputeBufferType.Structured);
                terraformEditBuf.SetData(new TerraformEdit[] { new TerraformEdit { position = Vector3.zero, strength = 0f, radius = 0f } });
            }

            // bind all
            genShader.SetBuffer(kMain, "_Header", headerBuf);
            genShader.SetBuffer(kMain, "_GeneratedTriangles", triBuf);
            genShader.SetBuffer(kMain, "_GeneratedSpawnPoints", spawnPointsBuf);
            genShader.SetBuffer(kMain, "_BiomeDensityOffsets", biomeBuffer);
            genShader.SetBuffer(kMain, "_TerraformEdits", terraformEditBuf);
            genShader.SetInt("_TerraformEditsCount", terraformEdits?.Count ?? 0);

            // params
            Vector3 scale = settings.scale;
            Vector3 dims = settings.chunkDims;
            Vector3 origin = position - new Vector3(scale.x * dims.x / 2f, scale.y * dims.y / 2f, scale.z * dims.z / 2f);

            genShader.SetMatrix("_Transform", Matrix4x4.TRS(origin, Quaternion.identity, scale));
            genShader.SetFloat("_IsoLevel", settings.isoLevel);
            genShader.SetInts("_ChunkDims", settings.chunkDims.x, settings.chunkDims.y, settings.chunkDims.z);

            genShader.SetFloat("_WorleyNoiseScale", settings.noiseScale);
            genShader.SetFloat("_WorleyVerticalScale", settings.verticalScale);
            genShader.SetFloat("_WorleyCaveHeightFalloff", settings.caveHeightFalloff);
            genShader.SetInt("_WorleySeed", settings.seed == 0 ? UnityEngine.Random.Range(0, 1_000_000) : (int)settings.seed);

            genShader.SetFloat("_DisplacementStrength", settings.displacementStrength);
            genShader.SetFloat("_DisplacementScale", settings.displacementScale);
            genShader.SetInt("_Octaves", settings.octaves);
            genShader.SetFloat("_Lacunarity", settings.lacunarity);
            genShader.SetFloat("_Persistence", settings.persistence);

            genShader.SetFloat("_BiomeScale", settings.biomeScale);
            genShader.SetFloat("_BiomeBorder", settings.biomeBorder);
            genShader.SetFloat("_BiomeDisplacementStrength", settings.biomeDisplacementStrength);
            genShader.SetFloat("_BiomeDisplacementScale", settings.biomeDisplacementScale);

            // dispatch terrain
            genShader.GetKernelThreadGroupSizes(kMain, out uint tgX, out uint tgY, out uint tgZ);
            int dx = Mathf.CeilToInt(settings.chunkDims.x / (float)tgX);
            int dy = Mathf.CeilToInt(settings.chunkDims.y / (float)tgY);
            int dz = Mathf.CeilToInt(settings.chunkDims.z / (float)tgZ);
            genShader.Dispatch(kMain, dx, dy, dz);

            // authoritative count from append buffer
            triCountBuf = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
            ComputeBuffer.CopyCount(triBuf, triCountBuf, 0);

            var spawnCountBuf = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
            ComputeBuffer.CopyCount(spawnPointsBuf, spawnCountBuf, 0);


            // pack kernel
            packShader.SetBuffer(kPack, "_Header", headerBuf);
            packShader.SetBuffer(kPack, "_GeneratedTriangles", triBuf);
            packShader.SetBuffer(kPack, "_GeneratedSpawnPoints", spawnPointsBuf);
            packShader.SetBuffer(kPack, "_Combined", combinedBuf);
            packShader.SetBuffer(kPack, "_TriCountRaw", triCountBuf);
            packShader.SetBuffer(kPack, "_SpawnPointCountRaw", spawnCountBuf);
            packShader.SetInt("_TriangleStrideBytes", TRIANGLE_STRIDE);
            packShader.SetInt("_SpawnPointStrideBytes", SPAWN_POINT_STRIDE);
            packShader.SetInt("_HeaderBytes", HEADER_BYTES);
            packShader.SetInt("_MaxTriangles", maxTriangles);


            packShader.GetKernelThreadGroupSizes(kPack, out uint px, out _, out _);
            int groups = Mathf.CeilToInt(maxTriangles / (float)px);
            packShader.Dispatch(kPack, Mathf.Max(1, groups), 1, 1);

            // single readback
            AsyncGPUReadback.Request(combinedBuf, req =>
            {
                try
                {
                    float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;

                    if (req.hasError)
                    {
                        ReleaseAll();
                        onComplete?.Invoke(null, 0, new List<SpawnPoint>(), elapsed);
                        return;
                    }

                    var raw = req.GetData<byte>();
                    uint triCount = BitConverter.ToUInt32(raw.Slice(0, 4).ToArray(), 0);
                    uint biomeMask = BitConverter.ToUInt32(raw.Slice(4, 4).ToArray(), 0);
                    uint spawnCount = BitConverter.ToUInt32(raw.Slice(8, 4).ToArray(), 0);

                    
                    if (triCount == 0)
                    {
                        ReleaseAll();
                        onComplete?.Invoke(null, biomeMask, new List<SpawnPoint>(), elapsed);
                        return;
                    }
                    // Triangles
                    int triBytes = (int)triCount * TRIANGLE_STRIDE;
                    int triOffset = HEADER_BYTES;
                    var triBytesArray = new NativeArray<byte>(triBytes, Allocator.Temp);
                    for (int i = 0; i < triBytes; i++)
                        triBytesArray[i] = raw[triOffset + i];
                    

                    var triNative = triBytesArray.Reinterpret<MCTriangle>(1);
                    var mesh = ComposeMesh(triNative);
                    triBytesArray.Dispose();

                    // Spawn points
                    int spawnOffset = HEADER_BYTES + (int)triCount * TRIANGLE_STRIDE;
                    int spawnBytes  = (int)spawnCount * SPAWN_POINT_STRIDE;

                    var spawnBytesArray = new NativeArray<byte>(spawnBytes, Allocator.Temp);
                    for (int i = 0; i < spawnBytes; i++)
                        spawnBytesArray[i] = raw[spawnOffset + i];

                    var spawnNative = spawnBytesArray.Reinterpret<SpawnPoint>(1);
                    var spawnPoints = new List<SpawnPoint>(spawnNative.Length);
                    // string debugString = "";
                    for (int i = 0; i < spawnNative.Length; i++){
                        spawnPoints.Add(spawnNative[i]);
                    }
                    // Debug.Log(debugString);

                    spawnBytesArray.Dispose();

                    // Debug.Log("Readback request completed in " + elapsed.ToString("F2") + " milliseconds");
                    onComplete?.Invoke(mesh, biomeMask, spawnPoints, elapsed);
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
            onComplete?.Invoke(null, 0, new List<SpawnPoint>(), 0f);
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
