using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class RuntimeDebugController : MonoBehaviour
{
    enum DebugOption
    {
        GenerateMesh,
        GenerateSdf,
        ChunkColliders,
        SpawnInstancingMeshes,
        FishSystem,
        VolumetricFog,
        DebugLights,
        ShowFps,
        TargetFrameRate,
        FishMaxInstances
    }

    static readonly DebugOption[] MenuOptions =
    {
        DebugOption.GenerateMesh,
        DebugOption.GenerateSdf,
        DebugOption.ChunkColliders,
        DebugOption.SpawnInstancingMeshes,
        DebugOption.FishSystem,
        DebugOption.VolumetricFog,
        DebugOption.DebugLights,
        DebugOption.ShowFps,
        DebugOption.TargetFrameRate,
        DebugOption.FishMaxInstances
    };

    static readonly int[] FrameRateOptions = { 60, 120, -1 };
    static readonly int[] FishCountOptions = { 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000 };

    [Header("UI")]
    [SerializeField] TextMeshProUGUI menuLabel;

    [Header("Generation")]
    [SerializeField] bool generateMesh = true;
    [SerializeField] bool generateSdf = true;
    [SerializeField] bool chunkColliders = true;

    [Header("Spawning")]
    [SerializeField] bool spawnInstancingMeshes = true;
    [SerializeField] bool fishSystem = true;
    [SerializeField] int fishMaxInstances = 5000;

    [Header("Performance")]
    [SerializeField] int targetFrameRate = 60;
    [SerializeField] bool showFps = true;
    [SerializeField] GameObject fpsUiObject;

    [Header("Rendering")]
    [SerializeField] bool volumetricFog;
    [SerializeField] VolumetricFogRenderFeature volumetricFogFeature;

    [SerializeField] bool debugLights;
    [SerializeField] GameObject debugLightObject;

    ChunkBuilder _chunkBuilder;
    ChunkStreamer _chunkStreamer;
    SpawnManager _spawnManager;
    FishSpawnSystem _fishSpawnSystem;
    SDFAtlas _sdfAtlas;

    bool _appliedGenerateMesh = true;
    bool _appliedGenerateSdf = true;
    bool _appliedChunkColliders = true;
    bool _appliedSpawnInstancingMeshes = true;
    bool _appliedFishSystem = true;
    bool _appliedVolumetricFog;
    bool _appliedDebugLights;
    bool _appliedShowFps;
    int _appliedTargetFrameRate = 60;
    int _appliedFishMaxInstances = 5000;

    bool _menuOpen = true;
    int _selectedIndex;

    public void Init(
        ChunkBuilder chunkBuilder,
        ChunkStreamer chunkStreamer,
        SpawnManager spawnManager,
        FishSpawnSystem fishSpawnSystem,
        SDFAtlas sdfAtlas)
    {
        _chunkBuilder = chunkBuilder;
        _chunkStreamer = chunkStreamer;
        _spawnManager = spawnManager;
        _fishSpawnSystem = fishSpawnSystem;
        _sdfAtlas = sdfAtlas;

        if (_fishSpawnSystem != null)
            fishMaxInstances = SnapToNearest(FishCountOptions, _fishSpawnSystem.maxInstances);

        targetFrameRate = SnapToNearest(FrameRateOptions, Application.targetFrameRate);

        _appliedGenerateMesh = generateMesh;
        _appliedGenerateSdf = generateSdf;
        _appliedChunkColliders = chunkColliders;
        _appliedSpawnInstancingMeshes = spawnInstancingMeshes;
        _appliedFishSystem = fishSystem;
        _appliedVolumetricFog = volumetricFog;
        _appliedDebugLights = debugLights;
        _appliedShowFps = showFps;
        _appliedTargetFrameRate = targetFrameRate;
        _appliedFishMaxInstances = fishMaxInstances;

        SetMenuOpen(_menuOpen);
        Apply();
        RefreshMenuLabel();
    }

    void Start()
    {
        SetMenuOpen(_menuOpen);
        ApplyShowFps();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.periodKey.wasPressedThisFrame)
            SetMenuOpen(!_menuOpen);

        if (_menuOpen)
        {
            HandleMenuInput();
            RefreshMenuLabel();
        }

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = locked;
        }
    }

    void OnValidate()
    {
        EnforceDependencies();
        fishMaxInstances = SnapToNearest(FishCountOptions, fishMaxInstances);
        targetFrameRate = SnapToNearest(FrameRateOptions, targetFrameRate);
    }

    void HandleMenuInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.upArrowKey.wasPressedThisFrame)
            _selectedIndex = (_selectedIndex - 1 + MenuOptions.Length) % MenuOptions.Length;

        if (keyboard.downArrowKey.wasPressedThisFrame)
            _selectedIndex = (_selectedIndex + 1) % MenuOptions.Length;

        if (keyboard.leftArrowKey.wasPressedThisFrame)
        {
            AdjustSelectedOption(-1);
            Apply();
        }

        if (keyboard.rightArrowKey.wasPressedThisFrame)
        {
            AdjustSelectedOption(1);
            Apply();
        }
    }

    void AdjustSelectedOption(int direction)
    {
        switch (MenuOptions[_selectedIndex])
        {
            case DebugOption.GenerateMesh:
                generateMesh = direction > 0;
                break;
            case DebugOption.GenerateSdf:
                generateSdf = direction > 0;
                break;
            case DebugOption.ChunkColliders:
                chunkColliders = direction > 0;
                break;
            case DebugOption.SpawnInstancingMeshes:
                spawnInstancingMeshes = direction > 0;
                break;
            case DebugOption.FishSystem:
                fishSystem = direction > 0;
                break;
            case DebugOption.VolumetricFog:
                volumetricFog = direction > 0;
                break;
            case DebugOption.DebugLights:
                debugLights = direction > 0;
                break;
            case DebugOption.ShowFps:
                showFps = direction > 0;
                break;
            case DebugOption.TargetFrameRate:
                targetFrameRate = StepInOptions(FrameRateOptions, targetFrameRate, direction);
                break;
            case DebugOption.FishMaxInstances:
                fishMaxInstances = StepInOptions(FishCountOptions, fishMaxInstances, direction);
                break;
        }

        EnforceDependencies();
    }

    static int StepInOptions(int[] options, int current, int direction)
    {
        int index = System.Array.IndexOf(options, current);
        if (index < 0)
            index = 0;

        index = (index + direction + options.Length) % options.Length;
        return options[index];
    }

    static int SnapToNearest(int[] options, int value)
    {
        int best = options[0];
        int bestDist = Mathf.Abs(value - best);
        for (int i = 1; i < options.Length; i++)
        {
            int dist = Mathf.Abs(value - options[i]);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = options[i];
            }
        }

        return best;
    }

    void RefreshMenuLabel()
    {
        if (menuLabel == null)
            return;

        var option = MenuOptions[_selectedIndex];
        menuLabel.text = $"{GetOptionName(option)} = {GetOptionDisplayValue(option)}";
    }

    static string GetOptionName(DebugOption option) => option switch
    {
        DebugOption.GenerateMesh => "generateMesh",
        DebugOption.GenerateSdf => "generateSdf",
        DebugOption.ChunkColliders => "chunkColliders",
        DebugOption.SpawnInstancingMeshes => "spawnInstancingMeshes",
        DebugOption.FishSystem => "fishSystem",
        DebugOption.VolumetricFog => "volumetricFog",
        DebugOption.DebugLights => "debugLights",
        DebugOption.ShowFps => "showFps",
        DebugOption.TargetFrameRate => "targetFrameRate",
        DebugOption.FishMaxInstances => "fishMaxInstances",
        _ => option.ToString()
    };

    string GetOptionDisplayValue(DebugOption option) => option switch
    {
        DebugOption.GenerateMesh => generateMesh.ToString().ToLower(),
        DebugOption.GenerateSdf => generateSdf.ToString().ToLower(),
        DebugOption.ChunkColliders => chunkColliders.ToString().ToLower(),
        DebugOption.SpawnInstancingMeshes => spawnInstancingMeshes.ToString().ToLower(),
        DebugOption.FishSystem => fishSystem.ToString().ToLower(),
        DebugOption.VolumetricFog => volumetricFog.ToString().ToLower(),
        DebugOption.DebugLights => debugLights.ToString().ToLower(),
        DebugOption.ShowFps => showFps.ToString().ToLower(),
        DebugOption.TargetFrameRate => FormatFrameRate(targetFrameRate),
        DebugOption.FishMaxInstances => fishMaxInstances.ToString(),
        _ => string.Empty
    };

    static string FormatFrameRate(int frameRate) => frameRate < 0 ? "max" : frameRate.ToString();

    void EnforceDependencies()
    {
        if (!generateSdf)
            fishSystem = false;

        if (!generateMesh)
            spawnInstancingMeshes = false;
    }

    public void Apply()
    {
        EnforceDependencies();
        ApplyDebugLights();
        ApplyShowFps();

        if (_chunkBuilder == null)
            return;

        bool meshChanged = generateMesh != _appliedGenerateMesh;
        bool sdfChanged = generateSdf != _appliedGenerateSdf;
        bool fishChanged = fishSystem != _appliedFishSystem;

        _chunkBuilder.GenerateMesh = generateMesh;
        _chunkBuilder.GenerateSdf = generateSdf;
        _chunkBuilder.GenerateColliders = chunkColliders;

        if (chunkColliders != _appliedChunkColliders)
        {
            _chunkBuilder.ApplyCollidersToLoadedChunks();
            _appliedChunkColliders = chunkColliders;
        }

        if (meshChanged || sdfChanged)
        {
            if (!generateSdf)
                _sdfAtlas?.ClearAll();

            if (!generateMesh || !generateSdf)
                _fishSpawnSystem?.ClearAllFish();

            _chunkStreamer?.RequestReload();
            _appliedGenerateMesh = generateMesh;
            _appliedGenerateSdf = generateSdf;
        }

        if (_spawnManager != null)
            _spawnManager.Enabled = spawnInstancingMeshes;
        _appliedSpawnInstancingMeshes = spawnInstancingMeshes;

        if (_fishSpawnSystem != null)
        {
            if (fishChanged && !fishSystem)
                _fishSpawnSystem.ClearAllFish();

            _fishSpawnSystem.Enabled = fishSystem && generateSdf;

            if (fishMaxInstances != _appliedFishMaxInstances)
            {
                _fishSpawnSystem.maxInstances = fishMaxInstances;
                _fishSpawnSystem.Reload();
                _appliedFishMaxInstances = fishMaxInstances;
            }
        }

        _appliedFishSystem = fishSystem;

        if (targetFrameRate != _appliedTargetFrameRate)
        {
            Application.targetFrameRate = targetFrameRate;
            _appliedTargetFrameRate = targetFrameRate;
        }

        if (volumetricFogFeature != null && volumetricFog != _appliedVolumetricFog)
            volumetricFogFeature.SetActive(volumetricFog);
        _appliedVolumetricFog = volumetricFog;
    }

    void ApplyDebugLights()
    {
        if (debugLightObject == null)
            return;

        debugLightObject.SetActive(debugLights);
        _appliedDebugLights = debugLights;
    }

    void ApplyShowFps()
    {
        if (fpsUiObject == null)
            return;

        fpsUiObject.SetActive(showFps);
        _appliedShowFps = showFps;
    }

    void SetMenuOpen(bool open)
    {
        _menuOpen = open;

        if (menuLabel != null)
            menuLabel.gameObject.SetActive(open);
    }
}
