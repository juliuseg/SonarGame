using UnityEngine;
using UnityEngine.Rendering;
using System.Diagnostics;

public class TestReadback : MonoBehaviour
{
    [Header("Compute setup")]
    public ComputeShader shader;

    [Header("Buffer configuration")]
    public int chunkX = 32;
    public int chunkY = 32;
    public int chunkZ = 32;
    public int trianglesPerCell = 5;
    public int triangleStride = 48;   // bytes per triangle
    public int headerBytes = 16;      // same as HEADER_BYTES
    public bool recreateBuffersEachFrame = true;

    ComputeBuffer headerBuf;
    ComputeBuffer triBuf;
    ComputeBuffer combinedBuf;
    ComputeBuffer triCountBuf;

    int kernel;
    Stopwatch timer;

    void Start()
    {
        kernel = shader.FindKernel("Main");
        timer = new Stopwatch();
    }

    void Update()
    {
        int maxCells = chunkX * chunkY * chunkZ;
        int maxTriangles = maxCells * trianglesPerCell;
        int combinedBytes = headerBytes + maxTriangles * triangleStride;
        int combinedCount = Mathf.CeilToInt(combinedBytes / 4f); // 4 bytes per element

        if (recreateBuffersEachFrame || combinedBuf == null)
        {
            ReleaseBuffers();

            headerBuf = new ComputeBuffer(1, sizeof(uint) * 2, ComputeBufferType.Structured);
            triBuf = new ComputeBuffer(maxTriangles, triangleStride, ComputeBufferType.Append);
            triBuf.SetCounterValue(0);
            combinedBuf = new ComputeBuffer(combinedCount, 4, ComputeBufferType.Raw);
            triCountBuf = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
        }

        shader.SetBuffer(kernel, "Result", combinedBuf);

        timer.Restart();
        shader.Dispatch(kernel, 1, 1, 1);

        AsyncGPUReadback.Request(combinedBuf, req =>
        {
            timer.Stop();
            if (req.hasError)
            {
                UnityEngine.Debug.LogWarning("Readback error");
                return;
            }

            float elapsed = (float)timer.Elapsed.TotalMilliseconds;
            float mb = combinedBytes / 1_000_000f;
            UnityEngine.Debug.Log($"Buffer {mb:F1} MB | {maxTriangles:N0} tris | Readback total: {elapsed:F2} ms");
        });
    }

    void ReleaseBuffers()
    {
        headerBuf?.Release(); headerBuf = null;
        triBuf?.Release(); triBuf = null;
        combinedBuf?.Release(); combinedBuf = null;
        triCountBuf?.Release(); triCountBuf = null;
    }

    void OnDestroy() => ReleaseBuffers();
}
