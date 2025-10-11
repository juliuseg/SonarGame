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

    [Header("Displacement Settings")]
    public float displacementStrength = 0.015f;
    public float displacementScale = 12.5f;
    public int octaves = 4;
    public float lacunarity = 2f;
    public float persistence = 0.5f;

    [Header("Candidates Settings")]
    public float cosUp = 0.5f;
    public float cosDown = -0.5f;
    public float cosSide = 0.5f;
    public float foliageDensity = 0.1f;

    [Header("Biome Settings")]
    public float biomeScale = 100.0f;
    public float biomeBorder = 4.0f;
    public float biomeDisplacementStrength = 0.05f;
    public float biomeDisplacementScale = 12.5f;

    public BiomeSettings[] biomeSettings;

    [Header("SDF Settings")]
    public int halo = 1;

}

