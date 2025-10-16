using UnityEngine;

[CreateAssetMenu(fileName = "InstancingMesh", menuName = "Own/InstancingMesh")]
public class InstancingMesh : ScriptableObject
{
    public Mesh mesh;
    public Material material;
    public float scale = 1f;
    public float scaleOffset = 0f;

    public float yOffset = 0f;
    public float probability = 1f;
}

