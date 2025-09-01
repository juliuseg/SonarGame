using UnityEngine;

/// <summary>
/// 2D heightmap terrain generator for traditional terrain
/// </summary>
public class HeightmapTerrainGenerator : TerrainGenerator
{
    [Header("2D Heightmap Settings")]
    [Tooltip("Scale of the noise (smaller = more detailed)")]
    public float noiseScale = 0.05f;
    [Tooltip("Height multiplier for the terrain")]
    public float heightMultiplier = 10f;
    [Tooltip("Base height of the terrain")]
    public float baseHeight = 0f;
    [Tooltip("Offset for the noise generation")]
    public Vector2 noiseOffset = Vector2.zero;
    [Tooltip("Number of noise octaves")]
    public int octaves = 4;
    [Tooltip("How much detail increases with each octave")]
    public float lacunarity = 2.0f;
    [Tooltip("How much amplitude decreases with each octave")]
    public float persistence = 0.5f;
    [Tooltip("Steepness of the terrain (higher = steeper)")]
    public float steepness = 1f;
    
    public override string GetGeneratorName()
    {
        return "2D Heightmap";
    }
    
    public override float[] GenerateDensityField(Vector3 chunkOrigin, Vector3Int fieldSize, float voxelSize)
    {
        if (!ValidateSettings())
            return null;
            
        var field = new float[fieldSize.x * fieldSize.y * fieldSize.z];
        
        // Generate heightmap for this chunk
        int idx = 0;
        for (int z = 0; z < fieldSize.z; z++)
        for (int y = 0; y < fieldSize.y; y++)
        for (int x = 0; x < fieldSize.x; x++, idx++)
        {
            Vector3 worldPos = chunkOrigin + new Vector3(x * voxelSize, y * voxelSize, z * voxelSize);
            
            // Generate 2D Perlin noise for height
            float noiseX = (worldPos.x + noiseOffset.x) * noiseScale;
            float noiseZ = (worldPos.z + noiseOffset.y) * noiseScale;
            
            // Multi-octave noise
            float height = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            
            for (int octave = 0; octave < octaves; octave++)
            {
                height += Mathf.PerlinNoise(noiseX * frequency, noiseZ * frequency) * amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            
            // Normalize height and apply multipliers
            height = (height - 0.5f) * 2f; // Convert from 0-1 to -1 to 1
            height = height * heightMultiplier + baseHeight;
            
            // Calculate density based on Y position vs height
            float density = (height - worldPos.y) * steepness;
            
            // Clamp density to reasonable range
            density = Mathf.Clamp(density, -2f, 2f);
            
            field[idx] = density;
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
        
        if (heightMultiplier <= 0)
        {
            Debug.LogError($"{GetGeneratorName()}: Height multiplier must be positive!");
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
        
        if (steepness <= 0)
        {
            Debug.LogError($"{GetGeneratorName()}: Steepness must be positive!");
            return false;
        }
        
        return true;
    }
}
