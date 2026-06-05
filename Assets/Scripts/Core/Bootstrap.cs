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
    [SerializeField] private Transform chunkLoaderTarget;
    [SerializeField] private Transform chunkParent;

    [Header("Rendering")]
    [SerializeField] private Material terrainMaterial;
    
    [Header("Systems")]
    [SerializeField] private SDFGradientMover sdfGradientMover;
    [SerializeField] private RandomSteeredMover randomSteeredMover;
    [SerializeField] private ChunkSDFVisualizer sdfVisualizer;
    [SerializeField] private ToolModeController toolModeController;
    [SerializeField] private RuntimeDebugController runtimeDebug;
    
    [Header("SDF Tests")]
    [SerializeField] private SDFAtlasTest sdfAtlasTest;
    [SerializeField] private ComputeShader sdfAtlasTestShader;

    [Header("Fish")]
    [SerializeField] private FishSpawnSystem fishSpawnSystem;
    
    private ChunkStreamer _chunkStreamer;
    private SpawnManager _spawnManager;
    private ChunkBuilder _chunkBuilder;
    private SDFAtlas _sdfAtlas;

    void Awake()
    {
        var baker = new MCBaker(terrainShader, packShader, mcSettings);
        var sdfGen = new SDFGpu(densityShader, edtShader, mcSettings);
        
        var sdfAtlas = new SDFAtlas(chunkStreamingSettings.maxSdfSlots, mcSettings.chunkDims);
        _sdfAtlas = sdfAtlas;
        
        var chunkManager = new ChunkManager(mcSettings, sdfAtlas);
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
        if (randomSteeredMover != null) randomSteeredMover.Init(chunkManager);
        if (sdfVisualizer != null) sdfVisualizer.Init(chunkManager, mcSettings);
        if (toolModeController != null) toolModeController.Init(_chunkStreamer);
        
        if (sdfAtlasTest != null) 
            sdfAtlasTest.Init(chunkManager, sdfAtlas, mcSettings);

        if (fishSpawnSystem != null)
            fishSpawnSystem.Init(chunkManager, chunkStreamingSettings, chunkLoaderTarget, sdfAtlas, mcSettings);

        if (runtimeDebug != null)
            runtimeDebug.Init(chunkBuilder, _chunkStreamer, _spawnManager, fishSpawnSystem, sdfAtlas);
    }

    void Update()
    {
        runtimeDebug?.Apply();

        _chunkStreamer.Tick();
        _spawnManager.Tick();
        if (fishSpawnSystem != null) fishSpawnSystem.Tick();
        
        _sdfAtlas.FlushLookup();
    }

    void OnDestroy()
    {
        if (_chunkBuilder != null && _spawnManager != null)
            _chunkBuilder.OnChunkReady -= _spawnManager.HandleChunkReady;

        _spawnManager?.Dispose();
        _chunkStreamer.Dispose();
        _sdfAtlas?.Dispose();
    }
}
