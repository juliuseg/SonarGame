using UnityEngine;

[CreateAssetMenu(fileName = "MCSettings", menuName = "Own/MCSettings")]
public class MCSettings : ScriptableObject
{
    public Vector3 scale;
    public Vector3Int chunkDims;
    public float isoLevel;
    public Vector3 noiseFrequency;
    public Vector3 noiseOffset;
}
