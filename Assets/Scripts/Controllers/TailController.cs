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
        segments = new Transform[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject seg = Instantiate(segmentPrefab, leader.position, Quaternion.identity);
            seg.name = $"TailSegment_{i}";
            segments[i] = seg.transform;
            segments[i].position = leader.position;
        }
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
