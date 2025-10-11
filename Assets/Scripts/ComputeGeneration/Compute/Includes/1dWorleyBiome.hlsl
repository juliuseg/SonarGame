// Simple deterministic hash → random 0–1
float hash1(float n)
{
    return frac(sin(n * 127.1) * 43758.5453123);
}

float4 worley1D_biome_buffered(float x, float borderFrac)
{
    float i = floor(x);
    float f = frac(x);

    float F1 = 1.0;
    float F2 = 1.0;
    float biome1 = 0.0;
    float biome2 = 0.0;

    float innerMin = borderFrac;
    float innerMax = 1.0 - borderFrac;
    float innerSize = innerMax - innerMin;

    [unroll]
    for (int offset = -1; offset <= 1; offset++)
    {
        float cell = i + offset;

        // Random feature point inside the *inner zone* only
        float feature = innerMin + hash1(cell) * innerSize;

        // Actual world position of that feature
        float featurePos = offset + feature;

        float d = abs(featurePos - f);

        // Stable biome ID
        float biome = floor(hash1(cell + 13.37) * 3.0);
        
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

    return float4(F1, F2, biome1, biome2);
}
