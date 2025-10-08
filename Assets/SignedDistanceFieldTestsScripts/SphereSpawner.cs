using UnityEngine;
using UnityEngine.InputSystem;

public class SphereSpawner : MonoBehaviour
{

    public GameObject spherePrefab;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
    // INSERT_YOUR_CODE
    // Using UnityEngine.InputSystem for the new Input System
    // Make sure you have "using UnityEngine.InputSystem;" at the top of your file if not already present

    if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
    {
        if (spherePrefab != null)
        {
            Instantiate(spherePrefab, transform.position, Quaternion.identity);
        }
        else
        {
            Debug.LogWarning("SphereSpawner: spherePrefab is not assigned!");
        }
    }
    }
}
