using System;
using UnityEngine;

public static class SpawnNodePersistence
{
    public static event Action<SpawnPointKey> EditedNodeRemoved;

    internal static void NotifyEditedNodeRemoved(SpawnPointKey key)
    {
        EditedNodeRemoved?.Invoke(key);
    }
}

public readonly struct SpawnPointKey : IEquatable<SpawnPointKey>
{
    readonly int _x;
    readonly int _y;
    readonly int _z;

    public SpawnPointKey(Vector3 spawnPointPosition)
    {
        _x = Mathf.FloorToInt(spawnPointPosition.x * 1000f);
        _y = Mathf.FloorToInt(spawnPointPosition.y * 1000f);
        _z = Mathf.FloorToInt(spawnPointPosition.z * 1000f);
    }

    public bool Equals(SpawnPointKey other) => _x == other._x && _y == other._y && _z == other._z;

    public override bool Equals(object obj) => obj is SpawnPointKey other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_x, _y, _z);
}

public class SpawnNodeController : MonoBehaviour
{
    [SerializeField] bool hasBeenEdited;
    Vector3 _spawnPointPosition;
    Vector3 _spawnPointNormal = Vector3.up;
    bool _initialized;

    public bool HasBeenEdited
    {
        get => hasBeenEdited;
        set => hasBeenEdited = value;
    }

    public Vector3 SpawnPointPosition => _spawnPointPosition;
    public Vector3 SpawnPointNormal => _spawnPointNormal;

    public void Initialize(Vector3 spawnPointPosition, Vector3 spawnPointNormal)
    {
        _spawnPointPosition = spawnPointPosition;
        _spawnPointNormal = spawnPointNormal.sqrMagnitude > 1e-6f ? spawnPointNormal.normalized : Vector3.up;
        _initialized = true;
    }

    public SpawnPointKey GetSpawnKey() => new SpawnPointKey(_spawnPointPosition);

    public void MarkEdited()
    {
        hasBeenEdited = true;
    }

    void OnDestroy()
    {
        if (!hasBeenEdited || !_initialized)
            return;

        SpawnNodePersistence.NotifyEditedNodeRemoved(GetSpawnKey());
    }
}
