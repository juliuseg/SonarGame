using UnityEngine;

public class PipeNodeController : MonoBehaviour
{
    public Vector3 tangent;
    public bool occupied;

    public Vector3 GetWorldTangent()
    {
        return transform.TransformVector(tangent);
    }
}
