// Read access to loaded chunk data and the GPU SDF atlas, for consumers that work on chunks directly (fish, debug views).
public interface ITerrainData
{
    ChunkManager ChunkManager { get; }
    SDFAtlas SDFAtlas { get; }
    MCSettings MCSettings { get; }
    ChunkStreamingSettings StreamingSettings { get; }
}
