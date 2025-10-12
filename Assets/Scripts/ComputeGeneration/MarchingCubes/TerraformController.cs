using UnityEngine;
using UnityEngine.InputSystem;

public class TerraformController : MonoBehaviour
{

    public GameObject mousePointer;
    private Camera targetCamera;
    public float maxRayDistance = 1000f;

    [SerializeField] 
    private ChunkLoader chunkLoader;

    public float terraformStrenght = 1.0f;
    public float terraformRadius = 1.0f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (mousePointer == null || targetCamera == null)
        {
            return;
        }

        // Ray from the center of the camera viewport forward
        Ray centerRay = targetCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(centerRay, out RaycastHit hitInfo, maxRayDistance))
        {
            mousePointer.transform.position = hitInfo.point;
            mousePointer.SetActive(true);

            // Handle mouse click using new Input System
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.isPressed)
                {
                    MouseClick(hitInfo, -1f);
                }
                else if (mouse.rightButton.isPressed)
                {
                    MouseClick(hitInfo, 1f);
                }
            }

        } else {
            mousePointer.SetActive(false);
        }
    }

    void MouseClick(RaycastHit hit, float multiplier)
    {

        chunkLoader.ApplyTerraformEdit(new TerraformEdit {
            position = hit.point,
            strength = terraformStrenght * multiplier,
            radius = terraformRadius
        });




    }
}
