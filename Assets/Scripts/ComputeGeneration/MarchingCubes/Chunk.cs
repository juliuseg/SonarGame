using UnityEngine;

public class Chunk {
    public GameObject gameObject;
    public float[,,] sdfData;
    public ComputeBuffer sdfBuffer;

    public Chunk(GameObject gameObject, float[,,] sdfData, ComputeBuffer sdfBuffer)
    {
        this.gameObject = gameObject;
        this.sdfData = sdfData;
        this.sdfBuffer = sdfBuffer;
    }
}