using System.Collections.Generic;
using UnityEngine;

public class ChunkLoader : MonoBehaviour
{
    [Header("References")]
    public MCBaker baker;
    public Transform target; // defaults to this.transform

    [Header("Streaming")]
    [Min(0)] public float unloadBuffer = 10f;       // margin before destroying
    [Min(1)] public int maxBuildsPerFrame = 4;      // generation budget


    [Header("Dynamic Radius")]
    public float surfaceRadius = 50f;   // used when above water
    public float deepRadius = 20f;      // used when deep below water
    public float waterLevel = 20f;      // reference height
    public float deepLevel = -10;      // depth where radius = deepRadius

    [Header("Debug")]
    public bool reloadTerrain = false;  // button to reload all terrain

    [Header("SDF")]
    public ComputeShader densityShader;
    public ComputeShader edtShader;
    public MCSettings settings;

    private SDFGpu sdfGen;

    public ChunkManager chunkManager;
    private readonly Queue<Vector3Int> _buildQueue = new();
    private readonly HashSet<Vector3Int> _pending = new(); // enqueued or building

    void Awake()
    {
        if (baker == null) baker = GetComponent<MCBaker>();
        if (target == null) target = transform;

        sdfGen = new SDFGpu(densityShader, edtShader, settings);
        chunkManager = new ChunkManager(settings);
    }



    void Update()
    {
        if (baker == null) return;

        
        Vector3 chunkSize = chunkManager.GetChunkSize();
        Vector3Int center = chunkManager.WorldToChunk(target.position);

        float radius = GetDynamicRadius();

        // Always evaluate needed and far chunks
        EnqueueNeeded(center, chunkSize, radius);
        UnloadFar(center, chunkSize, radius);

        BuildQueued(chunkSize, radius);


        // Debug chunks
        print($"Chunks: {chunkManager.chunks.Count}  Queue: {_buildQueue.Count}  Pending: {_pending.Count}");
    }


    // ----- core -----

    


    private void EnqueueNeeded(Vector3Int center, Vector3 chunkSize, float radius)
    {
        // Calculate how many chunk steps we need to search to cover the radius
        int maxRange = Mathf.CeilToInt((radius + unloadBuffer) / Mathf.Min(chunkSize.x, Mathf.Min(chunkSize.y, chunkSize.z)));

        for (int dx = -maxRange; dx <= maxRange; dx++)
        for (int dy = -maxRange; dy <= maxRange; dy++)
        for (int dz = -maxRange; dz <= maxRange; dz++)
        {
            var c = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);

            Vector3 worldCenter = chunkManager.ChunkCenterWorld(c);
            float dist = Vector3.Distance(worldCenter, target.position);

            // check vertical cutoff
            // float chunkHalfHeight = chunkSize.y * 0.5f;
            // if (worldCenter.y - chunkHalfHeight > waterLevel)
            //     continue;

            if (dist > radius) continue;
            if (chunkManager.TryGetChunk(c, out _)) continue;
            if (_buildQueue.Contains(c)) continue;
            if (_pending.Contains(c)) continue;

            

            // print("Enqueuing chunk: " + c);
            _buildQueue.Enqueue(c);
            _pending.Add(c);

        }
    }

    private void BuildQueued(Vector3 chunkSize, float radius)
    {
        int built = 0;

        while (_buildQueue.Count > 0 && built < maxBuildsPerFrame)
        {
            var coord = _buildQueue.Dequeue();

            // Skip if already loaded
            if (chunkManager.TryGetChunk(coord, out _)){
                _pending.Remove(coord);
                continue;
            }

            Vector3 centerWorld = chunkManager.ChunkCenterWorld(coord);
            float dist = Vector3.Distance(centerWorld, target.position);
            if (dist > radius){
                _pending.Remove(coord);
                continue;
            }

            // Reserve placeholder to prevent double-builds
            chunkManager.SetChunk(coord, new Chunk(null, null, null));

            float chunkHalfHeight = chunkSize.y * 0.5f;
            if (centerWorld.y - chunkHalfHeight < waterLevel){

                // Launch async GPU job (non-blocking)
                baker.RunAsync(centerWorld, (mesh) =>
                {
                    // Safety: chunk could have been unloaded while GPU was working
                    if (!chunkManager.TryGetChunk(coord, out var chunk))
                        return;

                    if (mesh == null || mesh.vertexCount == 0)
                    {
                        
                        chunk.gameObject = null;
                        chunkManager.SetChunk(coord, chunk);
                    
                        return;
                    }

                    // Create chunk GameObject
                    var go = new GameObject($"Chunk_{coord.x}_{coord.y}_{coord.z}");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = Vector3.zero;

                    var mf = go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();
                    var mc = go.AddComponent<MeshCollider>();

                    // print("Mesh loaded at: " + coord);

                    mf.sharedMesh = mesh;
                    mc.sharedMesh = mesh;

                    // Copy parent material
                    var parentRenderer = baker.GetComponent<MeshRenderer>();
                    if (parentRenderer)
                        mr.sharedMaterial = parentRenderer.sharedMaterial;

                    chunk.gameObject = go;

                    chunkManager.SetChunk(coord, chunk);
                });
            } else {
                if (!chunkManager.TryGetChunk(coord, out var chunk))
                    return;
                
                chunk.gameObject = null;
                chunkManager.SetChunk(coord, chunk);
            }

            // Build SDF
            sdfGen.GenerateAsync(centerWorld, (data, buffer, state) =>
            {
                Vector3Int coord = (Vector3Int)state;

                if (!chunkManager.TryGetChunk(coord, out var chunk))
                {
                    // chunk got unloaded before GPU finished
                    buffer?.Release(); // free GPU memory
                    return;
                }
                
                if (data == null || buffer == null)
                {
                    Debug.LogWarning($"SDF generation failed for chunk: {coord}");
                    return;
                }

                chunk.sdfData = data;
                chunk.sdfBuffer = buffer;
                chunkManager.SetChunk(coord, chunk);
            }, userState: coord);




            built++;
        }
    }

    public void OnDestroy()
    {
        chunkManager.Clear();

        _buildQueue.Clear();
        _pending.Clear();
    }




    // Replace UnloadFar
    private void UnloadFar(Vector3Int center, Vector3 chunkSize, float radius)
    {
        var toRemove = new List<Vector3Int>();

        foreach (var kvp in chunkManager.chunks)
        {
            Vector3 worldCenter = chunkManager.ChunkCenterWorld(kvp.Key);
            float dist = Vector3.Distance(worldCenter, target.position);
            if (dist > radius + unloadBuffer){
                // print("Unloading chunk: " + kvp.Key);
                toRemove.Add(kvp.Key);
            }

            
        }

        foreach (var c in toRemove) {
            _pending.Remove(c);
            chunkManager.DestroyChunk(c);
        }
    }



    private float GetDynamicRadius()
    {
        float y = target.position.y;

        if (y >= waterLevel)
            return surfaceRadius;

        if (y <= deepLevel)
            return deepRadius;

        // interpolate between surface and deep
        float t = Mathf.InverseLerp(waterLevel, deepLevel, y);
        return Mathf.Lerp(surfaceRadius, deepRadius, t);
    }


}
