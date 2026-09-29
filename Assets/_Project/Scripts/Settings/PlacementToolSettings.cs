using UnityEngine;

[CreateAssetMenu(fileName = "PlacementToolSettings", menuName = "Own/PlacementToolSettings")]
public class PlacementToolSettings : ScriptableObject
{
    public GameObject pointerPrefab;
    public Material placementErrorMaterial;
    public Material placementPossibleMaterial;
    public float snapDistance = 4f;
    public float maxRayDistance = 1000f;
    public float pointerWorldScale = 1f;
}
