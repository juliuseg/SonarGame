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
                float2 texelSize = _BlitTexture_TexelSize.xy;
                float2 lowResSize = _BlitTexture_TexelSize.zw;
                float centerDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);

                float2 pix = uv * lowResSize - 0.5;
                float2 pixBase = floor(pix);
                float2 f = pix - pixBase;

                half4 accum = half4(0, 0, 0, 0);
                float weightSum = 0.0;
                float depthThreshold = max(_DepthThreshold, 1e-4);

                [unroll]
                for (int j = 0; j < 2; j++)
                {
                    [unroll]
                    for (int i = 0; i < 2; i++)
                    {
                        float2 tapPix = pixBase + float2(i, j);
                        float2 tapUV = (tapPix + 0.5) * texelSize;
                        float bilinearW = (i == 0 ? (1.0 - f.x) : f.x) * (j == 0 ? (1.0 - f.y) : f.y);

                        float tapDepth = LinearEyeDepth(SampleSceneDepth(tapUV), _ZBufferParams);
                        float depthW = 1.0 - saturate(abs(centerDepth - tapDepth) / depthThreshold);
                        float w = bilinearW * depthW;

                        half4 tapFog = SAMPLE_TEXTURE2D(_BlitTexture, sampler_PointClamp, tapUV);
                        accum += tapFog * w;
                        weightSum += w;
                    }
                }

                // No depth-compatible low-res samples: leave the scene untouched.
                if (weightSum <= 1e-4)
                    return half4(0, 0, 0, 1);

                return accum / weightSum;
            }
            ENDHLSL
        }

        Pass
        {
            Name "TemporalReproject"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment TemporalFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_HistoryTexture);
            float4x4 _FogPrevViewProjMatrix;
            float _HistoryBlend;
            float _OccluderDepthThreshold;
            float _OccluderDepthFalloff;
            float _HistoryEdgeFadeDistance;
            float _HasValidHistory;

            float2 ReprojectToPreviousFrame(float3 worldPos)
            {
                float4 prevClip = mul(_FogPrevViewProjMatrix, float4(worldPos, 1.0));
                float2 prevNDC = prevClip.xy / prevClip.w;
                float2 prevUV = prevNDC * 0.5 + 0.5;
            #if UNITY_UV_STARTS_AT_TOP
                prevUV.y = 1.0 - prevUV.y;
            #endif
                return prevUV;
            }

            half4 ClampHistoryToNeighborhood(float2 uv, half4 current, half4 history)
            {
                half4 minFog = current;
                half4 maxFog = current;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 sampleUV = uv + float2(x, y) * _BlitTexture_TexelSize.xy;
                        half4 neighbor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, sampleUV);
                        minFog = min(minFog, neighbor);
                        maxFog = max(maxFog, neighbor);
                    }
                }

                return clamp(history, minFog, maxFog);
            }

            half4 TemporalFrag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 current = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                if (_HasValidHistory < 0.5)
                    return current;

                float depth = SampleSceneDepth(uv);
                float currLinear = LinearEyeDepth(depth, _ZBufferParams);
                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float2 prevUV = ReprojectToPreviousFrame(worldPos);

                float2 outside = max(-prevUV, prevUV - 1.0);
                float outsideDist = max(outside.x, outside.y);
                float edgeFade = 1.0;
                if (outsideDist > 0.0)
                {
                    edgeFade = saturate(1.0 - outsideDist / max(_HistoryEdgeFadeDistance, 1e-4));
                    prevUV = saturate(prevUV);
                }

                if (edgeFade <= 0.0)
                    return current;

                half4 history = SAMPLE_TEXTURE2D(_HistoryTexture, sampler_LinearClamp, prevUV);
                history = ClampHistoryToNeighborhood(uv, current, history);

                float blend = _HistoryBlend * edgeFade;

                if (_OccluderDepthThreshold > 0.0)
                {
                    float sampleDepth = SampleSceneDepth(prevUV);
                    float sampleLinear = LinearEyeDepth(sampleDepth, _ZBufferParams);
                    float depthGap = sampleLinear - currLinear;
                    float occluderFade = 1.0 - saturate((depthGap - _OccluderDepthThreshold) / max(_OccluderDepthFalloff, 1e-4));
                    blend *= occluderFade;
                }

                return lerp(current, history, blend);
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
