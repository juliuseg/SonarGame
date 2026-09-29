using UnityEngine;

public class FogLightManager : MonoBehaviour, IFogLightRegistry
{
    const int MaxFogLights = 8;

    static readonly int CountId      = Shader.PropertyToID("_FogLightCount");
    static readonly int PosId        = Shader.PropertyToID("_FogLightPos");
    static readonly int DirId        = Shader.PropertyToID("_FogLightDir");
    static readonly int ColorId      = Shader.PropertyToID("_FogLightColor");
    static readonly int SpotParamsId = Shader.PropertyToID("_FogLightSpotParams");

    readonly Vector4[] _pos        = new Vector4[MaxFogLights];
    readonly Vector4[] _dir        = new Vector4[MaxFogLights];
    readonly Vector4[] _color      = new Vector4[MaxFogLights];
    readonly Vector4[] _spotParams = new Vector4[MaxFogLights];

    readonly System.Collections.Generic.List<FogLight> _lights = new();

    public void Add(FogLight light)
    {
        if (!_lights.Contains(light))
            _lights.Add(light);
    }

    public void Remove(FogLight light) => _lights.Remove(light);

    void OnEnable() => GameServices.EnsureInitialized().Register<IFogLightRegistry>(this);

    void OnDisable() => GameServices.Resolver?.Unregister<IFogLightRegistry>();

    void LateUpdate()
    {
        int count = Mathf.Min(_lights.Count, MaxFogLights);

        for (int i = 0; i < count; i++)
        {
            var t = _lights[i].transform;
            var l = _lights[i].Light;

            _pos[i] = new Vector4(t.position.x, t.position.y, t.position.z, l.range);
            _dir[i] = t.forward;

            // finalColor = color * intensity, exactly what URP puts in the GPU light buffer
            _color[i] = (l.color.linear * l.intensity);

            if (l.type == LightType.Spot)
            {
                float cosInner = Mathf.Cos(l.innerSpotAngle * 0.5f * Mathf.Deg2Rad);
                float cosOuter = Mathf.Cos(l.spotAngle      * 0.5f * Mathf.Deg2Rad);
                float x        = 1.0f / Mathf.Max(cosInner - cosOuter, 0.001f);
                float y        = -cosOuter * x;
                _spotParams[i] = new Vector4(x, y, 0, 0);
            }
            else
            {
                // Point light — no cone restriction
                _spotParams[i] = new Vector4(0, 1, 0, 0);
            }
        }

        Shader.SetGlobalInt(CountId,              count);
        Shader.SetGlobalVectorArray(PosId,        _pos);
        Shader.SetGlobalVectorArray(DirId,        _dir);
        Shader.SetGlobalVectorArray(ColorId,      _color);
        Shader.SetGlobalVectorArray(SpotParamsId, _spotParams);
    }
}