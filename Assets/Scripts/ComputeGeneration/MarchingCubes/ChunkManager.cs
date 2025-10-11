using System.Collections.Generic;
using UnityEngine;


public class ChunkManager
{
    private readonly Dictionary<Vector3Int, Chunk> _chunks = new();

    public Dictionary<Vector3Int, Chunk> chunks => _chunks;

    public MCSettings settings;

    public ChunkManager(MCSettings settings)
    {
        this.settings = settings;
    }

    public bool TryGetChunk(Vector3Int coord, out Chunk chunk)
    {
        return _chunks.TryGetValue(coord, out chunk);
    }

    public void RemoveChunk(Vector3Int coord)
    {
        _chunks.Remove(coord);
    }

    public void SetChunk(Vector3Int coord, Chunk chunk)
    {
        _chunks[coord] = chunk;
    }

    public void Clear()
    {
        if (_chunks.Count == 0) return;

        var keys = new List<Vector3Int>(_chunks.Keys); // snapshot
        foreach (var key in keys) DestroyChunk(key);

    }

    public void DestroyChunk(Vector3Int coord)
    {
        if (_chunks.TryGetValue(coord, out var chunk))
        {
            if (chunk.gameObject != null)
            {
                var mf = chunk.gameObject.GetComponent<MeshFilter>();
                var mc = chunk.gameObject.GetComponent<MeshCollider>();

                // grab mesh once, clear refs, then destroy
                Mesh mesh = mf ? mf.sharedMesh : null;
                if (mc) mc.sharedMesh = null;
                if (mf) mf.sharedMesh = null;

                Object.Destroy(chunk.gameObject);
                if (mesh != null) Object.Destroy(mesh);
            }

            if (chunk.sdfBuffer != null) chunk.sdfBuffer.Release();
            if (chunk.sdfData != null) chunk.sdfData = null;
            _chunks.Remove(coord);
            // _pending.Remove(coord);
        }
    }

    public Vector3 GetChunkSize()
    {
        return Vector3.Scale(settings.scale, settings.chunkDims);
    }

    public Vector3Int WorldToChunk(Vector3 worldPos)
    {
        Vector3 chunkSize = GetChunkSize();
        return new Vector3Int(
            Mathf.FloorToInt(worldPos.x / chunkSize.x),
            Mathf.FloorToInt(worldPos.y / chunkSize.y),
            Mathf.FloorToInt(worldPos.z / chunkSize.z)
        );
    }

    public Vector3 ChunkCenterWorld(Vector3Int coord)
    {
        Vector3 chunkSize = GetChunkSize();
        return new Vector3(
            (coord.x + 0.5f) * chunkSize.x,
            (coord.y + 0.5f) * chunkSize.y,
            (coord.z + 0.5f) * chunkSize.z
        );
    }

    

    public bool TryGetSDFValue(Vector3 worldPos, out float value)
    {
        value = 0f;

        Vector3Int coord = WorldToChunk(worldPos);
        if (!TryGetChunk(coord, out var baseChunk) || baseChunk.sdfData == null)
        {
            Debug.LogError($"Missing chunk or SDF data for world position: {worldPos}");
            return false;
        }

        float[,,] data = baseChunk.sdfData;
        int sx = data.GetLength(0);
        int sy = data.GetLength(1);
        int sz = data.GetLength(2);

        Vector3 chunkCenter = ChunkCenterWorld(coord);
        Vector3 scale = settings.scale;

        // local voxel coordinates (continuous)
        Vector3 local = (worldPos - chunkCenter);
        float fx = local.x / scale.x + sx / 2f - 0.5f;
        float fy = local.y / scale.y + sy / 2f - 0.5f;
        float fz = local.z / scale.z + sz / 2f - 0.5f;

        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        int z0 = Mathf.FloorToInt(fz);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        int z1 = z0 + 1;

        float tx = fx - x0;
        float ty = fy - y0;
        float tz = fz - z0;

        // helper to read safely across chunk borders
        float GetValueFromChunks(int x, int y, int z)
        {
            Vector3Int c = coord;

            if (x < 0) { c.x -= 1; x += sx; }
            else if (x >= sx) { c.x += 1; x -= sx; }

            if (y < 0) { c.y -= 1; y += sy; }
            else if (y >= sy) { c.y += 1; y -= sy; }

            if (z < 0) { c.z -= 1; z += sz; }
            else if (z >= sz) { c.z += 1; z -= sz; }

            if (!TryGetChunk(c, out var neighbor) || neighbor.sdfData == null)
                return 0f;

            float[,,] d = neighbor.sdfData;
            return d[Mathf.Clamp(x, 0, d.GetLength(0) - 1),
                    Mathf.Clamp(y, 0, d.GetLength(1) - 1),
                    Mathf.Clamp(z, 0, d.GetLength(2) - 1)];
        }

        float c000 = GetValueFromChunks(x0, y0, z0);
        float c100 = GetValueFromChunks(x1, y0, z0);
        float c010 = GetValueFromChunks(x0, y1, z0);
        float c110 = GetValueFromChunks(x1, y1, z0);
        float c001 = GetValueFromChunks(x0, y0, z1);
        float c101 = GetValueFromChunks(x1, y0, z1);
        float c011 = GetValueFromChunks(x0, y1, z1);
        float c111 = GetValueFromChunks(x1, y1, z1);

        // trilinear interpolation
        float c00 = Mathf.Lerp(c000, c100, tx);
        float c10 = Mathf.Lerp(c010, c110, tx);
        float c01 = Mathf.Lerp(c001, c101, tx);
        float c11 = Mathf.Lerp(c011, c111, tx);
        float c0 = Mathf.Lerp(c00, c10, ty);
        float c1 = Mathf.Lerp(c01, c11, ty);
        value = Mathf.Lerp(c0, c1, tz);

        return true;
    }



    public bool TrySampleSDFGradient(Vector3 worldPos, out Vector3 gradient)
    {
        gradient = Vector3.zero;
        float delta = 0.05f;

        // Central difference sampling
        if (!TryGetSDFValue(worldPos + new Vector3(delta, 0, 0), out float dx1)) return false;
        if (!TryGetSDFValue(worldPos - new Vector3(delta, 0, 0), out float dx2)) return false;
        if (!TryGetSDFValue(worldPos + new Vector3(0, delta, 0), out float dy1)) return false;
        if (!TryGetSDFValue(worldPos - new Vector3(0, delta, 0), out float dy2)) return false;
        if (!TryGetSDFValue(worldPos + new Vector3(0, 0, delta), out float dz1)) return false;
        if (!TryGetSDFValue(worldPos - new Vector3(0, 0, delta), out float dz2)) return false;

        gradient = new Vector3(
            (dx1 - dx2) / (2f * delta),
            (dy1 - dy2) / (2f * delta),
            (dz1 - dz2) / (2f * delta)
        );

        return true;
    }



}