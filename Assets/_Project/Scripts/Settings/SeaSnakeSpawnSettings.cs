using UnityEngine;

[CreateAssetMenu(fileName = "SeaSnakeSpawnSettings", menuName = "Settings/SeaSnakeSpawnSettings")]
public class SeaSnakeSpawnSettings : ScriptableObject
{
    [Header("Prefab")]
    public GameObject prefab;

    [Header("Spawn Distance")]
    [Tooltip("Minimum distance from the player. Also used as the max ray length and despawn range.")]
    [Min(1f)] public float distanceFromPlayer = 30f;

    [Header("Capacity")]
    [Tooltip("Maximum sea snakes alive at once.")]
    [Min(1)] public int maxSpawned = 5;

    [Header("Timing")]
    [Tooltip("Seconds between spawn attempts.")]
    [Min(0.1f)] public float spawnInterval = 8f;

    [Header("Raycast")]
    [Tooltip("Random rays tried per spawn attempt before giving up.")]
    [Min(1)] public int maxRayAttempts = 32;
    [Tooltip("Only raycast against these layers (default: Walls, layer 6).")]
    public LayerMask wallLayerMask = 1 << 6;
}
