using UnityEngine;
using UnityEngine.InputSystem;

public class TerraformToolHandler
{
    readonly ITerrainEditor _terrainEditor;
    readonly TerraformToolSettings _settings;
    readonly Camera _camera;

    GameObject _pointer;

    public TerraformToolHandler(ITerrainEditor terrainEditor, TerraformToolSettings settings, Camera camera)
    {
        _terrainEditor = terrainEditor;
        _settings = settings;
        _camera = camera;
    }

    public void Enable()
    {
        if (_settings.pointerPrefab == null)
            return;

        _pointer = Object.Instantiate(_settings.pointerPrefab);
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
        if (_pointer == null || _camera == null || _settings == null)
            return;

        Ray centerRay = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(centerRay, out RaycastHit hit, _settings.maxRayDistance))
        {
            _pointer.transform.position = hit.point;

            float distance = hit.distance;
            float worldScale = 2f * distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * _settings.pointerScreenSize;
            _pointer.transform.localScale = Vector3.one * worldScale;
            _pointer.SetActive(true);

            var mouse = Mouse.current;
            if (mouse == null)
                return;

            if (mouse.leftButton.isPressed)
                ApplyTerraform(hit, -1f);
            else if (mouse.rightButton.isPressed)
                ApplyTerraform(hit, 1f);
        }
        else
        {
            _pointer.SetActive(false);
        }
    }

    void ApplyTerraform(RaycastHit hit, float multiplier)
    {
        _terrainEditor.ApplyTerraformEdit(new TerraformEdit
        {
            position = hit.point,
            strength = _settings.terraformStrength * multiplier,
            radius = _settings.terraformRadius
        });
    }
}
