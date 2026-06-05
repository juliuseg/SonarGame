using UnityEngine;

[CreateAssetMenu(fileName = "PipeToolSettings", menuName = "Own/PipeToolSettings")]
public class PipeToolSettings : ScriptableObject
{
    [Header("Pointer")]
    public GameObject pointerPrefab;
    public float pointerWorldScale = 1f;

    [Header("Raycast")]
    public float maxRayDistance = 1000f;
    public float previewEndDistance = 5f;

    [Header("Tube")]
    public float radius = 0.1f;
    public int sides = 8;

    [Header("Segments")]
    public int baseSegments = 5;
    public float segmentsPerUnit = 2f;

    [Header("Tangent Clamping")]
    public float maxTangentFraction = 0.8f;

    [Header("Material")]
    public Material material;
    public Material material_error;
    public Material material_placed;
    public float maxBuildableLenght;

    [Header("Debug")]
    public bool debugRegenerateAllPipes;
}
