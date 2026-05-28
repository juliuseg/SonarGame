Shader "Fish/InstancedIndirect"
{
    Properties
    {
        _BaseColor("Color", Color) = (0.2, 0.8, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Bound from C# via Material.SetBuffer (MPB does not reliably work with indirect + SSBO).
            StructuredBuffer<float4x4> _InstanceMatrices;
            float4 _BaseColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                float4x4 m = _InstanceMatrices[input.instanceID];
                float3 worldPos = mul(m, float4(input.positionOS.xyz, 1.0)).xyz;
                Varyings o;
                o.positionCS = TransformWorldToHClip(worldPos);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return half4(_BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
