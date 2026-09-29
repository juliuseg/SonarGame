using UnityEngine;

// Owns the machine/pipe logic graph and the player inventory.
public class AutomationSystem : MonoBehaviour
{
    private Inventory _inventory;
    private AutomationLogicSystem _logic;
    private bool _registered;

    private void Awake()
    {
        _inventory = new Inventory();
        _logic = new AutomationLogicSystem(_inventory);
    }

    private void OnEnable()
    {
        var resolver = GameServices.EnsureInitialized();
        resolver.Register<IInventory>(_inventory);
        resolver.Register<IAutomationSystem>(_logic);
        _registered = true;
    }

    private void OnDisable()
    {
        if (!_registered) return;
        var resolver = GameServices.Resolver;
        if (resolver != null)
        {
            resolver.Unregister<IInventory>();
            resolver.Unregister<IAutomationSystem>();
        }
        _registered = false;
    }

    private void Update()
    {
        _logic.Tick(Time.deltaTime);
    }

    private void OnDestroy()
    {
        _logic?.Dispose();
    }
}
