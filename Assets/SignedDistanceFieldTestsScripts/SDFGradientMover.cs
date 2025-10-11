using UnityEngine;

public class SDFGradientMover : MonoBehaviour
{
    [Header("References")]
    public ChunkLoader chunkLoader;

    [Header("Movement Settings")]
    public float speed = 5f;        // movement speed along gradient
    public bool normalizeGradient = true;

    private ChunkManager chunkManager;

    void Start()
    {
        if (chunkLoader == null)
        {
            Debug.LogError("SDFGradientMover requires a ChunkLoader reference.");
            enabled = false;
            return;
        }

        chunkManager = chunkLoader.chunkManager;
        if (chunkManager == null)
        {
            Debug.LogError("ChunkManager not found in ChunkLoader.");
            enabled = false;
        }
    }

    void Update()
    {
        if (chunkManager == null) {
            Debug.LogError("ChunkManager not found in ChunkLoader.");
            return;
        }

        if (chunkManager.TryGetSDFValue(transform.position, out float sdfValue))
        {
            Debug.Log("SDF Value: " + sdfValue);
        } else {
            Debug.LogError("Failed to get SDF Value");
        }

        if (chunkManager.TrySampleSDFGradient(transform.position, out Vector3 gradient))
        {
            if (normalizeGradient && gradient != Vector3.zero)
                gradient.Normalize();

            transform.position += gradient * (speed * Time.deltaTime);
        }
    }
}
