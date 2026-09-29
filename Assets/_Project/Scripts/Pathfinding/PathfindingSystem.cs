using UnityEngine;

// Hosts the navigation graph builder so it has a coroutine runner and a lifetime.
public class PathfindingSystem : MonoBehaviour
{
    [SerializeField] private PathfindingGraphSettings settings;

    private PathfindingGraphSystem _system;
    private bool _registered;

    private void Awake()
    {
        if (settings == null)
        {
            Debug.LogError("PathfindingSystem: no PathfindingGraphSettings assigned.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        _system ??= new PathfindingGraphSystem(new PathfindingGraphBuilder(settings), this);
        GameServices.EnsureInitialized().Register<IPathfindingSystem>(_system);
        _registered = true;
    }

    private void OnDisable()
    {
        if (!_registered) return;
        GameServices.Resolver?.Unregister<IPathfindingSystem>();
        _registered = false;
    }
}
