using System.Collections.Generic;
using UnityEngine;

public class SpawnManager
{
    private readonly ChunkManager _chunkManager;
    private readonly MCSettings _mcSettings;
    private readonly ChunkSettings _chunkSettings;
    private readonly Transform _target;

    public SpawnManager(
        ChunkManager chunkManager,
        MCSettings mcSettings,
        ChunkSettings chunkSettings,
        Transform target)
    {
        _chunkManager = chunkManager;
        _mcSettings = mcSettings;
        _chunkSettings = chunkSettings;
        _target = target;
    }

    public void Tick()
    {
        float radius = ChunkMath.GetDynamicRadius(_target.position, _chunkSettings);
        List<SpawnPoint>[] allPoints = CollectSpawnPoints(radius * 0.5f);
        DistributeAndDraw(allPoints);
    }

    // ---- Spawn point collection ----

    private List<SpawnPoint>[] CollectSpawnPoints(float radius)
    {
        int biomeCount = _mcSettings.biomeSettings.Length;
        List<SpawnPoint>[] allPoints = new List<SpawnPoint>[biomeCount];
        for (int i = 0; i < biomeCount; i++)
            allPoints[i] = new List<SpawnPoint>();

        foreach (var kvp in _chunkManager.chunks)
        {
            Vector3 worldCenter = _chunkManager.ChunkCenterWorld(kvp.Key);
            if (ChunkMath.IsOutOfRange(_target.position, worldCenter, radius)) continue;

            var chunk = kvp.Value;
            if (chunk.spawnPoints == null || chunk.spawnPoints.Count == 0) continue;

            foreach (var biomeIndex in chunk.GetBiomeMaskList())
            {
                if (biomeIndex < 0 || biomeIndex >= biomeCount) continue;
                allPoints[biomeIndex].AddRange(chunk.spawnPoints);
            }
        }

        return allPoints;
    }

    // ---- Distribution ----

    private void DistributeAndDraw(List<SpawnPoint>[] allPoints)
    {
        for (int i = 0; i < _mcSettings.biomeSettings.Length; i++)
        {
            List<InstancingMesh> instancingMeshes = _mcSettings.biomeSettings[i].instancingMeshes;

            var probs = new float[instancingMeshes.Count];
            for (int j = 0; j < instancingMeshes.Count; j++)
                probs[j] = instancingMeshes[j].probability;

            var lists = SpawnDistributor.Distribute(allPoints[i], probs, i);

            for (int j = 0; j < lists.Count; j++)
                DrawInstanced(lists[j], instancingMeshes[j]);

            // TODO: ore spawning bucket goes here
        }
    }

    // ---- Instancing ----

    private void DrawInstanced(List<SpawnPoint> spawnPoints, InstancingMesh instancingMesh)
    {
        if (instancingMesh == null || instancingMesh.material == null) return;
        if (spawnPoints.Count == 0) return;

        int count = spawnPoints.Count;
        Matrix4x4[] matrices = new Matrix4x4[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = spawnPoints[i].positionWS;
            Vector3 normal = spawnPoints[i].normalWS;
            float scale = instancingMesh.scale + (ChunkMath.Hash(pos) * 2f - 1f) * instancingMesh.scaleOffset;
            scale = Mathf.Max(scale, 0.01f);

            matrices[i] = Matrix4x4.TRS(
                pos + (1 - normal.y) * instancingMesh.yOffset * Vector3.up,
                Quaternion.Euler(0, 1f, 0),
                Vector3.one * scale
            );
        }

        Graphics.DrawMeshInstanced(instancingMesh.mesh, 0, instancingMesh.material, matrices, count);
    }
}