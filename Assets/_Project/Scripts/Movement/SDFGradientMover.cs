using UnityEngine;

public class SDFGradientMover : MonoBehaviour
{

    [Header("Movement Settings")]
    public float speed = 5f;        // movement speed along gradient
    public bool normalizeGradient = true;

    private ISdfSampler _chunkManager;

    void Start()
    {
        if (!GameServices.EnsureInitialized().TryResolve(out _chunkManager))
        {
            Debug.LogError("No ISdfSampler registered. Is a TerrainSystem in the scene?");
            enabled = false;
        }
    }

    void Update()
    {
        if (_chunkManager == null) {
            Debug.LogError("ChunkManager not found.");
            return;
        }

        if (_chunkManager.TryGetSDFValue(transform.position, out float sdfValue))
        {
            Debug.Log("SDF Value: " + sdfValue);
        } else {
            Debug.LogError("Failed to get SDF Value");
        }

        if (_chunkManager.TrySampleSDFGradient(transform.position, out Vector3 gradient))
        {
            if (normalizeGradient && gradient != Vector3.zero)
                gradient.Normalize();

            transform.position += gradient * (speed * Time.deltaTime);
        }
    }
}
