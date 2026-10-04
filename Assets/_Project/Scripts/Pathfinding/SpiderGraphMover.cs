using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Moves a spider along the navigation graph toward the graph's source point.
/// Replaces SpiderTestMovement: it only moves/turns the transform. SpiderWalkController still
/// handles wall alignment, body height and leg stepping, and SpiderBodyMotion the body sway.
/// Something else (e.g. PathfindingGraphDebug) calls Step each frame.
///
/// The spider tracks a current graph node and only ever looks at that node's edge neighbours, so it
/// can't be pulled toward nodes on the other side of a wall. Within that neighbourhood the heading is a
/// soft blend of the downhill neighbours, so the path doesn't have to follow the edges exactly.
/// </summary>
public class SpiderGraphMover : MonoBehaviour
{
    [Tooltip("Only used to find the starting node (and to re-find one if the spider ends up far from its node), in graph hex lengths.")]
    [SerializeField, Min(0.5f)] private float searchTolerance = 2f;
    [Tooltip("Stop when within this many graph hex lengths of the target.")]
    [SerializeField, Min(0f)] private float arriveTolerance = 1f;
    [Tooltip("How quickly the heading follows the graph gradient.")]
    [SerializeField, Min(0f)] private float turnSharpness = 6f;

    private const int MaxNodeHopsPerFrame = 4;

    private Vector3 _heading;
    private string _lastFailure;
    private PathfindingGraphBuilder.Node _current;
    private readonly List<PathfindingGraphBuilder.Node> _buffer = new List<PathfindingGraphBuilder.Node>();

    // Returns true while still travelling; false when arrived, graph not ready, or no way forward.
    public bool Step(PathfindingGraphBuilder graph, float speed, float deltaTime)
    {
        if (graph == null || !graph.IsBuilt)
        {
            _current = null;
            return Fail("graph not built");
        }

        Vector3 pos = transform.position;

        if (Vector3.Distance(pos, graph.SourcePosition) <= graph.ToleranceToRadius(arriveTolerance))
            return Fail("arrived at target");

        if (!TrackCurrentNode(graph, pos))
            return Fail("no graph node nearby", () => $"pos {pos}, search radius {graph.ToleranceToRadius(searchTolerance):F2}m, graph has {graph.Nodes.Count} nodes");

        if (!TryGetDownhillDirection(pos, out Vector3 dir, out Vector3 normal))
            return Fail("no downhill neighbour", () => $"pos {pos}, current node {_current.position} (dist {_current.distFromSource:F2}, {_current.edges.Count} edges)");

        Vector3 tangent = Vector3.ProjectOnPlane(dir, normal);
        if (tangent.sqrMagnitude < 1e-6f) return Fail("direction is perpendicular to the wall");
        tangent.Normalize();
        _lastFailure = null;

        float t = 1f - Mathf.Exp(-turnSharpness * deltaTime);
        _heading = _heading.sqrMagnitude < 1e-6f
            ? tangent
            : Vector3.Slerp(_heading, tangent, t).normalized;

        transform.position += _heading * (speed * deltaTime);

        // Face the heading. SpiderWalkController re-projects forward onto the ground plane.
        Vector3 fwd = Vector3.ProjectOnPlane(_heading, transform.up);
        if (fwd.sqrMagnitude > 1e-6f)
        {
            Quaternion rot = Quaternion.LookRotation(fwd, transform.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, t);
        }

        return true;
    }

    // Keeps _current as the graph node nearest the spider, only ever hopping along edges.
    // A spatial search is used just to find the first node, or to recover if the spider is far from its node.
    private bool TrackCurrentNode(PathfindingGraphBuilder graph, Vector3 pos)
    {
        float reacquireRadius = graph.ToleranceToRadius(searchTolerance);

        if (_current == null || (_current.position - pos).sqrMagnitude > reacquireRadius * reacquireRadius)
        {
            graph.GatherNearbyNodes(pos, reacquireRadius, _buffer);
            _current = null;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < _buffer.Count; i++)
            {
                float sqr = (_buffer[i].position - pos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; _current = _buffer[i]; }
            }
            return _current != null;
        }

        for (int hop = 0; hop < MaxNodeHopsPerFrame; hop++)
        {
            var best = _current;
            float bestSqr = (_current.position - pos).sqrMagnitude;
            for (int i = 0; i < _current.edges.Count; i++)
            {
                var neighbour = _current.edges[i].target;
                float sqr = (neighbour.position - pos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = neighbour; }
            }

            if (best == _current) break;
            _current = best;
        }
        return true;
    }

    // Blend of the directions to this node's downhill neighbours, each weighted by how much closer to the target it is.
    private bool TryGetDownhillDirection(Vector3 pos, out Vector3 direction, out Vector3 surfaceNormal)
    {
        direction = Vector3.zero;
        surfaceNormal = _current.normal;

        Vector3 sum = Vector3.zero;
        Vector3 normalSum = _current.normal;

        for (int i = 0; i < _current.edges.Count; i++)
        {
            var neighbour = _current.edges[i].target;
            if (float.IsInfinity(neighbour.distFromSource) || float.IsInfinity(_current.distFromSource)) continue;

            float gain = _current.distFromSource - neighbour.distFromSource;
            if (gain <= 0f) continue;

            Vector3 toNeighbour = neighbour.position - pos;
            float d = toNeighbour.magnitude;
            if (d < 1e-4f) continue;

            sum += toNeighbour / d * gain;
            normalSum += neighbour.normal;
        }

        if (sum.sqrMagnitude < 1e-8f) return false;

        surfaceNormal = normalSum.normalized;
        direction = sum.normalized;
        return true;
    }

    // Logs each distinct stop reason once, so a stuck spider says why without spamming the console.
    private bool Fail(string reason, System.Func<string> detail = null)
    {
        if (reason != _lastFailure)
        {
            Debug.Log($"[SpiderGraphMover] Not moving: {reason}{(detail != null ? " | " + detail() : "")}", this);
            _lastFailure = reason;
        }
        return false;
    }
}
