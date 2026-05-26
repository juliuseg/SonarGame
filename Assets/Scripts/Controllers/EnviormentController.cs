using UnityEngine;

public class EnviormentController : MonoBehaviour
{
    [Header("Water Settings")]
    public float waterHeight = 0f;
    public float darkestDepth = -50f;
    
    [Header("Fog Settings")]
    public float fogMin = 0.01f;
    public float fogMax = 0.1f;
    public Color surfaceColor = Color.cyan;
    public Color deepColor = Color.blue;

    [Header("Fog Settings: Debug Set Fog")]
    public bool setFog = false;
    public GameObject debugLight;
    
    [Header("Player Reference")]
    public Transform playerTransform;
    
    void Update()
    {
        if (playerTransform == null) return;
        
        

        if (setFog)
        {
            float playerHeight = playerTransform.position.y;
            float depthRatio = Mathf.Clamp01((waterHeight - playerHeight) / (waterHeight - darkestDepth));
            RenderSettings.fog = true;
            RenderSettings.fogDensity = Mathf.Lerp(fogMin, fogMax, depthRatio);
            RenderSettings.fogColor = Color.Lerp(surfaceColor, deepColor, depthRatio);
            debugLight.SetActive(false);
        } else {
            RenderSettings.fog = false;
            debugLight.SetActive(true);
        }
    }
}
