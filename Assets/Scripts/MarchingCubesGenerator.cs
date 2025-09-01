// PerlinTerrainGenerator.cs
using System.Collections.Generic;
using UnityEngine;
using MarchingCubesProject;

// [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class MarchingCubesGenerator : MonoBehaviour
{
    [Header("Chunk System")]
    public Transform player;
    public int renderDistance = 1; // 1 = ~3x3x3 chunks, 2 = ~5x5x5 chunks
    [Range(0, 4)] public int chunkOverlap = 2; // Overlap voxels between chunks to prevent seams
    [Header("Chunk Loading Optimization")]
    [Tooltip("Enable ball-shaped chunk loading instead of cubic for better performance")]
    public bool useBallShapedLoading = true;
    [Tooltip("Additional distance check to ensure smooth transitions")]
    public float smoothTransitionDistance = 0.5f;
    [Header("Debug")]
    public bool showDebugInfo = false;
    public bool autoCalculateThreshold = false;
    public bool visualizeOverlap = false; // Show overlap areas in debug
    [Tooltip("Enable random colors for debugging - ignores height-based coloring")]
    public bool useRandomColors = false;

    [Header("Terrain Generation")]
    [Tooltip("The terrain generator component that creates the density field")]
    public TerrainGenerator terrainGenerator;
    
    [Header("Surface")]
    public bool recalcNormals = true;
    
    [Header("Terrain Coloring")]
    [Tooltip("Minimum Y value - anything below this becomes deep color")]
    public float minTerrainHeight = -10f;
    [Tooltip("Maximum Y value - anything above this becomes shallow color")]
    public float maxTerrainHeight = 10f;
    [Tooltip("Color for deep/low areas")]
    public Color deepColor = new Color(0.1f, 0.3f, 0.8f); // Deep blue
    [Tooltip("Color for shallow/high areas")]
    public Color shallowColor = new Color(0.96f, 0.87f, 0.70f); // Light beige
    
    [Header("Material")]
    public Material chunkMaterial;
    
    [Header("Controls")]
    [SerializeField] private bool regenerateOnValidate = false;

    // Chunk management
    private Dictionary<Vector3Int, ChunkData> chunks = new Dictionary<Vector3Int, ChunkData>();
    private Vector3Int lastPlayerChunk;
    
    [System.Serializable]
    public class ChunkData
    {
        public Vector3Int chunkPos;
        public GameObject chunkObject;
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public MeshCollider meshCollider;
        public bool isGenerated;
    }

    void Start()
    {
        if (player == null)
        {
            Debug.LogError("Player transform not assigned! Please assign the player transform.");
            return;
        }
        
        if (terrainGenerator == null)
        {
            Debug.LogError("Terrain generator not assigned! Please assign a TerrainGenerator component.");
            return;
        }
        
        lastPlayerChunk = GetChunkPosition(player.position);
        GenerateChunksAroundPlayer();
    }

    [ContextMenu("Regenerate Mesh")]
    public void RegenerateMesh()
    {
        if (Application.isPlaying)
            GenerateChunksAroundPlayer();
    }
    
    [ContextMenu("Regenerate All Chunks")]
    public void RegenerateAllChunks()
    {
        if (!Application.isPlaying) return;
        
        // Clear all existing chunks completely
        ClearAllChunks();
        
        // Regenerate chunks around player with new parameters
        GenerateChunksAroundPlayer();
    }
    
    /// <summary>
    /// Called when terrain generator parameters change
    /// </summary>
    public void OnTerrainGeneratorChanged()
    {
        if (Application.isPlaying)
        {
            RegenerateAllChunks();
        }
    }
    
    [ContextMenu("Clear All Chunks")]
    public void ClearAllChunks()
    {
        if (!Application.isPlaying) return;
        
        foreach (var chunk in chunks.Values)
        {
            if (chunk.chunkObject != null)
                DestroyImmediate(chunk.chunkObject);
        }
        chunks.Clear();
    }
    
    [ContextMenu("Check Materials")]
    public void CheckMaterials()
    {
        if (!Application.isPlaying) return;
        
        Debug.Log($"Assigned material: {(chunkMaterial != null ? chunkMaterial.name : "NULL")}");
        Debug.Log($"Total chunks: {chunks.Count}");
        
        foreach (var chunk in chunks.Values)
        {
            if (chunk.chunkObject != null && chunk.meshRenderer != null)
            {
                Debug.Log($"Chunk {chunk.chunkPos}: Material = {(chunk.meshRenderer.material != null ? chunk.meshRenderer.material.name : "NULL")}");
            }
        }
    }
    
    void Update()
    {
        if (player == null) return;
        
        Vector3Int currentPlayerChunk = GetChunkPosition(player.position);
        
        // Check if player moved to a new chunk
        if (currentPlayerChunk != lastPlayerChunk)
        {
            lastPlayerChunk = currentPlayerChunk;
            GenerateChunksAroundPlayer();
        }
    }
    
    void OnValidate()
    {
        // Only regenerate in play mode, not during validation
        if (regenerateOnValidate && Application.isPlaying && player != null)
        {
            // Use Invoke to delay the call until after validation
            Invoke(nameof(RegenerateAllChunks), 0.1f);
        }
    }

    private Vector3Int GetChunkPosition(Vector3 worldPos)
    {
        if (terrainGenerator == null) return Vector3Int.zero;
        
        return new Vector3Int(
            Mathf.FloorToInt(worldPos.x / (terrainGenerator.fieldSize.x * terrainGenerator.voxelSize)),
            Mathf.FloorToInt(worldPos.y / (terrainGenerator.fieldSize.y * terrainGenerator.voxelSize)),
            Mathf.FloorToInt(worldPos.z / (terrainGenerator.fieldSize.z * terrainGenerator.voxelSize))
        );
    }
    
    public void Generate()
    {
        // Legacy method - now generates chunks instead
        GenerateChunksAroundPlayer();
    }
    
    private void GenerateChunksAroundPlayer()
    {
        if (player == null || !Application.isPlaying) return;
        
        Vector3Int playerChunk = GetChunkPosition(player.position);
        
        if (useBallShapedLoading)
        {
            GenerateChunksInBallShape(playerChunk);
        }
        else
        {
            GenerateChunksInCubeShape(playerChunk);
        }
        
        // Remove chunks outside render distance
        RemoveDistantChunks(playerChunk);
    }
    
    private void GenerateChunksInBallShape(Vector3Int playerChunk)
    {
        float chunkRadius = renderDistance + smoothTransitionDistance;
        float chunkRadiusSquared = chunkRadius * chunkRadius;
        
        // Calculate the bounding box for potential chunks
        int maxDistance = Mathf.CeilToInt(chunkRadius);
        
        for (int x = -maxDistance; x <= maxDistance; x++)
        {
            for (int y = -maxDistance; y <= maxDistance; y++)
            {
                for (int z = -maxDistance; z <= maxDistance; z++)
                {
                    // Calculate distance from player chunk center
                    float distanceSquared = x * x + y * y + z * z;
                    
                    // Only generate chunks within the ball radius
                    if (distanceSquared <= chunkRadiusSquared)
                    {
                        Vector3Int chunkPos = playerChunk + new Vector3Int(x, y, z);
                        if (!chunks.ContainsKey(chunkPos) || !chunks[chunkPos].isGenerated)
                        {
                            GenerateChunk(chunkPos);
                        }
                    }
                }
            }
        }
        
        if (showDebugInfo)
        {
            int chunksInBall = 0;
            foreach (var chunk in chunks)
            {
                Vector3Int offset = chunk.Key - playerChunk;
                float distSq = offset.x * offset.x + offset.y * offset.y + offset.z * offset.z;
                if (distSq <= chunkRadiusSquared) chunksInBall++;
            }
            Debug.Log($"Ball-shaped loading: {chunksInBall} chunks within radius {chunkRadius:F1}");
        }
    }
    
    private void GenerateChunksInCubeShape(Vector3Int playerChunk)
    {
        // Original cubic loading method
        for (int x = -renderDistance; x <= renderDistance; x++)
        {
            for (int y = -renderDistance; y <= renderDistance; y++)
            {
                for (int z = -renderDistance; z <= renderDistance; z++)
                {
                    Vector3Int chunkPos = playerChunk + new Vector3Int(x, y, z);
                    if (!chunks.ContainsKey(chunkPos) || !chunks[chunkPos].isGenerated)
                    {
                        GenerateChunk(chunkPos);
                    }
                }
            }
        }
        
        if (showDebugInfo)
        {
            int totalChunks = (renderDistance * 2 + 1) * (renderDistance * 2 + 1) * (renderDistance * 2 + 1);
            Debug.Log($"Cube-shaped loading: {totalChunks} chunks in {renderDistance * 2 + 1}x{renderDistance * 2 + 1}x{renderDistance * 2 + 1} grid");
        }
    }
    
    private void GenerateChunk(Vector3Int chunkPos)
    {
        if (!Application.isPlaying) return;
        
        // Create chunk GameObject
        GameObject chunkObj = new GameObject($"Chunk_{chunkPos.x}_{chunkPos.y}_{chunkPos.z}");
        chunkObj.transform.parent = transform;
        chunkObj.transform.position = new Vector3(
            chunkPos.x * terrainGenerator.fieldSize.x * terrainGenerator.voxelSize,
            chunkPos.y * terrainGenerator.fieldSize.y * terrainGenerator.voxelSize,
            chunkPos.z * terrainGenerator.fieldSize.z * terrainGenerator.voxelSize
        );
        
        // Add components
        MeshFilter meshFilter = chunkObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkObj.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = chunkObj.AddComponent<MeshCollider>();
        
        // Set material
        if (chunkMaterial != null)
        {
            meshRenderer.material = chunkMaterial;
            meshRenderer.sharedMaterial = chunkMaterial;
        }
        else
        {
            // Fallback to default material if none assigned
            meshRenderer.material = new Material(Shader.Find("Standard"));
        }
        
        // Store chunk data
        ChunkData chunkData = new ChunkData
        {
            chunkPos = chunkPos,
            chunkObject = chunkObj,
            meshFilter = meshFilter,
            meshRenderer = meshRenderer,
            meshCollider = meshCollider,
            isGenerated = false
        };
        
        chunks[chunkPos] = chunkData;
        
        // Generate the chunk mesh
        GenerateChunkMesh(chunkData);
    }
    
    private void GenerateChunkMesh(ChunkData chunkData)
    {
        // Add overlap to prevent seams between chunks
        int w = terrainGenerator.fieldSize.x + chunkOverlap * 2+1;
        int h = terrainGenerator.fieldSize.y + chunkOverlap * 2+1;
        int d = terrainGenerator.fieldSize.z + chunkOverlap * 2+1;
        var field = new float[w * h * d];
        
        // Calculate chunk origin in world space (no overlap offset needed)
        Vector3 chunkOrigin = new Vector3(
            chunkData.chunkPos.x * terrainGenerator.fieldSize.x * terrainGenerator.voxelSize,
            chunkData.chunkPos.y * terrainGenerator.fieldSize.y * terrainGenerator.voxelSize,
            chunkData.chunkPos.z * terrainGenerator.fieldSize.z * terrainGenerator.voxelSize
        );
        
        // Generate density field using the terrain generator
        if (terrainGenerator == null)
        {
            Debug.LogError("No terrain generator assigned! Please assign a TerrainGenerator component.");
            return;
        }
        
        // Use the terrain generator to create the density field
        field = terrainGenerator.GenerateDensityField(chunkOrigin, new Vector3Int(w, h, d), terrainGenerator.voxelSize);
        
        if (field == null)
        {
            Debug.LogError("Terrain generator failed to create density field!");
            return;
        }
        
        // Generate mesh using marching cubes
        var verts = new List<Vector3>(5000);
        var indices = new List<int>(7500);
        var colors = new List<Color>(5000);
        
        var mc = new MarchingCubes(terrainGenerator.isoLevel);
        mc.Generate(field, w, h, d, verts, indices);
        
        // Calculate triangle colors based on depth
        for (int i = 0; i < indices.Count; i += 3)
        {
            // Get triangle center position (before world scaling)
            Vector3 triCenter = (verts[indices[i]] + verts[indices[i+1]] + verts[indices[i+2]]) / 3f;
            
            // Convert triangle center to world space for global terrain coloring
            Vector3 worldTriCenter = triCenter * terrainGenerator.voxelSize + chunkData.chunkObject.transform.position;
            
            // Normalize Y position to 0-1 range using global terrain height range
            float normalizedDepth = Mathf.InverseLerp(minTerrainHeight, maxTerrainHeight, worldTriCenter.y);
            normalizedDepth = Mathf.Clamp01(normalizedDepth); // Clamp to 0-1 range
            
            // Create color: either height-based or random
            Color triColor;
            if (useRandomColors)
            {
                // Generate random color for debugging
                triColor = new Color(
                    Random.Range(0.2f, 1.0f),
                    Random.Range(0.2f, 1.0f),
                    Random.Range(0.2f, 1.0f)
                );
            }
            else
            {
                // Use height-based coloring
                triColor = Color.Lerp(deepColor, shallowColor, normalizedDepth);
            }
            
            // Assign same color to all 3 vertices of this triangle
            colors.Add(triColor);
            colors.Add(triColor);
            colors.Add(triColor);
        }
        
        // Debug info
        if (showDebugInfo)
        {
            float minDensity = float.MaxValue, maxDensity = float.MinValue;
            int rockVoxels = 0, airVoxels = 0;
            
            for (int i = 0; i < field.Length; i++)
            {
                minDensity = Mathf.Min(minDensity, field[i]);
                maxDensity = Mathf.Max(maxDensity, field[i]);
                if (field[i] < 0f) rockVoxels++; else airVoxels++;
            }
            
            float rockPercentage = (rockVoxels / (float)field.Length) * 100f;
            // Debug.Log($"Chunk {chunkData.chunkPos}: {rockVoxels} rock ({rockPercentage:F1}%), {airVoxels} air, {verts.Count} vertices");
            
            if (visualizeOverlap)
            {
                Debug.Log($"Chunk {chunkData.chunkPos}: Size {w}x{h}x{d} (includes {chunkOverlap} voxel overlap on each side)");
            }
        }
        
        // Convert to world units and fix winding
        for (int i = 0; i < verts.Count; i++)
            verts[i] = verts[i] * terrainGenerator.voxelSize;
            
        // for (int i = 0; i < indices.Count; i += 3)
        // {
        //     int temp = indices[i];
        //     indices[i] = indices[i + 1];
        //     indices[i + 1] = temp;
        // }
        
        // Create and assign mesh
        Mesh mesh = new Mesh { name = $"Chunk_{chunkData.chunkPos.x}_{chunkData.chunkPos.y}_{chunkData.chunkPos.z}" };
        mesh.indexFormat = (verts.Count > 65535) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetTriangles(indices, 0, true);
        mesh.SetColors(colors);
        if (recalcNormals) mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        chunkData.meshFilter.sharedMesh = mesh;
        chunkData.meshCollider.sharedMesh = mesh;
        chunkData.isGenerated = true;
    }

    private void RemoveDistantChunks(Vector3Int playerChunk)
    {
        List<Vector3Int> chunksToRemove = new List<Vector3Int>();
        float maxDistanceSquared;
        
        if (useBallShapedLoading)
        {
            float maxRadius = renderDistance + smoothTransitionDistance + 0.5f; // Add small buffer
            maxDistanceSquared = maxRadius * maxRadius;
        }
        else
        {
            maxDistanceSquared = (renderDistance + 0.5f) * (renderDistance + 0.5f);
        }
        
        foreach (var chunk in chunks)
        {
            Vector3Int offset = chunk.Key - playerChunk;
            float distanceSquared;
            
            if (useBallShapedLoading)
            {
                // Use spherical distance for ball-shaped loading
                distanceSquared = offset.x * offset.x + offset.y * offset.y + offset.z * offset.z;
            }
            else
            {
                // Use cubic distance for cube-shaped loading
                distanceSquared = Mathf.Max(offset.x * offset.x, offset.y * offset.y, offset.z * offset.z);
            }
            
            if (distanceSquared > maxDistanceSquared)
            {
                chunksToRemove.Add(chunk.Key);
            }
        }
        
        foreach (var chunkPos in chunksToRemove)
        {
            if (chunks[chunkPos].chunkObject != null)
                Destroy(chunks[chunkPos].chunkObject);
            chunks.Remove(chunkPos);
        }
        
        if (showDebugInfo && chunksToRemove.Count > 0)
        {
            Debug.Log($"Removed {chunksToRemove.Count} distant chunks");
        }
    }
    
    /// <summary>
    /// Calculate the expected number of chunks for the current loading method
    /// </summary>
    public int GetExpectedChunkCount()
    {
        if (useBallShapedLoading)
        {
            // Approximate sphere volume: 4/3 * π * r³
            float radius = renderDistance + smoothTransitionDistance;
            float volume = (4f / 3f) * Mathf.PI * radius * radius * radius;
            return Mathf.RoundToInt(volume);
        }
        else
        {
            // Cubic volume: (2r + 1)³
            int sideLength = renderDistance * 2 + 1;
            return sideLength * sideLength * sideLength;
        }
    }
    
    /// <summary>
    /// Get performance comparison between loading methods
    /// </summary>
    public string GetPerformanceComparison()
    {
        int ballChunks = GetExpectedChunkCount();
        int cubeChunks = (renderDistance * 2 + 1) * (renderDistance * 2 + 1) * (renderDistance * 2 + 1);
        float improvement = ((float)(cubeChunks - ballChunks) / cubeChunks) * 100f;
        
        return $"Ball-shaped: ~{ballChunks} chunks, Cube-shaped: {cubeChunks} chunks\n" +
               $"Performance improvement: {improvement:F1}% fewer chunks";
    }
}
