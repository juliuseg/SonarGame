using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectResolver
{
    private readonly Dictionary<Type, object> _instances = new();

    public void Register<T>(T instance)
    {
        Debug.Assert(!_instances.ContainsKey(typeof(T)), $"ObjectResolver: overwriting existing registration for {typeof(T).FullName}.");

        _instances[typeof(T)] = instance;
    }

    public void Unregister<T>()
    {
        var removed = _instances.Remove(typeof(T));
        Debug.Assert(removed, $"ObjectResolver: tried to unregister {typeof(T).FullName}, but nothing was registered.");
    }

    public bool TryResolve<T>(out T instance)
    {
        if (_instances.TryGetValue(typeof(T), out var raw))
        {
            instance = (T)raw;
            return true;
        }

        instance = default;
        return false;
    }

    public T Resolve<T>()
    {
        if (TryResolve<T>(out var instance))
            return instance;

        throw new InvalidOperationException($"No registration for {typeof(T).FullName}");
    }

    public IEnumerator ResolveWhenReady<T>(Action<T> onResolved, float warnAfterSeconds = 5f)
    {
        T instance;
        float start = Time.unscaledTime;
        bool warned = false;

        while (!TryResolve(out instance))
        {
            if (!warned && Time.unscaledTime - start > warnAfterSeconds)
            {
                warned = true;
                Debug.LogWarning($"ObjectResolver: still waiting for {typeof(T).FullName} after {warnAfterSeconds}s. Is it in the scene?");
            }

            yield return null;
        }

        onResolved(instance);
    }
}
