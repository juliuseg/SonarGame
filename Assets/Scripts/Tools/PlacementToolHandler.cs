using UnityEngine;
using UnityEngine.InputSystem;

public class PlacementToolHandler
{
    readonly PlacementToolSettings _settings;
    readonly Camera _camera;
    readonly ChunkManager _chunkManager;
    readonly AutomationLogicSystem _automationLogic;

    GameObject _pointer;
    CrystalController _snappedCrystal;

    public PlacementToolHandler(PlacementToolSettings settings, Camera camera, ChunkManager chunkManager, AutomationLogicSystem automationLogic)
    {
        _settings = settings;
        _camera = camera;
        _chunkManager = chunkManager;
        _automationLogic = automationLogic;
    }

    public void Enable()
    {
        if (_settings.pointerPrefab == null)
            return;

        _pointer = Object.Instantiate(_settings.pointerPrefab);
        _pointer.transform.localScale = Vector3.one * _settings.pointerWorldScale;
        ApplyPlacementMaterial(_pointer, _settings.placementErrorMaterial);
    }

    public void Disable()
    {
        _snappedCrystal = null;

        if (_pointer != null)
        {
            Object.Destroy(_pointer);
            _pointer = null;
        }
    }

    public void Tick()
    {
        if (_pointer == null || _camera == null || _settings == null)
            return;

        Ray centerRay = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(centerRay, out RaycastHit hit, _settings.maxRayDistance))
        {
            _snappedCrystal = null;
            _pointer.SetActive(false);
            return;
        }

        _snappedCrystal = FindNearestCrystal(hit.point, _settings.snapDistance);

        if (_snappedCrystal != null)
        {
            SnapToCrystal(_snappedCrystal);
            ApplyPlacementMaterial(_pointer, _settings.placementPossibleMaterial);
        }
        else
        {
            _pointer.transform.position = hit.point;
            _pointer.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            ApplyPlacementMaterial(_pointer, _settings.placementErrorMaterial);
        }

        _pointer.SetActive(true);

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && _snappedCrystal != null)
        {
            _snappedCrystal.OnPlacementSucceeded();

            Machine machine = _snappedCrystal.GetComponentInChildren<Machine>(true);
            if (machine != null)
                _automationLogic?.CreateNode(machine);
        }
    }

    void SnapToCrystal(CrystalController crystal)
    {
        Transform crystalTransform = crystal.transform;
        Vector3 normal = Vector3.up;

        if (crystal.TryGetComponent(out SpawnNodeController spawnNode))
            normal = spawnNode.SpawnPointNormal;

        _pointer.transform.position = crystalTransform.position;
        _pointer.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
    }

    CrystalController FindNearestCrystal(Vector3 placementPoint, float maxDistance)
    {
        if (_chunkManager == null)
            return null;

        CrystalController nearest = null;
        float nearestSqrDist = maxDistance * maxDistance;

        foreach (var chunk in _chunkManager.chunks.Values)
        {
            if (chunk.gameObject == null)
                continue;

            var crystals = chunk.gameObject.GetComponentsInChildren<CrystalController>(true);
            for (int i = 0; i < crystals.Length; i++)
            {
                if (crystals[i].occupied)
                    continue;

                float sqrDist = (crystals[i].transform.position - placementPoint).sqrMagnitude;
                if (sqrDist <= nearestSqrDist)
                {
                    nearestSqrDist = sqrDist;
                    nearest = crystals[i];
                }
            }
        }

        return nearest;
    }

    void ApplyPlacementMaterial(GameObject root, Material material)
    {
        if (material == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            var materials = renderer.sharedMaterials;
            for (int j = 0; j < materials.Length; j++)
                materials[j] = material;
            renderer.sharedMaterials = materials;
        }
    }
}
