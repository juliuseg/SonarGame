using UnityEngine;
using UnityEngine.Rendering;

public class FaunaInstancer : MonoBehaviour
{

    public Mesh grass_mesh;
    public Material material;

    private ComputeBuffer argsBuffer;

    private ComputeBuffer addNumberBuffer;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {


    }

}
