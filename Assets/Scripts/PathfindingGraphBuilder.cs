using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public class PathfindingGraphBuilder
{
    public class Node
    {
        public Vector3 position;
        public Vector3 normal;
        public float distFromSource;
        public bool explored;
        public List<Edge> edges = new List<Edge>();
    }

    public struct Edge
    {
        public Node target;
        public float weight;
    }

    private const int RaysPerNode = 12;

    private readonly PathfindingGraphSettings settings;

    private readonly List<Node> nodes = new List<Node>();
    private Dictionary<Vector3Int, List<Node>> spatialHash;
    private float cellSize;
    private Node sourceNode;

    public bool IsBuilding { get; private set; }
    public bool IsBuilt => sourceNode != null && !IsBuilding;
    public Vector3 SourcePosition => sourceNode != null ? sourceNode.position : Vector3.zero;
    public IReadOnlyList<Node> Nodes => nodes;

    public PathfindingGraphBuilder(PathfindingGraphSettings settings)
    {
        this.settings = settings;
    }

    public IEnumerator Build(Vector3 seedPosition, Vector3 seedNormal)
    {
        if (IsBuilding)
        {
            Debug.LogWarning("[GraphBuilder] Build already in progress, ignoring request.");
            yield break;
        }

        IsBuilding = true;
        ResetGraph();

        sourceNode = CreateNode(seedPosition, seedNormal, 0f);
        var frontier = new List<Node> { sourceNode };
        var batch = new List<Node>(settings.batchSize);

        while (frontier.Count > 0 && nodes.Count < settings.maxNodes)
        {
            batch.Clear();
            while (batch.Count < settings.batchSize && frontier.Count > 0)
            {
                Node current = PopClosest(frontier);
                if (current.explored) continue;
                current.explored = true;
                if (current.distFromSource > settings.maxExploreDistance) continue;
                batch.Add(current);
            }

            if (batch.Count == 0) continue;

            yield return ExpandBatch(batch, frontier);
        }

        Debug.Log($"[GraphBuilder] Built graph: {nodes.Count} nodes, {CountEdges()} edges.");
        IsBuilding = false;
    }

    private IEnumerator ExpandBatch(List<Node> batch, List<Node> frontier)
    {
        int n = batch.Count;
        int totalRays = n * RaysPerNode;

        var commands = new NativeArray<RaycastCommand>(totalRays, Allocator.TempJob);
        var results = new NativeArray<RaycastHit>(totalRays, Allocator.TempJob);

        var qp = new QueryParameters
        {
            layerMask = settings.wallMask.value,
            hitTriggers = QueryTriggerInteraction.Ignore,
            hitMultipleFaces = false,
            hitBackfaces = false
        };

        var basisU = new Vector3[n];
        var basisV = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            Node node = batch[i];
            BuildTangentBasis(node.normal, out Vector3 u, out Vector3 v);
            basisU[i] = u;
            basisV[i] = v;

            for (int k = 0; k < 6; k++)
            {
                float angle = k * 60f * Mathf.Deg2Rad;
                Vector3 dir = Mathf.Cos(angle) * u + Mathf.Sin(angle) * v;
                Vector3 probe = node.position + dir * settings.hexLength;

                Vector3 originA = probe + node.normal * settings.rayLength;
                Vector3 originB = probe - node.normal * settings.rayLength;
                float fullLen = settings.rayLength * 2f;

                int idx = i * RaysPerNode + k * 2;
                commands[idx] = new RaycastCommand(originA, -node.normal, qp, fullLen);
                commands[idx + 1] = new RaycastCommand(originB, node.normal, qp, fullLen);
            }
        }

        JobHandle handle = RaycastCommand.ScheduleBatch(commands, results, 32, default);
        while (!handle.IsCompleted)
            yield return null;
        handle.Complete();

        for (int i = 0; i < n; i++)
        {
            Node current = batch[i];
            Vector3 u = basisU[i];
            Vector3 v = basisV[i];

            for (int k = 0; k < 6; k++)
            {
                float angle = k * 60f * Mathf.Deg2Rad;
                Vector3 dir = Mathf.Cos(angle) * u + Mathf.Sin(angle) * v;
                Vector3 probe = current.position + dir * settings.hexLength;

                int idx = i * RaysPerNode + k * 2;
                RaycastHit hitA = results[idx];
                RaycastHit hitB = results[idx + 1];

                bool aHit = hitA.colliderInstanceID != 0;
                bool bHit = hitB.colliderInstanceID != 0;

                Vector3 hitPoint, hitNormal;
                if (aHit && bHit)
                {
                    float dA = Vector3.Distance(hitA.point, probe);
                    float dB = Vector3.Distance(hitB.point, probe);
                    if (dA <= dB) { hitPoint = hitA.point; hitNormal = hitA.normal; }
                    else { hitPoint = hitB.point; hitNormal = hitB.normal; }
                }
                else if (aHit) { hitPoint = hitA.point; hitNormal = hitA.normal; }
                else if (bHit) { hitPoint = hitB.point; hitNormal = hitB.normal; }
                else continue;

                Node target = FindNearbyNode(hitPoint, settings.hexLength * 0.5f);
                if (target == null)
                {
                    if (nodes.Count >= settings.maxNodes) { commands.Dispose(); results.Dispose(); yield break; }
                    target = CreateNode(hitPoint, hitNormal, float.PositiveInfinity);
                }

                float weight = Vector3.Distance(current.position, target.position);
                AddEdgeIfMissing(current, target, weight);

                float newDist = current.distFromSource + weight;
                if (newDist < target.distFromSource)
                {
                    target.distFromSource = newDist;
                    target.explored = false;
                    frontier.Add(target);
                }
            }
        }

        commands.Dispose();
        results.Dispose();
    }

    private void ResetGraph()
    {
        nodes.Clear();
        cellSize = settings.hexLength * 0.5f;
        spatialHash = new Dictionary<Vector3Int, List<Node>>();
        sourceNode = null;
    }

    private void BuildTangentBasis(Vector3 n, out Vector3 u, out Vector3 v)
    {
        Vector3 reference = Mathf.Abs(Vector3.Dot(n, Vector3.up)) < 0.9f
            ? Vector3.up
            : Vector3.right;
        u = Vector3.Normalize(Vector3.Cross(n, reference));
        v = Vector3.Cross(n, u);
    }

    private Node CreateNode(Vector3 pos, Vector3 normal, float dist)
    {
        var n = new Node { position = pos, normal = normal, distFromSource = dist };
        nodes.Add(n);
        InsertIntoHash(n);
        return n;
    }

    private void AddEdgeIfMissing(Node a, Node b, float weight)
    {
        if (a == b) return;
        for (int i = 0; i < a.edges.Count; i++)
            if (a.edges[i].target == b) return;

        a.edges.Add(new Edge { target = b, weight = weight });
        b.edges.Add(new Edge { target = a, weight = weight });
    }

    private int CountEdges()
    {
        int total = 0;
        for (int i = 0; i < nodes.Count; i++) total += nodes[i].edges.Count;
        return total / 2;
    }

    private Node PopClosest(List<Node> frontier)
    {
        int bestIdx = 0;
        float bestDist = frontier[0].distFromSource;
        for (int i = 1; i < frontier.Count; i++)
        {
            if (frontier[i].distFromSource < bestDist)
            {
                bestDist = frontier[i].distFromSource;
                bestIdx = i;
            }
        }
        Node best = frontier[bestIdx];
        frontier[bestIdx] = frontier[frontier.Count - 1];
        frontier.RemoveAt(frontier.Count - 1);
        return best;
    }

    private Vector3Int CellOf(Vector3 p)
    {
        return new Vector3Int(
            Mathf.FloorToInt(p.x / cellSize),
            Mathf.FloorToInt(p.y / cellSize),
            Mathf.FloorToInt(p.z / cellSize));
    }

    private void InsertIntoHash(Node n)
    {
        var cell = CellOf(n.position);
        if (!spatialHash.TryGetValue(cell, out var list))
        {
            list = new List<Node>();
            spatialHash[cell] = list;
        }
        list.Add(n);
    }

    private Node FindNearbyNode(Vector3 pos, float radius)
    {
        Vector3Int center = CellOf(pos);
        int cellRange = Mathf.CeilToInt(radius / cellSize);
        float bestSqr = radius * radius;
        Node best = null;

        for (int x = -cellRange; x <= cellRange; x++)
        for (int y = -cellRange; y <= cellRange; y++)
        for (int z = -cellRange; z <= cellRange; z++)
        {
            var key = new Vector3Int(center.x + x, center.y + y, center.z + z);
            if (!spatialHash.TryGetValue(key, out var list)) continue;
            for (int i = 0; i < list.Count; i++)
            {
                float sqr = (list[i].position - pos).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = list[i];
                }
            }
        }
        return best;
    }

    public void GatherNearbyNodes(Vector3 pos, float radius, List<Node> outList)
    {
        outList.Clear();
        if (spatialHash == null) return;

        Vector3Int center = CellOf(pos);
        int cellRange = Mathf.CeilToInt(radius / cellSize);
        float radiusSqr = radius * radius;

        for (int x = -cellRange; x <= cellRange; x++)
        for (int y = -cellRange; y <= cellRange; y++)
        for (int z = -cellRange; z <= cellRange; z++)
        {
            var key = new Vector3Int(center.x + x, center.y + y, center.z + z);
            if (!spatialHash.TryGetValue(key, out var list)) continue;
            for (int i = 0; i < list.Count; i++)
                if ((list[i].position - pos).sqrMagnitude <= radiusSqr)
                    outList.Add(list[i]);
        }
    }

    public bool SampleGradient(Vector3 from, float radius, out Vector3 direction, out Vector3 surfaceNormal)
    {
        direction = Vector3.zero;
        surfaceNormal = Vector3.up;

        var nearby = new List<Node>();
        GatherNearbyNodes(from, radius, nearby);
        if (nearby.Count == 0) return false;

        const float eps = 0.0001f;
        float weightSum = 0f;
        float distSum = 0f;
        Vector3 normalSum = Vector3.zero;

        for (int i = 0; i < nearby.Count; i++)
        {
            float d = Vector3.Distance(nearby[i].position, from) + eps;
            float w = 1f / d;
            weightSum += w;
            distSum += w * nearby[i].distFromSource;
            normalSum += w * nearby[i].normal;
        }
        float selfDist = distSum / weightSum;
        surfaceNormal = normalSum.normalized;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < nearby.Count; i++)
        {
            Vector3 toNode = nearby[i].position - from;
            float d = toNode.magnitude + eps;
            float w = 1f / d;
            float delta = selfDist - nearby[i].distFromSource;
            sum += (toNode / d) * w * delta;
        }

        if (sum.sqrMagnitude < 1e-8f) return false;

        direction = Vector3.ProjectOnPlane(sum, surfaceNormal).normalized;
        return true;
    }
}
