using UnityEngine;

// Debug helper: starts a navigation graph build from a chosen transform.
// Use the "Generate Graph" button in the inspector (play mode only).
public class PathfindingGraphDebug : MonoBehaviour
{
    [Tooltip("Where the graph build starts. The nearest wall surface to this point becomes the seed.")]
    [SerializeField] private Transform target;
    [Tooltip("Layers considered wall when looking for the seed surface. Should match the graph settings' wallMask.")]
    [SerializeField] private LayerMask wallMask = 1 << 6;
    [Tooltip("How far from the target to search for a wall.")]
    [SerializeField, Min(0.1f)] private float seedSearchRadius = 10f;

    [Header("Visualization")]
    [SerializeField] private bool drawPoints = true;
    [SerializeField, Min(0.01f)] private float pointSize = 0.1f;
    [SerializeField] private Color pointColor = Color.yellow;
    [Tooltip("Draw candidate points rejected by the clearance check, in blue (no edges).")]
    [SerializeField] private bool drawRejected = true;
    [SerializeField] private bool drawEdges = true;
    [SerializeField] private Color edgeColor = new Color(1f, 1f, 1f, 0.5f);

    [Header("Spider")]
    [Tooltip("Prefab with SpiderGraphMover on it (SporeSpreader).")]
    [SerializeField] private SpiderGraphMover spiderPrefab;
    [Tooltip("Spawns on a random node at least this fraction of the graph's max path distance from the target. It walks toward the target.")]
    [SerializeField, Range(0f, 1f)] private float spawnMinDistanceFraction = 0.8f;
    [Tooltip("Only spawn on surfaces facing upward (normal dot up > 0), so the spider doesn't start on the ceiling. Walls are fine.")]
    [SerializeField] private bool spawnOnFloorOnly = true;
    [SerializeField, Min(0f)] private float spiderSpeed = 2f;

    private SpiderGraphMover _spider;

    private void Update()
    {
        if (_spider == null) return;
        if (GameServices.Resolver == null || !GameServices.Resolver.TryResolve<IPathfindingSystem>(out var pathfinding)) return;

        _spider.Step(pathfinding.Builder, spiderSpeed, Time.deltaTime);
    }

    public void SpawnSpider()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[PathfindingGraphDebug] Enter play mode first.", this);
            return;
        }

        if (spiderPrefab == null)
        {
            Debug.LogWarning("[PathfindingGraphDebug] Assign a spider prefab.", this);
            return;
        }

        if (GameServices.Resolver == null || !GameServices.Resolver.TryResolve<IPathfindingSystem>(out var pathfinding) || !pathfinding.IsBuilt)
        {
            Debug.LogWarning("[PathfindingGraphDebug] Generate the graph first and wait for it to finish.", this);
            return;
        }

        // Pick a random node far from the source in graph distance (not world distance).
        var nodes = pathfinding.Builder.Nodes;
        float maxDist = 0f;
        for (int i = 0; i < nodes.Count; i++)
            if (!float.IsInfinity(nodes[i].distFromSource) && nodes[i].distFromSource > maxDist)
                maxDist = nodes[i].distFromSource;

        float minDist = maxDist * spawnMinDistanceFraction;
        var candidates = new System.Collections.Generic.List<PathfindingGraphBuilder.Node>();
        for (int i = 0; i < nodes.Count; i++)
            if (!float.IsInfinity(nodes[i].distFromSource) && nodes[i].distFromSource >= minDist
                && (!spawnOnFloorOnly || Vector3.Dot(nodes[i].normal, Vector3.up) > 0f))
                candidates.Add(nodes[i]);

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[PathfindingGraphDebug] Graph has no usable nodes to spawn on.", this);
            return;
        }

        var nearest = candidates[Random.Range(0, candidates.Count)];

        if (_spider != null) Destroy(_spider.gameObject);

        Vector3 fwd = Vector3.ProjectOnPlane(Random.onUnitSphere, nearest.normal);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.right, nearest.normal);
        _spider = Instantiate(spiderPrefab, nearest.position, Quaternion.LookRotation(fwd, nearest.normal));

        // WASD test driver would fight the graph mover.
        if (_spider.TryGetComponent(out SpiderTestMovement wasd)) wasd.enabled = false;
    }

    public void GenerateGraph()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[PathfindingGraphDebug] Enter play mode first.", this);
            return;
        }

        if (target == null)
        {
            Debug.LogWarning("[PathfindingGraphDebug] No target assigned.", this);
            return;
        }

        if (GameServices.Resolver == null || !GameServices.Resolver.TryResolve<IPathfindingSystem>(out var pathfinding))
        {
            Debug.LogWarning("[PathfindingGraphDebug] No IPathfindingSystem registered. Is a PathfindingSystem in the scene?", this);
            return;
        }

        if (pathfinding.IsBuilding)
        {
            Debug.LogWarning("[PathfindingGraphDebug] A build is already in progress.", this);
            return;
        }

        if (!TryFindSeed(target.position, out Vector3 seedPos, out Vector3 seedNormal))
        {
            Debug.LogWarning($"[PathfindingGraphDebug] No wall found within {seedSearchRadius}m of the target.", this);
            return;
        }

        Debug.Log($"[PathfindingGraphDebug] Building graph from {seedPos}.", this);
        pathfinding.StartBuild(seedPos, seedNormal);
    }

    // Finds the wall surface point closest to origin. Rays start outside (origin + dir * radius) and travel
    // inward through origin, so a target sitting right on a wall still hits that wall's front face.
    // A ray starting on or inside a surface can't hit it, which is why we don't cast outward from origin.
    private bool TryFindSeed(Vector3 origin, out Vector3 point, out Vector3 normal)
    {
        point = origin;
        normal = Vector3.up;

        float bestDist = seedSearchRadius;
        bool found = false;

        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
        {
            if (x == 0 && y == 0 && z == 0) continue;

            Vector3 dir = new Vector3(x, y, z).normalized;
            Vector3 start = origin + dir * seedSearchRadius;
            if (!Physics.Raycast(start, -dir, out RaycastHit hit, seedSearchRadius * 2f, wallMask, QueryTriggerInteraction.Ignore))
                continue;

            // Rank by distance from the target itself, not by how far the ray travelled.
            float dist = Vector3.Distance(hit.point, origin);
            if (dist <= bestDist)
            {
                bestDist = dist;
                point = hit.point;
                normal = hit.normal;
                found = true;
            }
        }
        return found;
    }

    private void OnDrawGizmos()
    {
        if (!drawPoints && !drawEdges && !drawRejected) return;
        if (GameServices.Resolver == null || !GameServices.Resolver.TryResolve<IPathfindingSystem>(out var pathfinding)) return;

        var nodes = pathfinding.Builder.Nodes;

        if (drawPoints)
        {
            Gizmos.color = pointColor;
            for (int i = 0; i < nodes.Count; i++)
                Gizmos.DrawSphere(nodes[i].position, pointSize);

            // The start node (graph source / spider destination).
            if (nodes.Count > 0)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(pathfinding.Builder.SourcePosition, pointSize * 2f);
            }
        }

        if (drawRejected)
        {
            var rejected = pathfinding.Builder.RejectedPoints;
            Gizmos.color = Color.blue;
            for (int i = 0; i < rejected.Count; i++)
                Gizmos.DrawSphere(rejected[i], pointSize);
        }

        if (drawEdges)
        {
            Gizmos.color = edgeColor;
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                for (int e = 0; e < node.edges.Count; e++)
                {
                    var other = node.edges[e].target;
                    // Edges are stored on both ends; draw each only once.
                    if (node.GetHashCode() <= other.GetHashCode())
                        Gizmos.DrawLine(node.position, other.position);
                }
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (target == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(target.position, 0.5f);
        Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
        Gizmos.DrawWireSphere(target.position, seedSearchRadius);
    }
}
