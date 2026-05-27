using UnityEngine;

public class ChunkSDFVisualizer : MonoBehaviour
{
    private ChunkManager _chunkManager;
    private MCSettings _mcSettings;

    [Header("Mode")]
    [Tooltip("On: fixed debugChunkCoord. Off: volume centered on Sdf Target.")]
    public bool useSingleChunkMode = true;

    [Header("Single chunk")]
    public Vector3Int debugChunkCoord;

    [Header("Follow target")]
    public Transform sdfTarget;
    [Tooltip("Size multiplier around target (1 = one chunk volume, 2 = 2x per axis).")]
    [Min(0.5f)]
    public float followRegionScale = 1f;

    [Header("Display")]
    public float colorMultiplier = 1f;
    [Range(0, 1)] public float alpha = 0.3f;
    [Tooltip("Skip voxels with |sdf| above this (keeps the shell easy to read).")]
    public float maxAbsDistance = 1f;

    public void Init(ChunkManager chunkManager, MCSettings chunkSettings)
    {
        _chunkManager = chunkManager;
        _mcSettings = chunkSettings;
    }

    void OnDrawGizmos()
    {
        if (!enabled || _chunkManager == null || _mcSettings == null) return;

        if (useSingleChunkMode)
        {
            if (!_chunkManager.TryGetChunk(debugChunkCoord, out var chunk) || !chunk.HasSdfData)
                return;
            DrawVolume(_chunkManager.ChunkCenterWorld(debugChunkCoord), chunk, 1f);
            return;
        }

        if (sdfTarget == null) return;
        DrawVolume(sdfTarget.position, null, followRegionScale);
    }

    void DrawVolume(Vector3 worldCenter, Chunk chunk, float regionScale)
    {
        Vector3Int dims = chunk != null ? chunk.sdfDims : _chunkManager.GetSdfChunkDims();
        Vector3 voxelScale = _mcSettings.scale;
        Vector3 cubeSize = Vector3.Scale(Vector3.one, voxelScale) * 0.9f;

        int halfX = Mathf.CeilToInt(dims.x * regionScale * 0.5f);
        int halfY = Mathf.CeilToInt(dims.y * regionScale * 0.5f);
        int halfZ = Mathf.CeilToInt(dims.z * regionScale * 0.5f);

        for (int x = -halfX; x < halfX; x++)
        for (int y = -halfY; y < halfY; y++)
        for (int z = -halfZ; z < halfZ; z++)
        {
            Vector3 pos = worldCenter + Vector3.Scale(new Vector3(x, y, z), voxelScale);

            float d;
            if (chunk != null)
            {
                if (!TryGetVoxelFromChunk(chunk, dims, x, y, z, out d))
                    continue;
            }
            else
            {
                if (!CanSampleSdfAt(pos) || !_chunkManager.TryGetSDFValue(pos, out d))
                    continue;
            }

            if (Mathf.Abs(d) > maxAbsDistance) continue;

            float f = d * colorMultiplier;
            Gizmos.color = new Color(f, -f, 0f, alpha);
            Gizmos.DrawCube(pos, cubeSize);
        }
    }

    bool TryGetVoxelFromChunk(Chunk chunk, Vector3Int dims, int x, int y, int z, out float value)
    {
        value = 0f;
        int minX = -dims.x / 2;
        int minY = -dims.y / 2;
        int minZ = -dims.z / 2;
        int ix = x - minX;
        int iy = y - minY;
        int iz = z - minZ;
        if (ix < 0 || ix >= dims.x || iy < 0 || iy >= dims.y || iz < 0 || iz >= dims.z)
            return false;
        value = chunk.GetVoxel(ix, iy, iz);
        return true;
    }

    /// <summary>
    /// All eight trilinear corners must live in loaded chunks (avoids 0-fill gaps at borders).
    /// </summary>
    bool CanSampleSdfAt(Vector3 worldPos)
    {
        Vector3Int coord = _chunkManager.WorldToChunk(worldPos);
        if (!_chunkManager.TryGetChunk(coord, out var chunk) || !chunk.HasSdfData)
            return false;

        int sx = chunk.sdfDims.x;
        int sy = chunk.sdfDims.y;
        int sz = chunk.sdfDims.z;

        Vector3 chunkCenter = _chunkManager.ChunkCenterWorld(coord);
        Vector3 scale = _mcSettings.scale;
        Vector3 local = worldPos - chunkCenter;

        int x0 = Mathf.FloorToInt(local.x / scale.x + sx / 2f - 0.5f);
        int y0 = Mathf.FloorToInt(local.y / scale.y + sy / 2f - 0.5f);
        int z0 = Mathf.FloorToInt(local.z / scale.z + sz / 2f - 0.5f);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        int z1 = z0 + 1;

        return CornerChunkHasSdf(coord, sx, sy, sz, x0, y0, z0)
            && CornerChunkHasSdf(coord, sx, sy, sz, x1, y0, z0)
            && CornerChunkHasSdf(coord, sx, sy, sz, x0, y1, z0)
            && CornerChunkHasSdf(coord, sx, sy, sz, x1, y1, z0)
            && CornerChunkHasSdf(coord, sx, sy, sz, x0, y0, z1)
            && CornerChunkHasSdf(coord, sx, sy, sz, x1, y0, z1)
            && CornerChunkHasSdf(coord, sx, sy, sz, x0, y1, z1)
            && CornerChunkHasSdf(coord, sx, sy, sz, x1, y1, z1);
    }

    bool CornerChunkHasSdf(Vector3Int baseCoord, int sx, int sy, int sz, int x, int y, int z)
    {
        Vector3Int c = baseCoord;

        if (x < 0) { c.x -= 1; x += sx; }
        else if (x >= sx) { c.x += 1; x -= sx; }

        if (y < 0) { c.y -= 1; y += sy; }
        else if (y >= sy) { c.y += 1; y -= sy; }

        if (z < 0) { c.z -= 1; z += sz; }
        else if (z >= sz) { c.z += 1; z -= sz; }

        return _chunkManager.TryGetChunk(c, out var neighbor) && neighbor.HasSdfData;
    }
}
