Shader "Tutorial/VolumetricFog"
{
    Properties
    {
        _Color("Color", Color) = (1, 1, 1, 1)
        _MaxDistance("Max distance", float) = 100
        _StepSize("Step size", Range(0.1, 20)) = 1
        _DensityMultiplier("Density multiplier", Range(0, 10)) = 1
        _NoiseOffset("Noise offset", float) = 0
        
        _FogNoise("Fog noise", 3D) = "white" {}
        _NoiseTiling("Noise tiling", float) = 1
        _FogNoiseMin("Fog noise min", Range(0, 1)) = 0.0
        _FogNoiseMax("Fog noise max", Range(0, 1)) = 1.0
        _DensityThreshold("Density threshold", Range(0, 1)) = 0.1
        
        [HDR]_LightContribution("Light contribution", Color) = (1, 1, 1, 1)
        _LightScattering("Light scattering", Range(0, 1)) = 0.2
        _FogNearRadius("Fog near radius", float) = 2.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _Color;
            float  _MaxDistance;
            float  _DensityMultiplier;
            float  _StepSize;
            float  _NoiseOffset;
            TEXTURE3D(_FogNoise);
            float  _NoiseTiling;
            float  _FogNoiseMin;
            float  _FogNoiseMax;
            float  _DensityThreshold;
            float4 _LightContribution;
            float  _LightScattering;
            float  _FogNearRadius;

            int    _FogLightCount;
            float4 _FogLightPos[8];
            float4 _FogLightDir[8];
            float4 _FogLightColor[8];
            float4 _FogLightSpotParams[8];

            float henyey_greenstein(float cosAngle, float scattering)
            {
                float denom = max(1e-5, 1.0 + scattering * scattering - 2.0 * scattering * cosAngle);
                return (1.0 - scattering * scattering) / (4.0 * PI * pow(denom, 1.5f));
            }

            float get_density(float3 worldPos)
            {
                float4 noise = _FogNoise.SampleLevel(sampler_TrilinearRepeat, worldPos * 0.01 * _NoiseTiling, 0);
                float density = dot(noise, noise);
                // Remap noise into desired range
                density = saturate((density - _FogNoiseMin) / max(_FogNoiseMax - _FogNoiseMin, 1e-5));
                // Threshold trims the bottom, multiplier scales overall strength
                density = saturate(density - _DensityThreshold) * _DensityMultiplier;
                return density;
            }

            float distance_attenuation(float distSqr, float range)
            {
                float lightAtten   = rcp(max(distSqr, 1e-4));
                float smoothFactor = saturate(distSqr * (-1.0 / (range * range)) + 1.0);
                return lightAtten * smoothFactor * smoothFactor;
            }

            float angle_attenuation(float3 lightForward, float3 lightToRayN, float2 spotParams)
            {
                float SdotL = dot(lightForward, lightToRayN);
                float atten = saturate(SdotL * spotParams.x + spotParams.y);
                return atten * atten;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float  depth    = SampleSceneDepth(IN.texcoord);
                float3 worldPos = ComputeWorldSpacePosition(IN.texcoord, depth, UNITY_MATRIX_I_VP);

                float3 entryPoint = _WorldSpaceCameraPos;
                float3 viewDir    = worldPos - _WorldSpaceCameraPos;
                float  viewLength = length(viewDir);
                float3 rayDir     = normalize(viewDir);

                float2 pixelCoords   = IN.texcoord * _ScreenParams.xy;
                float  distLimit     = min(viewLength, _MaxDistance);
                float  distTravelled = InterleavedGradientNoise(pixelCoords, (int)(_Time.y / max(HALF_EPS, unity_DeltaTime.x))) * _NoiseOffset;
                float  transmittance = 1;
                float4 fogCol        = _Color;

                while (distTravelled < distLimit)
                {
                    if (transmittance < 0.01)
                        break;

                    float3 rayPos  = entryPoint + rayDir * distTravelled;
                    float  density = get_density(rayPos);

                    if (density > 0.01)
                    {
                        for (int li = 0; li < _FogLightCount; li++)
                        {
                            float3 lightPos     = _FogLightPos[li].xyz;
                            float  range        = _FogLightPos[li].w;
                            float3 lightForward = _FogLightDir[li].xyz;

                            float3 lightToRay  = rayPos - lightPos;
                            float  distSqr     = max(dot(lightToRay, lightToRay), 1e-4);
                            float3 lightToRayN = lightToRay * rsqrt(distSqr);
                            float  dist        = sqrt(distSqr);

                            float nearFade = smoothstep(0.0, _FogNearRadius, dist);

                            float atten = saturate(
                                distance_attenuation(distSqr, range) *
                                angle_attenuation(lightForward, lightToRayN, _FogLightSpotParams[li].xy)
                            ) * nearFade;

                            float3 dirToLight = -lightToRayN;
                            float  cosAngle   = dot(rayDir, dirToLight);

                            float3 contrib = _FogLightColor[li].rgb * _LightContribution.rgb
                                * henyey_greenstein(cosAngle, _LightScattering)
                                * density * atten * _StepSize;

                            fogCol.rgb += min(contrib, 10.0);
                        }

                        transmittance *= exp(-density * _StepSize);
                    }

                    distTravelled += _StepSize;
                }

                return half4(fogCol.rgb, saturate(transmittance));
            }
            ENDHLSL
        }
    }
}