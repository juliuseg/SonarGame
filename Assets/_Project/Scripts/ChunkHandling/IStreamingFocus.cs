using UnityEngine;

// What the terrain streams chunks around.
public interface IStreamingFocus
{
    Vector3 Position { get; }
}
