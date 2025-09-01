using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;


[Serializable, VolumeComponentMenu("Custom/CustomEffectComponent")]
[SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public class CustomEffectComponent : VolumeComponent, IPostProcessComponent
{
    public ClampedFloatParameter intensity = new ClampedFloatParameter(0, 0, 1, true);
    public NoInterpColorParameter overlayColor = new NoInterpColorParameter(Color.cyan);

    public bool IsActive() => intensity.value > 0;
    public bool IsTileCompatible() => true;
}

