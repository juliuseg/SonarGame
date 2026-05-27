using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public class SDFAtlas : System.IDisposable
{
    public ComputeBuffer AtlasBuffer { get; private set; }
    public ComputeBuffer LookupBuffer { get; private set; }
    public int SlotSize { get; private set; }

    private readonly int _maxSlots;
    private readonly Stack<int> _freeSlots = new();
    private readonly Dictionary<Vector3Int, int> _coordToSlot = new();
    private readonly LookupEntry[] _lookupEntries;
    private bool _lookupDirty;
    
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
        
        // set flatdata to null?

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

    public bool TryGetSlot(Vector3Int coord, out int slot) => _coordToSlot.TryGetValue(coord, out slot);

    public void Dispose()
    {
        AtlasBuffer?.Release();
        LookupBuffer?.Release();
    }
}