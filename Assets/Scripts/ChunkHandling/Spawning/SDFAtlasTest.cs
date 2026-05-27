using UnityEngine;
using UnityEngine.Rendering;

public class SDFAtlasTest : MonoBehaviour
{
    [Header("References")]
    public Transform samplePoint;
    public ComputeShader testShader;

    [Header("Debug")]
    public float gpuSdfValue;
    public float cpuSdfValue;

    private ChunkManager _chunkManager;
    private SDFAtlas _atlas;
    private MCSettings _mcSettings;

    private ComputeBuffer _resultBuffer;
    private int _kernel;
    private bool _readbackPending;

    public void Init(ChunkManager chunkManager, SDFAtlas atlas, MCSettings mcSettings)
    {
        _chunkManager = chunkManager;
        _atlas = atlas;
        _mcSettings = mcSettings;

        _kernel = testShader.FindKernel("SampleSDF");
        _resultBuffer = new ComputeBuffer(1, sizeof(float));
    }

    void Update()
    {
        if (_chunkManager == null || _atlas == null || samplePoint == null) return;
        if (_readbackPending) return;

        Vector3 pos = samplePoint.position;
        Vector3 chunkSize = Vector3.Scale(_mcSettings.scale, _mcSettings.chunkDims);

        // CPU reference value
        _chunkManager.TryGetSDFValue(pos, out cpuSdfValue);

        // GPU lookup via atlas
        testShader.SetBuffer(_kernel, "_Lookup", _atlas.LookupBuffer);
        testShader.SetBuffer(_kernel, "_Atlas", _atlas.AtlasBuffer);
        testShader.SetBuffer(_kernel, "_Result", _resultBuffer);
        testShader.SetVector("_SamplePos", pos);
        testShader.SetVector("_ChunkSize", chunkSize);
        testShader.SetVector("_Scale", _mcSettings.scale);
        testShader.SetInts("_ChunkDims", _mcSettings.chunkDims.x, _mcSettings.chunkDims.y, _mcSettings.chunkDims.z);
        testShader.SetInt("_SlotSize", _atlas.SlotSize);
        testShader.SetInt("_LookupCount", _atlas.MaxSlots);

        testShader.Dispatch(_kernel, 1, 1, 1);

        _readbackPending = true;
        AsyncGPUReadback.Request(_resultBuffer, req =>
        {
            _readbackPending = false;
            if (req.hasError) return;
            gpuSdfValue = req.GetData<float>()[0];
            Debug.Log($"GPU SDF: {gpuSdfValue:F4} | CPU SDF: {cpuSdfValue:F4} | diff: {Mathf.Abs(gpuSdfValue - cpuSdfValue):F4}");
        });
    }

    void OnDestroy()
    {
        _resultBuffer?.Release();
    }
}