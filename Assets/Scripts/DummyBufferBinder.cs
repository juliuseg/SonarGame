using UnityEngine;

[ExecuteAlways]
public class DummyBufferBinder : MonoBehaviour
{
    public Material material;
    public string bufferName = "_AddNumber"; // match HLSL name

    static ComputeBuffer dummy;

    void OnEnable()
    {
        EnsureDummy();
        if (material != null)
            material.SetBuffer(bufferName, dummy);
    }

    void OnDisable()
    {
        // keep dummy alive globally, don’t Release here
    }

    static void EnsureDummy()
    {
        if (dummy == null)
        {
            dummy = new ComputeBuffer(1, sizeof(float));
            dummy.SetData(new float[1]{0});
        }
    }

    void OnDestroy()
    {
        // optional: Release globally when domain reload ends
        // if (dummy != null) { dummy.Release(); dummy = null; }
    }
}
