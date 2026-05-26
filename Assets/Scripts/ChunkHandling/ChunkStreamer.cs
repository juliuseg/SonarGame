using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class ChunkStreamer
{
    [Header("References")]
    private Transform _target; // defaults to this.transform
    
    private ChunkSettings _chunkSettings;
    
    private bool reloadTerrain = false;  // IMPLEMENT THIS AT SOME POINT...



    private readonly ChunkManager _chunkManager;
    
    private ChunkBuilder _chunkBuilder;
    
    
    
    private readonly Queue<Vector3Int> _buildQueue = new();
    private readonly HashSet<Vector3Int> _pending = new(); // enqueued or building


    private readonly HashSet<Vector3Int> _dirty = new();
    
    
    public ChunkStreamer(ChunkBuilder chunkBuilder, ChunkManager chunkManager, ChunkSettings chunkSettings, Transform target)
    {
        _chunkBuilder = chunkBuilder;
        _chunkManager = chunkManager;
        _chunkSettings = chunkSettings;
        _target = target;

        _chunkBuilder.OnChunkReady += OnChunkReady;
    }
    

    public void Tick()
    {

        if (reloadTerrain)
        {
            reloadTerrain = false;
            _chunkManager.Clear();
            _buildQueue.Clear();
            _pending.Clear();
            _dirty.Clear();
        }

        Vector3 chunkSize = _chunkManager.GetChunkSize();
        Vector3Int center = _chunkManager.WorldToChunk(_target.position);

        float radius = ChunkMath.GetDynamicRadius(_target.position, _chunkSettings);

        // Always evaluate needed and far chunks
        EnqueueNeeded(center, chunkSize, radius);
        UnloadFar(center, chunkSize, radius);



        BuildQueued(radius);

        


        // Compute potential max chunks based on radius and chunk size.
        // int maxRange = Mathf.CeilToInt((radius*0.5f + _chunkSettings.unloadBuffer) / Mathf.Min(chunkSize.x, Mathf.Min(chunkSize.y, chunkSize.z)));
        // int maxChunks = (maxRange * 2 + 1) * (maxRange * 2 + 1) * (maxRange * 2 + 1);
        // // Debug.Log($"Potential max chunks: {maxChunks}");
    }

    // ----- GPU Instancing -----





    // call this once per frame after terrain builds
    private void DrawSpawnInstances(List<SpawnPoint> spawnPoints, InstancingMesh instancingMesh)
    {
        if (instancingMesh == null || instancingMesh.material == null) return;
        Mesh instanceMesh = instancingMesh.mesh;
        Material instanceMaterial = instancingMesh.material;
        float meshScale = instancingMesh.scale;
        float meshScaleOffset = instancingMesh.scaleOffset;
        float meshYOffset = instancingMesh.yOffset;

        // after collecting from all chunks
        int total = spawnPoints.Count;
        if (total == 0) return;


        int count = total;
        // Debug.Log($"Drawing {count} spawn points");

        // prepare matrices
        Matrix4x4[] matrices = new Matrix4x4[count];

        int drawn = 0;
        
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = spawnPoints[drawn + i].positionWS;
            Vector3 normal = spawnPoints[drawn + i].normalWS;
            float scale = meshScale + (ChunkMath.Hash(pos) * 2f - 1f) * meshScaleOffset;
            scale = Mathf.Max(scale, 0.01f);

            matrices[i] = Matrix4x4.TRS(
                pos + (1-normal.y) * meshYOffset * Vector3.up,
                Quaternion.Euler(0,1f,0),//Quaternion.FromToRotation(Vector3.up, normal),
                Vector3.one * scale
            );

        }

        Graphics.DrawMeshInstanced(instanceMesh, 0, instanceMaterial, matrices, count);
        
        
    }



    // ----- core -----


    


    private void EnqueueNeeded(Vector3Int center, Vector3 chunkSize, float radius)
    {
        // Calculate how many chunk steps we need to search to cover the radius
        int maxRange = Mathf.CeilToInt((radius + _chunkSettings.unloadBuffer) / Mathf.Min(chunkSize.x, Mathf.Min(chunkSize.y, chunkSize.z)));

        for (int dx = -maxRange; dx <= maxRange; dx++)
        for (int dy = -maxRange; dy <= maxRange; dy++)
        for (int dz = -maxRange; dz <= maxRange; dz++)
        {
            var c = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);

            Vector3 worldCenter = _chunkManager.ChunkCenterWorld(c);
            bool outOfRange = ChunkMath.IsOutOfRange(_target.position, worldCenter, radius);

            if (outOfRange) continue;
            if (_chunkManager.TryGetChunk(c, out _)) continue;
            if (_buildQueue.Contains(c)) continue;
            if (_pending.Contains(c)) continue;

            

            // print("Enqueuing chunk: " + c);
            _buildQueue.Enqueue(c);
            _pending.Add(c);

        }
    }

    private void BuildQueued(float radius)
    {
        int built = 0;

        while (_buildQueue.Count > 0 && built < _chunkSettings.maxBuildsPerFrame)
        {
            var coord = _buildQueue.Dequeue();
            _pending.Remove(coord);

            Vector3 centerWorld = _chunkManager.ChunkCenterWorld(coord);
            if (ChunkMath.IsOutOfRange(_target.position, centerWorld, radius)) continue;

            _pending.Add(coord);
            _chunkBuilder.BuildOne(coord);
            built++;
        }

        _chunkBuilder.FlushPendingColliders();
    }


    public void Dispose()
    {
        _chunkManager.Clear();

        _buildQueue.Clear();
        _pending.Clear();
    }
    
    private void OnChunkReady(Vector3Int coord)
    {
        _pending.Remove(coord);

        if (_dirty.Remove(coord))
        {
            _buildQueue.Enqueue(coord);
            _pending.Add(coord);
        }
    }


    // Replace UnloadFar
    private void UnloadFar(Vector3Int center, Vector3 chunkSize, float radius)
    {
        var toRemove = new List<Vector3Int>();

        foreach (var kvp in _chunkManager.chunks)
        {
            Vector3 worldCenter = _chunkManager.ChunkCenterWorld(kvp.Key);
            bool outOfRange = ChunkMath.IsOutOfRange(_target.position, worldCenter, radius + _chunkSettings.unloadBuffer);
            if (outOfRange){
                // print("Unloading chunk: " + kvp.Key);
                toRemove.Add(kvp.Key);
            }

            
        }

        foreach (var c in toRemove) {
            _pending.Remove(c);
            _chunkManager.DestroyChunk(c);
        }
    }

    public void ApplyTerraformEdit(TerraformEdit edit)
    {
        var affectedChunks = _chunkManager.ApplyTerraformEdit(edit);

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


}


