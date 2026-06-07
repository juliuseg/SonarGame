using System;
using UnityEngine;

public class AutomationNode
{
    public int Id { get; }
    public GameObject MachineObject { get; }
    public MachineType MachineType { get; }

    readonly AutomationEdge[] _inputEdges;
    readonly AutomationEdge[] _outputEdges;

    public AutomationNode(int id, GameObject machineObject, MachineType machineType, int inputEdgeCount, int outputEdgeCount)
    {
        Id = id;
        MachineObject = machineObject;
        MachineType = machineType;
        _inputEdges = new AutomationEdge[inputEdgeCount];
        _outputEdges = new AutomationEdge[outputEdgeCount];
    }

    public int InputEdgeCount => _inputEdges.Length;
    public int OutputEdgeCount => _outputEdges.Length;

    public int OccupiedInputEdgeCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _inputEdges.Length; i++)
            {
                if (_inputEdges[i] != null)
                    count++;
            }

            return count;
        }
    }

    public int OccupiedOutputEdgeCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _outputEdges.Length; i++)
            {
                if (_outputEdges[i] != null)
                    count++;
            }

            return count;
        }
    }

    public void SetInputEdge(int slot, AutomationEdge edge)
    {
        if (slot < 0 || slot >= _inputEdges.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));

        _inputEdges[slot] = edge;
    }

    public void SetOutputEdge(int slot, AutomationEdge edge)
    {
        if (slot < 0 || slot >= _outputEdges.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));

        _outputEdges[slot] = edge;
    }
}
