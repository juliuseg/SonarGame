using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple WASD test driver for spider locomotion. Replace with AI movement later.
/// </summary>
public class SpiderTestMovement : MonoBehaviour
{
    [Tooltip("Planar move speed in units per second.")]
    public float moveSpeed = 3f;

    [Tooltip("Yaw speed in degrees per second (Q/E).")]
    public float turnSpeed = 90f;

    void Update()
    {
        if (!Application.isPlaying)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        float turnInput = 0f;
        if (keyboard.qKey.isPressed) turnInput -= 1f;
        if (keyboard.eKey.isPressed) turnInput += 1f;

        if (Mathf.Abs(turnInput) > 1e-6f)
            transform.Rotate(transform.up, turnInput * turnSpeed * Time.deltaTime, Space.World);

        Vector3 input = Vector3.zero;
        if (keyboard.wKey.isPressed) input += Vector3.forward;
        if (keyboard.sKey.isPressed) input += Vector3.back;
        if (keyboard.aKey.isPressed) input += Vector3.left;
        if (keyboard.dKey.isPressed) input += Vector3.right;

        if (input.sqrMagnitude < 1e-6f)
            return;

        input.Normalize();

        Vector3 worldDir = transform.TransformDirection(input);
        worldDir = Vector3.ProjectOnPlane(worldDir, transform.up);
        if (worldDir.sqrMagnitude < 1e-6f)
            return;

        transform.position += worldDir.normalized * (moveSpeed * Time.deltaTime);
    }
}
