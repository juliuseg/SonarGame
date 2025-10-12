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

    private readonly Dictionary<Vector3Int, BuildState> _buildStates = new();

    private readonly HashSet<Vector3Int> _dirty = new();

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


        // print($"Chunks: {chunkManager.chunks.Count}  Queue: {_buildQueue.Count}  Pending: {_pending.Count}");

        BuildQueued(chunkSize, radius);


        // Debug chunks
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
            bool outOfRange = GetRadiusRange(target.position, worldCenter, radius);

            // check vertical cutoff
            // float chunkHalfHeight = chunkSize.y * 0.5f;
            // if (worldCenter.y - chunkHalfHeight > waterLevel)
            //     continue;

            if (outOfRange) continue;
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
            _pending.Remove(coord); // remove now to keep state clean

            // Get existing chunk (if any)
            chunkManager.TryGetChunk(coord, out var existingChunk);
            Vector3 centerWorld = chunkManager.ChunkCenterWorld(coord);
            bool outOfRange = GetRadiusRange(target.position, centerWorld, radius);

            // Skip if out of active range
            if (outOfRange)
                continue;

            // If no chunk yet, create placeholder
            if (existingChunk == null)
            {
                existingChunk = new Chunk(null, null, null);
                chunkManager.SetChunk(coord, existingChunk);
            }

            // Track build state
            var bstate = new BuildState();
            _buildStates[coord] = bstate;
            _pending.Add(coord);

            float chunkHalfHeight = chunkSize.y * 0.5f;

            // -------- MESH GENERATION --------

            // Ensure GameObject exists before any generation
            if (existingChunk.gameObject == null || existingChunk.gameObject.Equals(null))
            {
                var go = new GameObject($"Chunk_{coord.x}_{coord.y}_{coord.z}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                existingChunk.gameObject = go;
                chunkManager.SetChunk(coord, existingChunk);
            }

            if (centerWorld.y - chunkHalfHeight < waterLevel)
            {
                baker.RunAsync(centerWorld, existingChunk.terraformEdits, (mesh, mask) =>
                {
                    if (!_buildStates.TryGetValue(coord, out var st) || !ReferenceEquals(st, bstate))
                        return; // outdated async callback

                    st.meshDone = true;

                    if (!chunkManager.TryGetChunk(coord, out var chunk) || chunk == null)
                        return;

                    // if chunk object destroyed in the meantime, abort
                    if (chunk.gameObject == null || chunk.gameObject.Equals(null))
                        return;

                    // get or add components safely
                    var go = chunk.gameObject;

                    var mf = go.GetComponent<MeshFilter>();
                    var mc = go.GetComponent<MeshCollider>();
                    var mr = go.GetComponent<MeshRenderer>();

                    // if Unity already destroyed the components, rebuild them
                    if (mf == null) mf = go.AddComponent<MeshFilter>();
                    if (mc == null) mc = go.AddComponent<MeshCollider>();
                    if (mr == null) mr = go.AddComponent<MeshRenderer>();

                    // verify again that components are valid
                    if (mf == null || mc == null || mr == null)
                        return;

                    mf.sharedMesh = mesh;
                    mc.sharedMesh = mesh;

                    // Debug.Log($"Chunk {coord} mesh generated. Chunk has edits: {chunk.terraformEdits.Count}");

                    var parentRenderer = baker.GetComponent<MeshRenderer>();
                    if (parentRenderer && mr.sharedMaterial != parentRenderer.sharedMaterial)
                        mr.sharedMaterial = parentRenderer.sharedMaterial;

                    chunkManager.SetChunk(coord, chunk);
                    TryFinalizeChunk(coord, st);
                });


            }
            else
            {
                // Above-water placeholder only
                if (existingChunk.gameObject == null){
                    existingChunk.gameObject = new GameObject($"Chunk_Empty_{coord.x}_{coord.y}_{coord.z}");
                    existingChunk.gameObject.transform.SetParent(transform);
                }

                chunkManager.SetChunk(coord, existingChunk);
                bstate.meshDone = true;
                TryFinalizeChunk(coord, bstate);
            }

            // -------- SDF GENERATION --------
            sdfGen.GenerateAsync(centerWorld, (data, buffer, state) =>
            {
                var coord = (Vector3Int)state;
                if (!_buildStates.TryGetValue(coord, out var st) || !ReferenceEquals(st, bstate))
                    return; // outdated

                st.sdfDone = true;

                if (!chunkManager.TryGetChunk(coord, out var chunk))
                {
                    buffer?.Release();
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

                TryFinalizeChunk(coord, st);
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

    private void TryFinalizeChunk(Vector3Int coord, BuildState state)
    {
        if (!_buildStates.TryGetValue(coord, out var currentState))
            return;

        if (!ReferenceEquals(currentState, state))
            return;

        if (state.meshDone && state.sdfDone)
        {
            _pending.Remove(coord);
            _buildStates.Remove(coord);

            // if dirty, requeue immediately
            if (_dirty.Remove(coord))
            {
                _buildQueue.Enqueue(coord);
                _pending.Add(coord);
            }
        }
    }



    private bool GetRadiusRange(Vector3 currentPos, Vector3 worldPos, float radius){

        Vector3 dif = currentPos - worldPos;
        dif.y *= 1.8f;
        float dist = dif.magnitude;

        // Skip if out of active range
        return dist > radius;
    }

    // Replace UnloadFar
    private void UnloadFar(Vector3Int center, Vector3 chunkSize, float radius)
    {
        var toRemove = new List<Vector3Int>();

        foreach (var kvp in chunkManager.chunks)
        {
            Vector3 worldCenter = chunkManager.ChunkCenterWorld(kvp.Key);
            bool outOfRange = GetRadiusRange(target.position, worldCenter, radius + unloadBuffer);
            if (outOfRange){
                // print("Unloading chunk: " + kvp.Key);
                toRemove.Add(kvp.Key);
            }

            
        }

        foreach (var c in toRemove) {
            _pending.Remove(c);
            chunkManager.DestroyChunk(c);
        }
    }

    public void ApplyTerraformEdit(TerraformEdit edit)
    {
        var affectedChunks = chunkManager.ApplyTerraformEdit(edit);

        foreach (var coord in affectedChunks)
        {
            if (_pending.Contains(coord))
            {
                // mark dirty instead of skipping
                _dirty.Add(coord);
            }
            else if (!_buildQueue.Contains(coord))
            {
                _buildQueue.Enqueue(coord);
                _pending.Add(coord);
            }
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

    private string MaskToString(uint mask)
    {
        System.Text.StringBuilder sb = new();
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1u << i)) != 0)
                sb.Append(i);
        }
        return sb.Length > 0 ? sb.ToString() : "None";
    }

    private class BuildState {
        public bool meshDone;
        public bool sdfDone;
    }

}


