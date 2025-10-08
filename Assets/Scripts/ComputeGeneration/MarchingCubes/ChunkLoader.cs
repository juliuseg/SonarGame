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



    private readonly Dictionary<Vector3Int, GameObject> _chunks = new();
    private readonly Queue<Vector3Int> _buildQueue = new();
    private Vector3Int _lastCenter = new(int.MinValue, int.MinValue, int.MinValue);

    void Awake()
    {
        if (baker == null) baker = GetComponent<MCBaker>();
        if (target == null) target = transform;

        
    }

    void Start()
    {
        
    }

    void Update()
    {
        if (baker == null || baker.settings == null) return;

        // Check for reload button press
        if (reloadTerrain)
        {
            ReloadAllTerrain();
            reloadTerrain = false; // turn off after triggering
            return;
        }

        Vector3 chunkSize = GetChunkSize();
        Vector3Int center = WorldToChunk(target.position, chunkSize);

        float currentRadius = GetDynamicRadius();

        // Always evaluate needed and far chunks
        EnqueueNeeded(center, chunkSize, currentRadius);
        UnloadFar(center, chunkSize, currentRadius);

        BuildQueued(chunkSize, currentRadius);

        _lastCenter = center; // optional bookkeeping
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

            Vector3 worldCenter = ChunkCenterWorld(c, chunkSize);
            float dist = Vector3.Distance(worldCenter, target.position);

            // check vertical cutoff
            float chunkHalfHeight = chunkSize.y * 0.5f;
            if (worldCenter.y - chunkHalfHeight > waterLevel)
                continue;

            if (dist > radius) continue;
            if (_chunks.ContainsKey(c)) continue;
            if (_buildQueue.Contains(c)) continue;

            


            _buildQueue.Enqueue(c);
        }
    }

    private void BuildQueued(Vector3 chunkSize, float radius)
    {
        int built = 0;

        while (_buildQueue.Count > 0 && built < maxBuildsPerFrame)
        {
            var coord = _buildQueue.Dequeue();

            // Skip if already loaded
            if (_chunks.ContainsKey(coord))
                continue;

            Vector3 centerWorld = ChunkCenterWorld(coord, chunkSize);

            // Skip if too far
            float dist = Vector3.Distance(centerWorld, target.position);
            if (dist > radius)
                continue;

            // Reserve placeholder to prevent double-builds
            _chunks[coord] = null;

            // Launch async GPU job (non-blocking)
            baker.RunAsync(centerWorld, (mesh) =>
            {
                // Safety: chunk could have been unloaded while GPU was working
                if (!_chunks.ContainsKey(coord))
                    return;

                if (mesh == null || mesh.vertexCount == 0)
                {
                    _chunks.Remove(coord);
                    return;
                }

                // Create chunk GameObject
                var go = new GameObject($"Chunk_{coord.x}_{coord.y}_{coord.z}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;

                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                var mc = go.AddComponent<MeshCollider>();

                print("Mesh loaded at: " + coord);

                mf.sharedMesh = mesh;
                mc.sharedMesh = mesh;

                // Copy parent material
                var parentRenderer = baker.GetComponent<MeshRenderer>();
                if (parentRenderer)
                    mr.sharedMaterial = parentRenderer.sharedMaterial;

                _chunks[coord] = go;
            });

            built++;
        }
    }


    // Replace ReloadAllTerrain
    private void ReloadAllTerrain()
    {
        var keys = new List<Vector3Int>(_chunks.Keys);
        foreach (var c in keys) DestroyChunk(c);
        _buildQueue.Clear();
    }


    // Replace UnloadFar
    private void UnloadFar(Vector3Int center, Vector3 chunkSize, float radius)
    {
        var toRemove = new List<Vector3Int>();

        foreach (var kvp in _chunks)
        {
            Vector3 worldCenter = ChunkCenterWorld(kvp.Key, chunkSize);
            float dist = Vector3.Distance(worldCenter, target.position);
            if (dist > radius + unloadBuffer) toRemove.Add(kvp.Key);
        }

        foreach (var c in toRemove) DestroyChunk(c);
    }


    // Add this helper
    private void DestroyChunk(Vector3Int coord)
    {
        if (_chunks.TryGetValue(coord, out var go))
        {
            if (go != null)
            {
                var mf = go.GetComponent<MeshFilter>();
                var mc = go.GetComponent<MeshCollider>();

                // grab mesh once, clear refs, then destroy
                Mesh mesh = mf ? mf.sharedMesh : null;
                if (mc) mc.sharedMesh = null;
                if (mf) mf.sharedMesh = null;

                Destroy(go);
                if (mesh != null) Destroy(mesh);
            }
            _chunks.Remove(coord);
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


    // ----- grid math -----

    private Vector3 GetChunkSize()
    {
        return Vector3.Scale(baker.settings.scale, baker.settings.chunkDims);
    }

    private static Vector3Int WorldToChunk(Vector3 worldPos, Vector3 chunkSize)
    {
        return new Vector3Int(
            Mathf.FloorToInt(worldPos.x / chunkSize.x),
            Mathf.FloorToInt(worldPos.y / chunkSize.y),
            Mathf.FloorToInt(worldPos.z / chunkSize.z)
        );
    }

    private static Vector3 ChunkCenterWorld(Vector3Int coord, Vector3 chunkSize)
    {
        return new Vector3(
            (coord.x + 0.5f) * chunkSize.x,
            (coord.y + 0.5f) * chunkSize.y,
            (coord.z + 0.5f) * chunkSize.z
        );
    }
}
