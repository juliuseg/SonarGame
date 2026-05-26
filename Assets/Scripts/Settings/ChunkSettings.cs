using UnityEngine;

[CreateAssetMenu(fileName = "ChunkSettings", menuName = "Settings/ChunkSettings")]
public class ChunkSettings : ScriptableObject
{
    [Header("Streaming")]
    [Min(0)] public float unloadBuffer = 10f;
    [Min(1)] public int maxBuildsPerFrame = 1;

    [Header("Dynamic Radius")]
    public float surfaceRadius = 50f;
    public float deepRadius = 20f;
    public float waterLevel = 20f;
    public float deepLevel = -10f;
}