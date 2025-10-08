using UnityEngine;

/// Local SDF centered near the last sampled position.
/// Rebuilds only when you move > rebuildThreshold from the last center.
/// Distance is computed by a 3D exact EDT to the occupancy boundary.
public class SDFBrick
{
    // config
    readonly float voxelSize;
    readonly int size;
    readonly float rebuildThreshold; // meters you can move before rebuild
    readonly MCSettings settings;
    readonly float isoLevel;
    readonly WorleyNoise worley;
    readonly SimplexFractalNoise simplexFractal;

    // state
    Vector3 center = new Vector3(float.NaN, float.NaN, float.NaN);
    Vector3 origin;
    float[,,] sdf;        // signed distance in meters
    bool[,,] occ;         // occupancy (true = wall/solid)

    public float Radius => 0.5f * size * voxelSize; // half-extent in meters

    public SDFBrick(MCSettings settings, float voxelSize, int size, float rebuildThresholdMeters = 2f, float isoLevelOffset = 0)
    {
        this.settings = settings;
        this.voxelSize = Mathf.Max(1e-4f, voxelSize);
        this.size = Mathf.Max(4, size);
        this.rebuildThreshold = Mathf.Max(0.1f, rebuildThresholdMeters);
        this.worley = new WorleyNoise(settings);
        this.simplexFractal = new SimplexFractalNoise(settings);
        this.isoLevel = settings.isoLevel + isoLevelOffset;
        sdf = new float[this.size, this.size, this.size];
        occ = new bool[this.size, this.size, this.size];
    }

    // Define "solid" here. Return true for WALL/OBSTACLE.
    bool IsWall(Vector3 p)
    {
        // Match compute SampleDensity: worley + (fractal * _DisplacementStrength)
        float worleyVal = worley.Sample(p);
        // float fractalDisplacement = simplexFractal.Sample(p);
        float density = worleyVal + 0f; //+ fractalDisplacement;
        return density > isoLevel;
    }

    void EnsureBuilt(Vector3 pos)
    {
        if (float.IsNaN(center.x) || Vector3.Distance(pos, center) > rebuildThreshold)
        {
            center = pos;
            origin = center - Vector3.one * Radius;
            BuildField();
        }
    }

    void BuildField()
    {
        // 1) Occupancy at voxel centers
        for (int z = 0; z < size; z++)
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            Vector3 p = origin + new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * voxelSize;
            occ[x, y, z] = IsWall(p);
        }

        // 2) Surface mask: voxels adjacent (6-neighbors) to a change in occ
        const float INF = 1e20f;
        // We’ll store squared-voxel distances during EDT
        float[,,] d2 = new float[size, size, size];
        for (int z = 0; z < size; z++)
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool o = occ[x, y, z];
            bool isSurface = false;
            if (x > 0)         isSurface |= occ[x - 1, y, z] != o;
            if (x < size - 1)  isSurface |= occ[x + 1, y, z] != o;
            if (y > 0)         isSurface |= occ[x, y - 1, z] != o;
            if (y < size - 1)  isSurface |= occ[x, y + 1, z] != o;
            if (z > 0)         isSurface |= occ[x, y, z - 1] != o;
            if (z < size - 1)  isSurface |= occ[x, y, z + 1] != o;

            d2[x, y, z] = isSurface ? 0f : INF;
        }

        // 3) Exact 3D Euclidean Distance Transform (Felzenszwalb & Huttenlocher)
        // Pass X
        float[] f = new float[size];
        float[] g = new float[size];
        for (int y = 0; y < size; y++)
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++) f[x] = d2[x, y, z];
            EDT1D(f, size, g);
            for (int x = 0; x < size; x++) d2[x, y, z] = g[x];
        }
        // Pass Y
        for (int x = 0; x < size; x++)
        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < size; y++) f[y] = d2[x, y, z];
            EDT1D(f, size, g);
            for (int y = 0; y < size; y++) d2[x, y, z] = g[y];
        }
        // Pass Z
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        {
            for (int z = 0; z < size; z++) f[z] = d2[x, y, z];
            EDT1D(f, size, g);
            for (int z = 0; z < size; z++) d2[x, y, z] = g[z];
        }

        // 4) Convert to signed meters. By convention: inside wall = negative
        for (int z = 0; z < size; z++)
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float unsignedMeters = Mathf.Sqrt(d2[x, y, z]) * voxelSize;
            float sign = occ[x, y, z] ? -1f : +1f; // wall negative
            sdf[x, y, z] = sign * unsignedMeters;
        }
    }

    // 1D exact squared-distance transform (Felzenszwalb & Huttenlocher).
    // Input f: 0 for surface, INF elsewhere. Output d: squared distance.
    static void EDT1D(float[] f, int n, float[] d)
    {
        int[] v = new int[n];
        float[] z = new float[n + 1];
        int k = 0;
        v[0] = 0;
        z[0] = -1e20f;
        z[1] = +1e20f;

        for (int q = 1; q < n; q++)
        {
            float s;
            int p;
            while (true)
            {
                p = v[k];
                // intersection of parabolas at p and q
                s = ((f[q] + q * q) - (f[p] + p * p)) / (2f * (q - p));
                if (s > z[k]) break;
                k--;
                if (k < 0) { k = 0; v[0] = q; z[0] = -1e20f; z[1] = +1e20f; s = -1e20f; break; }
            }
            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = +1e20f;
        }

        int kk = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[kk + 1] < q) kk++;
            int p = v[kk];
            float diff = q - p;
            d[q] = diff * diff + f[p];
        }
    }

    public float Sample(Vector3 worldPos)
    {
        EnsureBuilt(worldPos);

        // local coordinates in voxel units
        Vector3 local = (worldPos - origin) / voxelSize;
        int x0 = Mathf.FloorToInt(local.x);
        int y0 = Mathf.FloorToInt(local.y);
        int z0 = Mathf.FloorToInt(local.z);

        // clamp to valid cell range (need +1 on each axis for trilinear)
        x0 = Mathf.Clamp(x0, 0, size - 2);
        y0 = Mathf.Clamp(y0, 0, size - 2);
        z0 = Mathf.Clamp(z0, 0, size - 2);

        Vector3 t = local - new Vector3(x0, y0, z0);

        float c000 = sdf[x0,     y0,     z0    ];
        float c100 = sdf[x0 + 1, y0,     z0    ];
        float c010 = sdf[x0,     y0 + 1, z0    ];
        float c110 = sdf[x0 + 1, y0 + 1, z0    ];
        float c001 = sdf[x0,     y0,     z0 + 1];
        float c101 = sdf[x0 + 1, y0,     z0 + 1];
        float c011 = sdf[x0,     y0 + 1, z0 + 1];
        float c111 = sdf[x0 + 1, y0 + 1, z0 + 1];

        float c00 = Mathf.Lerp(c000, c100, t.x);
        float c10 = Mathf.Lerp(c010, c110, t.x);
        float c01 = Mathf.Lerp(c001, c101, t.x);
        float c11 = Mathf.Lerp(c011, c111, t.x);
        float c0  = Mathf.Lerp(c00,  c10,  t.y);
        float c1  = Mathf.Lerp(c01,  c11,  t.y);
        return Mathf.Lerp(c0, c1, t.z);
    }
}
