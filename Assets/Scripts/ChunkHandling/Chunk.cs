using UnityEngine;
using System.Collections.Generic;

public class Chunk {
    public GameObject gameObject;
    public float[,,] sdfData;
    public ComputeBuffer sdfBuffer;
    public List<TerraformEdit> terraformEdits; // Idea: Store these as a dict, so we don't overload the list. Then make a function to extract the array from the dict. And a function to add to the dict and have it voxel based.
    public List<SpawnPoint> spawnPoints;
    public uint biomeMask;
    public Chunk(GameObject gameObject, float[,,] sdfData, List<SpawnPoint> spawnPoints, ComputeBuffer sdfBuffer, uint biomeMask)
    {
        this.gameObject = gameObject;
        this.sdfData = sdfData;
        this.spawnPoints = spawnPoints;
        this.sdfBuffer = sdfBuffer;
        this.terraformEdits = new List<TerraformEdit>();
        this.biomeMask = biomeMask;
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