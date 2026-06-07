using System.Collections.Generic;
using UnityEngine;

public class AutomationLogicSystem
{
    readonly List<AutomationNode> _nodes = new();
    readonly List<AutomationEdge> _edges = new();
    readonly Dictionary<Machine, AutomationNode> _machineToNode = new();
    readonly Dictionary<Pipe, AutomationEdge> _pipeToEdge = new();
    readonly HashSet<AutomationEdge> _minerSubEdges = new();

    readonly Inventory _inventory;

    int _nextNodeId;
    int _nextEdgeId;

    const float OrePerSecondPerMiner = 1f;

    public AutomationLogicSystem(Inventory inventory)
    {
        _inventory = inventory;

        Pipe.Destroyed += HandlePipeDestroyed;
        Machine.Destroyed += HandleMachineDestroyed;
    }

    public IReadOnlyList<AutomationNode> Nodes => _nodes;
    public IReadOnlyList<AutomationEdge> Edges => _edges;
    public bool IsMiningActive => _minerSubEdges.Count > 0;

    public void Dispose()
    {
        Pipe.Destroyed -= HandlePipeDestroyed;
        Machine.Destroyed -= HandleMachineDestroyed;
    }

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

        // PrintNetworkState();
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

        var edge = new AutomationEdge(_nextEdgeId++, pipe, outputLogicNode, inputLogicNode);
        outputLogicNode.SetOutputEdge(outputSlot, edge);
        inputLogicNode.SetInputEdge(inputSlot, edge);

        _edges.Add(edge);
        _pipeToEdge[pipe] = edge;

        if (IsMinerSubConnection(outputLogicNode, inputLogicNode))
            OnMinerSubEdgeAdded(edge, outputLogicNode);

        // PrintNetworkState();
        return edge;
    }

    public void Tick(float deltaTime)
    {
        if (_minerSubEdges.Count <= 0)
            return;

        _inventory.AddOre(OrePerSecondPerMiner * _minerSubEdges.Count * deltaTime);
    }

    public void RemoveMachine(Machine machine)
    {
        if (machine == null || !_machineToNode.TryGetValue(machine, out AutomationNode node))
            return;

        RemoveNode(node, machine);
    }

    void HandlePipeDestroyed(Pipe pipe)
    {
        if (pipe == null || !_pipeToEdge.TryGetValue(pipe, out AutomationEdge edge))
            return;

        RemoveEdge(edge, destroyPipeObject: false);
    }

    void HandleMachineDestroyed(Machine machine)
    {
        if (machine == null || !_machineToNode.TryGetValue(machine, out AutomationNode node))
            return;

        RemoveNode(node, machine);
    }

    void RemoveNode(AutomationNode node, Machine machine)
    {
        List<AutomationEdge> connectedEdges = node.GetConnectedEdges();
        for (int i = 0; i < connectedEdges.Count; i++)
            RemoveEdge(connectedEdges[i], destroyPipeObject: true);

        if (node.MachineType == MachineType.Miner)
            StopMiner(node);

        _machineToNode.Remove(machine);
        _nodes.Remove(node);

        // PrintNetworkState();
    }

    void RemoveEdge(AutomationEdge edge, bool destroyPipeObject)
    {
        if (edge == null)
            return;

        bool wasMinerSubEdge = _minerSubEdges.Contains(edge);
        AutomationNode minerNode = wasMinerSubEdge ? edge.NodeA : null;

        edge.NodeA.ClearOutputEdge(edge);
        edge.NodeB.ClearInputEdge(edge);

        _edges.Remove(edge);

        Pipe pipe = edge.Pipe;
        if (pipe != null)
            _pipeToEdge.Remove(pipe);

        if (wasMinerSubEdge)
            OnMinerSubEdgeRemoved(edge, minerNode);

        if (destroyPipeObject && pipe != null)
            Object.Destroy(pipe.gameObject);

        // PrintNetworkState();
    }

    static bool IsMinerSubConnection(AutomationNode outputNode, AutomationNode inputNode)
    {
        return outputNode.MachineType == MachineType.Miner
            && inputNode.MachineType == MachineType.Submarine;
    }

    void OnMinerSubEdgeAdded(AutomationEdge edge, AutomationNode minerNode)
    {
        _minerSubEdges.Add(edge);

        if (CountMinerSubEdges(minerNode) == 1)
            StartMiner(minerNode);
    }

    void OnMinerSubEdgeRemoved(AutomationEdge edge, AutomationNode minerNode)
    {
        _minerSubEdges.Remove(edge);

        if (minerNode != null && CountMinerSubEdges(minerNode) == 0)
            StopMiner(minerNode);
    }

    int CountMinerSubEdges(AutomationNode minerNode)
    {
        int count = 0;

        foreach (AutomationEdge edge in _minerSubEdges)
        {
            if (edge.NodeA == minerNode)
                count++;
        }

        return count;
    }

    void StartMiner(AutomationNode minerNode)
    {
        MinerAnimation minerAnimation = minerNode.MachineObject.GetComponentInChildren<MinerAnimation>();
        if (minerAnimation == null)
        {
            Debug.LogWarning($"AutomationLogicSystem.StartMiner could not find MinerAnimation on {minerNode.MachineObject.name}.");
            return;
        }

        minerAnimation.ActivateAnimation();
    }

    void StopMiner(AutomationNode minerNode)
    {
        MinerAnimation minerAnimation = minerNode.MachineObject.GetComponentInChildren<MinerAnimation>();
        if (minerAnimation == null)
        {
            Debug.LogWarning($"AutomationLogicSystem.StopMiner could not find MinerAnimation on {minerNode.MachineObject.name}.");
            return;
        }

        minerAnimation.StopAnimation();
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
