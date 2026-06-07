using System;
using UnityEngine;

public class Pipe : MonoBehaviour
{
    public static event Action<Pipe> Destroyed;

    public PipeNodeController InputNode;
    public PipeNodeController OutputNode;

    void OnDestroy()
    {
        if (InputNode != null)
            InputNode.occupied = false;
        if (OutputNode != null)
            OutputNode.occupied = false;

        Destroyed?.Invoke(this);
    }
}
