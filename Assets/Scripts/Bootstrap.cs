using UnityEngine;

public class Bootstrap : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private ChunkSettings chunkSettings;
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
    
    
    private ChunkStreamer _chunkStreamer;
    private SpawnManager _spawnManager;

    void Awake()
    {
        // Create and set up:
        var baker = new MCBaker(terrainShader, packShader, mcSettings);
        var sdfGen = new SDFGpu(densityShader, edtShader, mcSettings);
        var chunkManager = new ChunkManager(mcSettings);
        var chunkBuilder = new ChunkBuilder(baker, sdfGen, chunkManager, chunkSettings, terrainMaterial, chunkParent);

        // Needs update loop
        _chunkStreamer = new ChunkStreamer(
            chunkBuilder,
            chunkManager,
            chunkSettings,
            chunkLoaderTarget
        );
        
        var spawnManager = new SpawnManager(chunkManager, mcSettings, chunkSettings, chunkLoaderTarget);
        _spawnManager = spawnManager;
        
        // Assign to systems
        if (sdfGradientMover != null) sdfGradientMover.Init(chunkManager);
        if (randomSteeredMover != null) randomSteeredMover.Init(chunkManager);
        if (sdfVisualizer != null) sdfVisualizer.Init(chunkManager, mcSettings);
    }

    void Update()
    {
        _chunkStreamer.Tick();
        _spawnManager.Tick();
    }

    void OnDestroy()
    {
        _chunkStreamer.Dispose();
    }
}