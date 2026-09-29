// Internals the runtime debug menu toggles.
public interface ITerrainDebug
{
    ChunkBuilder ChunkBuilder { get; }
    ChunkStreamer ChunkStreamer { get; }
    SpawnManager SpawnManager { get; }
    SDFAtlas SDFAtlas { get; }
}
