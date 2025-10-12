using UnityEngine;
using UnityEngine.InputSystem;

public class FreeCameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 2f;
    public float verticalSpeed = 5f;

    [Header("Mouse Settings")]
    public float mouseSensitivity = 2f;
    public bool lockCursor = true;

    private Vector2 lookInput;
    private Vector2 moveInput;
    private float rotationX;
    private float rotationY;

    void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }


        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 1;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (keyboard == null || mouse == null)
            return;

        // --- Look input ---
        Vector2 mouseDelta = mouse.delta.ReadValue() * mouseSensitivity;
        rotationX += mouseDelta.x;
        rotationY -= mouseDelta.y;
        rotationY = Mathf.Clamp(rotationY, -90f, 90f);
        transform.rotation = Quaternion.Euler(rotationY, rotationX, 0f);

        // --- Move input ---
        Vector3 direction = Vector3.zero;
        if (keyboard.wKey.isPressed) direction += transform.forward;
        if (keyboard.sKey.isPressed) direction -= transform.forward;
        if (keyboard.aKey.isPressed) direction -= transform.right;
        if (keyboard.dKey.isPressed) direction += transform.right;
        if (keyboard.spaceKey.isPressed) direction += transform.up;
        if (keyboard.leftShiftKey.isPressed) direction -= transform.up;

        direction.Normalize();

        float currentSpeed = moveSpeed;
        if (keyboard.leftCtrlKey.isPressed)
            currentSpeed *= sprintMultiplier;

        transform.position += direction * currentSpeed * Time.deltaTime;
    }
}
