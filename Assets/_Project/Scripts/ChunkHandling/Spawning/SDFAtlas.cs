using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class SDFAtlas : System.IDisposable
{
    public ComputeBuffer AtlasBuffer { get; private set; }
    public ComputeBuffer LookupBuffer { get; private set; }
    public ComputeBuffer ChunkToSlotBuffer { get; private set; }
    public Vector3Int ChunkLookupOrigin { get; private set; }
    public Vector3Int ChunkLookupDim { get; private set; }
    public int SlotSize { get; private set; }

    private readonly int _maxSlots;
    private readonly Stack<int> _freeSlots = new();
    private readonly Dictionary<Vector3Int, int> _coordToSlot = new();
    private readonly LookupEntry[] _lookupEntries;
    private bool _lookupDirty;

    private int[] _chunkToSlotEntries;
    private Vector3Int _cachedCenterChunk = new(int.MinValue, int.MinValue, int.MinValue);
    private int _cachedHalfDim = -1;

    public int MaxSlots => _maxSlots;

    [StructLayout(LayoutKind.Sequential)]
    private struct LookupEntry
    {
        public int X, Y, Z, SlotIndex;
    }

    public SDFAtlas(int maxSlots, Vector3Int chunkDims)
    {
        _maxSlots = maxSlots;
        SlotSize = chunkDims.x * chunkDims.y * chunkDims.z;

        AtlasBuffer = new ComputeBuffer(
            maxSlots * SlotSize,
            sizeof(float),
            ComputeBufferType.Default,
            ComputeBufferMode.SubUpdates);
        LookupBuffer = new ComputeBuffer(maxSlots, sizeof(int) * 4);

        _lookupEntries = new LookupEntry[maxSlots];
        for (int i = 0; i < maxSlots; i++)
        {
            _freeSlots.Push(i);
            _lookupEntries[i] = new LookupEntry { X = 0, Y = 0, Z = 0, SlotIndex = -1 };
        }

        LookupBuffer.SetData(_lookupEntries);
    }

    public int AllocateSlot(Vector3Int coord, float[] flatData)
    {
        if (_freeSlots.Count == 0)
        {
            Debug.LogWarning("SDFAtlas: no free slots!");
            return -1;
        }

        int slot = _freeSlots.Pop();
        _coordToSlot[coord] = slot;

        AtlasBuffer.SetData(flatData, 0, slot * SlotSize, SlotSize);

        _lookupEntries[slot] = new LookupEntry { X = coord.x, Y = coord.y, Z = coord.z, SlotIndex = slot };
        _lookupDirty = true;

        return slot;
    }

    public void FreeSlot(Vector3Int coord)
    {
        if (!_coordToSlot.TryGetValue(coord, out int slot)) return;

        _coordToSlot.Remove(coord);
        _freeSlots.Push(slot);

        _lookupEntries[slot] = new LookupEntry { X = 0, Y = 0, Z = 0, SlotIndex = -1 };
        _lookupDirty = true;
    }

    public void FlushLookup()
    {
        if (!_lookupDirty) return;
        LookupBuffer.SetData(_lookupEntries);
        _lookupDirty = false;
    }

    public void SyncChunkLookup(Vector3Int centerChunk, int halfDim)
    {
        halfDim = Mathf.Max(1, halfDim);
        bool layoutChanged = centerChunk != _cachedCenterChunk || halfDim != _cachedHalfDim;

        if (!layoutChanged && !_lookupDirty)
            return;

        int dim = halfDim * 2 + 1;
        int cellCount = dim * dim * dim;
        EnsureChunkToSlotCapacity(cellCount);

        ChunkLookupOrigin = centerChunk - new Vector3Int(halfDim, halfDim, halfDim);
        ChunkLookupDim = new Vector3Int(dim, dim, dim);

        Array.Fill(_chunkToSlotEntries, -1, 0, cellCount);

        foreach (var kvp in _coordToSlot)
        {
            Vector3Int rel = kvp.Key - ChunkLookupOrigin;
            if (rel.x < 0 || rel.y < 0 || rel.z < 0 ||
                rel.x >= dim || rel.y >= dim || rel.z >= dim)
                continue;

            int idx = rel.x + rel.y * dim + rel.z * dim * dim;
            _chunkToSlotEntries[idx] = kvp.Value;
        }

        ChunkToSlotBuffer.SetData(_chunkToSlotEntries, 0, 0, cellCount);

        _cachedCenterChunk = centerChunk;
        _cachedHalfDim = halfDim;

        if (_lookupDirty)
            FlushLookup();
    }

    public bool TryGetSlot(Vector3Int coord, out int slot) => _coordToSlot.TryGetValue(coord, out slot);

    public void ClearAll()
    {
        var coords = new List<Vector3Int>(_coordToSlot.Keys);
        foreach (var coord in coords)
            FreeSlot(coord);
        _lookupDirty = true;
        _cachedCenterChunk = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
    }

    void EnsureChunkToSlotCapacity(int cellCount)
    {
        if (_chunkToSlotEntries != null &&
            _chunkToSlotEntries.Length == cellCount &&
            ChunkToSlotBuffer != null)
            return;

        _chunkToSlotEntries = new int[cellCount];
        ChunkToSlotBuffer?.Release();
        ChunkToSlotBuffer = new ComputeBuffer(cellCount, sizeof(int));
    }

    public void Dispose()
    {
        AtlasBuffer?.Release();
        LookupBuffer?.Release();
        ChunkToSlotBuffer?.Release();
    }
}
