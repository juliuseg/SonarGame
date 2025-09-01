using UnityEngine;

public class test : MonoBehaviour
{
    [Header("Mesh Settings")]
    public int width = 30;
    public int height = 30;
    public float scale = 20f;
    
    [Header("Noise Settings")]
    public float noiseScale = 0.1f;
    public int octaves = 4;
    public float persistence = 0.5f;
    public float lacunarity = 2f;
    public float heightMultiplier = 10f;
    
    [Header("Generation")]
    public bool generateOnStart = true;
    public bool updateMesh = false;
    
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh mesh;
    
    void Start()
    {
        // Get or add required components
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = gameObject.AddComponent<MeshFilter>();
            
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            
        meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
            meshCollider = gameObject.AddComponent<MeshCollider>();
        
        // Create a default material if none exists
        if (meshRenderer.material == null)
        {
            meshRenderer.material = new Material(Shader.Find("Standard"));
        }
        
        if (generateOnStart)
        {
            GenerateMesh();
        }
    }
    
    void Update()
    {
        if (updateMesh)
        {
            GenerateMesh();
            updateMesh = false;
        }
    }
    
    public void GenerateMesh()
    {
        mesh = new Mesh();
        mesh.name = "FractalPerlinMesh";
        
        // Generate vertices
        Vector3[] vertices = GenerateVertices();
        int[] triangles = GenerateTriangles();
        Vector2[] uvs = GenerateUVs();
        
        // Apply to mesh
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        
        // Recalculate normals for proper lighting
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        // Apply mesh to components
        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = mesh;
    }
    
    Vector3[] GenerateVertices()
    {
        Vector3[] vertices = new Vector3[(width + 1) * (height + 1)];
        
        for (int z = 0; z <= height; z++)
        {
            for (int x = 0; x <= width; x++)
            {
                float xCoord = (float)x / width * scale;
                float zCoord = (float)z / height * scale;
                
                // Generate fractal Perlin noise
                float y = GenerateFractalNoise(xCoord, zCoord);
                
                vertices[z * (width + 1) + x] = new Vector3(x - width/2f, y, z - height/2f);
            }
        }
        
        return vertices;
    }
    
    float GenerateFractalNoise(float x, float z)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float noiseHeight = 0f;
        float maxValue = 0f;
        
        for (int i = 0; i < octaves; i++)
        {
            float sampleX = x * noiseScale * frequency;
            float sampleZ = z * noiseScale * frequency;
            
            float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ) * 2f - 1f;
            noiseHeight += perlinValue * amplitude;
            
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }
        
        return (noiseHeight / maxValue) * heightMultiplier;
    }
    
    int[] GenerateTriangles()
    {
        int[] triangles = new int[width * height * 6];
        int tris = 0;
        
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int vertIndex = z * (width + 1) + x;
                
                // First triangle
                triangles[tris + 0] = vertIndex;
                triangles[tris + 1] = vertIndex + width + 1;
                triangles[tris + 2] = vertIndex + 1;
                
                // Second triangle
                triangles[tris + 3] = vertIndex + 1;
                triangles[tris + 4] = vertIndex + width + 1;
                triangles[tris + 5] = vertIndex + width + 2;
                
                tris += 6;
            }
        }
        
        return triangles;
    }
    
    Vector2[] GenerateUVs()
    {
        Vector2[] uvs = new Vector2[(width + 1) * (height + 1)];
        
        for (int z = 0; z <= height; z++)
        {
            for (int x = 0; x <= width; x++)
            {
                uvs[z * (width + 1) + x] = new Vector2((float)x / width, (float)z / height);
            }
        }
        
        return uvs;
    }
    
    // Method to regenerate mesh from inspector
    [ContextMenu("Generate Mesh")]
    public void RegenerateMesh()
    {
        GenerateMesh();
    }
}
