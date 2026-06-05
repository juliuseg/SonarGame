using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

[RequireComponent(typeof(CamFollow), typeof(FreeCameraController))]
public class CamSwitcher : MonoBehaviour
{
    public CamFollow camFollow;
    public FreeCameraController freeCameraController;
    public ToolModeController toolModeController;

    private Vector3 lastCamFollowPosition;
    private Quaternion lastCamFollowRotation;

    private Vector3 lastFreeCameraControllerPosition;
    private Quaternion lastFreeCameraControllerRotation;

    [SerializeField] private Vector3 exitOffset;

    [SerializeField] private float distToEnterSub;

    [SerializeField] private GameObject enterSubText;

    void Start()
    {
        camFollow = GetComponent<CamFollow>();
        freeCameraController = GetComponent<FreeCameraController>();
        toolModeController = GetComponent<ToolModeController>();
        camFollow.enabled = true;
        freeCameraController.enabled = false;
        toolModeController.SetFreeCameraActive(false);

        lastCamFollowPosition = transform.position;
        lastCamFollowRotation = transform.rotation;
        lastFreeCameraControllerPosition = transform.position;
        lastFreeCameraControllerRotation = transform.rotation;
    }

    void Update()
    {
        if (freeCameraController.enabled && Vector3.Distance(transform.position, camFollow.target.position) < distToEnterSub)
            enterSubText.SetActive(true);
        else
            enterSubText.SetActive(false);

        if (Keyboard.current.tabKey.wasPressedThisFrame)
        {
            if (freeCameraController.enabled && Vector3.Distance(transform.position, camFollow.target.position) < distToEnterSub)
            {
                camFollow.enabled = true;
                freeCameraController.enabled = false;
                toolModeController.SetFreeCameraActive(false);
                transform.SetPositionAndRotation(lastCamFollowPosition, lastCamFollowRotation);
            }
            else if (camFollow.enabled)
            {
                camFollow.enabled = false;
                freeCameraController.enabled = true;
                toolModeController.SetFreeCameraActive(true);

                lastCamFollowPosition = transform.position;
                lastCamFollowRotation = transform.rotation;
                float yaw = camFollow.target.eulerAngles.y;
                Vector3 rotatedOffset = Quaternion.Euler(0, yaw, 0) * exitOffset;
                transform.position = camFollow.target.position + rotatedOffset;
                freeCameraController.resetRots();
            }
        }
    }
}
