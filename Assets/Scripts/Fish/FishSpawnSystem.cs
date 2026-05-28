using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns fish once per chunk when interior spawn data is ready.
/// Quota is spread across the estimated collect volume, scaled by interior
/// spawn density. Fish are placed in spread-out schools or as strays.
/// Despawns only by distance to the target — not tied to chunk lifetime.
/// </summary>
public class FishSpawnSystem : MonoBehaviour
{
    const int SelectionSeed = 12345;
    const int ArgsStride = 5;

    struct Fish
    {
        public Vector3 position;
        public Vector3Int sourceChunk;
        public bool isStray;
    }

    [Header("Draw")]
    public Mesh fishMesh;
    [Tooltip("One material per submesh, same order as the mesh (0 = first submesh).")]
    public List<Material> fishMaterials = new();
    public ComputeShader fishCompute;

    [Header("Counts")]
    [Min(1)] public int maxInstances = 5000;
    [Min(0.01f)] public float fishScale = 1f;

    [Header("Schools")]
    [Min(1)] public int schoolSize = 30;
    [Tooltip("Min world-space distance between school anchor spawn points.")]
    [Min(0f)] public float schoolCenterSeparation = 10f;
    [Tooltip("Min distance from anchor for school members, as a multiple of interior voxel spacing.")]
    [Min(0f)] public float schoolMemberSpread = 1.5f;

    [Header("Strays")]
    [Range(0f, 1f)] public float strayPercent = 0.15f;
    [Tooltip("If a chunk spawns fewer fish than this, every fish is a stray (no schools).")]
    [Min(0)] public int strayCutoff = 15;

    [Header("Debug")]
    public bool logDrawCount = true;
    [Min(0.25f)] public float logInterval = 1f;
    public bool drawSpawnGizmos = true;
    [Min(0.1f)] public float gizmoRadiusMultiplier = 1f;
    [Min(1)] public int maxGizmosDrawn = 256;

    private ChunkManager _chunkManager;
    private ChunkStreamingSettings _streamingSettings;
    private Transform _target;

    private Material[] _drawMaterials;
    private bool _materialMismatchLogged;
    private readonly List<Fish> _fish = new();
    private readonly HashSet<Vector3Int> _spawnedOnce = new();
    private readonly List<Vector3Int> _sortedChunkKeys = new();
    private readonly HashSet<int> _usedSpawnIndices = new();
    private readonly List<int> _schoolSizes = new();
    private readonly List<int> _schoolSpawnIndices = new();
    private readonly List<int> _schoolCenterIndices = new();

    private struct SpawnIndexDist
    {
        public int index;
        public float distSq;
    }

    private readonly List<SpawnIndexDist> _nearestSpawnScratch = new();

    private ComputeBuffer _positionsBuffer;
    private ComputeBuffer _matricesBuffer;
    private ComputeBuffer _argsBuffer;
    private Matrix4x4[] _matrixScratch;
    private readonly List<Vector4> _drawPositions = new();

    private int _kernelBoid;
    private int _activeCount;
    private uint[] _args;
    private int _argsSubMeshCapacity;
    private float _nextLogTime;

    public void Init(
        ChunkManager chunkManager,
        ChunkStreamingSettings streamingSettings,
        Transform target)
    {
        _chunkManager = chunkManager;
        _streamingSettings = streamingSettings;
        _target = target;

        if (fishCompute != null)
            _kernelBoid = fishCompute.FindKernel("BoidUpdate");

        _matrixScratch = new Matrix4x4[maxInstances];
        _positionsBuffer = new ComputeBuffer(maxInstances, sizeof(float) * 4);
        _matricesBuffer = new ComputeBuffer(maxInstances, sizeof(float) * 16);
        EnsureArgsCapacity(fishMesh);
        EnsureDrawMaterials();
    }

    private int GetSubMeshCount()
    {
        if (fishMesh == null)
            return 0;
        return Mathf.Max(1, fishMesh.subMeshCount);
    }

    private bool TryValidateFishMaterials(out int subMeshCount)
    {
        subMeshCount = GetSubMeshCount();
        if (subMeshCount == 0)
            return false;

        int materialCount = fishMaterials != null ? fishMaterials.Count : 0;
        if (materialCount != subMeshCount)
        {
            LogMaterialMismatch(
                $"fishMaterials has {materialCount} entries but fishMesh has {subMeshCount} submesh(es). " +
                "Assign one material per submesh in order.");
            return false;
        }

        for (int i = 0; i < materialCount; i++)
        {
            if (fishMaterials[i] != null)
                continue;
            LogMaterialMismatch($"fishMaterials[{i}] is null.");
            return false;
        }

        _materialMismatchLogged = false;
        return true;
    }

    private void LogMaterialMismatch(string message)
    {
        if (_materialMismatchLogged)
            return;
        _materialMismatchLogged = true;
        Debug.LogWarning($"[FishSpawn] {message} Fish drawing disabled until fixed.");
    }

    private void EnsureDrawMaterials()
    {
        if (!TryValidateFishMaterials(out int subMeshCount))
        {
            DestroyDrawMaterials();
            return;
        }

        if (_drawMaterials != null && _drawMaterials.Length == subMeshCount)
            return;

        DestroyDrawMaterials();
        _drawMaterials = new Material[subMeshCount];
        for (int i = 0; i < subMeshCount; i++)
        {
            _drawMaterials[i] = new Material(fishMaterials[i]);
            _drawMaterials[i].enableInstancing = true;
        }
    }

    private void DestroyDrawMaterials()
    {
        if (_drawMaterials == null)
            return;

        for (int i = 0; i < _drawMaterials.Length; i++)
        {
            if (_drawMaterials[i] != null)
                Destroy(_drawMaterials[i]);
        }

        _drawMaterials = null;
    }

    private bool CanDrawFish()
    {
        if (fishMesh == null || _drawMaterials == null || _drawMaterials.Length == 0)
            return false;
        if (!TryValidateFishMaterials(out _))
        {
            DestroyDrawMaterials();
            return false;
        }

        return true;
    }

    private void EnsureArgsCapacity(Mesh mesh)
    {
        int subMeshCount = mesh != null ? Mathf.Max(1, mesh.subMeshCount) : 1;
        if (subMeshCount <= _argsSubMeshCapacity && _argsBuffer != null)
            return;

        _argsSubMeshCapacity = subMeshCount;
        _args = new uint[_argsSubMeshCapacity * ArgsStride];
        _argsBuffer?.Release();
        _argsBuffer = new ComputeBuffer(
            _argsSubMeshCapacity,
            sizeof(uint) * ArgsStride,
            ComputeBufferType.IndirectArguments);
    }

    public void Tick()
    {
        if (_chunkManager == null || _target == null)
            return;

        EnsureDrawMaterials();
        if (!CanDrawFish())
            return;

        DespawnByDistance();
        TrySpawnNewChunksOnce();
        SyncDrawBuffers();

        if (_activeCount == 0)
        {
            LogDrawCount(0);
            return;
        }

        DispatchBoidPlaceholder();
        DrawIndirect();
    }

    private float GetStreamRadius() =>
        ChunkMath.GetDynamicRadius(_target.position, _streamingSettings);

    private float GetCollectRadius() => GetStreamRadius() * 0.8f;

    /// <summary>
    /// How many chunk centers fall inside the fish collect volume (same ellipse as streaming).
    /// Stable at startup — does not depend on loaded chunks or SDF readiness.
    /// </summary>
    private int EstimateChunksInCollectRadius()
    {
        float collectRadius = GetCollectRadius();
        Vector3 chunkSize = _chunkManager.GetChunkSize();
        Vector3Int center = _chunkManager.WorldToChunk(_target.position);
        float minAxis = Mathf.Min(chunkSize.x, Mathf.Min(chunkSize.y, chunkSize.z));
        int maxRange = Mathf.CeilToInt(collectRadius / minAxis);

        int count = 0;
        for (int dx = -maxRange; dx <= maxRange; dx++)
        for (int dy = -maxRange; dy <= maxRange; dy++)
        for (int dz = -maxRange; dz <= maxRange; dz++)
        {
            var c = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
            Vector3 worldCenter = _chunkManager.ChunkCenterWorld(c);
            if (!ChunkMath.IsOutOfRange(_target.position, worldCenter, collectRadius))
                count++;
        }

        return Mathf.Max(1, count);
    }

    private int GetMaxInteriorSpawnPointsPerChunk()
    {
        Vector3Int dims = _chunkManager.GetSdfChunkDims();
        return dims.x * dims.y * dims.z;
    }

    private void DespawnByDistance()
    {
        Vector3 player = _target.position;
        float r = GetStreamRadius() * 0.95f;
        float rSq = r * r;

        for (int i = _fish.Count - 1; i >= 0; i--)
        {
            if ((_fish[i].position - player).sqrMagnitude > rSq)
                _fish.RemoveAt(i);
        }
    }

    private void TrySpawnNewChunksOnce()
    {
        _sortedChunkKeys.Clear();
        float collectRadius = GetCollectRadius();

        foreach (var kvp in _chunkManager.chunks)
        {
            Vector3 chunkCenter = _chunkManager.ChunkCenterWorld(kvp.Key);
            if (ChunkMath.IsOutOfRange(_target.position, chunkCenter, collectRadius))
                continue;
            _sortedChunkKeys.Add(kvp.Key);
        }

        if (_sortedChunkKeys.Count == 0)
            return;

        int quotaPerChunk = maxInstances / EstimateChunksInCollectRadius();
        if (quotaPerChunk <= 0)
            return;

        _sortedChunkKeys.Sort(CompareChunkKeys);

        foreach (var key in _sortedChunkKeys)
        {
            if (_spawnedOnce.Contains(key))
                continue;

            if (!_chunkManager.TryGetChunk(key, out var chunk))
                continue;
            if (chunk.interiorSpawnPositions == null || chunk.interiorSpawnPositions.Count == 0)
                continue;

            if (_fish.Count >= maxInstances)
                continue;

            int actualSpawnPoints = chunk.interiorSpawnPositions.Count;
            int maxSpawnPoints = GetMaxInteriorSpawnPointsPerChunk();
            int scaledQuota = Mathf.RoundToInt(
                quotaPerChunk * (actualSpawnPoints / (float)maxSpawnPoints));
            int toAdd = Mathf.Min(scaledQuota, actualSpawnPoints, maxInstances - _fish.Count);
            if (toAdd > 0)
                TryAddFishFromChunk(key, chunk.interiorSpawnPositions, toAdd);
            _spawnedOnce.Add(key);
        }
    }

    private float GetApproxVoxelSpacing()
    {
        Vector3 chunkSize = _chunkManager.GetChunkSize();
        Vector3Int dims = _chunkManager.GetSdfChunkDims();
        return Mathf.Min(
            chunkSize.x / Mathf.Max(1, dims.x),
            Mathf.Min(chunkSize.y / Mathf.Max(1, dims.y),
                chunkSize.z / Mathf.Max(1, dims.z)));
    }

    private int TryAddFishFromChunk(Vector3Int chunk, List<Vector3> spawns, int totalFish)
    {
        int spawnCount = spawns.Count;
        if (spawnCount == 0 || totalFish <= 0)
            return 0;

        int limit = Mathf.Min(totalFish, maxInstances - _fish.Count);
        if (limit <= 0)
            return 0;

        int strayCount = limit < strayCutoff
            ? limit
            : Mathf.RoundToInt(limit * strayPercent);
        strayCount = Mathf.Clamp(strayCount, 0, limit);
        int schoolFish = limit - strayCount;

        _usedSpawnIndices.Clear();
        int added = 0;

        if (strayCount > 0)
            added += SpawnStrayFish(chunk, spawns, strayCount);

        if (schoolFish > 0)
            added += SpawnSchoolFish(chunk, spawns, schoolFish);

        return added;
    }

    private int SpawnStrayFish(Vector3Int chunk, List<Vector3> spawns, int count)
    {
        int spawnCount = spawns.Count;
        int added = 0;
        int slot = 0;
        int guard = 0;
        int limit = Mathf.Min(count, maxInstances - _fish.Count);

        while (added < limit && guard < limit * 8)
        {
            int idx = DeterministicSpawnIndex(chunk, 9000 + slot, spawnCount);
            slot++;
            guard++;

            if (!_usedSpawnIndices.Add(idx))
                continue;

            _fish.Add(new Fish
            {
                position = spawns[idx],
                sourceChunk = chunk,
                isStray = true
            });
            added++;
        }

        return added;
    }

    private int SpawnSchoolFish(Vector3Int chunk, List<Vector3> spawns, int schoolFish)
    {
        int spawnCount = spawns.Count;
        int room = Mathf.Min(schoolFish, maxInstances - _fish.Count);
        if (room <= 0)
            return 0;

        float minMemberDistSq = Mathf.Pow(GetApproxVoxelSpacing() * schoolMemberSpread, 2f);
        float centerSepSq = schoolCenterSeparation * schoolCenterSeparation;

        PartitionIntoSchoolSizes(chunk, room, _schoolSizes);
        _schoolCenterIndices.Clear();
        int added = 0;

        for (int s = 0; s < _schoolSizes.Count; s++)
        {
            int need = _schoolSizes[s];
            if (need <= 0)
                continue;

            int space = maxInstances - _fish.Count;
            if (space <= 0)
                break;
            need = Mathf.Min(need, space);

            int centerIdx = PickSchoolCenterIndex(chunk, s, spawns, centerSepSq);
            _schoolCenterIndices.Add(centerIdx);

            PickSchoolMemberIndices(spawns, centerIdx, need, minMemberDistSq, _schoolSpawnIndices);

            for (int i = 0; i < _schoolSpawnIndices.Count; i++)
            {
                int idx = _schoolSpawnIndices[i];
                if (!_usedSpawnIndices.Add(idx))
                    continue;

                _fish.Add(new Fish
                {
                    position = spawns[idx],
                    sourceChunk = chunk,
                    isStray = false
                });
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Splits <paramref name="totalFish"/> into schools (~schoolSize each, jittered). Sum equals totalFish.
    /// </summary>
    private void PartitionIntoSchoolSizes(Vector3Int chunk, int totalFish, List<int> sizes)
    {
        sizes.Clear();
        if (totalFish <= 0)
            return;

        int schoolCount = Mathf.Max(1, Mathf.RoundToInt(totalFish / (float)schoolSize));
        int remaining = totalFish;

        for (int i = 0; i < schoolCount; i++)
        {
            int schoolsLeft = schoolCount - i;
            if (schoolsLeft == 1)
            {
                sizes.Add(remaining);
                break;
            }

            float jitter = 0.75f + SchoolHash01(chunk, i) * 0.5f;
            int target = Mathf.RoundToInt(schoolSize * jitter);
            int maxHere = remaining - (schoolsLeft - 1);
            int size = Mathf.Clamp(target, 1, maxHere);
            sizes.Add(size);
            remaining -= size;
        }
    }

    private int PickSchoolCenterIndex(
        Vector3Int chunk,
        int schoolSlot,
        List<Vector3> spawns,
        float minCenterSepSq)
    {
        int spawnCount = spawns.Count;
        int bestIdx = -1;
        float bestSepSq = -1f;

        for (int attempt = 0; attempt < 16; attempt++)
        {
            int idx = DeterministicSpawnIndex(chunk, schoolSlot * 131 + attempt * 17, spawnCount);
            if (_usedSpawnIndices.Contains(idx))
                continue;

            float nearestPriorSq = float.MaxValue;
            for (int p = 0; p < _schoolCenterIndices.Count; p++)
            {
                int prior = _schoolCenterIndices[p];
                float dSq = (spawns[idx] - spawns[prior]).sqrMagnitude;
                if (dSq < nearestPriorSq)
                    nearestPriorSq = dSq;
            }

            if (_schoolCenterIndices.Count == 0 || nearestPriorSq >= minCenterSepSq)
                return idx;

            if (nearestPriorSq > bestSepSq)
            {
                bestSepSq = nearestPriorSq;
                bestIdx = idx;
            }
        }

        if (bestIdx >= 0)
            return bestIdx;

        for (int i = 0; i < spawnCount; i++)
        {
            if (!_usedSpawnIndices.Contains(i))
                return i;
        }

        return DeterministicSpawnIndex(chunk, schoolSlot * 131, spawnCount);
    }

    /// <summary>
    /// Picks up to <paramref name="need"/> spawn points near <paramref name="centerIdx"/>.
    /// Prefers points at least <paramref name="minMemberDistSq"/> from the anchor; fills with closer points if needed.
    /// </summary>
    private void PickSchoolMemberIndices(
        List<Vector3> spawns,
        int centerIdx,
        int need,
        float minMemberDistSq,
        List<int> result)
    {
        result.Clear();
        if (need <= 0 || spawns.Count == 0)
            return;

        centerIdx = Mathf.Clamp(centerIdx, 0, spawns.Count - 1);
        Vector3 center = spawns[centerIdx];

        CollectNearestSpawnIndices(spawns, center, centerIdx, need, minMemberDistSq, enforceMinDist: true, result);
        if (result.Count >= need)
            return;

        int stillNeed = need - result.Count;
        for (int i = 0; i < result.Count; i++)
            _usedSpawnIndices.Add(result[i]);

        CollectNearestSpawnIndices(spawns, center, centerIdx, stillNeed, minMemberDistSq, enforceMinDist: false, _schoolSpawnIndices);
        for (int i = 0; i < _schoolSpawnIndices.Count; i++)
            result.Add(_schoolSpawnIndices[i]);

        for (int i = 0; i < result.Count; i++)
            _usedSpawnIndices.Remove(result[i]);
    }

    private void CollectNearestSpawnIndices(
        List<Vector3> spawns,
        Vector3 center,
        int centerIdx,
        int need,
        float minMemberDistSq,
        bool enforceMinDist,
        List<int> result)
    {
        if (enforceMinDist)
            result.Clear();

        _nearestSpawnScratch.Clear();

        for (int i = 0; i < spawns.Count; i++)
        {
            if (_usedSpawnIndices.Contains(i))
                continue;

            float distSq = (spawns[i] - center).sqrMagnitude;
            if (enforceMinDist && i != centerIdx && distSq < minMemberDistSq)
                continue;

            if (_nearestSpawnScratch.Count < need)
            {
                _nearestSpawnScratch.Add(new SpawnIndexDist { index = i, distSq = distSq });
                continue;
            }

            int worst = 0;
            float worstDist = _nearestSpawnScratch[0].distSq;
            for (int k = 1; k < _nearestSpawnScratch.Count; k++)
            {
                if (_nearestSpawnScratch[k].distSq <= worstDist)
                    continue;
                worstDist = _nearestSpawnScratch[k].distSq;
                worst = k;
            }

            if (distSq < worstDist)
                _nearestSpawnScratch[worst] = new SpawnIndexDist { index = i, distSq = distSq };
        }

        for (int k = 0; k < _nearestSpawnScratch.Count; k++)
            result.Add(_nearestSpawnScratch[k].index);
    }

    private static float SchoolHash01(Vector3Int chunk, int schoolIndex)
    {
        uint h = (uint)(chunk.x * 73856093
                      ^ chunk.y * 19349663
                      ^ chunk.z * 83492791
                      ^ (schoolIndex * 97266353)
                      ^ SelectionSeed);
        h ^= h >> 16;
        h *= 2246822507u;
        h ^= h >> 13;
        return (h & 0x00FFFFFF) / 16777216f;
    }

    private void SyncDrawBuffers()
    {
        _drawPositions.Clear();
        int count = Mathf.Min(_fish.Count, maxInstances);

        for (int i = 0; i < count; i++)
        {
            Vector3 p = _fish[i].position;
            _drawPositions.Add(new Vector4(p.x, p.y, p.z, 1f));
        }

        _activeCount = count;

        Vector3 scale = Vector3.one * fishScale;
        for (int i = 0; i < _activeCount; i++)
        {
            Vector3 p = _drawPositions[i];
            float spin = ChunkMath.Hash(p + Vector3.one * 0.37f) * 360f;
            _matrixScratch[i] = Matrix4x4.TRS(p, Quaternion.Euler(0f, spin, 0f), scale);
        }

        if (_activeCount > 0)
        {
            _matricesBuffer.SetData(_matrixScratch, 0, 0, _activeCount);
            _positionsBuffer.SetData(_drawPositions, 0, 0, _activeCount);
        }
    }

    private static int CompareChunkKeys(Vector3Int a, Vector3Int b)
    {
        int c = a.x.CompareTo(b.x);
        if (c != 0) return c;
        c = a.y.CompareTo(b.y);
        if (c != 0) return c;
        return a.z.CompareTo(b.z);
    }

    private static int DeterministicSpawnIndex(Vector3Int chunk, int slot, int count)
    {
        uint h = (uint)(chunk.x * 73856093
                      ^ chunk.y * 19349663
                      ^ chunk.z * 83492791
                      ^ (slot * 50331653)
                      ^ SelectionSeed);
        h ^= h >> 16;
        h *= 2246822507u;
        h ^= h >> 13;
        return (int)(h % (uint)count);
    }

    private void DispatchBoidPlaceholder()
    {
        if (fishCompute == null || _activeCount == 0) return;

        fishCompute.SetBuffer(_kernelBoid, "_Matrices", _matricesBuffer);
        fishCompute.SetInt("_Count", _activeCount);
        int groups = Mathf.CeilToInt(_activeCount / 64f);
        fishCompute.Dispatch(_kernelBoid, groups, 1, 1);
    }

    private void DrawIndirect()
    {
        if (!CanDrawFish())
            return;

        EnsureArgsCapacity(fishMesh);
        int subMeshCount = _drawMaterials.Length;
        uint instanceCount = (uint)_activeCount;

        for (int s = 0; s < subMeshCount; s++)
        {
            int offset = s * ArgsStride;
            _args[offset] = (uint)fishMesh.GetIndexCount(s);
            _args[offset + 1] = instanceCount;
            _args[offset + 2] = (uint)fishMesh.GetIndexStart(s);
            _args[offset + 3] = (uint)fishMesh.GetBaseVertex(s);
            _args[offset + 4] = 0;
        }

        _argsBuffer.SetData(_args, 0, 0, subMeshCount * ArgsStride);

        var bounds = new Bounds(_target.position, Vector3.one * 10000f);
        for (int s = 0; s < subMeshCount; s++)
        {
            _drawMaterials[s].SetBuffer("_InstanceMatrices", _matricesBuffer);
            Graphics.DrawMeshInstancedIndirect(
                fishMesh,
                s,
                _drawMaterials[s],
                bounds,
                _argsBuffer,
                s * ArgsStride * sizeof(uint));
        }

        LogDrawCount(_activeCount);
    }

    private float GetGizmoRadius()
    {
        float meshExtent = 0.5f;
        if (fishMesh != null)
            meshExtent = Mathf.Max(fishMesh.bounds.extents.magnitude, 0.25f);
        return fishScale * meshExtent * gizmoRadiusMultiplier;
    }

    private void LogDrawCount(int count)
    {
        if (!logDrawCount || Time.time < _nextLogTime) return;
        _nextLogTime = Time.time + logInterval;
        Debug.Log($"[FishSpawn] draw={count} alive={_fish.Count} despawnR={GetStreamRadius() * 0.7f:F0}");
    }

    private void OnDrawGizmos()
    {
        if (!drawSpawnGizmos) return;

        float r = GetGizmoRadius();
        Gizmos.color = Color.cyan;
        int n = Mathf.Min(maxGizmosDrawn, _fish.Count);
        for (int i = 0; i < n; i++)
            Gizmos.DrawWireSphere(_fish[i].position, r);
    }

    private void OnDestroy()
    {
        DestroyDrawMaterials();
        _positionsBuffer?.Release();
        _matricesBuffer?.Release();
        _argsBuffer?.Release();
    }
}
