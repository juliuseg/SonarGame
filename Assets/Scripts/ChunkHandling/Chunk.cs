using UnityEngine;
using System.Collections.Generic;

public class Chunk {
    public GameObject gameObject;
    public float[] sdfData;
    public Vector3Int sdfDims;
    public List<TerraformEdit> terraformEdits;
    public List<SpawnPoint> spawnPoints;
    public List<Vector3> interiorSpawnPositions;
    public uint biomeMask;
    public int sdfSlotIndex = -1;
    
    public Chunk(GameObject gameObject, float[] sdfData, List<SpawnPoint> spawnPoints, ComputeBuffer sdfBuffer, uint biomeMask)
    {
        this.gameObject = gameObject;
        this.sdfData = sdfData;
        this.spawnPoints = spawnPoints;
        this.terraformEdits = new List<TerraformEdit>();
        this.biomeMask = biomeMask;
    }

    public bool HasSdfData => sdfData != null && sdfDims.x > 0 && sdfDims.y > 0 && sdfDims.z > 0;

    public static int ToFlatIndex(int x, int y, int z, int sx, int sy, int sz) =>
        z + y * sz + x * sz * sy;

    public float GetVoxel(int x, int y, int z) =>
        sdfData[ToFlatIndex(x, y, z, sdfDims.x, sdfDims.y, sdfDims.z)];

    public bool TryGetVoxel(int x, int y, int z, out float value)
    {
        value = 0f;
        if (!HasSdfData)
            return false;
        if (x < 0 || x >= sdfDims.x || y < 0 || y >= sdfDims.y || z < 0 || z >= sdfDims.z)
            return false;
        value = GetVoxel(x, y, z);
        return true;
    }

    public List<int> GetBiomeMaskList()
    {
        var list = new List<int>(32);
        for (int i = 0; i < 32; i++)
        {
            if ((biomeMask & (1u << i)) != 0)
                list.Add(i);
        }
        return list;
    }

}

public struct TerraformEdit {
    public Vector3 position;
    public float strength;
    public float radius;
} 

public struct SpawnPoint
{
    public Vector3 positionWS;
    public Vector3 normalWS;
    public Vector3 colorWS;
};
