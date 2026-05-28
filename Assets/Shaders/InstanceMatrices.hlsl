#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

StructuredBuffer<float4x4> _InstanceMatrices;

float3 TransformInstanceDirection(float3 dirOS, uint instanceID)
{
    float4x4 m = _InstanceMatrices[instanceID];
    float3 worldDir = mul(m, float4(dirOS, 0.0)).xyz;
    return normalize(mul((float3x3)unity_WorldToObject, worldDir));
}

void ApplyInstanceMatrix_float(float3 positionOS, float instanceID, out float3 positionOut)
{
    float4x4 m = _InstanceMatrices[(uint)instanceID];
    float3 worldPos = mul(m, float4(positionOS, 1.0)).xyz;
    positionOut = mul(unity_WorldToObject, float4(worldPos, 1.0)).xyz;
}

void ApplyInstanceNormal_float(float3 normalOS, float instanceID, out float3 normalOut)
{
    normalOut = TransformInstanceDirection(normalOS, (uint)instanceID);
}

void ApplyInstanceTangent_float(float3 tangentOS, float instanceID, out float3 tangentOut)
{
    tangentOut = TransformInstanceDirection(tangentOS, (uint)instanceID);
}

// Prefer this in Shader Graph: rebuilds an orthogonal TBN after instance rotation.
void ApplyInstanceNormalAndTangent_float(
    float3 normalOS,
    float3 tangentOS,
    float instanceID,
    out float3 normalOut,
    out float3 tangentOut)
{
    float4x4 m = _InstanceMatrices[(uint)instanceID];

    float3 worldNormal = normalize(mul(m, float4(normalOS, 0.0)).xyz);
    float3 worldTangent = normalize(mul(m, float4(tangentOS, 0.0)).xyz);
    worldTangent = normalize(worldTangent - worldNormal * dot(worldTangent, worldNormal));

    normalOut = normalize(mul((float3x3)unity_WorldToObject, worldNormal));
    tangentOut = normalize(mul((float3x3)unity_WorldToObject, worldTangent));
}
