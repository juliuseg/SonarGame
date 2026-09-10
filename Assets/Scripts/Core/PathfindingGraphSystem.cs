using System.Collections;
using UnityEngine;

public class PathfindingGraphSystem
{
    readonly PathfindingGraphBuilder _builder;
    readonly MonoBehaviour _coroutineRunner;

    bool _buildRequested;

    public PathfindingGraphSystem(PathfindingGraphBuilder builder, MonoBehaviour coroutineRunner)
    {
        _builder = builder;
        _coroutineRunner = coroutineRunner;
    }

    public PathfindingGraphBuilder Builder => _builder;
    public bool IsBuilt => _builder.IsBuilt;
    public bool IsBuilding => _builder.IsBuilding;

    public void StartBuild(Vector3 position, Vector3 normal)
    {
        if (_builder.IsBuilding || _buildRequested) return;

        _buildRequested = true;
        _coroutineRunner.StartCoroutine(BuildRoutine(position, normal));
    }

    IEnumerator BuildRoutine(Vector3 position, Vector3 normal)
    {
        yield return _builder.Build(position, normal);
        _buildRequested = false;
    }
}
