public interface IAutomationSystem
{
    bool IsMiningActive { get; }
    AutomationNode CreateNode(Machine machine);
    AutomationEdge CreateEdge(Pipe pipe);
    void RemoveMachine(Machine machine);
    void NotifyMachineDestroyed(Machine machine);
    void NotifyPipeDestroyed(Pipe pipe);
}
