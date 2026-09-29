using UnityEngine;

public interface IPathfindingSystem
{
    PathfindingGraphBuilder Builder { get; }
    bool IsBuilt { get; }
    bool IsBuilding { get; }
    void StartBuild(Vector3 position, Vector3 normal);
}
