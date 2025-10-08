using UnityEngine;

public class ConstCamFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 lookEuler = Vector3.zero; // rotation that defines the look direction
    public float length = 5f; // distance from target along look direction

    void OnValidate()
    {
        if (length < 0f) length = 0f;
    }

    void LateUpdate()
    {
        if (target == null) return;

        Quaternion q = Quaternion.Euler(lookEuler);
        Vector3 dir = q * Vector3.forward;

        transform.position = target.position - dir * length;
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }
}
