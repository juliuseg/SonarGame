using UnityEngine;
using System.Collections.Generic;

public class Chunk {
    public GameObject gameObject;
    public float[,,] sdfData;
    public ComputeBuffer sdfBuffer;
    public List<TerraformEdit> terraformEdits; // Idea: Store these as a dict, so we don't overload the list. Then make a function to extract the array from the dict. And a function to add to the dict and have it voxel based.

    public Chunk(GameObject gameObject, float[,,] sdfData, ComputeBuffer sdfBuffer)
    {
        this.gameObject = gameObject;
        this.sdfData = sdfData;
        this.sdfBuffer = sdfBuffer;
        this.terraformEdits = new List<TerraformEdit>();
    }
}

public struct TerraformEdit {
    public Vector3 position;
    public float strength;
    public float radius;
} 