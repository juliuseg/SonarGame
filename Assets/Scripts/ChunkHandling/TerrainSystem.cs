using UnityEngine;

// Owns the chunk streaming / building / spawning stack.
// Builds in Awake (after the -200 anchors have registered) and registers in OnEnable, so consumers can resolve in Start.
[DefaultExecutionOrder(-100)]
public class TerrainSystem : MonoBehaviour, ISdfSampler, ITerrainData, ITerrainEditor, ITerrainDebug
{
    [SerializeField] private TerrainResources resources;

    private IStreamingFocus _focus;
    private bool _registered;

    private SDFAtlas _sdfAtlas;
    private ChunkManager _chunkManager;
    private ChunkBuilder _chunkBuilder;
    private ChunkStreamer _chunkStreamer;
    private SpawnManager _spawnManager;

    public ChunkManager ChunkManager => _chunkManager;
    public SDFAtlas SDFAtlas => _sdfAtlas;
    public ChunkStreamer ChunkStreamer => _chunkStreamer;
    public ChunkBuilder ChunkBuilder => _chunkBuilder;
    public SpawnManager SpawnManager => _spawnManager;
    public MCSettings MCSettings => resources.mcSettings;
    public ChunkStreamingSettings StreamingSettings => resources.chunkStreamingSettings;

    public bool TryGetSDFValue(Vector3 worldPos, out float value) => _chunkManager.TryGetSDFValue(worldPos, out value);
    public bool TrySampleSDFGradient(Vector3 worldPos, out Vector3 gradient) => _chunkManager.TrySampleSDFGradient(worldPos, out gradient);
    public void ApplyTerraformEdit(TerraformEdit edit) => _chunkStreamer.ApplyTerraformEdit(edit);

    private void Awake()
    {
        if (resources == null)
        {
            Debug.LogError("TerrainSystem: no TerrainResources assigned.", this);
            enabled = false;
            return;
        }

        var resolver = GameServices.EnsureInitialized();
        if (!resolver.TryResolve<IStreamingFocus>(out _focus))
        {
            Debug.LogError("TerrainSystem: no ChunkLoaderTarget in the scene.", this);
            enabled = false;
            return;
        }

        if (!resolver.TryResolve<IChunkParent>(out var chunkParent))
        {
            Debug.LogError("TerrainSystem: no ChunkParentAnchor in the scene.", this);
            enabled = false;
            return;
        }

        var mcSettings = resources.mcSettings;
        var streamingSettings = resources.chunkStreamingSettings;

        var baker = new MCBaker(resources.terrainShader, resources.packShader, mcSettings);
        var sdfGen = new SDFGpu(resources.densityShader, resources.edtShader, mcSettings);

        _sdfAtlas = new SDFAtlas(streamingSettings.maxSdfSlots, mcSettings.chunkDims);
        _chunkManager = new ChunkManager(mcSettings, _sdfAtlas);
        _chunkBuilder = new ChunkBuilder(baker, sdfGen, _chunkManager, streamingSettings, resources.terrainMaterial, chunkParent.Parent, _sdfAtlas);
        _chunkStreamer = new ChunkStreamer(_chunkBuilder, _chunkManager, streamingSettings, _focus);
        _spawnManager = new SpawnManager(_chunkManager, mcSettings, streamingSettings, _focus, resources.propInstanceBuildShader);
        _chunkBuilder.OnChunkReady += _spawnManager.HandleChunkReady;
    }

    private void OnEnable()
    {
        var resolver = GameServices.EnsureInitialized();
        resolver.Register<ISdfSampler>(this);
        resolver.Register<ITerrainData>(this);
        resolver.Register<ITerrainEditor>(this);
        resolver.Register<ITerrainDebug>(this);
        _registered = true;
    }

    private void OnDisable()
    {
        if (!_registered) return;
        var resolver = GameServices.Resolver;
        if (resolver != null)
        {
            resolver.Unregister<ISdfSampler>();
            resolver.Unregister<ITerrainData>();
            resolver.Unregister<ITerrainEditor>();
            resolver.Unregister<ITerrainDebug>();
        }
        _registered = false;
    }

    private void Update()
    {
        _chunkStreamer.Tick();
        _spawnManager.Tick();

        Vector3 position = _focus.Position;
        Vector3 chunkSize = _chunkManager.GetChunkSize();
        Vector3Int centerChunk = _chunkManager.WorldToChunk(position);
        int halfDim = ChunkMath.GetStreamHalfRangeChunks(position, chunkSize, resources.chunkStreamingSettings);
        _sdfAtlas.SyncChunkLookup(centerChunk, halfDim);
    }

    private void OnDestroy()
    {
        if (_chunkBuilder != null && _spawnManager != null)
            _chunkBuilder.OnChunkReady -= _spawnManager.HandleChunkReady;

        _spawnManager?.Dispose();
        _chunkStreamer?.Dispose();
        _sdfAtlas?.Dispose();
    }
}
