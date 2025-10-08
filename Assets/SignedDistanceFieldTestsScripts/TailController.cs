using UnityEngine;

public class TailController : MonoBehaviour
{
	public GameObject tailPrefab;
	public float[] segmentSizes;
	public float segmentLength = 0.5f; // distance between consecutive nodes

	private Transform[] segments;
	private int builtCount = 0;

	private void Start()
	{
		BuildSegments();
	}

	private void OnValidate()
	{
		segmentLength = Mathf.Max(0.0001f, segmentLength);

		if (Application.isPlaying && builtCount != segmentSizes.Length-1)
		{
			ClearSegments();
			BuildSegments();
		}
	}

	private void BuildSegments()
	{
		ClearSegments();
		segments = new Transform[segmentSizes.Length-1];

		Vector3 headPosition = transform.position;
		Vector3 backDirection = -transform.forward;
		if (backDirection.sqrMagnitude < 1e-6f) backDirection = Vector3.back;

        transform.localScale = new Vector3(segmentSizes[0], segmentSizes[0], segmentSizes[0]);
		for (int i = 0; i < segmentSizes.Length-1; i++)
		{
			GameObject segmentObject = tailPrefab != null
				? Instantiate(tailPrefab)
				: GameObject.CreatePrimitive(PrimitiveType.Sphere);

			segmentObject.name = $"TailSegment_{i}";
			segmentObject.transform.SetParent(transform.parent, true);

			Vector3 initialPosition = headPosition + backDirection * ((i + 1) * segmentLength);
			segmentObject.transform.position = initialPosition;

			segments[i] = segmentObject.transform;

            // Scale the segment to the size of the segment
            segmentObject.transform.localScale = new Vector3(segmentSizes[i+1], segmentSizes[i+1], segmentSizes[i+1]);
		}

		builtCount = segmentSizes.Length-1;
	}

	private void ClearSegments()
	{
		if (segments == null) return;
		for (int i = 0; i < segments.Length; i++)
		{
			Transform t = segments[i];
			if (t != null)
			{
				Destroy(t.gameObject);
			}
		}
		segments = null;
	}

	private void Update()
	{
		if (segments == null || segments.Length != segmentSizes.Length-1)
		{
			BuildSegments();
			if (segments == null) return;
		}

		Vector3 previousPosition = transform.position; // the head (this GameObject)
		Vector3 fallbackDir = -transform.forward;
		if (fallbackDir.sqrMagnitude < 1e-6f) fallbackDir = Vector3.back;

		for (int i = 0; i < segments.Length; i++)
		{
			Vector3 current = segments[i].position;
			Vector3 delta = current - previousPosition;
			float distance = delta.magnitude;

			if (distance <= 1e-6f)
			{
				// If overlapping, push directly behind the previous node
				delta = fallbackDir;
				distance = 1f;
			}

			Vector3 constrained = previousPosition + (delta / distance) * segmentLength;
			segments[i].position = constrained;

			previousPosition = constrained;
		}
	}
}
