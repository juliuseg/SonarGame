using UnityEngine;

// Put on the transform chunks should stream around. Only one may exist.
[DefaultExecutionOrder(-200)]
public class ChunkLoaderTarget : MonoBehaviour, IStreamingFocus
{
    private bool _registered;

    public Vector3 Position => transform.position;

    private void OnEnable()
    {
        var resolver = GameServices.EnsureInitialized();
        if (resolver.TryResolve<IStreamingFocus>(out _))
        {
            Debug.LogError("ChunkLoaderTarget: another streaming focus is already registered. Only one is allowed.", this);
            enabled = false;
            return;
        }

        resolver.Register<IStreamingFocus>(this);
        _registered = true;
    }

    private void OnDisable()
    {
        if (!_registered) return;
        GameServices.Resolver?.Unregister<IStreamingFocus>();
        _registered = false;
    }
}
