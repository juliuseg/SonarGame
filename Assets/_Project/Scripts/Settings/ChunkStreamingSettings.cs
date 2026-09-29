using UnityEngine;

[CreateAssetMenu(fileName = "ChunkSettings", menuName = "Settings/ChunkSettings")]
public class ChunkStreamingSettings : ScriptableObject
{
    [Header("Streaming")]
    [Min(0)] public float unloadBuffer = 10f;
    [Tooltip("Max concurrent AsyncGPUReadback requests for chunk mesh + SDF.")]
    [Min(1)] public int maxAsyncReadbacks = 4;

    [Header("Dynamic Radius")]
    public float surfaceRadius = 50f;
    public float deepRadius = 20f;
    public float waterLevel = 20f;
    public float deepLevel = -10f;
    
    [Header("SDF Atlas")]
    public int maxSdfSlots = 512;

    [Header("Interior Spawn (fish / cave space)")]
    public float interiorSdfThreshold = 1f;
}