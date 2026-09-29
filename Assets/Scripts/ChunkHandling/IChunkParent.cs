using UnityEngine;

// Scene transform that generated chunk objects are parented under.
public interface IChunkParent
{
    Transform Parent { get; }
}
