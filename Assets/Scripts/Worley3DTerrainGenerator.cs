using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 3D Worley noise terrain generator using procedural cell hashing
/// Works with infinite/chunked worlds
/// </summary>
public class Worley3DTerrainGenerator : TerrainGenerator
{
    [Header("Worley Noise Settings")]
    public float noiseScale = 0.05f;
    [Range(0f, 1f)]
    public float caveThreshold = 0.5f;
    public Vector3 noiseOffset = Vector3.zero;

    [Tooltip("Scale factor for vertical stretching (Y axis). <1 = squashed, >1 = stretched vertically")]
    public float verticalScale = 0.5f;

    [Tooltip("Falloff factor for cave height")]
    public float caveHeightFalloff = 20f;

    public int seed = 12345;

    [Header("Displacement Settings")]
    public float displacementStrength = 0.01f;   // base strength
    public float displacementScale = 25f;        // base scale
    public int octaves = 4;                      // number of noise layers
    public float lacunarity = 2f;                // frequency multiplier per octave
    public float persistence = 0.5f;             // amplitude multiplier per octave

    [Header("Debug")]
    public bool generateDebugSlice = false;
    public int debugSliceY = 0;
    public Texture2D debugTexture;

    public override string GetGeneratorName() => "3D Worley Noise";

    // Hash-based deterministic pseudo-random (so we don’t need Random.InitState)
    private Vector3 GetCellPoint(Vector3Int cell)
    {
        int h = Hash(cell.x, cell.y, cell.z, seed);

        // map hash → [0,1] jitter inside the cell
        float jx = ((h & 0xFF)       ) / 255f;
        float jy = ((h >> 8) & 0xFF) / 255f;
        float jz = ((h >>16) & 0xFF) / 255f;

        // INSERT_YOUR_CODE
        // If random 0-1 is >0.99, print "hello" (using regular randomness)
        return new Vector3(cell.x + jx, cell.y + jy, cell.z + jz);
        

    }

    private int Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            int h = seed;
            h ^= x * 374761393;
            h ^= y * 668265263;
            h ^= z * 2147483647;
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    public override float[] GenerateDensityField(Vector3 chunkOrigin, Vector3Int fieldSize, float voxelSize)
    {
        if (!ValidateSettings()) return null;

        var field = new float[fieldSize.x * fieldSize.y * fieldSize.z];
        int idx = 0;

        int printFrequency = 1000000; // adjust to taste

        for (int z = 0; z < fieldSize.z; z++)
        for (int y = 0; y < fieldSize.y; y++)
        for (int x = 0; x < fieldSize.x; x++, idx++)
        {
            Vector3 worldPos = chunkOrigin + new Vector3(x * voxelSize, y * voxelSize, z * voxelSize);
            Vector3 samplePos = new Vector3(
                (worldPos.x + noiseOffset.x) * noiseScale,
                (worldPos.y + noiseOffset.y) * noiseScale * verticalScale,
                (worldPos.z + noiseOffset.z) * noiseScale
            );

            // apply displacement
            samplePos += PerlinDisplacement(samplePos);


            // Find integer cell coordinates
            Vector3Int baseCell = new Vector3Int(
                Mathf.FloorToInt(samplePos.x),
                Mathf.FloorToInt(samplePos.y),
                Mathf.FloorToInt(samplePos.z)
            );

            float f1 = float.MaxValue, f2 = float.MaxValue, f3 = float.MaxValue;

            // collect for debug printing
            string cellPointsString = "samplePos " + samplePos + " ->";

            // Check this cell + neighbors (3x3x3 = 27 cells)
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                Vector3Int cell = new Vector3Int(baseCell.x + dx, baseCell.y + dy, baseCell.z + dz);
                Vector3 featurePoint = GetCellPoint(cell);

                if (idx % printFrequency == 0)
                    cellPointsString += " " + featurePoint;

                float d = Vector3.Distance(samplePos, featurePoint);

                if (d < f1) { f3 = f2; f2 = f1; f1 = d; }
                else if (d < f2) { f3 = f2; f2 = d; }
                else if (d < f3) { f3 = d; }
                
            }

            if (idx % printFrequency == 0)
                Debug.Log("cellPointsString, " + cellPointsString);

            float caveDensityHeightOffset = -Mathf.Max(0f, worldPos.y/caveHeightFalloff);
            
            {
                float worleyValue = f1 / f3; // try also f2 - f1
                float density = 1f - worleyValue;
                float caveDensity = density - caveThreshold;
                caveDensity += caveDensityHeightOffset;
                field[idx] = caveDensity;

                if (idx % printFrequency == 0)
                {
                    Debug.Log($"caveDensity {caveDensity} worleyValue {worleyValue} density {density} f1 {f1} f2 {f2}");
                }
            }
        }

        if (generateDebugSlice)
            GenerateDebugTexture(field, fieldSize);

        return field;
    }

    
    // 3D Perlin noise displacement with fractal octaves
    private Vector3 PerlinDisplacement(Vector3 pos)
    {
        Vector3 displacement = Vector3.zero;
        float amplitude = displacementStrength;
        float frequency = displacementScale;

        for (int i = 0; i < octaves; i++)
        {
            float dx = Mathf.PerlinNoise(pos.y * frequency, pos.z * frequency);
            float dy = Mathf.PerlinNoise(pos.z * frequency, pos.x * frequency);
            float dz = Mathf.PerlinNoise(pos.x * frequency, pos.y * frequency);

            displacement += new Vector3(
                (dx * 2f - 1f) * amplitude,
                (dy * 2f - 1f) * amplitude,
                (dz * 2f - 1f) * amplitude
            );

            frequency *= lacunarity;   // increase frequency
            amplitude *= persistence; // decrease amplitude
        }

        return displacement;
    }




    void GenerateDebugTexture(float[] field, Vector3Int fieldSize)
    {
        int y = Mathf.Clamp(debugSliceY, 0, fieldSize.y - 1);
        debugTexture = new Texture2D(fieldSize.x, fieldSize.z, TextureFormat.RGB24, false);
        debugTexture.filterMode = FilterMode.Point;

        for (int z = 0; z < fieldSize.z; z++)
        for (int x = 0; x < fieldSize.x; x++)
        {
            int idx = (z * fieldSize.y * fieldSize.x) + (y * fieldSize.x) + x;
            float value = field[idx];
            float gray = Mathf.InverseLerp(-1f, 1f, value);
            debugTexture.SetPixel(x, z, new Color(gray, gray, gray, 1f));
        }

        debugTexture.Apply();
    }

    public override bool ValidateSettings()
    {
        if (!base.ValidateSettings()) return false;
        if (noiseScale <= 0) { Debug.LogError($"{GetGeneratorName()}: Noise scale must be positive!"); return false; }
        return true;
    }
}

