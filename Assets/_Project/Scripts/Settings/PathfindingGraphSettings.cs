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

    [Header("Clearance")]
    [Tooltip("A ray this long is cast from each candidate point along its normal. If it hits a wall, the point is rejected. 0 = off.")]
    [Min(0f)] public float clearanceRayLength = 1f;

    [Header("Cleanup")]
    [Tooltip("After building: merge crowded nodes, then throw away all edges and relink the survivors from scratch.")]
    public bool rebuildEdgesAfterBuild = true;
    [Tooltip("Nodes closer than this (fraction of hexLength) that face the same way are merged into one.")]
    [Min(0f)] public float mergeRadius = 0.6f;
    [Tooltip("An original link (remapped after merging) is kept only if its ends are within this distance (fraction of hexLength). Links the original build didn't make are never added.")]
    [Min(0.1f)] public float edgeRadius = 1.8f;
    [Tooltip("Nodes closer than this (fraction of hexLength) that face the same way are linked without the wall check. 0 = always check.")]
    [Min(0f)] public float edgeTrustRadius = 1f;
    [Tooltip("Meters each end of the link-check line is lifted off the surface, so it doesn't clip the wall it sits on.")]
    [Min(0f)] public float edgeLift = 0.15f;

    [Header("Async")]
    [Tooltip("Max nodes processed per frame. All their raycasts are batched into one job.")]
    [Min(1)] public int batchSize = 64;
}
