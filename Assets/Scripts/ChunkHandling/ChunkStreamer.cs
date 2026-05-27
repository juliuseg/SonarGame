using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class ChunkStreamer
{
    [Header("References")]
    private Transform _target; // defaults to this.transform
    
    private ChunkStreamingSettings _chunkStreamingSettings;
    
    private bool reloadTerrain = false;  // IMPLEMENT THIS AT SOME POINT...



    private readonly ChunkManager _chunkManager;
    
    private ChunkBuilder _chunkBuilder;
    
    
    
    private readonly Queue<Vector3Int> _buildQueue = new();
    private readonly HashSet<Vector3Int> _pending = new(); // enqueued or building


    private readonly HashSet<Vector3Int> _dirty = new();
    private readonly List<Vector3Int> _unloadScratch = new();
    
    
    public ChunkStreamer(ChunkBuilder chunkBuilder, ChunkManager chunkManager, ChunkStreamingSettings chunkStreamingSettings, Transform target)
    {
        _chunkBuilder = chunkBuilder;
        _chunkManager = chunkManager;
        _chunkStreamingSettings = chunkStreamingSettings;
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

        float radius = ChunkMath.GetDynamicRadius(_target.position, _chunkStreamingSettings);

        // Always evaluate needed and far chunks
        EnqueueNeeded(center, chunkSize, radius);
        UnloadFar(center, chunkSize, radius);



        BuildQueued(radius);

        


        // Compute potential max chunks based on radius and chunk size.
        int maxRange = Mathf.CeilToInt((radius*0.5f + _chunkStreamingSettings.unloadBuffer) / Mathf.Min(chunkSize.x, Mathf.Min(chunkSize.y, chunkSize.z)));
        int maxChunks = (maxRange * 2 + 1) * (maxRange * 2 + 1) * (maxRange * 2 + 1);
        // Debug.Log($"Potential max chunks: {maxChunks}");
    }


    // ----- core -----

    private void EnqueueNeeded(Vector3Int center, Vector3 chunkSize, float radius)
    {
        // Calculate how many chunk steps we need to search to cover the radius
        int maxRange = Mathf.CeilToInt((radius + _chunkStreamingSettings.unloadBuffer) / Mathf.Min(chunkSize.x, Mathf.Min(chunkSize.y, chunkSize.z)));

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
        while (_buildQueue.Count > 0)
        {
            var coord = _buildQueue.Peek();

            Vector3 centerWorld = _chunkManager.ChunkCenterWorld(coord);
            if (ChunkMath.IsOutOfRange(_target.position, centerWorld, radius))
            {
                _buildQueue.Dequeue();
                _pending.Remove(coord);
                continue;
            }

            if (!_chunkBuilder.TrySubmitChunkBuild(coord))
                break;

            _buildQueue.Dequeue();
            _pending.Add(coord);
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
        _unloadScratch.Clear();
        float unloadRadius = radius + _chunkStreamingSettings.unloadBuffer;

        foreach (var kvp in _chunkManager.chunks)
        {
            Vector3 worldCenter = _chunkManager.ChunkCenterWorld(kvp.Key);
            if (ChunkMath.IsOutOfRange(_target.position, worldCenter, unloadRadius))
                _unloadScratch.Add(kvp.Key);
        }

        foreach (var c in _unloadScratch)
        {
            _pending.Remove(c);
            _chunkBuilder.CancelBuild(c);
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


