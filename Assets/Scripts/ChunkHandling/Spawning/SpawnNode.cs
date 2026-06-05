using UnityEngine;

[CreateAssetMenu(fileName = "SpawnNode", menuName = "Own/SpawnNode")]
public class SpawnNode : ScriptableObject
{
    public GameObject prefab;
    public float scale = 1f;
    public float scaleOffset = 0f;
    [Range(0f, 1f)] public float verticalBias = 0f;
    public float yOffset = 0f;
    public float probability = 1f;
}
