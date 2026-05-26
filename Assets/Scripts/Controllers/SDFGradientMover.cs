using UnityEngine;

public class SDFGradientMover : MonoBehaviour
{

    [Header("Movement Settings")]
    public float speed = 5f;        // movement speed along gradient
    public bool normalizeGradient = true;

    private ChunkManager _chunkManager;
    
    public void Init(ChunkManager chunkManager)
    {
        _chunkManager = chunkManager;
    }

    void Start()
    {

        if (_chunkManager == null)
        {
            Debug.LogError("ChunkManager not found. Should be given via Init()");
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
