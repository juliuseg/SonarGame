using UnityEngine;

[CreateAssetMenu(fileName = "TerraformToolSettings", menuName = "Own/TerraformToolSettings")]
public class TerraformToolSettings : ScriptableObject
{
    public GameObject pointerPrefab;
    public float maxRayDistance = 1000f;
    [Tooltip("Apparent pointer size as a fraction of screen height.")]
    public float pointerScreenSize = 0.05f;
    public float terraformStrength = 1f;
    public float terraformRadius = 1f;
}
