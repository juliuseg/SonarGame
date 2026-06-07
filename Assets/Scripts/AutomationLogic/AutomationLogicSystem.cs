using System.Collections.Generic;
using UnityEngine;

public class AutomationLogicSystem
{
    readonly List<AutomationNode> _nodes = new();
    readonly List<AutomationEdge> _edges = new();
    readonly Dictionary<Machine, AutomationNode> _machineToNode = new();
    readonly Dictionary<Pipe, AutomationEdge> _pipeToEdge = new();

    int _nextNodeId;
    int _nextEdgeId;

    public IReadOnlyList<AutomationNode> Nodes => _nodes;
    public IReadOnlyList<AutomationEdge> Edges => _edges;

    public AutomationNode CreateNode(Machine machine)
    {
        if (machine == null)
        {
            Debug.LogWarning("AutomationLogicSystem.CreateNode called with null machine.");
            return null;
        }

        if (_machineToNode.TryGetValue(machine, out AutomationNode existingNode))
            return existingNode;

        machine.AssignPipeNodeMetadata();

        var node = new AutomationNode(
            _nextNodeId++,
            machine.gameObject,
            machine.MachineType,
            machine.InputNodes.Count,
            machine.OutputNodes.Count);

        _nodes.Add(node);
        _machineToNode[machine] = node;

        PrintNetworkState();
        return node;
    }

    public AutomationEdge CreateEdge(Pipe pipe)
    {
        if (pipe == null)
        {
            Debug.LogWarning("AutomationLogicSystem.CreateEdge called with null pipe.");
            return null;
        }

        if (pipe.InputNode == null || pipe.OutputNode == null)
        {
            Debug.LogWarning("AutomationLogicSystem.CreateEdge called with missing input or output node.");
            return null;
        }

        if (_pipeToEdge.ContainsKey(pipe))
            return _pipeToEdge[pipe];

        Machine outputMachine = pipe.OutputNode.Parent;
        Machine inputMachine = pipe.InputNode.Parent;

        if (outputMachine == null || inputMachine == null)
        {
            Debug.LogWarning("AutomationLogicSystem.CreateEdge could not resolve machine parents.");
            return null;
        }

        if (!_machineToNode.TryGetValue(outputMachine, out AutomationNode outputLogicNode)
            || !_machineToNode.TryGetValue(inputMachine, out AutomationNode inputLogicNode))
        {
            Debug.LogWarning("AutomationLogicSystem.CreateEdge could not find logic nodes for connected machines.");
            return null;
        }

        int outputSlot = outputMachine.GetOutputSlot(pipe.OutputNode);
        int inputSlot = inputMachine.GetInputSlot(pipe.InputNode);

        if (outputSlot < 0 || inputSlot < 0)
        {
            Debug.LogWarning("AutomationLogicSystem.CreateEdge could not resolve edge slots.");
            return null;
        }

        var edge = new AutomationEdge(_nextEdgeId++, pipe.gameObject, outputLogicNode, inputLogicNode);
        outputLogicNode.SetOutputEdge(outputSlot, edge);
        inputLogicNode.SetInputEdge(inputSlot, edge);

        _edges.Add(edge);
        _pipeToEdge[pipe] = edge;

        PrintNetworkState();
        return edge;
    }

    public void PrintNetworkState()
    {
        Debug.Log("=== Automation Network ===");

        for (int i = 0; i < _edges.Count; i++)
        {
            AutomationEdge edge = _edges[i];
            Debug.Log($"Edge {edge.Id} connecting Node {edge.NodeA.Id} to Node {edge.NodeB.Id}");
        }

        for (int i = 0; i < _nodes.Count; i++)
        {
            AutomationNode node = _nodes[i];
            Debug.Log(
                $"Node {node.Id} ({node.MachineType}): " +
                $"output edges {node.OccupiedOutputEdgeCount}/{node.OutputEdgeCount}, " +
                $"input edges {node.OccupiedInputEdgeCount}/{node.InputEdgeCount}");
        }
    }
}
