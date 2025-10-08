using UnityEngine;

public class SphereUpAndDown : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        // Set x position equal to sin(time)
        transform.position = new Vector3(transform.position.x, Mathf.Sin(Time.time*10f)*3f, transform.position.z);
    }
}
