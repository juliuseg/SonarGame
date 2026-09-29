using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public static class SpawnDistributor
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Hash(Vector3 p)
    {
        int xi = Mathf.FloorToInt(p.x * 1000f);
        int yi = Mathf.FloorToInt(p.y * 1000f);
        int zi = Mathf.FloorToInt(p.z * 1000f);

        uint h = (uint)(xi * 374761393 + yi * 668265263 + zi * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= (h >> 16);
        return (h & 0x00FFFFFF) / 16777216f;
    }

    public static List<List<SpawnPoint>> Distribute(
        List<SpawnPoint> points,
        IList<float> probabilities,
        int biomeIndex)
    {
        var results = new List<List<SpawnPoint>>(probabilities.Count);
        for (int i = 0; i < probabilities.Count; i++)
            results.Add(new List<SpawnPoint>());

        if (probabilities.Count == 0)
            return results;

        var weights = new float[probabilities.Count];
        float total = 0f;
        for (int i = 0; i < probabilities.Count; i++)
        {
            float prob = Mathf.Max(0f, probabilities[i]);
            weights[i] = prob;
            total += prob;
        }

        if (total <= 0f)
            return results;

        if (total > 1f)
        {
            for (int i = 0; i < weights.Length; i++)
                weights[i] /= total;
        }

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].colorWS.x != biomeIndex)
                continue;

            float rnd = Hash(points[i].positionWS);
            float acc = 0f;
            for (int j = 0; j < weights.Length; j++)
            {
                acc += weights[j];
                if (rnd < acc)
                {
                    results[j].Add(points[i]);
                    break;
                }
            }
        }

        return results;
    }
}
