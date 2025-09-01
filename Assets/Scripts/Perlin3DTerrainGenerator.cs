using UnityEngine;

/// <summary>
/// 3D Perlin noise terrain generator for caves and overhangs
/// </summary>
public class Perlin3DTerrainGenerator : TerrainGenerator
{
    [Header("3D Perlin Noise Settings")]
    [Tooltip("Scale of the noise (smaller = more detailed)")]
    public float noiseScale = 0.1f;
    [Range(0f, 1f), Tooltip("0.0 = 100% rock, 1.0 = 100% air, 0.5 = 50/50")]
    public float caveThreshold = 0.5f;
    [Tooltip("Offset for the noise generation")]
    public Vector3 noiseOffset = Vector3.zero;
    [Tooltip("Number of noise octaves")]
    public int octaves = 2;
    [Tooltip("How much detail increases with each octave")]
    public float lacunarity = 2.0f;
    [Tooltip("How much amplitude decreases with each octave")]
    public float persistence = 0.5f;
    [Range(0f, 1f), Tooltip("Intensity of cave generation")]
    public float caveIntensity = 0.5f;
    [Tooltip("Height range over which caves become less prominent (0 to this value)")]
    public float caveHeightFalloff = 50f;
    [Tooltip("How much cave intensity decreases with height (0 = no falloff, 1 = complete falloff)")]
    [Range(0f, 1f)]
    public float heightFalloffStrength = 0.8f;
    
    public override string GetGeneratorName()
    {
        return "3D Perlin Noise";
    }
    
    public override float[] GenerateDensityField(Vector3 chunkOrigin, Vector3Int fieldSize, float voxelSize)
    {
        if (!ValidateSettings())
            return null;
            
        var field = new float[fieldSize.x * fieldSize.y * fieldSize.z];
        
        // Generate density field for this chunk
        int idx = 0;
        for (int z = 0; z < fieldSize.z; z++)
        for (int y = 0; y < fieldSize.y; y++)
        for (int x = 0; x < fieldSize.x; x++, idx++)
        {
            Vector3 worldPos = chunkOrigin + new Vector3(x * voxelSize, y * voxelSize, z * voxelSize);
            
            // Generate 3D Perlin noise for caves and overhangs
            float noiseX = (worldPos.x + noiseOffset.x) * noiseScale;
            float noiseY = (worldPos.y + noiseOffset.y) * noiseScale;
            float noiseZ = (worldPos.z + noiseOffset.z) * noiseScale;
            
            // Calculate height-based cave intensity modifier
            float heightModifier = 1f;
            if (worldPos.y > 0f && worldPos.y <= caveHeightFalloff)
            {
                float normalizedHeight = worldPos.y / caveHeightFalloff;
                heightModifier = 1f - (normalizedHeight * heightFalloffStrength);
                heightModifier = Mathf.Max(heightModifier, 0.1f); // Ensure minimum cave generation
            }
            else if (worldPos.y > caveHeightFalloff)
            {
                heightModifier = 0.1f; // Very minimal caves above falloff height
            }
            
            // Simple 3D noise using multiple 2D slices
            float density = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            
            for (int octave = 0; octave < octaves; octave++)
            {
                // Combine three 2D noise slices for 3D effect
                float slice1 = Mathf.PerlinNoise(noiseX * frequency, noiseY * frequency);
                float slice2 = Mathf.PerlinNoise(noiseY * frequency, noiseZ * frequency);
                float slice3 = Mathf.PerlinNoise(noiseZ * frequency, noiseX * frequency);
                
                density += (slice1 + slice2 + slice3) * amplitude * caveIntensity * heightModifier;
                
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            
            // Normalize to proper range and create cave structures
            float maxPossibleDensity = 0f;
            float currentAmplitude = 1f;
            for (int i = 0; i < octaves; i++)
            {
                maxPossibleDensity += 3f * currentAmplitude;
                currentAmplitude *= persistence;
            }
            
            density = density / maxPossibleDensity;
            // density = 1f - density;
            float caveDensity = density - caveThreshold;
            field[idx] = caveDensity;
        }
        
        return field;
    }
    
    public override bool ValidateSettings()
    {
        if (!base.ValidateSettings())
            return false;
            
        if (noiseScale <= 0)
        {
            Debug.LogError($"{GetGeneratorName()}: Noise scale must be positive!");
            return false;
        }
        
        if (octaves <= 0)
        {
            Debug.LogError($"{GetGeneratorName()}: Octaves must be positive!");
            return false;
        }
        
        if (lacunarity <= 0)
        {
            Debug.LogError($"{GetGeneratorName()}: Lacunarity must be positive!");
            return false;
        }
        
        if (persistence <= 0 || persistence >= 1)
        {
            Debug.LogError($"{GetGeneratorName()}: Persistence must be between 0 and 1!");
            return false;
        }
        
        return true;
    }
}
