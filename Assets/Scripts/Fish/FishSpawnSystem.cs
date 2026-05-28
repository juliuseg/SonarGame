using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fixed-size GPU boid pool (maxInstances). Simulation, spawn, despawn, and draw
/// matrices all run on GPU — no simulation readback. CPU builds school spawn lists
/// when a chunk enters collect range; GPU builder fills unloaded slots.
/// </summary>
public class FishSpawnSystem : MonoBehaviour
{
    const int SelectionSeed = 12345;
    const int ArgsStride = 5;
    static readonly Vector3 ParkedPosition = new(0f, 100f, 0f);

    [Header("Collect and despawn")]
    [SerializeField] private float collectParam;
    [SerializeField] private float despawnParam;

    [Header("Draw")]
    public Mesh fishMesh;
    [Tooltip("One material per submesh, same order as the mesh (0 = first submesh).")]
    public List<Material> fishMaterials = new();
    public ComputeShader fishCompute;

    [Header("Mesh orientation (draw)")]
    [Tooltip("World 'up' hint when aligning mesh to swim velocity (roll around forward axis).")]
    public Vector3 meshSwimUp = Vector3.up;
    [Tooltip("Euler fix if imported mesh forward/up don't match Unity (+Z forward, +Y up).")]
    public Vector3 meshOrientationOffset;

    [Header("Counts")]
    [Min(1)] public int maxInstances = 5000;
    [Min(0.01f)] public float fishScale = 1f;

    [Header("Movement")]
    [Min(0f)] public float swimSpeed = 2f;
    [Min(0f)] public float minSwimSpeed = 0.5f;
    [Min(0f)] public float maxSteerForce = 4f;
    [Tooltip("Max speed change per second under normal swimming.")]
    [Min(0.01f)] public float speedAccel = 3f;

    [Header("Boids")]
    [Min(0.1f)] public float boidNeighborRadius = 8f;
    [Min(0.1f)] public float boidSeparationRadius = 3f;
    [Min(0f)] public float weightSeparation = 1.5f;
    [Min(0f)] public float weightAlignment = 1f;
    [Min(0f)] public float weightCohesion = 0.8f;

    [Header("SDF Avoidance")]
    [Tooltip("Start steering away when SDF is below this (world units).")]
    [Min(0.01f)] public float sdfAvoidRadius = 2f;
    [Min(0f)] public float sdfAvoidWeight = 12f;
    [Min(0.01f)] public float sdfGradientDelta = 0.5f;
    [Tooltip("Extra steer multiplier when sdf < 0 (inside solid).")]
    [Min(1f)] public float sdfPenetrationForceMult = 4f;
    [Tooltip("Min sdf after correction — fish are pushed out to at least this.")]
    [Min(0.01f)] public float sdfMinClearance = 0.15f;
    [Tooltip("Slow down when velocity points into the wall within this sdf range.")]
    [Min(0.01f)] public float sdfWallBrakeRadius = 2.5f;
    [Range(0f, 1f)] public float sdfWallBrakeStrength = 0.85f;
    [Range(0f, 1f)] public float sdfThreatSuppress = 0.95f;

    [Header("Threat Avoidance")]
    [Tooltip("Player, large enemies, etc. Fish steer away when within radius.")]
    public List<Transform> threatTransforms = new();
    [Min(1)] public int maxThreats = 16;
    [Min(0.1f)] public float threatAvoidRadius = 10f;
    [Min(0f)] public float threatAvoidWeight = 18f;
    [Tooltip("Inside this radius fish panic and can burst to high speed.")]
    [Min(0.1f)] public float threatPanicRadius = 6f;
    [Min(1f)] public float panicSpeedMultiplier = 2.5f;
    [Tooltip("How fast speed can rise during panic (units/s per second).")]
    [Min(0.01f)] public float panicAccel = 24f;
    [Tooltip("Keeps elevated speed briefly after leaving panic radius.")]
    [Min(0f)] public float panicBoostDuration = 1.5f;

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
    public bool turnOff;

    private ChunkManager _chunkManager;
    private ChunkStreamingSettings _streamingSettings;
    private SDFAtlas _sdfAtlas;
    private MCSettings _mcSettings;
    private Transform _target;
    private Vector3 _chunkSizeWorld;

    private Material[] _drawMaterials;
    private bool _materialMismatchLogged;

    private readonly HashSet<Vector3Int> _chunksInCollectRange = new();
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

    private ComputeBuffer _boidStateBuffer;
    private ComputeBuffer _positionsBuffer;
    private ComputeBuffer _velocitiesBuffer;
    private ComputeBuffer _matricesBuffer;
    private ComputeBuffer _spawnPosBuffer;
    private ComputeBuffer _spawnVelBuffer;
    private ComputeBuffer _spawnCounterBuffer;
    private ComputeBuffer _threatBuffer;
    private ComputeBuffer _argsBuffer;

    private Vector4[] _spawnPosScratch;
    private Vector4[] _spawnVelScratch;
    private Vector4[] _threatScratch;
    private int[] _spawnCounterScratch;

    private int _kernelBoid;
    private int _kernelBuilder;
    private int _kernelMatrices;
    private uint[] _args;
    private int _argsSubMeshCapacity;
    private float _nextLogTime;
    private bool _gpuInitialized;

    public void Init(
        ChunkManager chunkManager,
        ChunkStreamingSettings streamingSettings,
        Transform target,
        SDFAtlas sdfAtlas,
        MCSettings mcSettings)
    {
        if (turnOff) return;
        _chunkManager = chunkManager;
        _streamingSettings = streamingSettings;
        _sdfAtlas = sdfAtlas;
        _mcSettings = mcSettings;
        _target = target;
        _chunkSizeWorld = mcSettings != null
            ? Vector3.Scale(mcSettings.scale, mcSettings.chunkDims)
            : _chunkManager.GetChunkSize();

        if (fishCompute != null)
        {
            _kernelBoid = fishCompute.FindKernel("BoidUpdate");
            _kernelBuilder = fishCompute.FindKernel("FishBuilder");
            _kernelMatrices = fishCompute.FindKernel("BuildMatrices");
        }

        _spawnPosScratch = new Vector4[maxInstances];
        _spawnVelScratch = new Vector4[maxInstances];
        _spawnCounterScratch = new int[1];

        int threatCapacity = Mathf.Max(1, maxThreats);
        _threatScratch = new Vector4[threatCapacity];

        _boidStateBuffer = new ComputeBuffer(maxInstances, sizeof(uint));
        _positionsBuffer = new ComputeBuffer(maxInstances, sizeof(float) * 4);
        _velocitiesBuffer = new ComputeBuffer(maxInstances, sizeof(float) * 4);
        _matricesBuffer = new ComputeBuffer(maxInstances, sizeof(float) * 16);
        _spawnPosBuffer = new ComputeBuffer(maxInstances, sizeof(float) * 4);
        _spawnVelBuffer = new ComputeBuffer(maxInstances, sizeof(float) * 4);
        _spawnCounterBuffer = new ComputeBuffer(1, sizeof(int));
        _threatBuffer = new ComputeBuffer(threatCapacity, sizeof(float) * 4);

        InitializeGpuPool();
        EnsureArgsCapacity(fishMesh);
        EnsureDrawMaterials();
        _gpuInitialized = fishCompute != null;
    }

    private void InitializeGpuPool()
    {
        var states = new uint[maxInstances];
        var positions = new Vector4[maxInstances];
        var parked = new Vector4(ParkedPosition.x, ParkedPosition.y, ParkedPosition.z, 0f);

        for (int i = 0; i < maxInstances; i++)
            positions[i] = parked;

        _boidStateBuffer.SetData(states);
        _positionsBuffer.SetData(positions);
        _velocitiesBuffer.SetData(new Vector4[maxInstances]);
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
        if (turnOff) return;
        if (_chunkManager == null || _target == null)
            return;

        EnsureDrawMaterials();
        if (!CanDrawFish() || !_gpuInitialized)
            return;

        TrySpawnChunksEnteringRange();
        DispatchBoidUpdate();
        DispatchBuildMatrices();
        DrawIndirect();
    }

    private float GetStreamRadius() =>
        ChunkMath.GetDynamicRadius(_target.position, _streamingSettings);

    private float GetCollectRadius() => GetStreamRadius() * collectParam;

    private float GetDespawnRadius() => GetStreamRadius() * despawnParam;

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

    /// <summary>
    /// When a chunk enters collect range, build spawn data on CPU and dispatch GPU builder.
    /// Leaving range removes the chunk from the set so it can respawn on re-entry.
    /// </summary>
    private void TrySpawnChunksEnteringRange()
    {
        _sortedChunkKeys.Clear();
        float collectRadius = GetCollectRadius();

        foreach (var kvp in _chunkManager.chunks)
        {
            Vector3 chunkCenter = _chunkManager.ChunkCenterWorld(kvp.Key);
            if (ChunkMath.IsOutOfRange(_target.position, chunkCenter, collectRadius))
            {
                _chunksInCollectRange.Remove(kvp.Key);
                continue;
            }

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
            if (_chunksInCollectRange.Contains(key))
                continue;

            if (!_chunkManager.TryGetChunk(key, out var chunk))
                continue;
            if (chunk.interiorSpawnPositions == null || chunk.interiorSpawnPositions.Count == 0)
                continue;

            _chunksInCollectRange.Add(key);

            int actualSpawnPoints = chunk.interiorSpawnPositions.Count;
            int maxSpawnPoints = GetMaxInteriorSpawnPointsPerChunk();
            int scaledQuota = Mathf.RoundToInt(
                quotaPerChunk * (actualSpawnPoints / (float)maxSpawnPoints));
            int toAdd = Mathf.Min(scaledQuota, actualSpawnPoints);
            if (toAdd > 0)
                DispatchBuilderForChunk(key, chunk.interiorSpawnPositions, toAdd);
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

    private void DispatchBuilderForChunk(Vector3Int chunk, List<Vector3> spawns, int totalFish)
    {
        int spawnCount = BuildSpawnListFromChunk(chunk, spawns, totalFish);
        if (spawnCount <= 0)
            return;

        _spawnPosBuffer.SetData(_spawnPosScratch, 0, 0, spawnCount);
        _spawnVelBuffer.SetData(_spawnVelScratch, 0, 0, spawnCount);

        _spawnCounterScratch[0] = spawnCount;
        _spawnCounterBuffer.SetData(_spawnCounterScratch);

        BindSimulationBuffers(_kernelBuilder);
        fishCompute.SetBuffer(_kernelBuilder, "_SpawnPositions", _spawnPosBuffer);
        fishCompute.SetBuffer(_kernelBuilder, "_SpawnVelocities", _spawnVelBuffer);
        fishCompute.SetBuffer(_kernelBuilder, "_SpawnRemaining", _spawnCounterBuffer);
        fishCompute.SetInt("_Count", maxInstances);

        int groups = Mathf.CeilToInt(maxInstances / 64f);
        fishCompute.Dispatch(_kernelBuilder, groups, 1, 1);
    }

    private int BuildSpawnListFromChunk(Vector3Int chunk, List<Vector3> spawns, int totalFish)
    {
        int spawnPointCount = spawns.Count;
        if (spawnPointCount == 0 || totalFish <= 0)
            return 0;

        int limit = Mathf.Min(totalFish, maxInstances);
        if (limit <= 0)
            return 0;

        int strayCount = limit < strayCutoff
            ? limit
            : Mathf.RoundToInt(limit * strayPercent);
        strayCount = Mathf.Clamp(strayCount, 0, limit);
        int schoolFish = limit - strayCount;

        _usedSpawnIndices.Clear();
        int writeIndex = 0;

        if (strayCount > 0)
            writeIndex += AppendStraySpawns(chunk, spawns, strayCount, writeIndex);

        if (schoolFish > 0)
            writeIndex += AppendSchoolSpawns(chunk, spawns, schoolFish, writeIndex);

        return writeIndex;
    }

    private int AppendStraySpawns(Vector3Int chunk, List<Vector3> spawns, int count, int writeIndex)
    {
        int spawnCount = spawns.Count;
        int added = 0;
        int slot = 0;
        int guard = 0;

        while (added < count && guard < count * 8)
        {
            int idx = DeterministicSpawnIndex(chunk, 9000 + slot, spawnCount);
            slot++;
            guard++;

            if (!_usedSpawnIndices.Add(idx))
                continue;

            Vector3 p = spawns[idx];
            Vector3 v = RandomSwimVelocity(chunk, 9000 + slot);
            _spawnPosScratch[writeIndex] = new Vector4(p.x, p.y, p.z, 1f);
            _spawnVelScratch[writeIndex] = new Vector4(v.x, v.y, v.z, 0f);
            writeIndex++;
            added++;
        }

        return added;
    }

    private int AppendSchoolSpawns(Vector3Int chunk, List<Vector3> spawns, int schoolFish, int writeIndex)
    {
        if (schoolFish <= 0)
            return 0;

        float minMemberDistSq = Mathf.Pow(GetApproxVoxelSpacing() * schoolMemberSpread, 2f);
        float centerSepSq = schoolCenterSeparation * schoolCenterSeparation;

        PartitionIntoSchoolSizes(chunk, schoolFish, _schoolSizes);
        _schoolCenterIndices.Clear();
        int added = 0;
        int startWriteIndex = writeIndex;

        for (int s = 0; s < _schoolSizes.Count; s++)
        {
            int need = _schoolSizes[s];
            if (need <= 0)
                continue;

            need = Mathf.Min(need, schoolFish - added);
            if (need <= 0)
                break;

            int centerIdx = PickSchoolCenterIndex(chunk, s, spawns, centerSepSq);
            _schoolCenterIndices.Add(centerIdx);

            PickSchoolMemberIndices(spawns, centerIdx, need, minMemberDistSq, _schoolSpawnIndices);
            Vector3 schoolVelocity = RandomSwimVelocity(chunk, s * 31 + 7);

            for (int i = 0; i < _schoolSpawnIndices.Count; i++)
            {
                int idx = _schoolSpawnIndices[i];
                if (!_usedSpawnIndices.Add(idx))
                    continue;

                Vector3 p = spawns[idx];
                _spawnPosScratch[writeIndex] = new Vector4(p.x, p.y, p.z, 1f);
                _spawnVelScratch[writeIndex] = new Vector4(
                    schoolVelocity.x, schoolVelocity.y, schoolVelocity.z, 0f);
                writeIndex++;
                added++;
            }
        }

        return writeIndex - startWriteIndex;
    }

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

    private Vector3 RandomSwimVelocity(Vector3Int chunk, int seed)
    {
        float u = SchoolHash01(chunk, seed * 3 + 11);
        float v = SchoolHash01(chunk, seed * 3 + 22);
        float yaw = u * Mathf.PI * 2f;
        float pitch = (v - 0.5f) * 0.5f;
        float cp = Mathf.Cos(pitch);
        var dir = new Vector3(Mathf.Sin(yaw) * cp, Mathf.Sin(pitch), Mathf.Cos(yaw) * cp);
        if (dir.sqrMagnitude < 1e-8f)
            dir = Vector3.forward;
        return dir.normalized * swimSpeed;
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

    private void BindSimulationBuffers(int kernel)
    {
        fishCompute.SetBuffer(kernel, "_BoidState", _boidStateBuffer);
        fishCompute.SetBuffer(kernel, "_Positions", _positionsBuffer);
        fishCompute.SetBuffer(kernel, "_Velocities", _velocitiesBuffer);
    }

    private void BindSdfAndThreat(int kernel)
    {
        bool useAtlas = _sdfAtlas != null && _mcSettings != null;
        if (useAtlas)
            _sdfAtlas.FlushLookup();

        fishCompute.SetInt("_UseSdfAtlas", useAtlas ? 1 : 0);
        fishCompute.SetFloat("_SdfAvoidRadius", sdfAvoidRadius);
        fishCompute.SetFloat("_SdfAvoidWeight", sdfAvoidWeight);
        fishCompute.SetFloat("_SdfGradientDelta", sdfGradientDelta);
        fishCompute.SetFloat("_SdfPenetrationForceMult", sdfPenetrationForceMult);
        fishCompute.SetFloat("_SdfMinClearance", sdfMinClearance);
        fishCompute.SetFloat("_SdfWallBrakeRadius", sdfWallBrakeRadius);
        fishCompute.SetFloat("_SdfWallBrakeStrength", sdfWallBrakeStrength);
        fishCompute.SetFloat("_SdfThreatSuppress", sdfThreatSuppress);

        int threatCount = UploadThreatPositions();
        fishCompute.SetBuffer(kernel, "_ThreatPositions", _threatBuffer);
        fishCompute.SetInt("_ThreatCount", threatCount);
        fishCompute.SetFloat("_ThreatAvoidRadius", threatAvoidRadius);
        fishCompute.SetFloat("_ThreatAvoidWeight", threatAvoidWeight);
        fishCompute.SetFloat("_ThreatPanicRadius", threatPanicRadius);
        fishCompute.SetFloat("_SpeedAccel", speedAccel);
        fishCompute.SetFloat("_PanicSpeedMult", panicSpeedMultiplier);
        fishCompute.SetFloat("_PanicAccel", panicAccel);
        fishCompute.SetFloat("_PanicBoostDuration", panicBoostDuration);

        if (!useAtlas)
            return;

        fishCompute.SetBuffer(kernel, "_Lookup", _sdfAtlas.LookupBuffer);
        fishCompute.SetBuffer(kernel, "_Atlas", _sdfAtlas.AtlasBuffer);
        fishCompute.SetVector("_ChunkSize", _chunkSizeWorld);
        fishCompute.SetVector("_Scale", _mcSettings.scale);
        fishCompute.SetInts("_ChunkDims",
            _mcSettings.chunkDims.x,
            _mcSettings.chunkDims.y,
            _mcSettings.chunkDims.z);
        fishCompute.SetInt("_SlotSize", _sdfAtlas.SlotSize);
        fishCompute.SetInt("_LookupCount", _sdfAtlas.MaxSlots);
    }

    private int UploadThreatPositions()
    {
        if (threatTransforms == null || _threatScratch == null)
            return 0;

        int capacity = _threatScratch.Length;
        int count = 0;
        for (int i = 0; i < threatTransforms.Count && count < capacity; i++)
        {
            Transform t = threatTransforms[i];
            if (t == null)
                continue;
            Vector3 p = t.position;
            _threatScratch[count] = new Vector4(p.x, p.y, p.z, 0f);
            count++;
        }

        if (count > 0)
            _threatBuffer.SetData(_threatScratch, 0, 0, count);

        return count;
    }

    private void DispatchBoidUpdate()
    {
        if (fishCompute == null)
            return;

        BindSimulationBuffers(_kernelBoid);
        BindSdfAndThreat(_kernelBoid);

        fishCompute.SetInt("_Count", maxInstances);
        fishCompute.SetFloat("_DeltaTime", Time.deltaTime);
        fishCompute.SetVector("_PlayerPosition", _target.position);
        fishCompute.SetFloat("_DespawnRadius", GetDespawnRadius());
        fishCompute.SetFloat("_NeighborRadius", boidNeighborRadius);
        fishCompute.SetFloat("_SeparationRadius", boidSeparationRadius);
        fishCompute.SetFloat("_WeightSeparation", weightSeparation);
        fishCompute.SetFloat("_WeightAlignment", weightAlignment);
        fishCompute.SetFloat("_WeightCohesion", weightCohesion);
        fishCompute.SetFloat("_MaxSpeed", swimSpeed);
        fishCompute.SetFloat("_MinSpeed", Mathf.Min(minSwimSpeed, swimSpeed));
        fishCompute.SetFloat("_MaxSteerForce", maxSteerForce);

        int groups = Mathf.CeilToInt(maxInstances / 64f);
        fishCompute.Dispatch(_kernelBoid, groups, 1, 1);
    }

    private void DispatchBuildMatrices()
    {
        if (fishCompute == null)
            return;

        BindSimulationBuffers(_kernelMatrices);
        fishCompute.SetBuffer(_kernelMatrices, "_InstanceMatrices", _matricesBuffer);
        fishCompute.SetInt("_Count", maxInstances);
        fishCompute.SetFloat("_FishScale", fishScale);
        fishCompute.SetVector("_MeshSwimUp", meshSwimUp);

        Quaternion meshFix = Quaternion.Euler(meshOrientationOffset);
        fishCompute.SetVector("_MeshOrientationQuat", new Vector4(
            meshFix.x, meshFix.y, meshFix.z, meshFix.w));

        int groups = Mathf.CeilToInt(maxInstances / 64f);
        fishCompute.Dispatch(_kernelMatrices, groups, 1, 1);
    }

    private void DrawIndirect()
    {
        if (!CanDrawFish())
            return;

        EnsureArgsCapacity(fishMesh);
        int subMeshCount = _drawMaterials.Length;
        uint instanceCount = (uint)maxInstances;

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

        LogDrawCount(maxInstances);
    }

    private void LogDrawCount(int count)
    {
        if (!logDrawCount || Time.time < _nextLogTime) return;
        _nextLogTime = Time.time + logInterval;
        // Debug.Log($"[FishSpawn] draw={count} pool={maxInstances}");
    }

    private void OnDestroy()
    {
        DestroyDrawMaterials();
        _boidStateBuffer?.Release();
        _positionsBuffer?.Release();
        _velocitiesBuffer?.Release();
        _matricesBuffer?.Release();
        _spawnPosBuffer?.Release();
        _spawnVelBuffer?.Release();
        _spawnCounterBuffer?.Release();
        _threatBuffer?.Release();
        _argsBuffer?.Release();
    }
}
