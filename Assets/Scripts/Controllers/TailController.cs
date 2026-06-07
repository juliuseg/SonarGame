using System.Collections.Generic;
using UnityEngine;

public class TailController : MonoBehaviour
{
    [Header("Setup")]
    public Transform leader;
    public GameObject segmentPrefab;

    [Header("Settings")]
    public int segmentCount = 10;
    public float segmentDistance = 0.5f;

    private Transform[] segments;
    private readonly List<Vector3> _trail = new List<Vector3>();

    private const int MaxTrailPoints = 512;

    void Start()
    {
        SpawnSegments();
        _trail.Add(leader.position);
    }

    void SpawnSegments()
    {
        Transform parent = leader != null ? leader : transform;
        Vector3 spawnPos = leader != null ? leader.position : transform.position;
        Vector3 prefabScale = segmentPrefab.transform.localScale;
        Vector3 compensatedScale = CompensateScaleForParent(prefabScale, parent.lossyScale);
        segments = new Transform[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject seg = Instantiate(segmentPrefab, spawnPos, Quaternion.identity, parent);
            seg.name = $"TailSegment_{i}";
            segments[i] = seg.transform;
            segments[i].position = spawnPos;
            segments[i].localScale = compensatedScale;
        }
    }

    static Vector3 CompensateScaleForParent(Vector3 prefabLocalScale, Vector3 parentLossyScale)
    {
        return new Vector3(
            DivideUnlessZero(prefabLocalScale.x, parentLossyScale.x),
            DivideUnlessZero(prefabLocalScale.y, parentLossyScale.y),
            DivideUnlessZero(prefabLocalScale.z, parentLossyScale.z));
    }

    static float DivideUnlessZero(float numerator, float denominator)
    {
        return Mathf.Abs(denominator) > 1e-6f ? numerator / denominator : numerator;
    }

    void LateUpdate()
    {
        RecordLeaderPosition();

        for (int i = 0; i < segments.Length; i++)
        {
            float distBack = (i + 1) * segmentDistance;
            segments[i].position = SampleTrail(distBack);
        }
    }

    void RecordLeaderPosition()
    {
        Vector3 pos = leader.position;
        if (_trail.Count == 0 || (pos - _trail[_trail.Count - 1]).sqrMagnitude > 1e-4f)
        {
            _trail.Add(pos);
            if (_trail.Count > MaxTrailPoints)
                _trail.RemoveAt(0);
        }
    }

    Vector3 SampleTrail(float distanceBack)
    {
        if (_trail.Count == 0)
            return leader.position;

        float remaining = distanceBack;
        for (int i = _trail.Count - 1; i > 0; i--)
        {
            Vector3 a = _trail[i];
            Vector3 b = _trail[i - 1];
            float segLen = Vector3.Distance(a, b);
            if (segLen <= 1e-6f)
                continue;

            if (remaining <= segLen)
                return Vector3.Lerp(a, b, remaining / segLen);

            remaining -= segLen;
        }

        return _trail[0];
    }
}
