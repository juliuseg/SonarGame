using UnityEngine;
using UnityEngine.InputSystem;

public class TerraformController : MonoBehaviour
{

    public GameObject mousePointer;
    private Camera targetCamera;
    public float maxRayDistance = 1000f;

    private ChunkStreamer _chunkStreamer;

    public float terraformStrenght = 1.0f;
    public float terraformRadius = 1.0f;

    [Tooltip("Apparent pointer size as a fraction of screen height. Larger = bigger on screen.")]
    public float pointerScreenSize = 0.05f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void Init(ChunkStreamer chunkStreamer)
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
        
        _chunkStreamer = chunkStreamer;
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

            float distance = hitInfo.distance;
            float worldScale = 2f * distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * pointerScreenSize;
            mousePointer.transform.localScale = Vector3.one * worldScale;

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
        _chunkStreamer.ApplyTerraformEdit(new TerraformEdit {
            position = hit.point,
            strength = terraformStrenght * multiplier,
            radius = terraformRadius
        });
        
    }
}
