using System.Collections.Generic;
using UnityEngine;

public class GraphFollower : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 2f;
    [Tooltip("How far around the agent to sample graph nodes. ~2-3x the builder's hexLength.")]
    [SerializeField] private float sampleRadius = 3f;
    [SerializeField] private float arriveDistance = 0.75f;

    [Header("Gradient shaping")]
    [SerializeField] private float downhillSharpness = 1.5f;
    [SerializeField] private float directionSmoothing = 6f;

    [Header("Surface stick")]
    [SerializeField] private LayerMask wallMask;
    [Tooltip("How far to search along the local normal when snapping to the wall.")]
    [SerializeField] private float stickRayLength = 2f;
    [Tooltip("Small gap kept between the agent and the wall so it isn't visually embedded.")]
    [SerializeField] private float surfaceOffset = 0.05f;

    [Header("Rotation")]
    [SerializeField] private bool alignToSurface = true;
    [SerializeField] private float rotateSpeed = 8f;

    private PathfindingGraphBuilder graph;
    private Vector3 targetPosition;
    private bool active;

    private Vector3 smoothedTangent;
    private Vector3 currentNormal = Vector3.up;

    private readonly List<PathfindingGraphBuilder.Node> buffer = new List<PathfindingGraphBuilder.Node>();

    public void Initialize(PathfindingGraphBuilder graph, Vector3 startPosition, Vector3 targetPosition)
    {
        this.graph = graph;
        this.targetPosition = targetPosition;
        active = true;
        smoothedTangent = Vector3.zero;

        // Seed currentNormal from the closest known node.
        graph.GatherNearbyNodes(startPosition, sampleRadius * 2f, buffer);
        if (buffer.Count > 0)
        {
            var nearest = buffer[0];
            float bestSqr = (nearest.position - startPosition).sqrMagnitude;
            for (int i = 1; i < buffer.Count; i++)
            {
                float sqr = (buffer[i].position - startPosition).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; nearest = buffer[i]; }
            }
            currentNormal = nearest.normal;
        }
        else
        {
            currentNormal = Vector3.up;
        }

        // Snap onto the actual surface right away.
        if (StickToSurface(startPosition, currentNormal, out Vector3 snapped, out Vector3 n))
        {
            transform.position = snapped;
            currentNormal = n;
        }
        else
        {
            transform.position = startPosition;
        }
    }

    private void Update()
    {
        if (!active || graph == null) return;

        if (Vector3.Distance(transform.position, targetPosition) <= arriveDistance)
        {
            active = false;
            Debug.Log("[GraphFollower] Arrived at target.");
            return;
        }

        if (!ComputeGradient(out Vector3 desired, out Vector3 graphNormal))
            return; // no signal — stay put

        // Project desired direction onto the tangent plane defined by the graph's interpolated normal.
        // This guarantees the heading lies along the wall, not through it.
        Vector3 tangent = Vector3.ProjectOnPlane(desired, graphNormal);
        if (tangent.sqrMagnitude < 1e-6f) return;
        tangent.Normalize();

        // Framerate-independent smoothing of the heading.
        float t = 1f - Mathf.Exp(-directionSmoothing * Time.deltaTime);
        smoothedTangent = smoothedTangent.sqrMagnitude < 1e-6f
            ? tangent
            : Vector3.Slerp(smoothedTangent, tangent, t).normalized;

        // Step in the tangent direction, then snap back to the actual mesh surface.
        Vector3 candidate = transform.position + smoothedTangent * moveSpeed * Time.deltaTime;

        if (StickToSurface(candidate, graphNormal, out Vector3 stuckPos, out Vector3 stuckNormal))
        {
            transform.position = stuckPos;
            currentNormal = stuckNormal;
        }
        // If the stick fails, don't move this frame — better to pause than to fly off the wall.

        if (alignToSurface)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(smoothedTangent, currentNormal);
            if (fwd.sqrMagnitude > 1e-6f)
            {
                Quaternion rot = Quaternion.LookRotation(fwd, currentNormal);
                transform.rotation = Quaternion.Slerp(transform.rotation, rot, rotateSpeed * Time.deltaTime);
            }
        }
    }

    // Snap a candidate point to the nearest wall surface along the supplied axis.
    // Same "ray starts outside the wall, casts through" pattern as the builder.
    private bool StickToSurface(Vector3 candidate, Vector3 axis, out Vector3 stuckPos, out Vector3 stuckNormal)
    {
        stuckPos = candidate;
        stuckNormal = axis;

        if (axis.sqrMagnitude < 1e-6f) return false;
        axis.Normalize();

        float fullLen = stickRayLength * 2f;
        Vector3 originA = candidate + axis * stickRayLength;
        Vector3 originB = candidate - axis * stickRayLength;

        bool a = Physics.Raycast(originA, -axis, out RaycastHit hitA, fullLen, wallMask, QueryTriggerInteraction.Ignore);
        bool b = Physics.Raycast(originB,  axis, out RaycastHit hitB, fullLen, wallMask, QueryTriggerInteraction.Ignore);

        RaycastHit chosen;
        if (a && b)
        {
            float dA = (hitA.point - candidate).sqrMagnitude;
            float dB = (hitB.point - candidate).sqrMagnitude;
            chosen = dA <= dB ? hitA : hitB;
        }
        else if (a) chosen = hitA;
        else if (b) chosen = hitB;
        else return false;

        stuckPos = chosen.point + chosen.normal * surfaceOffset;
        stuckNormal = chosen.normal;
        return true;
    }

    // Weighted centroid biased toward low-distFromSource nodes; also returns the weighted surface normal.
    private bool ComputeGradient(out Vector3 direction, out Vector3 surfaceNormal)
    {
        direction = Vector3.zero;
        surfaceNormal = currentNormal;

        graph.GatherNearbyNodes(transform.position, sampleRadius, buffer);
        if (buffer.Count == 0) return false;

        float minDist = float.PositiveInfinity;
        for (int i = 0; i < buffer.Count; i++)
            if (buffer[i].distFromSource < minDist)
                minDist = buffer[i].distFromSource;

        Vector3 weightedPos = Vector3.zero;
        Vector3 weightedNormal = Vector3.zero;
        float totalW = 0f;

        for (int i = 0; i < buffer.Count; i++)
        {
            var node = buffer[i];
            float d = Vector3.Distance(node.position, transform.position);
            float prox = SmoothFalloff(d, sampleRadius);
            if (prox <= 0f) continue;

            float advantage = node.distFromSource - minDist;
            float distW = Mathf.Exp(-advantage * downhillSharpness);
            float w = prox * distW;

            weightedPos    += node.position * w;
            weightedNormal += node.normal   * w;
            totalW         += w;
        }
        if (totalW < 1e-6f) return false;

        Vector3 centroid = weightedPos / totalW;
        surfaceNormal = weightedNormal.normalized;

        Vector3 toCentroid = centroid - transform.position;
        if (toCentroid.sqrMagnitude < 1e-6f) return false;
        direction = toCentroid.normalized;
        return true;
    }

    private static float SmoothFalloff(float d, float r)
    {
        if (d >= r) return 0f;
        float u = 1f - d / r;
        return u * u;
    }
}