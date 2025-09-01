using UnityEngine;
using TMPro;

public class FPSCounter : MonoBehaviour
{
    [Header("FPS Display Settings")]
    [Tooltip("The TextMeshPro text component to display FPS")]
    public TextMeshProUGUI fpsText;
    
    [Tooltip("How often to update the FPS display (in seconds)")]
    [Range(0.1f, 2.0f)]
    public float updateInterval = 0.5f;
    
    [Tooltip("Show average FPS instead of current FPS")]
    public bool showAverageFPS = true;
    
    [Tooltip("Show frame time in milliseconds")]
    public bool showFrameTime = true;
    
    [Tooltip("Color for good FPS (60+)")]
    public Color goodFPSColor = Color.green;
    
    [Tooltip("Color for medium FPS (30-59)")]
    public Color mediumFPSColor = Color.yellow;
    
    [Tooltip("Color for low FPS (<30)")]
    public Color lowFPSColor = Color.red;
    
    [Header("Debug")]
    [Tooltip("Enable debug logging")]
    public bool enableDebugLog = false;
    
    // Private variables
    private float deltaTime = 0.0f;
    private float lastUpdateTime = 0.0f;
    private int frameCount = 0;
    private float fps = 0.0f;
    private float frameTime = 0.0f;
    
    // Rolling average for smoother FPS display
    private float[] fpsBuffer = new float[10];
    private int fpsBufferIndex = 0;
    private float averageFPS = 0.0f;
    
    void Start()
    {
        // Validate text component
        if (fpsText == null)
        {
            Debug.LogError("FPSCounter: No TextMeshPro text component assigned! Please assign one in the inspector.");
            enabled = false;
            return;
        }
        
        // Initialize FPS buffer
        for (int i = 0; i < fpsBuffer.Length; i++)
        {
            fpsBuffer[i] = 60f;
        }
        
        if (enableDebugLog)
        {
            Debug.Log("FPSCounter initialized successfully");
        }
    }
    
    void Update()
    {
        // Calculate current frame time and FPS
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        frameTime = deltaTime * 1000.0f;
        fps = 1.0f / deltaTime;
        
        // Add to rolling average buffer
        fpsBuffer[fpsBufferIndex] = fps;
        fpsBufferIndex = (fpsBufferIndex + 1) % fpsBuffer.Length;
        
        // Calculate average FPS
        float sum = 0f;
        for (int i = 0; i < fpsBuffer.Length; i++)
        {
            sum += fpsBuffer[i];
        }
        averageFPS = sum / fpsBuffer.Length;
        
        // Update display at specified interval
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateFPSText();
            lastUpdateTime = Time.time;
            frameCount = 0;
        }
        
        frameCount++;
    }
    
    void UpdateFPSText()
    {
        if (fpsText == null) return;
        
        // Choose which FPS value to display
        float displayFPS = showAverageFPS ? averageFPS : fps;
        
        // Format the text
        string fpsString = $"FPS: {displayFPS:F1}";
        
        if (showFrameTime)
        {
            fpsString += $" ({frameTime:F1}ms)";
        }
        
        // Set the text
        fpsText.text = fpsString;
        
        // Set color based on FPS performance
        if (displayFPS >= 60f)
        {
            fpsText.color = goodFPSColor;
        }
        else if (displayFPS >= 30f)
        {
            fpsText.color = mediumFPSColor;
        }
        else
        {
            fpsText.color = lowFPSColor;
        }
        
        if (enableDebugLog)
        {
            Debug.Log($"FPS Updated: {fpsString}, Color: {fpsText.color}");
        }
    }
    
    /// <summary>
    /// Get the current FPS value
    /// </summary>
    public float GetCurrentFPS()
    {
        return fps;
    }
    
    /// <summary>
    /// Get the average FPS value
    /// </summary>
    public float GetAverageFPS()
    {
        return averageFPS;
    }
    
    /// <summary>
    /// Get the current frame time in milliseconds
    /// </summary>
    public float GetFrameTime()
    {
        return frameTime;
    }
    
    /// <summary>
    /// Manually refresh the FPS display
    /// </summary>
    public void RefreshDisplay()
    {
        UpdateFPSText();
    }
    
    /// <summary>
    /// Reset the FPS counter and buffer
    /// </summary>
    public void ResetCounter()
    {
        deltaTime = 0.0f;
        frameCount = 0;
        lastUpdateTime = Time.time;
        
        for (int i = 0; i < fpsBuffer.Length; i++)
        {
            fpsBuffer[i] = 60f;
        }
        
        if (enableDebugLog)
        {
            Debug.Log("FPS Counter reset");
        }
    }
    
    void OnValidate()
    {
        // Ensure update interval is reasonable
        if (updateInterval < 0.1f)
            updateInterval = 0.1f;
        if (updateInterval > 2.0f)
            updateInterval = 2.0f;
    }
}

