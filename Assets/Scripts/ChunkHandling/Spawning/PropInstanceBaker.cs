using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public sealed class PropInstanceBaker
{
    const int SpawnPointStride = sizeof(float) * 8;

    [StructLayout(LayoutKind.Sequential)]
    struct GpuSpawnPoint
    {
        public Vector4 positionWS;
        public Vector4 normalWS;
    }

    readonly ComputeShader _buildShader;
    readonly int _kernelBuild;

    ComputeBuffer _spawnPointsBuffer;

    public PropInstanceBaker(ComputeShader buildShader, MCSettings mcSettings, ChunkManager chunkManager)
    {
        _buildShader = buildShader;
        _kernelBuild = buildShader != null ? buildShader.FindKernel("BuildPropMatrices") : -1;
    }

    public void BeginBake()
    {
        ReleaseSpawnScratch();
    }

    public ChunkPropBatch TryBakeBatch(InstancingMesh meshType, List<SpawnPoint> spawnPoints, Bounds bounds)
    {
        if (_buildShader == null || _kernelBuild < 0)
            return null;

        return BakeBatch(meshType, spawnPoints, bounds);
    }

    public void EndBake()
    {
        ReleaseSpawnScratch();
    }

    ChunkPropBatch BakeBatch(InstancingMesh meshType, List<SpawnPoint> spawnPoints, Bounds bounds)
    {
        if (meshType == null || meshType.mesh == null || meshType.material == null)
            return null;
        if (spawnPoints == null || spawnPoints.Count == 0)
            return null;

        int count = spawnPoints.Count;
        EnsureSpawnBufferCapacity(count);

        var gpuPoints = new GpuSpawnPoint[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = spawnPoints[i].positionWS;
            Vector3 normal = spawnPoints[i].normalWS;
            gpuPoints[i] = new GpuSpawnPoint
            {
                positionWS = new Vector4(pos.x, pos.y, pos.z, 0f),
                normalWS = new Vector4(normal.x, normal.y, normal.z, 0f)
            };
        }

        _spawnPointsBuffer.SetData(gpuPoints);

        var matricesBuffer = new ComputeBuffer(count, sizeof(float) * 16);
        _buildShader.SetBuffer(_kernelBuild, "_SpawnPoints", _spawnPointsBuffer);
        _buildShader.SetBuffer(_kernelBuild, "_InstanceMatrices", matricesBuffer);
        _buildShader.SetInt("_Count", count);
        _buildShader.SetFloat("_Scale", meshType.scale);
        _buildShader.SetFloat("_ScaleOffset", meshType.scaleOffset);
        _buildShader.SetFloat("_VerticalBias", meshType.verticalBias);
        _buildShader.SetFloat("_YOffset", meshType.yOffset);

        int groups = Mathf.CeilToInt(count / 64f);
        _buildShader.Dispatch(_kernelBuild, groups, 1, 1);

        return new ChunkPropBatch(meshType, matricesBuffer, count, bounds);
    }

    void EnsureSpawnBufferCapacity(int spawnCount)
    {
        if (_spawnPointsBuffer != null && _spawnPointsBuffer.count >= spawnCount)
            return;

        _spawnPointsBuffer?.Release();
        _spawnPointsBuffer = new ComputeBuffer(Mathf.Max(spawnCount, 1), SpawnPointStride);
    }

    void ReleaseSpawnScratch()
    {
        _spawnPointsBuffer?.Release();
        _spawnPointsBuffer = null;
    }

    public void Dispose()
    {
        ReleaseSpawnScratch();
    }
}
