Shader "Tutorial/VolumetricFogUpscale"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "NearestDepthUpscale"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _DepthThreshold;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float centerDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);

                half4 bestSample = half4(0, 0, 0, 1);
                float bestDiff = _DepthThreshold;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 sampleUV = uv + float2(x, y) * _BlitTexture_TexelSize.xy;
                        float sampleDepth = LinearEyeDepth(SampleSceneDepth(sampleUV), _ZBufferParams);
                        float diff = abs(centerDepth - sampleDepth);

                        if (diff < bestDiff)
                        {
                            bestDiff = diff;
                            bestSample = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, sampleUV);
                        }
                    }
                }

                return bestSample;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_FogTexture);

            half4 CompositeFrag(Varyings input) : SV_Target
            {
                half4 scene = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half4 fog = SAMPLE_TEXTURE2D(_FogTexture, sampler_LinearClamp, input.texcoord);
                return half4(scene.rgb * fog.a + fog.rgb, scene.a);
            }
            ENDHLSL
        }
    }
}
