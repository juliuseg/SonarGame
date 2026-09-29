using UnityEngine;

[CreateAssetMenu(fileName = "DismantleToolSettings", menuName = "Own/DismantleToolSettings")]
public class DismantleToolSettings : ScriptableObject
{
    [Header("Raycast")]
    public LayerMask layerMask;
    public float maxRayDistance = 100f;

    [Header("Pointer")]
    public GameObject pointerPrefab;
    public float pointerWorldScale = 1f;
}
