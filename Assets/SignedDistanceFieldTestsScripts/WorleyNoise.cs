using UnityEngine;

public class WorleyNoise
{
    private float noiseScale;
    private float verticalScale;
    private uint seed;
    private float caveHeightFalloff;

    public WorleyNoise(MCSettings settings)
    {
        noiseScale = settings.noiseScale;
        verticalScale = settings.verticalScale;
        seed = settings.seed;
        caveHeightFalloff = settings.caveHeightFalloff;
    }

    // Hash function matching HLSL
    private uint Hash32(uint x)
    {
        x ^= x >> 16;  x *= 0x7FEB352Du;
        x ^= x >> 15;  x *= 0x846CA68Bu;
        x ^= x >> 16;  return x;
    }

    private Vector3 GetCellPoint(Vector3Int cell, uint seed)
    {
        uint h = Hash32((uint)cell.x ^ seed);
        h = Hash32(h ^ (uint)cell.y * 0x85EBCA6Bu);
        h = Hash32(h ^ (uint)cell.z * 0xC2B2AE35u);
        const float inv255 = 1.0f / 255.0f;
        float jx = (h        & 0xFFu) * inv255;
        float jy = ((h >>  8) & 0xFFu) * inv255;
        float jz = ((h >> 16) & 0xFFu) * inv255;
        return new Vector3(cell.x + jx, cell.y + jy, cell.z + jz);
    }

    // Returns F1^2, F2^2, F3^2
    private Vector3 WorleyF123Sq(Vector3 p, uint seed)
    {
        Vector3Int baseCell = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
        float f1 = float.MaxValue, f2 = float.MaxValue, f3 = float.MaxValue;
        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            Vector3Int c = baseCell + new Vector3Int(dx, dy, dz);
            Vector3 d = GetCellPoint(c, seed) - p;
            float d2 = Vector3.Dot(d, d);
            // sorted insert into f1 <= f2 <= f3
            if (d2 < f1) { f3 = f2; f2 = f1; f1 = d2; }
            else if (d2 < f2) { f3 = f2; f2 = d2; }
            else if (d2 < f3) { f3 = d2; }
        }
        return new Vector3(f1, f2, f3);
    }

    // Matches SampleWorleyCaves in HLSL
    public float Sample(Vector3 pWS)
    {
        Vector3 s = pWS * noiseScale;
        s.y *= verticalScale;
        Vector3 Fsq = WorleyF123Sq(s, seed);
        float ratio = Fsq.x / Mathf.Max(Fsq.z, 1e-12f);
        float worleyValue = Mathf.Sqrt(ratio);
        float density = 1.0f - worleyValue;
        float heightBias = -Mathf.Max(0.0f, pWS.y / caveHeightFalloff);
        return density + heightBias;
    }
}
