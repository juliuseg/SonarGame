using System.Collections.Generic;
using UnityEngine;

public class Machine : MonoBehaviour
{
    [SerializeField] MachineType machineType;
    [SerializeField] List<PipeNodeController> inputNodes = new();
    [SerializeField] List<PipeNodeController> outputNodes = new();
    [SerializeField] bool registerOnStart;

    public MachineType MachineType => machineType;
    public bool RegisterOnStart => registerOnStart;
    public IReadOnlyList<PipeNodeController> InputNodes => inputNodes;
    public IReadOnlyList<PipeNodeController> OutputNodes => outputNodes;

    public void AssignPipeNodeMetadata()
    {
        for (int i = 0; i < inputNodes.Count; i++)
        {
            if (inputNodes[i] == null)
                continue;

            inputNodes[i].Parent = this;
            inputNodes[i].Direction = PipeNodeDirection.Input;
        }

        for (int i = 0; i < outputNodes.Count; i++)
        {
            if (outputNodes[i] == null)
                continue;

            outputNodes[i].Parent = this;
            outputNodes[i].Direction = PipeNodeDirection.Output;
        }
    }

    public int GetInputSlot(PipeNodeController node)
    {
        for (int i = 0; i < inputNodes.Count; i++)
        {
            if (inputNodes[i] == node)
                return i;
        }

        return -1;
    }

    public int GetOutputSlot(PipeNodeController node)
    {
        for (int i = 0; i < outputNodes.Count; i++)
        {
            if (outputNodes[i] == node)
                return i;
        }

        return -1;
    }
}
