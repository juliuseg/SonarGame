using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

public class SpawnManager
{
    readonly ChunkManager _chunkManager;
    readonly MCSettings _mcSettings;
    readonly ChunkStreamingSettings _chunkStreamingSettings;
    readonly Transform _target;
    readonly PropInstanceBaker _baker;
    readonly MaterialPropertyBlock _propertyBlock = new();

    public bool Enabled { get; set; } = true;

    public SpawnManager(
        ChunkManager chunkManager,
        MCSettings mcSettings,
        ChunkStreamingSettings chunkStreamingSettings,
        Transform target,
        ComputeShader propBuildShader)
    {
        _chunkManager = chunkManager;
        _mcSettings = mcSettings;
        _chunkStreamingSettings = chunkStreamingSettings;
        _target = target;
        _baker = new PropInstanceBaker(propBuildShader, mcSettings, chunkManager);
    }

    public void HandleChunkReady(Vector3Int coord)
    {
        if (!_chunkManager.TryGetChunk(coord, out var chunk))
            return;

        chunk.ReleasePropBatches();
        chunk.propBatches = _baker.Bake(coord, chunk);
    }

    public void Dispose()
    {
        _baker.Dispose();
    }

    public void Tick()
    {
        if (!Enabled)
            return;

        Profiler.BeginSample("SpawnManager.Tick");
        try
        {
            float radius = ChunkMath.GetDynamicRadius(_target.position, _chunkStreamingSettings);
            DrawVisibleChunks(radius * 0.75f);
        }
        finally
        {
            Profiler.EndSample();
        }
    }

    void DrawVisibleChunks(float radius)
    {
        Profiler.BeginSample("SpawnManager.DrawVisibleChunks");
        try
        {
            foreach (var kvp in _chunkManager.chunks)
            {
                Vector3 worldCenter = _chunkManager.ChunkCenterWorld(kvp.Key);
                if (ChunkMath.IsOutOfRange(_target.position, worldCenter, radius))
                    continue;

                var batches = kvp.Value.propBatches;
                if (batches == null || batches.Count == 0)
                    continue;

                Profiler.BeginSample("SpawnManager.DrawMeshInstancedIndirect");
                try
                {
                    for (int i = 0; i < batches.Count; i++)
                        batches[i].Draw(_propertyBlock);
                }
                finally
                {
                    Profiler.EndSample();
                }
            }
        }
        finally
        {
            Profiler.EndSample();
        }
    }
}
