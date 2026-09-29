using UnityEngine;

/// <summary>
/// Optional helper for multi-leg creatures. Each leg solves independently.
/// </summary>
public class AnalyticalLegIkRig : MonoBehaviour
{
    public AnalyticalLegIk[] legs;

    [Header("Shared pole (optional)")]
    public Transform pole;

    void Reset()
    {
        legs = GetComponentsInChildren<AnalyticalLegIk>();
    }

    void OnValidate()
    {
        if (legs == null) return;
        foreach (var leg in legs)
        {
            if (leg != null && leg.pole == null && pole != null)
                leg.pole = pole;
        }
    }
}
