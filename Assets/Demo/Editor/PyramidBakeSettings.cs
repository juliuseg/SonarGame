using UnityEngine;

[CreateAssetMenu(fileName = "PyramidBakeSettings", menuName = "Own/PyramidBakeSettings")]
public class PyramidBakeSettings : ScriptableObject
{
    public Mesh sourceMesh;
    public int sourceSubMeshIndex;
    public Vector3 scale;
    public Vector3 rotation;
    public float pyramidHeight;
}
