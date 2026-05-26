using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

[RequireComponent(typeof(CamFollow), typeof(FreeCameraController))]
public class CamSwitcher : MonoBehaviour
{
    
    public CamFollow camFollow;
    public FreeCameraController freeCameraController;
    public TerraformController terraformController;

    private Vector3 lastCamFollowPosition;
    private Quaternion lastCamFollowRotation;

    private Vector3 lastFreeCameraControllerPosition;
    private Quaternion lastFreeCameraControllerRotation;

    [SerializeField] private Vector3 exitOffset;

    [SerializeField] private float distToEnterSub;

    [SerializeField] private GameObject enterSubText;   
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        camFollow = GetComponent<CamFollow>();
        freeCameraController = GetComponent<FreeCameraController>();
        terraformController = GetComponent<TerraformController>();
        camFollow.enabled = true;
        freeCameraController.enabled = false;
        terraformController.mousePointer.SetActive(false);
        terraformController.enabled = false;

        
        lastCamFollowPosition = transform.position;
        lastCamFollowRotation = transform.rotation;
        lastFreeCameraControllerPosition = transform.position;
        lastFreeCameraControllerRotation = transform.rotation;
    }

    // Update is called once per frame
    void Update()
    {

        if (freeCameraController.enabled && Vector3.Distance(transform.position, camFollow.target.position) < distToEnterSub){
            enterSubText.SetActive(true);
        } else {
            enterSubText.SetActive(false);
        }

        if (Keyboard.current.tabKey.wasPressedThisFrame){
            if (freeCameraController.enabled && Vector3.Distance(transform.position, camFollow.target.position) < distToEnterSub){
                camFollow.enabled = true;
                freeCameraController.enabled = false;
                terraformController.mousePointer.SetActive(false);
                terraformController.enabled = false;
                transform.SetPositionAndRotation(lastCamFollowPosition, lastCamFollowRotation);


            } else if (camFollow.enabled) {
                camFollow.enabled = false;
                freeCameraController.enabled = true;
                terraformController.enabled = true;
                terraformController.mousePointer.SetActive(true);

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
