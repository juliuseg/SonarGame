using UnityEngine;

/// <summary>
/// Base class for terrain generation methods
/// Inherit from this to create different terrain generation algorithms
/// </summary>
public abstract class TerrainGenerator : MonoBehaviour
{
    [Header("Terrain Generator Settings")]
    [Tooltip("Size of the density field (width, height, depth)")]
    public Vector3Int fieldSize = new Vector3Int(16, 16, 16);
    [Tooltip("Size of each voxel in world units")]
    public float voxelSize = 0.5f;
    [Tooltip("Surface threshold for marching cubes")]
    public float isoLevel = 0.0f;
    
    /// <summary>
    /// Generate the density field for terrain generation
    /// </summary>
    /// <param name="chunkOrigin">World position of the chunk origin</param>
    /// <param name="fieldSize">Size of the density field</param>
    /// <param name="voxelSize">Size of each voxel</param>
    /// <returns>3D array of density values (1D array flattened)</returns>
    public abstract float[] GenerateDensityField(Vector3 chunkOrigin, Vector3Int fieldSize, float voxelSize);
    
    /// <summary>
    /// Get the name of this terrain generator for display purposes
    /// </summary>
    public abstract string GetGeneratorName();
    
    /// <summary>
    /// Validate the generator settings
    /// </summary>
    public virtual bool ValidateSettings()
    {
        if (fieldSize.x <= 0 || fieldSize.y <= 0 || fieldSize.z <= 0)
        {
            Debug.LogError($"{GetGeneratorName()}: Field size must be positive!");
            return false;
        }
        
        if (voxelSize <= 0)
        {
            Debug.LogError($"{GetGeneratorName()}: Voxel size must be positive!");
            return false;
        }
        
        return true;
    }
}
