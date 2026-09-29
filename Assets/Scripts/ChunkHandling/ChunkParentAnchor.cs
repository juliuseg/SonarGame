using UnityEngine;

// Put on the transform chunk objects should live under. Only one may exist.
[DefaultExecutionOrder(-200)]
public class ChunkParentAnchor : MonoBehaviour, IChunkParent
{
    private bool _registered;

    public Transform Parent => transform;

    private void OnEnable()
    {
        var resolver = GameServices.EnsureInitialized();
        if (resolver.TryResolve<IChunkParent>(out _))
        {
            Debug.LogError("ChunkParentAnchor: another chunk parent is already registered. Only one is allowed.", this);
            enabled = false;
            return;
        }

        resolver.Register<IChunkParent>(this);
        _registered = true;
    }

    private void OnDisable()
    {
        if (!_registered) return;
        GameServices.Resolver?.Unregister<IChunkParent>();
        _registered = false;
    }
}
