using UnityEngine;

public class PlacedPipe : MonoBehaviour
{
    public PipeNodeController startNode;
    public PipeNodeController endNode;

    Vector3 _cachedStartPosition;
    Vector3 _cachedEndPosition;
    Quaternion _cachedStartRotation;
    Quaternion _cachedEndRotation;

    public void CacheEndpointPositions()
    {
        if (startNode != null)
        {
            _cachedStartPosition = startNode.transform.position;
            _cachedStartRotation = startNode.transform.rotation;
        }

        if (endNode != null)
        {
            _cachedEndPosition = endNode.transform.position;
            _cachedEndRotation = endNode.transform.rotation;
        }
    }

    public bool HaveEndpointsMoved()
    {
        if (startNode == null || endNode == null)
            return false;

        return (startNode.transform.position - _cachedStartPosition).sqrMagnitude > 0.0001f
            || (endNode.transform.position - _cachedEndPosition).sqrMagnitude > 0.0001f
            || Quaternion.Angle(startNode.transform.rotation, _cachedStartRotation) > 0.01f
            || Quaternion.Angle(endNode.transform.rotation, _cachedEndRotation) > 0.01f;
    }
}
