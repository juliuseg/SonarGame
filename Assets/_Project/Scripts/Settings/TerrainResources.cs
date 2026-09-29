using UnityEngine;

[CreateAssetMenu(fileName = "TerrainResources", menuName = "Own/TerrainResources")]
public class TerrainResources : ScriptableObject
{
    [Header("Settings")]
    public ChunkStreamingSettings chunkStreamingSettings;
    public MCSettings mcSettings;

    [Header("Shaders")]
    public ComputeShader densityShader;
    public ComputeShader edtShader;
    public ComputeShader terrainShader;
    public ComputeShader packShader;
    public ComputeShader propInstanceBuildShader;

    [Header("Rendering")]
    public Material terrainMaterial;
}
