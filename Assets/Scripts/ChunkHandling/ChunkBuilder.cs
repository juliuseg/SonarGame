using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;

public class ChunkBuilder
{
    private readonly MCBaker _baker;
    private readonly SDFGpu _sdfGen;
    private readonly ChunkManager _chunkManager;
    private readonly ChunkStreamingSettings _chunkStreamingSettings;
    private readonly Material _terrainMaterial;
    private readonly Transform _chunkParent;
    private readonly SDFAtlas _atlas;

    private readonly Dictionary<Vector3Int, BuildState> _buildStates = new();
    private int _inFlightReadbacks;

    private struct PendingCollider
    {
        public MeshCollider Collider;
        public Mesh Mesh;
    }
    private readonly List<PendingCollider> _pendingColliders = new();
    private readonly object _pendingCollidersLock = new();

    public event System.Action<Vector3Int> OnChunkReady;

    public ChunkBuilder(
        MCBaker baker,
        SDFGpu sdfGen,
        ChunkManager chunkManager,
        ChunkStreamingSettings chunkStreamingSettings,
        Material terrainMaterial,
        Transform chunkParent,
        SDFAtlas atlas)
    {
        _baker = baker;
        _sdfGen = sdfGen;
        _chunkManager = chunkManager;
        _chunkStreamingSettings = chunkStreamingSettings;
        _terrainMaterial = terrainMaterial;
        _chunkParent = chunkParent;
        _atlas = atlas;
    }

    // ---- Public API ----

    /// <summary>
    /// Starts mesh/SDF readbacks when budget allows. Returns true once all required readbacks are queued.
    /// </summary>
    public bool TrySubmitChunkBuild(Vector3Int coord)
    {
        var chunk = GetOrCreateChunk(coord);
        if (!_buildStates.TryGetValue(coord, out var bstate))
        {
            bstate = RegisterBuildState(coord);
            EnsureGameObjectExists(coord, chunk);
        }

        Vector3 centerWorld = _chunkManager.ChunkCenterWorld(coord);
        float chunkHalfHeight = _chunkManager.GetChunkSize().y * 0.5f;
        bool underwater = centerWorld.y - chunkHalfHeight < _chunkStreamingSettings.waterLevel;

        if (underwater)
        {
            if (!bstate.MeshReadbackQueued)
                TryQueueMeshReadback(coord, chunk, bstate, centerWorld);
        }
        else if (!bstate.MeshDone)
        {
            HandleAboveWaterChunk(coord, chunk, bstate);
        }

        if (!bstate.SdfReadbackQueued)
            TryQueueSdfReadback(coord, bstate, centerWorld);

        bool meshReady = bstate.MeshDone || bstate.MeshReadbackQueued;
        return bstate.SdfReadbackQueued && meshReady;
    }

    private bool TryBeginReadback()
    {
        if (_inFlightReadbacks >= _chunkStreamingSettings.maxAsyncReadbacks)
            return false;
        _inFlightReadbacks++;
        return true;
    }

    private void EndReadback() => _inFlightReadbacks = Mathf.Max(0, _inFlightReadbacks - 1);

    public void CancelBuild(Vector3Int coord) => _buildStates.Remove(coord);

    private bool IsBuildStillValid(Vector3Int coord, BuildState bstate) =>
        _buildStates.TryGetValue(coord, out var st) && ReferenceEquals(st, bstate)
        && _chunkManager.TryGetChunk(coord, out _);

    public void FlushPendingColliders()
    {
        lock (_pendingCollidersLock)
        {
            foreach (var pending in _pendingColliders)
            {
                if (pending.Collider != null && !pending.Collider.Equals(null))
                    pending.Collider.sharedMesh = pending.Mesh;
            }
            _pendingColliders.Clear();
        }
    }

    // ---- Chunk setup ----

    private Chunk GetOrCreateChunk(Vector3Int coord)
    {
        _chunkManager.TryGetChunk(coord, out var chunk);

        if (chunk == null)
        {
            chunk = new Chunk(null, null, null, null, 100);
            _chunkManager.SetChunk(coord, chunk);
        }

        if (_chunkManager.TryGetOffloadedEdits(coord, true, out var edits))
            chunk.terraformEdits = edits;

        return chunk;
    }

    private BuildState RegisterBuildState(Vector3Int coord)
    {
        var bstate = new BuildState();
        _buildStates[coord] = bstate;
        return bstate;
    }

    private void EnsureGameObjectExists(Vector3Int coord, Chunk chunk)
    {
        if (chunk.gameObject != null && !chunk.gameObject.Equals(null)) return;

        var go = new GameObject($"Chunk_{coord.x}_{coord.y}_{coord.z}");
        go.transform.SetParent(_chunkParent, false);
        go.transform.localPosition = Vector3.zero;
        chunk.gameObject = go;
        _chunkManager.SetChunk(coord, chunk);
    }

    // ---- Mesh generation ----

    private void TryQueueMeshReadback(Vector3Int coord, Chunk chunk, BuildState bstate, Vector3 centerWorld)
    {
        _baker.RunAsync(
            centerWorld,
            chunk.terraformEdits,
            (mesh, mask, spawnPoints, elapsed) =>
            {
                if (!_buildStates.TryGetValue(coord, out var st) || !ReferenceEquals(st, bstate)) return;
                st.MeshDone = true;

                if (!_chunkManager.TryGetChunk(coord, out var c) || c == null) return;
                if (c.gameObject == null || c.gameObject.Equals(null)) return;

                c.spawnPoints = FilterSpawnPointsBelowWater(spawnPoints);
                c.biomeMask = mask;

                ApplyMeshToGameObject(c.gameObject, mesh);

                _chunkManager.SetChunk(coord, c);
                TryFinalizeChunk(coord, st);
            },
            TryBeginReadback,
            EndReadback,
            onReadbackQueued: () => bstate.MeshReadbackQueued = true,
            isBuildStillValid: () => IsBuildStillValid(coord, bstate));
    }

    private void TryQueueSdfReadback(Vector3Int coord, BuildState bstate, Vector3 centerWorld)
    {
        _sdfGen.GenerateAsync(
            centerWorld,
            (data, buffer, state) =>
            {
                var sdfCoord = (Vector3Int)state;
                if (!_buildStates.TryGetValue(sdfCoord, out var st) || !ReferenceEquals(st, bstate))
                {
                    buffer?.Release();
                    return;
                }

                st.SdfDone = true;

                if (!_chunkManager.TryGetChunk(sdfCoord, out var chunk))
                {
                    buffer?.Release();
                    return;
                }

                if (data == null || buffer == null)
                {
                    buffer?.Release();
                    Debug.LogWarning($"SDF generation failed for chunk: {sdfCoord}");
                    return;
                }

                Profiler.BeginSample("Chunk.SDF.Apply");
                try
                {
                    chunk.sdfData = data;
                    chunk.sdfDims = _chunkManager.GetSdfChunkDims();

                    Vector3 chunkSize = _chunkManager.GetChunkSize();
                    Vector3Int dims = chunk.sdfDims;
                    Vector3 voxelScale = new Vector3(
                        chunkSize.x / dims.x,
                        chunkSize.y / dims.y,
                        chunkSize.z / dims.z);

                    chunk.interiorSpawnPositions = new List<Vector3>();
                    PopulateInteriorSpawnPositions(
                        chunk,
                        centerWorld,
                        voxelScale,
                        _chunkStreamingSettings.interiorSdfThreshold);

                    if (_atlas != null)
                        chunk.sdfSlotIndex = _atlas.AllocateSlot(sdfCoord, chunk.sdfData);

                    _chunkManager.SetChunk(sdfCoord, chunk);
                    buffer.Release();
                    TryFinalizeChunk(sdfCoord, st);
                }
                finally
                {
                    Profiler.EndSample();
                }
            },
            userState: coord,
            TryBeginReadback,
            EndReadback,
            onReadbackQueued: () => bstate.SdfReadbackQueued = true,
            isBuildStillValid: () => IsBuildStillValid(coord, bstate));
    }

    private void HandleAboveWaterChunk(Vector3Int coord, Chunk chunk, BuildState bstate)
    {
        if (chunk.gameObject == null)
        {
            chunk.gameObject = new GameObject($"Chunk_Empty_{coord.x}_{coord.y}_{coord.z}");
            chunk.gameObject.transform.SetParent(_chunkParent);
        }

        _chunkManager.SetChunk(coord, chunk);
        bstate.MeshDone = true;
        TryFinalizeChunk(coord, bstate);
    }

    private void ApplyMeshToGameObject(GameObject go, Mesh mesh)
    {
        var mf = go.GetComponent<MeshFilter>();
        var mc = go.GetComponent<MeshCollider>();
        var mr = go.GetComponent<MeshRenderer>();

        if (mf == null) mf = go.AddComponent<MeshFilter>();
        if (mc == null) mc = go.AddComponent<MeshCollider>();
        if (mr == null) mr = go.AddComponent<MeshRenderer>();

        if (mf == null || mc == null || mr == null) return;

        mf.sharedMesh = mesh;
        mr.sharedMaterial = _terrainMaterial;

        if (mesh != null)
            BakeColliderAsync(mc, mesh);
    }

    private void BakeColliderAsync(MeshCollider mc, Mesh mesh)
    {
        int meshId = mesh.GetInstanceID();
        var capturedMc = mc;
        var capturedMesh = mesh;

        Task.Run(() =>
        {
            Physics.BakeMesh(meshId, false);
            lock (_pendingCollidersLock)
            {
                _pendingColliders.Add(new PendingCollider
                {
                    Collider = capturedMc,
                    Mesh = capturedMesh
                });
            }
        });
    }

    // ---- Finalization ----

    private void TryFinalizeChunk(Vector3Int coord, BuildState state)
    {
        if (!_buildStates.TryGetValue(coord, out var current)) return;
        if (!ReferenceEquals(current, state)) return;

        if (state.MeshDone && state.SdfDone)
        {
            _buildStates.Remove(coord);
            OnChunkReady?.Invoke(coord);
        }
    }

    private class BuildState
    {
        public bool MeshReadbackQueued;
        public bool SdfReadbackQueued;
        public bool MeshDone;
        public bool SdfDone;
    }
    
    private void PopulateInteriorSpawnPositions(
        Chunk chunk,
        Vector3 chunkCenter,
        Vector3 voxelScale,
        float interiorSdfThreshold)
    {
        float waterLevel = _chunkStreamingSettings.waterLevel;
        int sx = chunk.sdfDims.x;
        int sy = chunk.sdfDims.y;
        int sz = chunk.sdfDims.z;

        for (int x = 0; x < sx; x++)
        for (int y = 0; y < sy; y++)
        for (int z = 0; z < sz; z++)
        {
            if (chunk.GetVoxel(x, y, z) <= interiorSdfThreshold)
                continue;

            Vector3 worldPos = chunkCenter + new Vector3(
                (x - sx * 0.5f + 0.5f) * voxelScale.x,
                (y - sy * 0.5f + 0.5f) * voxelScale.y,
                (z - sz * 0.5f + 0.5f) * voxelScale.z);

            if (worldPos.y > waterLevel)
                continue;

            chunk.interiorSpawnPositions.Add(worldPos);
        }
    }

    private List<SpawnPoint> FilterSpawnPointsBelowWater(List<SpawnPoint> points)
    {
        if (points == null || points.Count == 0)
            return points;

        float waterLevel = _chunkStreamingSettings.waterLevel;
        var filtered = new List<SpawnPoint>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].positionWS.y <= waterLevel)
                filtered.Add(points[i]);
        }

        return filtered;
    }
}