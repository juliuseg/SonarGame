using UnityEngine;

[RequireComponent(typeof(Light))]
public class FogLight : MonoBehaviour
{
    public Light Light { get; private set; }
    void Awake() => Light = GetComponent<Light>();
}