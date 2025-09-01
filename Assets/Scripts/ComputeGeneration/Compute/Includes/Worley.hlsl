#ifndef WORLEY_PARAMS_HLSL
#define WORLEY_PARAMS_HLSL

// Worley parameters set from MCSettings
float _WorleyNoiseScale;
float _WorleyVerticalScale;
int _WorleySeed;
float _WorleyCaveHeightFalloff;

// --- uint hash, stable and fast ---
static uint Hash32(uint x) {
    x ^= x >> 16;  x *= 0x7FEB352Du;
    x ^= x >> 15;  x *= 0x846CA68Bu;
    x ^= x >> 16;  return x;
}

static float3 GetCellPoint(int3 cell, uint seed) {
    uint h = Hash32((uint)cell.x ^ seed);
    h = Hash32(h ^ (uint)cell.y * 0x85EBCA6Bu);
    h = Hash32(h ^ (uint)cell.z * 0xC2B2AE35u);
    // extract 3 bytes -> [0,1)
    const float inv255 = 1.0/255.0;
    float jx = float( ( h        & 0xFFu) ) * inv255;
    float jy = float( ((h >>  8) & 0xFFu) ) * inv255;
    float jz = float( ((h >> 16) & 0xFFu) ) * inv255;
    return float3(cell) + float3(jx, jy, jz);
}

// Return squared distances F1^2, F2^2, F3^2 (cheaper)
static float3 WorleyF123Sq(float3 p, uint seed) {
    int3 baseCell = (int3)floor(p);
    float3 f = float3(1e30, 1e30, 1e30);   // big
    [unroll] for (int dz=-1; dz<=1; dz++)
    [unroll] for (int dy=-1; dy<=1; dy++)
    [unroll] for (int dx=-1; dx<=1; dx++) {
        int3 c = baseCell + int3(dx,dy,dz);
        float3 d = GetCellPoint(c, seed) - p;
        float d2 = dot(d,d);               // no sqrt
        // sorted insert into f.x<=f.y<=f.z
        if (d2 < f.x) { f.z = f.y; f.y = f.x; f.x = d2; }
        else if (d2 < f.y) { f.z = f.y; f.y = d2; }
        else if (d2 < f.z) { f.z = d2; }
    }
    return f;
}

// Matches your CPU pipeline but with one sqrt total
static float SampleWorleyCaves(float3 pWS) {
    float3 s = pWS * _WorleyNoiseScale;
    s.y *= _WorleyVerticalScale;

    float3 Fsq = WorleyF123Sq(s, (uint)_WorleySeed);          // F1^2, F2^2, F3^2
    // worleyValue = F1/F3, but compute with squared values: sqrt(F1^2/F3^2)
    float ratio = Fsq.x / max(Fsq.z, 1e-12);
    float worleyValue = sqrt(ratio);              // single sqrt
    float density = 1.0 - worleyValue;
    float heightBias = -max(0.0, pWS.y / _WorleyCaveHeightFalloff);
    return density + heightBias;                     // iso = 0
}

#endif
