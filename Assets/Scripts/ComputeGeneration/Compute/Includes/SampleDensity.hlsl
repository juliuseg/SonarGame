#ifndef SAMPLE_DENSITY_INCLUDED
#define SAMPLE_DENSITY_INCLUDED

#include "Includes/Noise.compute"
#include "Includes/Worley.hlsl"
#include "Includes/3dWorleyBiome.hlsl"

// Shared parameters
float _DisplacementStrength;
float _DisplacementScale;
int   _Octaves;
float _Lacunarity;
float _Persistence;

float _BiomeScale;
float _BiomeBorder;
float _BiomeDisplacementStrength;
float _BiomeDisplacementScale;

StructuredBuffer<float> _BiomeDensityOffsets;

struct TerraformEdit {
    float3 position;
    float strength;
    float radius;
};
 
StructuredBuffer<TerraformEdit> _TerraformEdits;
int _TerraformEditsCount; 



// ---- Density helpers ----
float SampleFractalNoise(float3 p)
{
    float value = 0;
    float amplitude = 1.0;
    float frequency = 1.0;

    for (int i = 0; i < _Octaves; i++)
    {
        value += snoise(p * frequency / _DisplacementScale) * amplitude;
        frequency *= _Lacunarity;
        amplitude *= _Persistence;
    }
    return value * _DisplacementStrength;
}

float GetDensityOffsetForBiome(float id)
{
    uint i = (uint)id;
    return _BiomeDensityOffsets[i];
    
}

float3 GetBiomeVertexColor(float3 pos)
{
    float scale = 1.0 / _BiomeScale;
    float border = _BiomeBorder / _BiomeScale;
    float displacement = snoise(pos * _BiomeDisplacementScale) * _BiomeDisplacementStrength;
    float4 w = worley3D_biome_buffered(pos, scale, displacement, border);

    float diff = w.y - w.x;
    float blend = saturate(1.0 - diff / border) * 0.5;

    return float3(w.z, w.w, blend);
}

float SampleTerraformEdits(float3 p)
{
    float densityDelta = 0.0;

    for (int i = 0; i < _TerraformEditsCount; i++)
    {
        TerraformEdit edit = _TerraformEdits[i];
        float3 d = p - edit.position;
        float dist = length(d);

        if (dist < edit.radius)
        {
            // 1.0 at center, 0.0 at edge
            float t = 1.0 - saturate(dist / edit.radius);


            densityDelta += edit.strength * t;
        }
    }

    return densityDelta;
}

float SampleDensity(float3 p, out bool terrainEdit)
{
    
    float worley = SampleWorleyCaves(p);
    float displacement = SampleFractalNoise(p);
    float3 biome = GetBiomeVertexColor(p);
    float biomeOffset = lerp(GetDensityOffsetForBiome(biome.x),
                             GetDensityOffsetForBiome(biome.y),
                             biome.z);
    float terraformEdits = SampleTerraformEdits(p); // We could check if not zero and if we assign another biome value so its rock like or whatever. 
    terrainEdit = terraformEdits != 0.0;

    return worley + displacement + biomeOffset+ terraformEdits;
}

 

#endif
