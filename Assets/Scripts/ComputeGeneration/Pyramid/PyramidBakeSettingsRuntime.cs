using UnityEngine;

[CreateAssetMenu(fileName = "PyramidBakeSettingsRuntime", menuName = "Own/PyramidBakeSettingsRuntime")]
public class PyramidBakeSettingsRuntime : ScriptableObject
{
    public Mesh sourceMesh;
    public int sourceSubMeshIndex;
    public Vector3 scale;
    public Vector3 rotation;
    public float pyramidHeight;
}
