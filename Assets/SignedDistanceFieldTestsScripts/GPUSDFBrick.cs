using UnityEngine;

public sealed class GPUSDFBrick : System.IDisposable
{
    // Hard cap to keep one-dispatch design with group-shared memory
    public const int MAX_SIZE = 8;

    readonly ComputeShader cs;
    readonly int kernel;

    readonly int size;
    readonly float voxelSize;
    readonly float isoLevel;
    readonly float displacementStrength;

    ComputeBuffer sdfBuf; // float[size^3]
    ComputeBuffer occBuf; // uint[size^3]

    Vector3 center = new Vector3(float.NaN, float.NaN, float.NaN);
    Vector3 origin;
    float[] sdfCPU; // cached after readback

    public float Radius => 0.5f * size * voxelSize;

    // shader param ids
    static readonly int _OriginID = Shader.PropertyToID("_Origin");
    static readonly int _VoxelSizeID = Shader.PropertyToID("_VoxelSize");
    static readonly int _SizeID = Shader.PropertyToID("_Size");
    static readonly int _IsoLevelID = Shader.PropertyToID("_IsoLevel");
    static readonly int _DisplacementStrengthID = Shader.PropertyToID("_DisplacementStrength");
    static readonly int _OutSDFID = Shader.PropertyToID("OutSDF");
    static readonly int _OutOccID = Shader.PropertyToID("OutOcc");

    public GPUSDFBrick(ComputeShader sdfBrickCS, MCSettings mcSettings, int size, float voxelSize)
    {
        if (size < 2 || size > MAX_SIZE) throw new System.ArgumentOutOfRangeException(nameof(size), $"size must be 2..{MAX_SIZE}");
        this.cs = sdfBrickCS;
        this.kernel = cs.FindKernel("BuildSDF");
        this.size = size;
        this.voxelSize = Mathf.Max(1e-4f, voxelSize);
        this.isoLevel = mcSettings.isoLevel;
        this.displacementStrength = mcSettings.displacementStrength;

        int n = size * size * size;
        sdfBuf = new ComputeBuffer(n, sizeof(float), ComputeBufferType.Structured);
        occBuf = new ComputeBuffer(n, sizeof(uint), ComputeBufferType.Structured);
        sdfCPU = new float[n];
    }

    public void Dispose()
    {
        sdfBuf?.Dispose(); sdfBuf = null;
        occBuf?.Dispose(); occBuf = null;
    }

    public void BuildAt(Vector3 worldCenter)
    {
        // one-dispatch build at given center
        center = worldCenter;
        origin = center - Vector3.one * Radius;

        cs.SetVector(_OriginID, origin);
        cs.SetFloat(_VoxelSizeID, voxelSize);
        cs.SetInt(_SizeID, size);
        cs.SetFloat(_IsoLevelID, isoLevel);
        cs.SetFloat(_DisplacementStrengthID, displacementStrength);
        cs.SetBuffer(kernel, _OutSDFID, sdfBuf);
        cs.SetBuffer(kernel, _OutOccID, occBuf);

        // Single group. Threads are [MAX_SIZE,MAX_SIZE,MAX_SIZE]. Kernel early-outs if ltid>=size.
        cs.Dispatch(kernel, 1, 1, 1);

        // Optional CPU readback for CPU-side sampling
        sdfBuf.GetData(sdfCPU);
    }

    // Bind for GPU consumers (e.g., physics, raymarch, etc.)
    public ComputeBuffer GetSDFBuffer() => sdfBuf;
    public ComputeBuffer GetOccBuffer() => occBuf;

    // Trilinear sample on CPU from cached array
    public float Sample(Vector3 worldPos)
    {
        if (float.IsNaN(center.x)) throw new System.InvalidOperationException("Call BuildAt first.");

        Vector3 local = (worldPos - origin) / voxelSize;
        int x0 = Mathf.Clamp(Mathf.FloorToInt(local.x), 0, size - 2);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(local.y), 0, size - 2);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(local.z), 0, size - 2);
        Vector3 t = local - new Vector3(x0, y0, z0);

        int S = size;
        int Idx(int x, int y, int z) => x + y * S + z * S * S;

        float c000 = sdfCPU[Idx(x0,     y0,     z0    )];
        float c100 = sdfCPU[Idx(x0 + 1, y0,     z0    )];
        float c010 = sdfCPU[Idx(x0,     y0 + 1, z0    )];
        float c110 = sdfCPU[Idx(x0 + 1, y0 + 1, z0    )];
        float c001 = sdfCPU[Idx(x0,     y0,     z0 + 1)];
        float c101 = sdfCPU[Idx(x0 + 1, y0,     z0 + 1)];
        float c011 = sdfCPU[Idx(x0,     y0 + 1, z0 + 1)];
        float c111 = sdfCPU[Idx(x0 + 1, y0 + 1, z0 + 1)];

        float c00 = Mathf.Lerp(c000, c100, t.x);
        float c10 = Mathf.Lerp(c010, c110, t.x);
        float c01 = Mathf.Lerp(c001, c101, t.x);
        float c11 = Mathf.Lerp(c011, c111, t.x);
        float c0  = Mathf.Lerp(c00,  c10,  t.y);
        float c1  = Mathf.Lerp(c01,  c11,  t.y);
        return Mathf.Lerp(c0, c1, t.z);
    }

    public Vector3 Origin => origin;
    public int Size => size;
    public float VoxelSize => voxelSize;
}
