using System;
using UnityEngine;

public class Pipe : MonoBehaviour
{
    public PipeNodeController InputNode;
    public PipeNodeController OutputNode;

    void OnDestroy()
    {
        if (InputNode != null)
            InputNode.occupied = false;
        if (OutputNode != null)
            OutputNode.occupied = false;

        var resolver = GameServices.Resolver;
        if (resolver != null && resolver.TryResolve<IAutomationSystem>(out var automation))
            automation.NotifyPipeDestroyed(this);
    }
}
