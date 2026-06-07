using UnityEngine;
using UnityEngine.InputSystem;

public class DismantleToolHandler
{
    readonly DismantleToolSettings _settings;
    readonly Camera _camera;
    readonly AutomationLogicSystem _automationLogic;

    GameObject _pointer;

    public DismantleToolHandler(DismantleToolSettings settings, Camera camera, AutomationLogicSystem automationLogic)
    {
        _settings = settings;
        _camera = camera;
        _automationLogic = automationLogic;
    }

    public void Enable()
    {
        if (_settings == null || _settings.pointerPrefab == null)
            return;

        _pointer = Object.Instantiate(_settings.pointerPrefab);
        _pointer.SetActive(false);
    }

    public void Disable()
    {
        if (_pointer != null)
        {
            Object.Destroy(_pointer);
            _pointer = null;
        }
    }

    public void Tick()
    {
        if (_camera == null || _settings == null)
            return;

        Ray centerRay = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        bool hasTarget = TryGetTarget(centerRay, out Pipe pipe, out Machine machine, out RaycastHit hit);

        UpdatePointer(hasTarget, hit);

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame || !hasTarget)
            return;

        if (pipe != null)
            Object.Destroy(pipe.gameObject);
        else if (machine != null)
            DismantleMachine(machine);
    }

    void DismantleMachine(Machine machine)
    {
        CrystalController crystal = machine.GetComponentInParent<CrystalController>();
        if (crystal != null)
        {
            _automationLogic?.RemoveMachine(machine);
            crystal.ClearMiner();
            return;
        }

        Object.Destroy(machine.gameObject);
    }

    void UpdatePointer(bool hasTarget, RaycastHit hit)
    {
        if (_pointer == null)
            return;

        if (!hasTarget)
        {
            _pointer.SetActive(false);
            return;
        }

        _pointer.transform.position = hit.point;
        _pointer.transform.localScale = Vector3.one * _settings.pointerWorldScale;
        _pointer.SetActive(true);
    }

    bool TryGetTarget(Ray ray, out Pipe pipe, out Machine machine, out RaycastHit hit)
    {
        pipe = null;
        machine = null;
        hit = default;

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            _settings.maxRayDistance,
            _settings.layerMask,
            QueryTriggerInteraction.Collide);

        if (hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit candidate = hits[i];

            pipe = candidate.collider.GetComponentInParent<Pipe>();
            if (pipe != null)
            {
                hit = candidate;
                return true;
            }

            machine = candidate.collider.GetComponentInParent<Machine>();
            if (machine != null)
            {
                hit = candidate;
                return true;
            }
        }

        return false;
    }
}
