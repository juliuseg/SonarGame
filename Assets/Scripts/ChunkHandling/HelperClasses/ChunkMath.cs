using UnityEngine;
using System.Runtime.CompilerServices;


public static class ChunkMath
{
    public static float GetDynamicRadius(Vector3 position, ChunkStreamingSettings streamingSettings)
    {
        float y = position.y;

        if (y >= streamingSettings.waterLevel) return streamingSettings.surfaceRadius;
        if (y <= streamingSettings.deepLevel) return streamingSettings.deepRadius;

        float t = Mathf.InverseLerp(streamingSettings.waterLevel, streamingSettings.deepLevel, y);
        return Mathf.Lerp(streamingSettings.surfaceRadius, streamingSettings.deepRadius, t);
    }

    public static bool IsOutOfRange(Vector3 currentPos, Vector3 worldPos, float radius)
    {
        Vector3 dif = currentPos - worldPos;
        dif.y *= 1.8f;
        return dif.magnitude > radius;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Hash(Vector3 p)
    {
        // quantize to millimeter scale (adjust as needed)
        int xi = Mathf.FloorToInt(p.x * 1000f);
        int yi = Mathf.FloorToInt(p.y * 1000f);
        int zi = Mathf.FloorToInt(p.z * 1000f);

        uint h = (uint)(xi * 374761393 + yi * 668265263 + zi * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= (h >> 16);
        return (h & 0x00FFFFFF) / 16777216f;
    }
}