using System.Collections.Generic;
using UnityEngine;

public class Bootstrap : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private ChunkStreamingSettings chunkStreamingSettings;
    [SerializeField] private MCSettings mcSettings;

    [Header("Shaders")]
    [SerializeField] private ComputeShader densityShader;
    [SerializeField] private ComputeShader edtShader;
    [SerializeField] private ComputeShader terrainShader;
    [SerializeField] private ComputeShader packShader;
    [SerializeField] private ComputeShader propInstanceBuildShader;

    [Header("Scene References")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform chunkLoaderTarget;
    [SerializeField] private Transform chunkParent;

    [Header("Rendering")]
    [SerializeField] private Material terrainMaterial;
    
    [Header("Systems")]
    [SerializeField] private SDFGradientMover sdfGradientMover;
    [SerializeField] private List<RandomSteeredMover> randomSteeredMovers = new();
    [SerializeField] private ChunkSDFVisualizer sdfVisualizer;
    [SerializeField] private ToolModeController toolModeController;
    [SerializeField] private RuntimeDebugController runtimeDebug;
    
    [Header("SDF Tests")]
    [SerializeField] private SDFAtlasTest sdfAtlasTest;
    [SerializeField] private ComputeShader sdfAtlasTestShader;

    [Header("UI")]
    [SerializeField] private UIController uiController;

    [Header("Fish")]
    [SerializeField] private FishSpawnSystem fishSpawnSystem;

    [Header("Enemies")]
    [SerializeField] private SeaSnakeSpawnSystem seaSnakeSpawnSystem;

    [Header("Pathfinding")]
    [SerializeField] private PathfindingGraphSettings pathfindingGraphSettings;
    
    private ChunkStreamer _chunkStreamer;
    private SpawnManager _spawnManager;
    private ChunkBuilder _chunkBuilder;
    private ChunkManager _chunkManager;
    private SDFAtlas _sdfAtlas;
    private AutomationLogicSystem _automationLogic;
    private Inventory _inventory;
    private PathfindingGraphSystem _pathfindingGraphSystem;

    void Awake()
    {
        var baker = new MCBaker(terrainShader, packShader, mcSettings);
        var sdfGen = new SDFGpu(densityShader, edtShader, mcSettings);
        
        var sdfAtlas = new SDFAtlas(chunkStreamingSettings.maxSdfSlots, mcSettings.chunkDims);
        _sdfAtlas = sdfAtlas;
        
        var chunkManager = new ChunkManager(mcSettings, sdfAtlas);
        _chunkManager = chunkManager;
        var chunkBuilder = new ChunkBuilder(baker, sdfGen, chunkManager, chunkStreamingSettings, terrainMaterial, chunkParent, sdfAtlas);
        _chunkBuilder = chunkBuilder;
        
        _chunkStreamer = new ChunkStreamer(
            chunkBuilder,
            chunkManager,
            chunkStreamingSettings,
            chunkLoaderTarget
        );
        
        _spawnManager = new SpawnManager(chunkManager, mcSettings, chunkStreamingSettings, chunkLoaderTarget, propInstanceBuildShader);
        chunkBuilder.OnChunkReady += _spawnManager.HandleChunkReady;
        
        if (sdfGradientMover != null) sdfGradientMover.Init(chunkManager);
        foreach (var mover in randomSteeredMovers)
        {
            if (mover != null) mover.Init(chunkManager);
        }
        if (sdfVisualizer != null) sdfVisualizer.Init(chunkManager, mcSettings);

        _inventory = new Inventory();
        _automationLogic = new AutomationLogicSystem(_inventory);
        if (toolModeController != null) toolModeController.Init(_chunkStreamer, _automationLogic);

        foreach (Machine machine in FindObjectsByType<Machine>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (machine.RegisterOnStart)
                _automationLogic.CreateNode(machine);
        }
        
        if (sdfAtlasTest != null)
            sdfAtlasTest.Init(chunkManager, sdfAtlas, mcSettings, chunkStreamingSettings);

        if (fishSpawnSystem != null)
            fishSpawnSystem.Init(chunkManager, chunkStreamingSettings, chunkLoaderTarget, sdfAtlas, mcSettings);

        if (seaSnakeSpawnSystem != null)
            seaSnakeSpawnSystem.Init(_automationLogic, player, chunkManager);

        if (runtimeDebug != null)
            runtimeDebug.Init(chunkBuilder, _chunkStreamer, _spawnManager, fishSpawnSystem, sdfAtlas);

        if (uiController != null)
            uiController.Init(_inventory);

        if (pathfindingGraphSettings != null)
        {
            var pathfindingBuilder = new PathfindingGraphBuilder(pathfindingGraphSettings);
            _pathfindingGraphSystem = new PathfindingGraphSystem(pathfindingBuilder, this);
        }
    }

    public PathfindingGraphSystem PathfindingGraphSystem => _pathfindingGraphSystem;

    void Update()
    {
        runtimeDebug?.Apply();

        _chunkStreamer.Tick();
        _spawnManager.Tick();
        _automationLogic?.Tick(Time.deltaTime);
        if (fishSpawnSystem != null) fishSpawnSystem.Tick();
        if (seaSnakeSpawnSystem != null) seaSnakeSpawnSystem.Tick();
        
        if (_sdfAtlas != null && chunkLoaderTarget != null && _chunkManager != null)
        {
            Vector3 chunkSize = _chunkManager.GetChunkSize();
            Vector3Int centerChunk = _chunkManager.WorldToChunk(chunkLoaderTarget.position);
            int halfDim = ChunkMath.GetStreamHalfRangeChunks(
                chunkLoaderTarget.position, chunkSize, chunkStreamingSettings);
            _sdfAtlas.SyncChunkLookup(centerChunk, halfDim);
        }
    }

    void OnDestroy()
    {
        if (_chunkBuilder != null && _spawnManager != null)
            _chunkBuilder.OnChunkReady -= _spawnManager.HandleChunkReady;

        _spawnManager?.Dispose();
        _chunkStreamer.Dispose();
        _sdfAtlas?.Dispose();
        _automationLogic?.Dispose();
    }
}
