using UnityEngine;

[RequireComponent(typeof(Light))]
public class FogLight : MonoBehaviour
{
    public Light Light { get; private set; }

    IFogLightRegistry _registry;

    void Awake() => Light = GetComponent<Light>();

    void OnEnable()
    {
        StartCoroutine(GameServices.EnsureInitialized().ResolveWhenReady<IFogLightRegistry>(registry =>
        {
            _registry = registry;
            registry.Add(this);
        }));
    }

    void OnDisable()
    {
        _registry?.Remove(this);
        _registry = null;
    }
}
