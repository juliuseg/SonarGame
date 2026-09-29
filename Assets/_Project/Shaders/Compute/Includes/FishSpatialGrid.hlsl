#ifndef FISH_SPATIAL_GRID_INCLUDED
#define FISH_SPATIAL_GRID_INCLUDED

#define FISH_GRID_SCAN_GROUP_SIZE 256
#define FISH_GRID_INVALID 0xFFFFFFFFu

uint _GridCellCount;
int3 _GridDim;
float _CellSize;
float3 _GridOrigin;
int _GridNeighborCellRadius;


RWStructuredBuffer<uint> _GridCellCounts;
RWStructuredBuffer<uint> _GridCellOffsets;
RWStructuredBuffer<uint> _GridCellScatter;
RWStructuredBuffer<uint> _GridCellFish;
RWStructuredBuffer<uint> _GridScanBlockSums;

int3 FishWorldToCell(float3 pos)
{
    return int3(floor((pos - _GridOrigin) / _CellSize));
}

uint FishFlatCell(int3 c)
{
    if (any(c < int3(0, 0, 0)) ||
        c.x >= _GridDim.x || c.y >= _GridDim.y || c.z >= _GridDim.z)
        return FISH_GRID_INVALID;

    return (uint)(c.x + c.y * _GridDim.x + c.z * _GridDim.x * _GridDim.y);
}

void AccumulateBoidNeighbors(
    uint selfIndex,
    float3 pos,
    float neighborRadiusSq,
    float separationRadius,
    inout float3 sep,
    inout float3 alignSum,
    inout float3 cohSum,
    inout uint neighborCount)
{
    int3 centerCell = FishWorldToCell(pos);
    int cellRadius = max(_GridNeighborCellRadius, 1);

    [loop]
    for (int dz = -cellRadius; dz <= cellRadius; dz++)
    [loop]
    for (int dy = -cellRadius; dy <= cellRadius; dy++)
    [loop]
    for (int dx = -cellRadius; dx <= cellRadius; dx++)
    {
        uint flat = FishFlatCell(centerCell + int3(dx, dy, dz));
        if (flat == FISH_GRID_INVALID)
            continue;

        uint start = _GridCellOffsets[flat];
        uint end = start + _GridCellCounts[flat];

        [loop]
        for (uint k = start; k < end; k++)
        {
            uint j = _GridCellFish[k];
            if (j == selfIndex || _BoidState[j] != BOID_STATE_LOADED)
                continue;

            float3 posJ = _Positions[j].xyz;
            float3 velJ = _Velocities[j].xyz;
            float3 offset = pos - posJ;
            float distSq = dot(offset, offset);
            if (distSq < 1e-8 || distSq > neighborRadiusSq)
                continue;

            float dist = sqrt(distSq);
            neighborCount++;

            if (dist < separationRadius)
                sep += offset / (distSq);

            alignSum += velJ;
            cohSum += posJ;
        }
    }
}

#endif
