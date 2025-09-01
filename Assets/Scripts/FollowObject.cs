using UnityEngine;

public class FollowObject : MonoBehaviour
{

    public Transform target;

    public Vector3 offset;

    public float constHeight;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (constHeight > 0){
            transform.position = new Vector3(target.position.x, constHeight, target.position.z) + offset;
        }else{
            transform.position = target.position + offset;
        }
    }
}
