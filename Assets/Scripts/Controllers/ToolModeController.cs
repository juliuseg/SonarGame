using UnityEngine;
using UnityEngine.InputSystem;

public class ToolModeController : MonoBehaviour
{
    enum ToolMode
    {
        None,
        Placement,
        Terraform
    }

    [SerializeField] private TerraformToolSettings terraformSettings;
    [SerializeField] private PlacementToolSettings placementSettings;
    [SerializeField] private Camera targetCamera;

    TerraformToolHandler _terraformHandler;
    PlacementToolHandler _placementHandler;

    ToolMode _mode = ToolMode.None;
    bool _freeCameraActive;

    public void Init(ChunkStreamer chunkStreamer)
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        _terraformHandler = new TerraformToolHandler(chunkStreamer, terraformSettings, targetCamera);
        _placementHandler = new PlacementToolHandler(placementSettings, targetCamera, chunkStreamer.ChunkManager);
    }

    public void SetFreeCameraActive(bool active)
    {
        _freeCameraActive = active;

        if (!active)
            SetMode(ToolMode.None);
    }

    void Update()
    {
        if (!_freeCameraActive)
            return;

        HandleModeInput();

        switch (_mode)
        {
            case ToolMode.Terraform:
                _terraformHandler.Tick();
                break;
            case ToolMode.Placement:
                _placementHandler.Tick();
                break;
        }
    }

    void HandleModeInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.digit1Key.wasPressedThisFrame)
            SetMode(ToolMode.None);
        else if (keyboard.digit2Key.wasPressedThisFrame)
            SetMode(ToolMode.Placement);
        else if (keyboard.digit3Key.wasPressedThisFrame)
            SetMode(ToolMode.Terraform);
        else if (keyboard.digit4Key.wasPressedThisFrame
                 || keyboard.digit5Key.wasPressedThisFrame
                 || keyboard.digit6Key.wasPressedThisFrame
                 || keyboard.digit7Key.wasPressedThisFrame
                 || keyboard.digit8Key.wasPressedThisFrame
                 || keyboard.digit9Key.wasPressedThisFrame)
            SetMode(ToolMode.None);
    }

    void SetMode(ToolMode mode)
    {
        if (_mode == mode)
            return;

        _terraformHandler.Disable();
        _placementHandler.Disable();

        _mode = mode;

        switch (_mode)
        {
            case ToolMode.Terraform:
                _terraformHandler.Enable();
                break;
            case ToolMode.Placement:
                _placementHandler.Enable();
                break;
        }
    }
}
