using UnityEngine;

[CreateAssetMenu(fileName = "PathfindingGraphSettings", menuName = "Settings/PathfindingGraphSettings")]
public class PathfindingGraphSettings : ScriptableObject
{
    [Header("Raycast")]
    public LayerMask wallMask = 1 << 6;

    [Header("Grid")]
    public float hexLength = 1.5f;
    public float rayLength = 2f;
    public float maxExploreDistance = 30f;
    [Min(1)] public int maxNodes = 5000;

    [Header("Async")]
    [Tooltip("Max nodes processed per frame. All their raycasts are batched into one job.")]
    [Min(1)] public int batchSize = 64;
}
