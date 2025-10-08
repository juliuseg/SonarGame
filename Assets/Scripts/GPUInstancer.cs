using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

public class GPUInstancer : MonoBehaviour
{

    public Mesh mesh;
    public Material material;

    private ComputeBuffer argsBuffer;

    private ComputeBuffer addNumberBuffer;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        uint count = 10;
        material.SetKeyword(new LocalKeyword(material.shader, "_USE_ADDNUMBER"),true);

        addNumberBuffer = new ComputeBuffer((int)count, sizeof(float)*3);
        Vector3[] values = new Vector3[count];
        for (int i = 0; i < count; i++) values[i] = new Vector3(i, i, i);
        addNumberBuffer.SetData(values);

        // assign to material that uses your Shader Graph
        material.SetBuffer("_AddNumber", addNumberBuffer);

        // Set arguments for indirect instance draw
        argsBuffer = new ComputeBuffer(1, 5*sizeof(uint), ComputeBufferType.IndirectArguments);

        uint[] args = new uint[5];
        args[0] = mesh.GetIndexCount(0); // Indicies of our mesh
        args[1] = count; // Instance count
        args[2] = mesh.GetIndexStart(0); // Start index
        args[3] = mesh.GetBaseVertex(0); // Base vertex
        args[4] = 0; // ??? not sure what this is

        argsBuffer.SetData(args);

    }

    // Update is called once per frame
    void Update()
    {
        Bounds bounds = new Bounds(Vector3.zero, new Vector3(10, 10, 10));

        if (Keyboard.current.spaceKey.isPressed){
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer);
        }
    }

    void OnDestroy()
{
    if (addNumberBuffer != null) addNumberBuffer.Release();
    if (argsBuffer != null) argsBuffer.Release();
    material.SetKeyword(new LocalKeyword(material.shader, "_USE_ADDNUMBER"),false);

}
}
