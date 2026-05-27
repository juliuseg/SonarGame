// Simple deterministic 3D hash → random 0–1
float hash3(float3 n)
{
    return frac(sin(dot(n, float3(127.1, 311.7, 74.7))) * 43758.5453123);
}

// 3D Worley with buffer zone and biome IDs
// Returns F1, F2, biome1, biome2
float4 worley3D_biome_buffered(float3 p, float scale, float displacement, float borderFrac, float sandYThreshold = 25)
{
    p = float3(p.x, p.y+sandYThreshold, p.z);
    int3 i = floor(p * scale + displacement);
    float3 f = frac(p * scale + displacement);

    float F1 = 1.0;
    float F2 = 1.0;
    float biome1 = 0.0;
    float biome2 = 0.0;

    float innerMin = borderFrac;
    float innerMax = 1.0 - borderFrac;
    float innerSize = innerMax - innerMin;

    // [unroll]
    for (int xo = -1; xo <= 1; xo++)
    {
        // [unroll]
        for (int yo = -1; yo <= 1; yo++)
        {
            // [unroll]
            for (int zo = -1; zo <= 1; zo++)
            {
                float3 cell = i + int3(xo, yo, zo);

                // Random feature inside inner cube
                float3 feature = innerMin + float3(
                    hash3(cell + 13.1),
                    hash3(cell + 17.7),
                    hash3(cell + 19.3)
                ) * innerSize;

                float3 featurePos = (float3(xo, yo, zo) + feature);
                float d = length(featurePos - f);

                // Stable biome ID [0, 1, 2]
                float biome = 0.0;
                if (cell.y < 0)
                {
                    biome = 1.0+floor(hash3(cell + 37.37) * 3.0);
                }

                if (d < F1)
                {
                    F2 = F1;
                    biome2 = biome1;
                    F1 = d;
                    biome1 = biome;
                }
                else if (d < F2)
                {
                    F2 = d;
                    biome2 = biome;
                }
            }
        }
    }

    return float4(F1, F2, biome1, biome2);
}
