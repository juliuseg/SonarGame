using UnityEngine;

public class ChunkSDFVisualizer : MonoBehaviour
{

    private ChunkLoader chunkLoader;

    public Vector3Int debugChunkCoord;

    public float colorMultiplier = 1f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        chunkLoader = GetComponent<ChunkLoader>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnDrawGizmos()
    {
        if (chunkLoader == null || chunkLoader.chunkManager == null) return;
        if (!chunkLoader.chunkManager.TryGetChunk(debugChunkCoord, out var chunk)) return;
        if (chunk.sdfData == null) return;
        float[,,] data = chunk.sdfData;

        int sx = data.GetLength(0);
        int sy = data.GetLength(1);
        int sz = data.GetLength(2);
        int minX = -sx / 2;
        int minY = -sy / 2;
        int minZ = -sz / 2;

        Vector3 chunkCenter = chunkLoader.chunkManager.ChunkCenterWorld(debugChunkCoord);

        Vector3 scale = chunkLoader.settings.scale;

        for (int x = minX; x < sx + minX; x++)
        for (int y = minY; y < sy + minY; y++)
        for (int z = minZ; z < sz + minZ; z++)
        {
            float d = data[x - minX, y - minY, z - minZ];
            float f = d * colorMultiplier;
            Gizmos.color = new Color(f, -f, 0f, 0.3f * Mathf.Clamp01(Mathf.Abs(f)));
            Vector3 pos = chunkCenter + new Vector3(x * scale.x, y * scale.y, z * scale.z);
            Gizmos.DrawCube(pos, Vector3.Scale(Vector3.one, scale)*0.9f);
        }
    }
}


