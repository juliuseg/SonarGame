using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 followOffset = new Vector3(0, 2, -6);
    public float lookAheadDst = 10f;
    public float rotSmoothSpeed = 6f;

    private Rigidbody _rb;

    // The two physics-step positions we interpolate between
    private Vector3 _prevPos;
    private Quaternion _prevRot;
    private Vector3 _currPos;
    private Quaternion _currRot;

    void Start()
    {
        _rb = target.GetComponent<Rigidbody>();
        _prevPos = _currPos = _rb.position;
        _prevRot = _currRot = _rb.rotation;
    }

    void FixedUpdate()
    {
        // Record the last two physics positions
        _prevPos = _currPos;
        _prevRot = _currRot;
        _currPos = _rb.position;
        _currRot = _rb.rotation;
    }

    void LateUpdate()
    {
        // How far between the last two physics steps are we right now?
        float t = (Time.time - Time.fixedTime) / Time.fixedDeltaTime;

        // Interpolate target transform
        Vector3 pos = Vector3.Lerp(_prevPos, _currPos, t);
        Quaternion rot = Quaternion.Slerp(_prevRot, _currRot, t);

        // Place camera
        transform.position = pos + rot * followOffset;

        Vector3 lookPoint = pos + rot * new Vector3(0, 0, lookAheadDst);
        Quaternion desiredRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);
        float smoothT = 1f - Mathf.Exp(-rotSmoothSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, smoothT);
    }
}