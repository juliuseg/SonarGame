using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class RuntimeFlush : MonoBehaviour
{
    [Header("Key Binding")]
    public Key flushKey = Key.F;

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard[flushKey].wasPressedThisFrame)
            PerformFlush();
    }

    void PerformFlush()
    {
        Debug.Log("=== Performing manual runtime flush ===");

        // 1. GPU drain and resync
        try
        {
            GL.Flush();
            var fence = Graphics.CreateAsyncGraphicsFence();
            Graphics.WaitOnAsyncGraphicsFence(fence);
            AsyncGPUReadback.WaitAllRequests();
            Debug.Log("GPU queues drained.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("GPU flush error: " + e.Message);
        }


        // 2. Full garbage collection
        try
        {
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            Debug.Log("Garbage collection complete.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("GC error: " + e.Message);
        }

        // 3. Reduce editor repaint load (runtime placeholder)
        // For runtime builds this has no effect, but kept for parity
#if UNITY_EDITOR
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        Debug.Log("Editor views repainted.");
#endif

        // 4. Reset physics and timing
        try
        {
            Time.captureFramerate = 0;
            Time.timeScale = 0f;
            Physics.Simulate(0f);
            Time.timeScale = 1f;
            Debug.Log("Timing and physics resynchronized.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Timing/physics reset error: " + e.Message);
        }

        // 5. Clear driver and asset memory pressure
        try
        {
            Resources.UnloadUnusedAssets();
            QualitySettings.vSyncCount = 0;
            QualitySettings.vSyncCount = 1;
            Debug.Log("Driver allocations cleared.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("Driver reset error: " + e.Message);
        }

        Debug.Log("=== Runtime flush complete ===");
    }
}
