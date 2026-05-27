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

    [Header("Scene References")]
    [SerializeField] private Transform chunkLoaderTarget;
    [SerializeField] private Transform chunkParent;

    [Header("Rendering")]
    [SerializeField] private Material terrainMaterial;
    
    [Header("Systems")]
    [SerializeField] private SDFGradientMover sdfGradientMover;
    [SerializeField] private RandomSteeredMover randomSteeredMover;
    [SerializeField] private ChunkSDFVisualizer sdfVisualizer;
    [SerializeField] private TerraformController terraformController;
    
    [Header("SDF Tests")]
    [SerializeField] private SDFAtlasTest sdfAtlasTest;
    [SerializeField] private ComputeShader sdfAtlasTestShader;
    
    
    private ChunkStreamer _chunkStreamer;
    private SpawnManager _spawnManager;
    private SDFAtlas _sdfAtlas;

    void Awake()
    {
        // Create and set up:
        var baker = new MCBaker(terrainShader, packShader, mcSettings);
        var sdfGen = new SDFGpu(densityShader, edtShader, mcSettings);
        
        var sdfAtlas = new SDFAtlas(chunkStreamingSettings.maxSdfSlots, mcSettings.chunkDims);
        _sdfAtlas = sdfAtlas;
        
        var chunkManager = new ChunkManager(mcSettings, sdfAtlas);
        var chunkBuilder = new ChunkBuilder(baker, sdfGen, chunkManager, chunkStreamingSettings, terrainMaterial, chunkParent, sdfAtlas);
        
        // Needs update loop
        _chunkStreamer = new ChunkStreamer(
            chunkBuilder,
            chunkManager,
            chunkStreamingSettings,
            chunkLoaderTarget
        );
        
        var spawnManager = new SpawnManager(chunkManager, mcSettings, chunkStreamingSettings, chunkLoaderTarget);
        _spawnManager = spawnManager;
        
        
        // Assign to systems
        if (sdfGradientMover != null) sdfGradientMover.Init(chunkManager);
        if (randomSteeredMover != null) randomSteeredMover.Init(chunkManager);
        if (sdfVisualizer != null) sdfVisualizer.Init(chunkManager, mcSettings);
        if (terraformController != null) terraformController.Init(_chunkStreamer);
        
        // SDF Tests
        if (sdfAtlasTest != null) 
            sdfAtlasTest.Init(chunkManager, sdfAtlas, mcSettings);
    }

    void Update()
    {
        _chunkStreamer.Tick();
        _spawnManager.Tick();
        
        _sdfAtlas.FlushLookup();
    }

    void OnDestroy()
    {
        _chunkStreamer.Dispose();
        _sdfAtlas?.Dispose();
    }
}