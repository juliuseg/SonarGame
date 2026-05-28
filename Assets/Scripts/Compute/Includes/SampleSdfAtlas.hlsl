// Shared SDF atlas sampling (matches SDFAtlasTest.compute / ChunkManager.TryGetSDFValue).

struct LookupEntry
{
    int x, y, z, slotIndex;
};

StructuredBuffer<LookupEntry> _Lookup;
StructuredBuffer<float> _Atlas;

float3 _ChunkSize;
float3 _Scale;
int3 _ChunkDims;
int _SlotSize;
int _LookupCount;

int FindSdfSlot(int3 chunkCoord)
{
    for (int i = 0; i < _LookupCount; i++)
    {
        LookupEntry e = _Lookup[i];
        if (e.slotIndex >= 0 &&
            e.x == chunkCoord.x &&
            e.y == chunkCoord.y &&
            e.z == chunkCoord.z)
            return e.slotIndex;
    }
    return -1;
}

float ReadSdfVoxel(int3 chunkCoord, int x, int y, int z)
{
    int sx = _ChunkDims.x;
    int sy = _ChunkDims.y;
    int sz = _ChunkDims.z;

    int3 c = chunkCoord;
    if (x < 0)        { c.x -= 1; x += sx; }
    else if (x >= sx) { c.x += 1; x -= sx; }
    if (y < 0)        { c.y -= 1; y += sy; }
    else if (y >= sy) { c.y += 1; y -= sy; }
    if (z < 0)        { c.z -= 1; z += sz; }
    else if (z >= sz) { c.z += 1; z -= sz; }

    int slot = FindSdfSlot(c);
    if (slot < 0)
        return 0.0;

    int baseIdx = slot * _SlotSize;
    return _Atlas[baseIdx + z + y * sz + x * sz * sy];
}

float SampleSdfAtlas(float3 worldPos)
{
    int3 chunkCoord = int3(
        (int)floor(worldPos.x / _ChunkSize.x),
        (int)floor(worldPos.y / _ChunkSize.y),
        (int)floor(worldPos.z / _ChunkSize.z)
    );

    if (FindSdfSlot(chunkCoord) < 0)
        return 999999.0;

    float3 chunkCenter = float3(
        (chunkCoord.x + 0.5) * _ChunkSize.x,
        (chunkCoord.y + 0.5) * _ChunkSize.y,
        (chunkCoord.z + 0.5) * _ChunkSize.z
    );

    float3 local = worldPos - chunkCenter;
    float fx = local.x / _Scale.x + _ChunkDims.x / 2.0 - 0.5;
    float fy = local.y / _Scale.y + _ChunkDims.y / 2.0 - 0.5;
    float fz = local.z / _Scale.z + _ChunkDims.z / 2.0 - 0.5;

    int x0 = (int)floor(fx);
    int y0 = (int)floor(fy);
    int z0 = (int)floor(fz);
    int x1 = x0 + 1;
    int y1 = y0 + 1;
    int z1 = z0 + 1;

    float tx = fx - x0;
    float ty = fy - y0;
    float tz = fz - z0;

    float c000 = ReadSdfVoxel(chunkCoord, x0, y0, z0);
    float c100 = ReadSdfVoxel(chunkCoord, x1, y0, z0);
    float c010 = ReadSdfVoxel(chunkCoord, x0, y1, z0);
    float c110 = ReadSdfVoxel(chunkCoord, x1, y1, z0);
    float c001 = ReadSdfVoxel(chunkCoord, x0, y0, z1);
    float c101 = ReadSdfVoxel(chunkCoord, x1, y0, z1);
    float c011 = ReadSdfVoxel(chunkCoord, x0, y1, z1);
    float c111 = ReadSdfVoxel(chunkCoord, x1, y1, z1);

    float c00 = lerp(c000, c100, tx);
    float c10 = lerp(c010, c110, tx);
    float c01 = lerp(c001, c101, tx);
    float c11 = lerp(c011, c111, tx);
    float c0 = lerp(c00, c10, ty);
    float c1 = lerp(c01, c11, ty);
    return lerp(c0, c1, tz);
}

float3 SampleSdfAtlasGradient(float3 worldPos, float delta)
{
    float dx = SampleSdfAtlas(worldPos + float3(delta, 0, 0))
             - SampleSdfAtlas(worldPos - float3(delta, 0, 0));
    float dy = SampleSdfAtlas(worldPos + float3(0, delta, 0))
             - SampleSdfAtlas(worldPos - float3(0, delta, 0));
    float dz = SampleSdfAtlas(worldPos + float3(0, 0, delta))
             - SampleSdfAtlas(worldPos - float3(0, 0, delta));
    return float3(dx, dy, dz) / max(2.0 * delta, 1e-4);
}
