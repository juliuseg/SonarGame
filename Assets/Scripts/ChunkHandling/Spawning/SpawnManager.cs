using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

public class SpawnManager
{
    sealed class PersistedSpawnNode
    {
        public GameObject Instance;
    }

    readonly ChunkManager _chunkManager;
    readonly MCSettings _mcSettings;
    readonly ChunkStreamingSettings _chunkStreamingSettings;
    readonly Transform _target;
    readonly PropInstanceBaker _baker;
    readonly MaterialPropertyBlock _propertyBlock = new();
    readonly Dictionary<Vector3Int, List<GameObject>> _spawnedObjects = new();
    readonly Dictionary<SpawnPointKey, PersistedSpawnNode> _persistedEditedNodes = new();
    readonly Transform _persistedRoot;

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

        var rootObject = new GameObject("PersistedSpawnNodes");
        Object.DontDestroyOnLoad(rootObject);
        _persistedRoot = rootObject.transform;

        SpawnNodePersistence.EditedNodeRemoved += OnEditedNodeRemoved;
    }

    void OnEditedNodeRemoved(SpawnPointKey key)
    {
        _persistedEditedNodes[key] = new PersistedSpawnNode();
    }

    public void HandleChunkReady(Vector3Int coord)
    {
        if (!_chunkManager.TryGetChunk(coord, out var chunk))
            return;

        ReleaseSpawnedObjects(coord);
        chunk.ReleasePropBatches();
        SpawnChunkContent(coord, chunk);
    }

    public void Dispose()
    {
        var coords = new List<Vector3Int>(_spawnedObjects.Keys);
        for (int i = 0; i < coords.Count; i++)
            ReleaseSpawnedObjects(coords[i], destroyAll: true);

        _spawnedObjects.Clear();

        foreach (var kvp in _persistedEditedNodes)
        {
            if (kvp.Value.Instance != null)
                Object.Destroy(kvp.Value.Instance);
        }

        _persistedEditedNodes.Clear();

        if (_persistedRoot != null)
            Object.Destroy(_persistedRoot.gameObject);

        SpawnNodePersistence.EditedNodeRemoved -= OnEditedNodeRemoved;
        _baker.Dispose();
    }

    public void Tick()
    {
        CleanupOrphanedSpawns();

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

    void SpawnChunkContent(Vector3Int coord, Chunk chunk)
    {
        var batches = new List<ChunkPropBatch>();
        chunk.propBatches = batches;

        if (chunk.spawnPoints == null || chunk.spawnPoints.Count == 0)
            return;

        Vector3 chunkCenter = _chunkManager.ChunkCenterWorld(coord);
        var bounds = new Bounds(chunkCenter, _chunkManager.GetChunkSize());
        var instances = new List<GameObject>();
        var placed = new List<PlacedSpawn>();

        _baker.BeginBake();

        foreach (int biomeIndex in chunk.GetBiomeMaskList())
        {
            if (biomeIndex < 0 || biomeIndex >= _mcSettings.biomeSettings.Length)
                continue;

            var biome = _mcSettings.biomeSettings[biomeIndex];
            var instancingMeshes = biome.instancingMeshes;
            var spawnNodes = biome.spawnNodes;

            int instancingCount = instancingMeshes != null ? instancingMeshes.Count : 0;
            int spawnNodeCount = spawnNodes != null ? spawnNodes.Count : 0;
            int typeCount = instancingCount + spawnNodeCount;
            if (typeCount == 0)
                continue;

            var probs = new float[typeCount];
            for (int j = 0; j < instancingCount; j++)
                probs[j] = instancingMeshes[j].probability;
            for (int j = 0; j < spawnNodeCount; j++)
                probs[instancingCount + j] = spawnNodes[j].probability;

            var lists = SpawnDistributor.Distribute(chunk.spawnPoints, probs, biomeIndex);

            for (int j = 0; j < instancingCount; j++)
            {
                var batch = _baker.TryBakeBatch(instancingMeshes[j], lists[j], bounds);
                if (batch != null)
                    batches.Add(batch);
            }

            if (Enabled)
            {
                for (int j = 0; j < spawnNodeCount; j++)
                    TrySpawnNodeList(spawnNodes[j], lists[instancingCount + j], chunk, placed, instances);
            }
        }

        _baker.EndBake();

        if (instances.Count > 0)
            _spawnedObjects[coord] = instances;
    }

    void TrySpawnNodeList(
        SpawnNode spawnNode,
        List<SpawnPoint> spawnPoints,
        Chunk chunk,
        List<PlacedSpawn> placed,
        List<GameObject> instances)
    {
        if (spawnNode == null || spawnNode.prefab == null || spawnPoints == null || spawnPoints.Count == 0)
            return;

        Transform parent = chunk.gameObject != null ? chunk.gameObject.transform : null;

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            var spawnKey = new SpawnPointKey(spawnPoints[i].positionWS);

            if (_persistedEditedNodes.TryGetValue(spawnKey, out PersistedSpawnNode persisted))
            {
                _persistedEditedNodes.Remove(spawnKey);

                if (persisted.Instance == null)
                    continue;

                RestorePersistedInstance(persisted.Instance, parent, placed, instances);
                continue;
            }

            ComputeSpawnTransform(spawnPoints[i], spawnNode, out Vector3 position, out Quaternion rotation, out float scale);

            if (OverlapsExisting(position, scale, placed))
                continue;

            var instance = Object.Instantiate(spawnNode.prefab, position, rotation, parent);
            instance.transform.localScale = Vector3.one * scale;
            InitializeSpawnNodeController(instance, spawnPoints[i].positionWS);
            instances.Add(instance);
            placed.Add(new PlacedSpawn(position, scale));
        }
    }

    static void InitializeSpawnNodeController(GameObject instance, Vector3 spawnPointPosition)
    {
        if (!instance.TryGetComponent(out SpawnNodeController controller))
            controller = instance.AddComponent<SpawnNodeController>();

        controller.Initialize(spawnPointPosition);
    }

    static void RestorePersistedInstance(
        GameObject instance,
        Transform parent,
        List<PlacedSpawn> placed,
        List<GameObject> instances)
    {
        instance.transform.SetParent(parent, true);
        instance.SetActive(true);

        float scale = Mathf.Max(instance.transform.lossyScale.x, 0.01f);
        placed.Add(new PlacedSpawn(instance.transform.position, scale));
        instances.Add(instance);
    }

    static bool OverlapsExisting(Vector3 position, float scale, List<PlacedSpawn> placed)
    {
        float radius = scale * 0.5f;
        for (int i = 0; i < placed.Count; i++)
        {
            float minDist = radius + placed[i].Radius;
            if ((position - placed[i].Position).sqrMagnitude < minDist * minDist)
                return true;
        }

        return false;
    }

    static void ComputeSpawnTransform(
        SpawnPoint spawnPoint,
        SpawnNode spawnNode,
        out Vector3 position,
        out Quaternion rotation,
        out float scale)
    {
        Vector3 pos = spawnPoint.positionWS;
        Vector3 normal = spawnPoint.normalWS;
        if (normal.sqrMagnitude < 1e-6f)
            normal = Vector3.up;
        else
            normal.Normalize();

        float h = PositionHash(pos);
        scale = Mathf.Max(spawnNode.scale + (h * 2f - 1f) * spawnNode.scaleOffset, 0.01f);

        Vector3 alignedUp = Vector3.Slerp(normal, Vector3.up, spawnNode.verticalBias).normalized;
        float spin = PositionHash(pos + new Vector3(0.37f, 0.37f, 0.37f)) * 360f;

        Quaternion alignRotation = Quaternion.FromToRotation(Vector3.up, alignedUp);
        Quaternion spinRotation = Quaternion.AngleAxis(spin, alignedUp);
        rotation = spinRotation * alignRotation;
        position = pos + alignedUp * spawnNode.yOffset;
    }

    static float PositionHash(Vector3 p)
    {
        int xi = Mathf.FloorToInt(p.x * 1000f);
        int yi = Mathf.FloorToInt(p.y * 1000f);
        int zi = Mathf.FloorToInt(p.z * 1000f);

        uint h = (uint)(xi * 374761393 + yi * 668265263 + zi * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= (h >> 16);
        return (h & 0x00FFFFFF) / 16777216f;
    }

    void ReleaseSpawnedObjects(Vector3Int coord, bool destroyAll = false)
    {
        if (!_spawnedObjects.TryGetValue(coord, out var instances))
            return;

        for (int i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
            if (instance == null)
                continue;

            if (!destroyAll && instance.TryGetComponent(out SpawnNodeController controller) && controller.HasBeenEdited)
            {
                var key = controller.GetSpawnKey();
                if (!_persistedEditedNodes.ContainsKey(key))
                    PersistEditedNode(instance);

                continue;
            }

            Object.Destroy(instance);
        }

        instances.Clear();
        _spawnedObjects.Remove(coord);
    }

    void PersistEditedNode(GameObject instance)
    {
        var key = instance.GetComponent<SpawnNodeController>().GetSpawnKey();
        instance.transform.SetParent(_persistedRoot, true);
        _persistedEditedNodes[key] = new PersistedSpawnNode { Instance = instance };
    }

    void CleanupOrphanedSpawns()
    {
        var stale = new List<Vector3Int>();
        foreach (var kvp in _spawnedObjects)
        {
            if (!_chunkManager.chunks.ContainsKey(kvp.Key))
                stale.Add(kvp.Key);
        }

        for (int i = 0; i < stale.Count; i++)
            ReleaseSpawnedObjects(stale[i]);
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

    readonly struct PlacedSpawn
    {
        public Vector3 Position { get; }
        public float Radius { get; }

        public PlacedSpawn(Vector3 position, float scale)
        {
            Position = position;
            Radius = scale * 0.5f;
        }
    }
}
