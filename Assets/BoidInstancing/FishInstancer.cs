using UnityEngine;

public class FishInstancer : MonoBehaviour
{
    [SerializeField] private Mesh fishMesh;
    [SerializeField] private Material[] fishMaterials;   // 4 materials in correct submesh order
    [SerializeField] private int instanceCount = 10;
    [SerializeField] private float[] speeds;             // same length as instanceCount
    [SerializeField] private float[] phases;             // same length as instanceCount
    [SerializeField] private string phaseProperty = "_Phase";
    [SerializeField] private float spacing = 1f;
    private Matrix4x4[] matrices;
    private MaterialPropertyBlock propertyBlock;

    void Start()
    {
        if (speeds == null || speeds.Length != instanceCount)
            speeds = new float[instanceCount];
        if (phases == null || phases.Length != instanceCount)
            phases = new float[instanceCount];

        // Initialize random phases and speeds if not already set
        for (int i = 0; i < instanceCount; i++)
        {
            if (speeds[i] == 0f)
                speeds[i] = Random.Range(1f, 30f); //(i % 2 == 0) ? 1f : 10f;
            phases[i] = Random.Range(0f, Mathf.PI * 2f);
        }

        matrices = new Matrix4x4[instanceCount];

        int dimension = Mathf.CeilToInt(Mathf.Pow(instanceCount, 1f / 3f));
        for (int i = 0; i < instanceCount; i++)
        {
            int x = i % dimension;
            int y = (i / dimension) % dimension;
            int z = i / (dimension * dimension);

            Vector3 pos = new Vector3(x * spacing, y * spacing, z * spacing);
            // Set a random heading by generating random Euler angles and converting to a Quaternion
            Vector3 randomEuler = Random.onUnitSphere;
            randomEuler = randomEuler.normalized * 360f; // Each component [-360,360] (not strictly Euler, but gives variety)
            Quaternion randomRotation = Quaternion.Euler(randomEuler);
            matrices[i] = Matrix4x4.TRS(pos, randomRotation, Vector3.one);
        }

        propertyBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        // Update per-instance phase array
        for (int i = 0; i < instanceCount; i++)
            phases[i] += Time.deltaTime * speeds[i];

        // GPU instanced draw for each submesh/material
        for (int s = 0; s < fishMaterials.Length; s++)
        {
            // Use property block array for per-instance data
            propertyBlock.Clear();
            propertyBlock.SetFloatArray(phaseProperty, phases);

            Graphics.DrawMeshInstanced(
                fishMesh,
                s,
                fishMaterials[s],
                matrices,
                instanceCount,
                propertyBlock,
                UnityEngine.Rendering.ShadowCastingMode.On,
                true,
                0,
                null,
                UnityEngine.Rendering.LightProbeUsage.BlendProbes,
                null
            );
        }
    }
}
