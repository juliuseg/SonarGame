using UnityEngine;
using UnityEngine.InputSystem;

public class ToolModeController : MonoBehaviour
{
    enum ToolMode
    {
        None,
        Dismantle,
        Placement,
        Pipe,
        Terraform
    }

    [SerializeField] private DismantleToolSettings dismantleSettings;
    [SerializeField] private PlacementToolSettings placementSettings;
    [SerializeField] private PipeToolSettings pipeSettings;
    [SerializeField] private TerraformToolSettings terraformSettings;

    DismantleToolHandler _dismantleHandler;
    PlacementToolHandler _placementHandler;
    PipeToolHandler _pipeHandler;
    TerraformToolHandler _terraformHandler;

    ToolMode _mode = ToolMode.None;
    bool _freeCameraActive;

    void Start()
    {
        var resolver = GameServices.EnsureInitialized();
        var automation = resolver.Resolve<IAutomationSystem>();
        var terrainData = resolver.Resolve<ITerrainData>();
        var terrainEditor = resolver.Resolve<ITerrainEditor>();

        Camera targetCamera = Camera.main;
        Transform pipeParent = new GameObject("Pipes").transform;

        _dismantleHandler = new DismantleToolHandler(dismantleSettings, targetCamera, automation);
        _placementHandler = new PlacementToolHandler(placementSettings, targetCamera, terrainData.ChunkManager, automation);
        _pipeHandler = new PipeToolHandler(pipeSettings, targetCamera, pipeParent, automation);
        _terraformHandler = new TerraformToolHandler(terrainEditor, terraformSettings, targetCamera);
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
            case ToolMode.Dismantle:
                _dismantleHandler.Tick();
                break;
            case ToolMode.Placement:
                _placementHandler.Tick();
                break;
            case ToolMode.Pipe:
                _pipeHandler.Tick();
                break;
            case ToolMode.Terraform:
                _terraformHandler.Tick();
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
            SetMode(ToolMode.Dismantle);
        else if (keyboard.digit3Key.wasPressedThisFrame)
            SetMode(ToolMode.Placement);
        else if (keyboard.digit4Key.wasPressedThisFrame)
            SetMode(ToolMode.Pipe);
        else if (keyboard.digit5Key.wasPressedThisFrame
                 || keyboard.digit6Key.wasPressedThisFrame
                 || keyboard.digit7Key.wasPressedThisFrame
                 || keyboard.digit8Key.wasPressedThisFrame)
            SetMode(ToolMode.None);
        else if (keyboard.digit9Key.wasPressedThisFrame)
            SetMode(ToolMode.Terraform);
    }

    void SetMode(ToolMode mode)
    {
        if (_mode == mode)
            return;

        _dismantleHandler.Disable();
        _placementHandler.Disable();
        _pipeHandler.Disable();
        _terraformHandler.Disable();

        _mode = mode;

        switch (_mode)
        {
            case ToolMode.Dismantle:
                _dismantleHandler.Enable();
                break;
            case ToolMode.Placement:
                _placementHandler.Enable();
                break;
            case ToolMode.Pipe:
                _pipeHandler.Enable();
                break;
            case ToolMode.Terraform:
                _terraformHandler.Enable();
                break;
        }
    }
}
