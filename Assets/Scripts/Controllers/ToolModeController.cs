using UnityEngine;
using UnityEngine.InputSystem;

public class ToolModeController : MonoBehaviour
{
    enum ToolMode
    {
        None,
        Placement,
        Terraform,
        Pipe
    }

    [SerializeField] private TerraformToolSettings terraformSettings;
    [SerializeField] private PlacementToolSettings placementSettings;
    [SerializeField] private PipeToolSettings pipeSettings;
    [SerializeField] private Transform pipeParent;
    [SerializeField] private Camera targetCamera;

    TerraformToolHandler _terraformHandler;
    PlacementToolHandler _placementHandler;
    PipeToolHandler _pipeHandler;

    ToolMode _mode = ToolMode.None;
    bool _freeCameraActive;

    public void Init(ChunkStreamer chunkStreamer, AutomationLogicSystem automationLogic)
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        _terraformHandler = new TerraformToolHandler(chunkStreamer, terraformSettings, targetCamera);
        _placementHandler = new PlacementToolHandler(placementSettings, targetCamera, chunkStreamer.ChunkManager, automationLogic);
        _pipeHandler = new PipeToolHandler(pipeSettings, targetCamera, pipeParent, automationLogic);
    }

    public void SetFreeCameraActive(bool active)
    {
        _freeCameraActive = active;

        if (!active)
            SetMode(ToolMode.None);
    }

    void Update()
    {
        if (_pipeHandler != null)
            _pipeHandler.UpdatePlacedPipes();

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
            case ToolMode.Pipe:
                _pipeHandler.Tick();
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
        else if (keyboard.digit4Key.wasPressedThisFrame)
            SetMode(ToolMode.Pipe);
        else if (keyboard.digit5Key.wasPressedThisFrame
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
        _pipeHandler.Disable();

        _mode = mode;

        switch (_mode)
        {
            case ToolMode.Terraform:
                _terraformHandler.Enable();
                break;
            case ToolMode.Placement:
                _placementHandler.Enable();
                break;
            case ToolMode.Pipe:
                _pipeHandler.Enable();
                break;
        }
    }
}
