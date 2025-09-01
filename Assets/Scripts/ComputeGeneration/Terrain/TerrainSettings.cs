using UnityEngine;

[CreateAssetMenu(fileName = "TerrainSettings", menuName = "Own/TerrainSettings")]
public class TerrainSettings : ScriptableObject
{
    public Vector3 scale;
    public Vector2Int heightmapDims;
    public float heightMultiplier;
}
