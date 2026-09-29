using UnityEngine;

// CPU-side signed distance queries against the world.
public interface ISdfSampler
{
    bool TryGetSDFValue(Vector3 worldPos, out float value);
    bool TrySampleSDFGradient(Vector3 worldPos, out Vector3 gradient);
}
