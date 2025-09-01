using UnityEngine;

[CreateAssetMenu(fileName = "MCSettings", menuName = "Own/MCSettings")]
public class MCSettings : ScriptableObject
{
    public Vector3 scale;
    public Vector3Int chunkDims;
    public float isoLevel;
    public Vector3 noiseFrequency;
    public Vector3 noiseOffset;
    
    [Header("Worley Noise Parameters")]
    public float noiseScale = 0.03f;
    public float verticalScale = 1.0f;
    public uint seed = 12345;
    public float caveHeightFalloff = 20.0f;
}
