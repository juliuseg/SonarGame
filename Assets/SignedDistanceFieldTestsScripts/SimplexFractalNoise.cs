using UnityEngine;

/// <summary>
/// CPU-side equivalent of Keijiro's Simplex Noise (GLSL/Ashima version).
/// Matches the GPU version numerically.
/// </summary>
public class SimplexFractalNoise
{
    private readonly int octaves;
    private readonly float lacunarity;
    private readonly float persistence;
    private readonly float displacementScale;
    private readonly float displacementStrength;

    public SimplexFractalNoise(MCSettings settings)
    {
        this.octaves = Mathf.Max(1, settings.octaves);
        this.lacunarity = settings.lacunarity;
        this.persistence = settings.persistence;
        this.displacementScale = Mathf.Max(1e-6f, settings.displacementScale);
        this.displacementStrength = settings.displacementStrength;
    }

    public float Sample(Vector3 p)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;

        for (int i = 0; i < octaves; i++)
        {
            Vector3 q = p * (frequency / displacementScale);
            value += Snoise(q) * amplitude;
            frequency *= lacunarity;
            amplitude *= persistence;
        }

        return value * displacementStrength;
    }

    // --- Math below mirrors the shader implementation exactly ---
    private static Vector3 Mod289(Vector3 x)
    {
        return x - FloorDiv(x, 289.0f) * 289.0f;
    }

    private static Vector4 Mod289(Vector4 x)
    {
        return x - FloorDiv(x, 289.0f) * 289.0f;
    }

    private static Vector4 Permute(Vector4 x)
    {
        Vector4 t = x * 34.0f + Vector4.one;      // (x * 34 + 1)
        return Mod289(Vector4.Scale(t, x));       // component-wise multiply, then mod289
    }



    private static Vector4 TaylorInvSqrt(Vector4 r)
    {
        return new Vector4(
            1.79284291400159f - r.x * 0.85373472095314f,
            1.79284291400159f - r.y * 0.85373472095314f,
            1.79284291400159f - r.z * 0.85373472095314f,
            1.79284291400159f - r.w * 0.85373472095314f
        );
    }

    private static float Snoise(Vector3 v)
    {
        const float Cx = 1.0f / 6.0f;
        const float Cy = 1.0f / 3.0f;
        Vector2 C = new Vector2(Cx, Cy);

        Vector3 i = Floor(v + Vector3.one * Dot(v, new Vector3(C.y, C.y, C.y)));
        Vector3 x0 = v - i + Vector3.one * Dot(i, new Vector3(C.x, C.x, C.x));

        // explicit yzx reordering
        Vector3 x0_yzx = new Vector3(x0.y, x0.z, x0.x);
        Vector3 g = Step(x0_yzx, x0);
        Vector3 l = Vector3.one - g;
        Vector3 i1 = Vector3.Min(g, new Vector3(l.z, l.x, l.y));
        Vector3 i2 = Vector3.Max(g, new Vector3(l.z, l.x, l.y));

        Vector3 x1 = x0 - i1 + new Vector3(C.x, C.x, C.x);
        Vector3 x2 = x0 - i2 + new Vector3(C.y, C.y, C.y);
        Vector3 x3 = x0 - new Vector3(0.5f, 0.5f, 0.5f);

        i = Mod289(i);

        Vector4 p =
            Permute(
                Permute(
                    Permute(new Vector4(i.z, i.z + i1.z, i.z + i2.z, i.z + 1.0f))
                    + new Vector4(i.y, i.y + i1.y, i.y + i2.y, i.y + 1.0f))
                + new Vector4(i.x, i.x + i1.x, i.x + i2.x, i.x + 1.0f)
            );

        Vector4 j = p - 49.0f * FloorDiv(p, 49.0f);
        Vector4 x_ = FloorDiv(j, 7.0f);
        Vector4 y_ = j - 7.0f * x_;

        Vector4 x = (x_ * 2.0f + new Vector4(0.5f, 0.5f, 0.5f, 0.5f)) / 7.0f - Vector4.one;
        Vector4 y = (y_ * 2.0f + new Vector4(0.5f, 0.5f, 0.5f, 0.5f)) / 7.0f - Vector4.one;

        Vector4 h = Vector4.one - Abs(x) - Abs(y);

        Vector4 b0 = new Vector4(x.x, x.y, y.x, y.y);
        Vector4 b1 = new Vector4(x.z, x.w, y.z, y.w);

        Vector4 s0 = Floor(b0) * 2.0f + Vector4.one;
        Vector4 s1 = Floor(b1) * 2.0f + Vector4.one;
        Vector4 sh = Vector4.Scale(Step(Vector4.zero, h), new Vector4(-1f, -1f, -1f, -1f)); // -step(h,0)

        Vector4 a0 = new Vector4(
            b0.x + s0.x * sh.x,
            b0.z + s0.z * sh.x,
            b0.y + s0.y * sh.y,
            b0.w + s0.w * sh.y
        );

        Vector4 a1 = new Vector4(
            b1.x + s1.x * sh.z,
            b1.z + s1.z * sh.z,
            b1.y + s1.y * sh.w,
            b1.w + s1.w * sh.w
        );

        Vector3 g0 = new Vector3(a0.x, a0.y, h.x);
        Vector3 g1 = new Vector3(a0.z, a0.w, h.y);
        Vector3 g2 = new Vector3(a1.x, a1.y, h.z);
        Vector3 g3 = new Vector3(a1.z, a1.w, h.w);

        Vector4 norm = TaylorInvSqrt(new Vector4(
            Vector3.Dot(g0, g0),
            Vector3.Dot(g1, g1),
            Vector3.Dot(g2, g2),
            Vector3.Dot(g3, g3)
        ));

        g0 *= norm.x;
        g1 *= norm.y;
        g2 *= norm.z;
        g3 *= norm.w;

        Vector4 m = Max(new Vector4(0.6f, 0.6f, 0.6f, 0.6f) - new Vector4(
            Vector3.Dot(x0, x0),
            Vector3.Dot(x1, x1),
            Vector3.Dot(x2, x2),
            Vector3.Dot(x3, x3)
        ), Vector4.zero);

        m = Vector4.Scale(m, m);
        m = Vector4.Scale(m, m);

        Vector4 px = new Vector4(
            Vector3.Dot(x0, g0),
            Vector3.Dot(x1, g1),
            Vector3.Dot(x2, g2),
            Vector3.Dot(x3, g3)
        );

        return 42.0f * Vector4.Dot(m, px);
    }



    // --- Helper vector math ---

    private static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

    private static Vector3 Floor(Vector3 v) => new Vector3(Mathf.Floor(v.x), Mathf.Floor(v.y), Mathf.Floor(v.z));
    private static Vector4 Floor(Vector4 v) => new Vector4(Mathf.Floor(v.x), Mathf.Floor(v.y), Mathf.Floor(v.z), Mathf.Floor(v.w));
    private static Vector3 FloorDiv(Vector3 v, float d) => new Vector3(Mathf.Floor(v.x / d), Mathf.Floor(v.y / d), Mathf.Floor(v.z / d));
    private static Vector4 FloorDiv(Vector4 v, float d) => new Vector4(Mathf.Floor(v.x / d), Mathf.Floor(v.y / d), Mathf.Floor(v.z / d), Mathf.Floor(v.w / d));

    private static Vector3 Step(Vector3 edge, Vector3 x) => new Vector3(x.x >= edge.x ? 1f : 0f, x.y >= edge.y ? 1f : 0f, x.z >= edge.z ? 1f : 0f);
    private static Vector4 Step(Vector4 edge, Vector4 x) => new Vector4(x.x >= edge.x ? 1f : 0f, x.y >= edge.y ? 1f : 0f, x.z >= edge.z ? 1f : 0f, x.w >= edge.w ? 1f : 0f);
    private static Vector4 Max(Vector4 a, Vector4 b) => new Vector4(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z), Mathf.Max(a.w, b.w));
    private static Vector4 Abs(Vector4 v) => new Vector4(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z), Mathf.Abs(v.w));
    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
}
