using UnityEngine;

public class PipeNodeController : MonoBehaviour
{
    public Vector3 tangent;
    public bool occupied;

    [HideInInspector] public Machine Parent;
    [HideInInspector] public PipeNodeDirection Direction;

    public Vector3 GetWorldTangent()
    {
        return transform.TransformVector(tangent);
    }
}
