using UnityEngine;

public sealed class ChunkPropBatch
{
    const int ArgsStride = 5;

    public InstancingMesh MeshType { get; }
    public ComputeBuffer MatricesBuffer { get; private set; }
    public ComputeBuffer ArgsBuffer { get; private set; }
    public int InstanceCount { get; private set; }
    public Bounds Bounds { get; private set; }

    readonly Material _drawMaterial;
    readonly uint[] _args = new uint[ArgsStride];

    public ChunkPropBatch(InstancingMesh meshType, ComputeBuffer matricesBuffer, int instanceCount, Bounds bounds)
    {
        MeshType = meshType;
        MatricesBuffer = matricesBuffer;
        InstanceCount = instanceCount;
        Bounds = bounds;

        _args[0] = (uint)meshType.mesh.GetIndexCount(0);
        _args[1] = (uint)instanceCount;
        _args[2] = (uint)meshType.mesh.GetIndexStart(0);
        _args[3] = (uint)meshType.mesh.GetBaseVertex(0);
        _args[4] = 0;

        ArgsBuffer = new ComputeBuffer(1, sizeof(uint) * ArgsStride, ComputeBufferType.IndirectArguments);
        ArgsBuffer.SetData(_args);

        // MPB.SetBuffer is unreliable for SSBOs with DrawMeshInstancedIndirect on Metal.
        _drawMaterial = new Material(meshType.material);
        _drawMaterial.enableInstancing = true;
        _drawMaterial.SetBuffer("_InstanceMatrices", MatricesBuffer);
    }

    public void Draw(MaterialPropertyBlock propertyBlock)
    {
        if (MeshType == null || MeshType.mesh == null || _drawMaterial == null)
            return;
        if (InstanceCount <= 0 || MatricesBuffer == null || ArgsBuffer == null)
            return;

        Graphics.DrawMeshInstancedIndirect(
            MeshType.mesh,
            0,
            _drawMaterial,
            Bounds,
            ArgsBuffer,
            0,
            propertyBlock);
    }

    public void Dispose()
    {
        if (_drawMaterial != null)
            Object.Destroy(_drawMaterial);

        MatricesBuffer?.Release();
        ArgsBuffer?.Release();
        MatricesBuffer = null;
        ArgsBuffer = null;
    }
}
