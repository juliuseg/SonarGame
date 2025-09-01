using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CamFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 followOffset;
    public float lookAheadDst = 10;
    public float smoothTime = .1f;
    public float rotSmoothSpeed = 3;

    Vector3 smoothV;

    public Color surfaceColor;
    public Color deepColor;

    public float deepColorDepth = 10;
    public float surfaceColorDepth = 50;

    
    void LateUpdate()
    {
        Vector3 targetPos = target.position + target.forward * followOffset.z + target.up * followOffset.y + target.right * followOffset.x;
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref smoothV, smoothTime);

        Quaternion rot = transform.rotation;
        transform.LookAt(target.position + target.forward * lookAheadDst);
        Quaternion targetRot = transform.rotation;

        transform.rotation = Quaternion.Slerp(rot,targetRot,Time.deltaTime * rotSmoothSpeed);

        float depth = transform.position.y;

        float t = Mathf.InverseLerp(deepColorDepth, surfaceColorDepth, Mathf.Clamp(depth, deepColorDepth, surfaceColorDepth));
        Color fogColor = Color.Lerp(deepColor, surfaceColor, t);
        
        RenderSettings.fogColor = fogColor;
        GetComponent<Camera>().backgroundColor = fogColor;
    }
}