using UnityEngine;

public class AutomationEdge
{
    public int Id { get; }
    public GameObject PipeObject { get; }
    public AutomationNode NodeA { get; }
    public AutomationNode NodeB { get; }

    public AutomationEdge(int id, GameObject pipeObject, AutomationNode nodeA, AutomationNode nodeB)
    {
        Id = id;
        PipeObject = pipeObject;
        NodeA = nodeA;
        NodeB = nodeB;
    }
}
