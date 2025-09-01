using UnityEngine;
using UnityEngine.InputSystem;

public class FPSController : MonoBehaviour
{
    public float moveForce = 10f;
    public float turnForce = 0.1f;
    public float pitchForce = 0.1f;
    
    private Vector2 moveInput;
    private float forwardInput;
    
    // Input System components
    public InputActionAsset inputActions;
    private InputAction moveAction;
    private InputAction forwardAction;
    
    private Rigidbody rb;
    
    void Awake()
    {
        // Check if input actions asset is assigned
        if (inputActions == null)
        {
            Debug.LogError("FPSController: Input Actions Asset is not assigned! Please assign it in the inspector.");
            return;
        }
        
        // Get the input actions
        moveAction = inputActions.FindAction("Player/Move");
        forwardAction = inputActions.FindAction("Player/Forward");
        
        // Check if actions were found
        if (moveAction == null || forwardAction == null)
        {
            Debug.LogError("FPSController: Could not find Move or Forward actions in the Input Actions Asset!");
            return;
        }
        
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Debug.LogError("FPSController: No Rigidbody component found!");
        }

        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }
    
    void OnEnable()
    {
        if (moveAction == null || forwardAction == null) return;
        
        moveAction.Enable();
        forwardAction.Enable();
        
        moveAction.performed += OnMove;
        moveAction.canceled += OnMove;
        forwardAction.performed += OnForward;
        forwardAction.canceled += OnForward;
    }
    
    void OnDisable()
    {
        if (moveAction == null || forwardAction == null) return;
        
        moveAction.performed -= OnMove;
        moveAction.canceled -= OnMove;
        forwardAction.performed -= OnForward;
        forwardAction.canceled -= OnForward;
        
        moveAction.Disable();
        forwardAction.Disable();
    }
    
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }
    
    void FixedUpdate()
    {
        if (rb == null) return;
        
        // Yaw rotation (A/D from Move action)
        if (Mathf.Abs(moveInput.x) > 0.1f)
        {
            rb.AddTorque(moveInput.x * turnForce * Vector3.up, ForceMode.Force);
        }
        
        // Pitch rotation (W/S from Move action)
        if (Mathf.Abs(moveInput.y) > 0.1f)
        {
            // Simple torque application like yaw, but with pitch limits
            float currentPitch = transform.eulerAngles.x;
            if (currentPitch > 180f) currentPitch -= 360f;
            
            // Only apply torque if we're within limits or moving in the right direction
            if ((currentPitch > -50f || moveInput.y < 0) && (currentPitch < 50f || moveInput.y > 0))
            {
                // Use local X axis to prevent Z rotation
                rb.AddTorque(-moveInput.y * pitchForce * (transform.localRotation * Vector3.right), ForceMode.Force);
            }
        }
        
        // Forward movement (Space)
        if (Mathf.Abs(forwardInput) > 0.1f)
        {
            rb.AddForce(forwardInput * moveForce * transform.forward, ForceMode.Force);
        }

        transform.eulerAngles = new Vector3(transform.eulerAngles.x, transform.eulerAngles.y, 0);
    }
    
    // Input System callbacks
    public void OnMove(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();

    }
    
    public void OnForward(InputAction.CallbackContext context)
    {
        forwardInput = context.ReadValue<float>();

    }
}