using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class ChunkBuilder
{
    private readonly MCBaker _baker;
    private readonly SDFGpu _sdfGen;
    private readonly ChunkManager _chunkManager;
    private readonly ChunkSettings _chunkSettings;
    private readonly Material _terrainMaterial;
    private readonly Transform _chunkParent;

    private readonly Dictionary<Vector3Int, BuildState> _buildStates = new();

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
        ChunkSettings chunkSettings,
        Material terrainMaterial,
        Transform chunkParent)
    {
        _baker = baker;
        _sdfGen = sdfGen;
        _chunkManager = chunkManager;
        _chunkSettings = chunkSettings;
        _terrainMaterial = terrainMaterial;
        _chunkParent = chunkParent;
    }

    // ---- Public API ----

    public void BuildOne(Vector3Int coord)
    {
        var chunk = GetOrCreateChunk(coord);
        var bstate = RegisterBuildState(coord);
        EnsureGameObjectExists(coord, chunk);

        Vector3 centerWorld = _chunkManager.ChunkCenterWorld(coord);
        float chunkHalfHeight = _chunkManager.GetChunkSize().y * 0.5f;

        if (centerWorld.y - chunkHalfHeight < _chunkSettings.waterLevel)
            StartMeshGeneration(coord, chunk, bstate, centerWorld);
        else
            HandleAboveWaterChunk(coord, chunk, bstate);

        StartSDFGeneration(coord, bstate, centerWorld);
    }

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

    private void StartMeshGeneration(Vector3Int coord, Chunk chunk, BuildState bstate, Vector3 centerWorld)
    {
        _baker.RunAsync(centerWorld, chunk.terraformEdits, (mesh, mask, spawnPoints, elapsed) =>
        {
            if (!_buildStates.TryGetValue(coord, out var st) || !ReferenceEquals(st, bstate)) return;
            st.MeshDone = true;

            if (!_chunkManager.TryGetChunk(coord, out var c) || c == null) return;
            if (c.gameObject == null || c.gameObject.Equals(null)) return;

            c.spawnPoints = spawnPoints;
            c.biomeMask = mask;

            ApplyMeshToGameObject(c.gameObject, mesh);

            _chunkManager.SetChunk(coord, c);
            TryFinalizeChunk(coord, st);
        });
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

    // ---- SDF generation ----

    private void StartSDFGeneration(Vector3Int coord, BuildState bstate, Vector3 centerWorld)
    {
        _sdfGen.GenerateAsync(centerWorld, (data, buffer, state) =>
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

            chunk.sdfData = data;
            chunk.sdfBuffer = buffer;
            _chunkManager.SetChunk(sdfCoord, chunk);

            TryFinalizeChunk(sdfCoord, st);
        }, userState: coord);
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
        public bool MeshDone;
        public bool SdfDone;
    }
}