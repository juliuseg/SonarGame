using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class CamFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 followOffset = new Vector3(0, 2, -6);
    public float lookAheadDst = 10f;
    public float smoothTime = 0.1f;
    public float rotSmoothSpeed = 6f;

    Vector3 vel;

    void LateUpdate()
    {
        if (!target) return;

        // Stable desired pos from target space
        Vector3 desiredPos = target.TransformPoint(followOffset);
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref vel, smoothTime);

        // Stable look point from target space
        Vector3 lookPoint = target.TransformPoint(new Vector3(0, 0, lookAheadDst));
        Quaternion targetRot = Quaternion.LookRotation(lookPoint - transform.position, Vector3.up);

        // Smooth once (no interim LookAt write)
        float t = 1f - Mathf.Exp(-rotSmoothSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);

        // optional cosmetics
        var cam = GetComponent<Camera>();
        if (cam) cam.backgroundColor = RenderSettings.fogColor;
    }
}
