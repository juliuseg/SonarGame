#ifndef FISH_INSTANCE_MATRICES_INCLUDED
#define FISH_INSTANCE_MATRICES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

StructuredBuffer<float4x4> _InstanceMatrices;
StructuredBuffer<uint> _DrawIndices;

uint ResolveFishInstanceID(uint drawInstanceID)
{
    return _DrawIndices[drawInstanceID];
}

void ApplyInstanceMatrix_float(float3 positionOS, float instanceID, out float3 positionOut)
{
    float4x4 m = _InstanceMatrices[ResolveFishInstanceID((uint)instanceID)];
    float3 worldPos = mul(m, float4(positionOS, 1.0)).xyz;
    positionOut = mul(unity_WorldToObject, float4(worldPos, 1.0)).xyz;
}

#endif
