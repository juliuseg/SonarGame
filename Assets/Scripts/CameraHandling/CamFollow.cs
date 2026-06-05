using UnityEngine;

public class CamFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 followOffset = new Vector3(0, 2, -6);
    public float lookAheadDst = 10f;
    public float rotSmoothSpeed = 6f;

    private Rigidbody _rb;
    private Quaternion _smoothedOffsetRot;

    private Vector3 _prevPos;
    private Quaternion _prevRot;
    private Vector3 _currPos;
    private Quaternion _currRot;

    void Start()
    {
        _rb = target.GetComponent<Rigidbody>();
        _prevPos = _currPos = _rb.position;
        _prevRot = _currRot = _rb.rotation;
        _smoothedOffsetRot = _rb.rotation;
    }

    void FixedUpdate()
    {
        _prevPos = _currPos;
        _prevRot = _currRot;
        _currPos = _rb.position;
        _currRot = _rb.rotation;
    }

    void LateUpdate()
    {
        float t = (Time.time - Time.fixedTime) / Time.fixedDeltaTime;

        Vector3 pos = Vector3.Lerp(_prevPos, _currPos, t);
        Quaternion rot = Quaternion.Slerp(_prevRot, _currRot, t);

        float smoothT = 1f - Mathf.Exp(-rotSmoothSpeed * Time.deltaTime);
        _smoothedOffsetRot = Quaternion.Slerp(_smoothedOffsetRot, rot, smoothT);

        transform.position = pos + _smoothedOffsetRot * followOffset;

        Vector3 lookPoint = pos + rot * new Vector3(0, 0, lookAheadDst);
        transform.rotation = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
    }
}
