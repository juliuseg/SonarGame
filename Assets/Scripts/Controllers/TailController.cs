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

    void Start()
    {
        SpawnSegments();
    }

    void SpawnSegments()
    {
        segments = new Transform[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject seg = Instantiate(segmentPrefab, leader.position, Quaternion.identity);
            seg.name = $"TailSegment_{i}";
            segments[i] = seg.transform;
        }
    }

    void LateUpdate()
    {
        // First segment follows the leader
        ConstrainSegment(segments[0], leader);

        // Each subsequent segment follows the one before it
        for (int i = 1; i < segments.Length; i++)
        {
            ConstrainSegment(segments[i], segments[i - 1]);
        }
    }

    void ConstrainSegment(Transform segment, Transform target)
    {
        float dist = Vector3.Distance(segment.position, target.position);

        if (dist > segmentDistance)
        {
            // Pull the segment toward its target so it's exactly segmentDistance away
            Vector3 direction = (segment.position - target.position).normalized;
            segment.position = target.position + direction * segmentDistance;
        }
    }
}
