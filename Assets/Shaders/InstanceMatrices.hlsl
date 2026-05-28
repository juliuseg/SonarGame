#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

StructuredBuffer<float4x4> _InstanceMatrices;

void ApplyInstanceMatrix_float(float3 positionOS, float instanceID, out float3 positionOut)
{
    float4x4 m = _InstanceMatrices[(uint)instanceID];
    float3 worldPos = mul(m, float4(positionOS, 1.0)).xyz;
    positionOut = mul(unity_WorldToObject, float4(worldPos, 1.0)).xyz;
}