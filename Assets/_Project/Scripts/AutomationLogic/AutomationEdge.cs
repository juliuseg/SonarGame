using UnityEngine;

public class AutomationEdge
{
    public int Id { get; }
    public Pipe Pipe { get; }
    public GameObject PipeObject { get; }
    public AutomationNode NodeA { get; }
    public AutomationNode NodeB { get; }

    public AutomationEdge(int id, Pipe pipe, AutomationNode nodeA, AutomationNode nodeB)
    {
        Id = id;
        Pipe = pipe;
        PipeObject = pipe.gameObject;
        NodeA = nodeA;
        NodeB = nodeB;
    }
}
