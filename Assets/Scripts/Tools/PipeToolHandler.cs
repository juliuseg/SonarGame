using UnityEngine;
using UnityEngine.InputSystem;

public class PipeToolHandler
{
    readonly PipeToolSettings _settings;
    readonly Camera _camera;
    readonly Transform _pipeParent;

    PipeNodeController _startNode;
    GameObject _cursor;
    GameObject _previewPipe;
    Mesh _previewMesh;
    MeshFilter _previewMeshFilter;
    MeshRenderer _previewMeshRenderer;

    public PipeToolHandler(PipeToolSettings settings, Camera camera, Transform pipeParent)
    {
        _settings = settings;
        _camera = camera;
        _pipeParent = pipeParent;
    }

    public void Enable()
    {
        ClearStartNode();

        if (_settings.pointerPrefab != null)
        {
            _cursor = Object.Instantiate(_settings.pointerPrefab);
            _cursor.transform.localScale = Vector3.one * _settings.pointerWorldScale;
            _cursor.SetActive(false);
        }
    }

    public void Disable()
    {
        ClearStartNode();
        DestroyPreviewPipe();
        DestroyCursor();
    }

    public void UpdatePlacedPipes()
    {
        if (_pipeParent == null || _settings == null)
            return;

        var placedPipes = _pipeParent.GetComponentsInChildren<PlacedPipe>(true);
        for (int i = placedPipes.Length - 1; i >= 0; i--)
        {
            var placedPipe = placedPipes[i];
            if (placedPipe.startNode == null || placedPipe.endNode == null)
            {
                DestroyPlacedPipe(placedPipe);
                continue;
            }

            if (!_settings.debugRegenerateAllPipes && !placedPipe.HaveEndpointsMoved())
                continue;

            if (!placedPipe.TryGetComponent(out MeshFilter meshFilter) || meshFilter.mesh == null)
                continue;

            if (!placedPipe.TryGetComponent(out MeshRenderer meshRenderer))
                continue;

            GetPipeEndpoints(
                placedPipe.startNode,
                placedPipe.endNode,
                out Vector3 startPosition,
                out Vector3 startTangent,
                out Vector3 endPosition,
                out Vector3 endTangent,
                out Vector3 startRingUp,
                out Vector3 endRingUp);

            float curveLength = BuildPipeMesh(
                placedPipe.transform,
                meshFilter.mesh,
                meshRenderer,
                startPosition,
                startTangent,
                endPosition,
                endTangent,
                startRingUp,
                endRingUp,
                _settings.material_placed);

            placedPipe.CacheEndpointPositions();

            if (curveLength >= _settings.maxBuildableLenght)
                DestroyPlacedPipe(placedPipe);
        }
    }

    public void Tick()
    {
        if (_camera == null || _settings == null)
            return;

        TryGetPipeNodeFromRay(out PipeNodeController hoveredNode, _startNode);
        UpdateCursor(hoveredNode);
        UpdatePreviewPipe(hoveredNode);

        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        if (hoveredNode == null)
        {
            ClearStartNode();
            return;
        }

        if (_startNode == null)
        {
            _startNode = hoveredNode;
            return;
        }

        if (_startNode == hoveredNode)
        {
            ClearStartNode();
            return;
        }

        TryPlacePipe(_startNode, hoveredNode);
        ClearStartNode();
    }

    void UpdateCursor(PipeNodeController hoveredNode)
    {
        if (_cursor == null)
            return;

        if (hoveredNode == null)
        {
            _cursor.SetActive(false);
            return;
        }

        _cursor.transform.position = hoveredNode.transform.position;

        Vector3 tangent = hoveredNode.GetWorldTangent();
        _cursor.transform.rotation = tangent.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(tangent.normalized)
            : Quaternion.identity;

        _cursor.SetActive(true);
    }

    void UpdatePreviewPipe(PipeNodeController hoveredNode)
    {
        if (_startNode == null)
        {
            if (_previewPipe != null)
                _previewPipe.SetActive(false);
            return;
        }

        Vector3 startPosition = _startNode.transform.position;
        Vector3 startTangent = _startNode.GetWorldTangent();

        Vector3 endPosition;
        Vector3 endTangent;

        if (hoveredNode != null)
        {
            endPosition = hoveredNode.transform.position;
            endTangent = hoveredNode.GetWorldTangent();
        }
        else
        {
            Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            endPosition = ray.origin + ray.direction * _settings.previewEndDistance;
            endTangent = startPosition - endPosition;
        }

        Vector3 startRingUp = _startNode.transform.up;
        Vector3 endRingUp = hoveredNode != null ? hoveredNode.transform.up : Vector3.up;

        EnsurePreviewPipe();
        BuildPipeMesh(_previewPipe.transform, _previewMesh, _previewMeshRenderer, startPosition, startTangent, endPosition, endTangent, startRingUp, endRingUp);
        _previewPipe.SetActive(true);
    }

    bool TryGetPipeNodeFromRay(out PipeNodeController node, PipeNodeController exclude = null)
    {
        node = null;

        Ray ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        RaycastHit[] hits = Physics.RaycastAll(ray, _settings.maxRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        if (hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];

            if (!hit.collider.TryGetComponent(out PipeNodeController pipeNode))
                continue;

            if (pipeNode == exclude)
                continue;

            if (pipeNode.occupied)
                continue;

            node = pipeNode;
            return true;
        }

        return false;
    }

    static void GetPipeEndpoints(
        PipeNodeController startNode,
        PipeNodeController endNode,
        out Vector3 startPosition,
        out Vector3 startTangent,
        out Vector3 endPosition,
        out Vector3 endTangent,
        out Vector3 startRingUp,
        out Vector3 endRingUp)
    {
        startPosition = startNode.transform.position;
        endPosition = endNode.transform.position;
        startTangent = startNode.GetWorldTangent();
        endTangent = endNode.GetWorldTangent();
        startRingUp = startNode.transform.up;
        endRingUp = endNode.transform.up;
    }

    void TryPlacePipe(PipeNodeController startNode, PipeNodeController endNode)
    {
        GetPipeEndpoints(
            startNode,
            endNode,
            out Vector3 startPosition,
            out Vector3 startTangent,
            out Vector3 endPosition,
            out Vector3 endTangent,
            out Vector3 startRingUp,
            out Vector3 endRingUp);

        float curveLength = BezierPipeMeshBuilder.EstimateLength(
            _settings,
            startPosition,
            startTangent,
            endPosition,
            endTangent);

        if (curveLength >= _settings.maxBuildableLenght)
            return;

        var pipeObject = new GameObject($"Pipe_{startNode.name}_to_{endNode.name}");
        pipeObject.transform.SetParent(_pipeParent, false);

        var meshFilter = pipeObject.AddComponent<MeshFilter>();
        var meshRenderer = pipeObject.AddComponent<MeshRenderer>();
        var mesh = new Mesh();
        meshFilter.mesh = mesh;

        BuildPipeMesh(
            pipeObject.transform,
            mesh,
            meshRenderer,
            startPosition,
            startTangent,
            endPosition,
            endTangent,
            startRingUp,
            endRingUp,
            _settings.material_placed);

        var placedPipe = pipeObject.AddComponent<PlacedPipe>();
        placedPipe.startNode = startNode;
        placedPipe.endNode = endNode;
        placedPipe.CacheEndpointPositions();

        startNode.occupied = true;
        endNode.occupied = true;
    }

    void DestroyPlacedPipe(PlacedPipe placedPipe)
    {
        if (placedPipe.startNode != null)
            placedPipe.startNode.occupied = false;
        if (placedPipe.endNode != null)
            placedPipe.endNode.occupied = false;

        Object.Destroy(placedPipe.gameObject);
    }

    float BuildPipeMesh(
        Transform pipeTransform,
        Mesh mesh,
        MeshRenderer meshRenderer,
        Vector3 startPosition,
        Vector3 startTangent,
        Vector3 endPosition,
        Vector3 endTangent,
        Vector3 startRingUp,
        Vector3 endRingUp,
        Material materialOverride = null)
    {
        return BezierPipeMeshBuilder.Build(
            pipeTransform,
            mesh,
            meshRenderer,
            _settings,
            startPosition,
            startTangent,
            endPosition,
            endTangent,
            startRingUp,
            endRingUp,
            materialOverride);
    }

    void EnsurePreviewPipe()
    {
        if (_previewPipe != null)
            return;

        _previewPipe = new GameObject("PipePreview");
        _previewPipe.transform.SetParent(_pipeParent, false);
        _previewMeshFilter = _previewPipe.AddComponent<MeshFilter>();
        _previewMeshRenderer = _previewPipe.AddComponent<MeshRenderer>();
        _previewMesh = new Mesh();
        _previewMeshFilter.mesh = _previewMesh;
    }

    void DestroyPreviewPipe()
    {
        if (_previewMesh != null)
            Object.Destroy(_previewMesh);

        if (_previewPipe != null)
            Object.Destroy(_previewPipe);

        _previewPipe = null;
        _previewMesh = null;
        _previewMeshFilter = null;
        _previewMeshRenderer = null;
    }

    void DestroyCursor()
    {
        if (_cursor != null)
            Object.Destroy(_cursor);

        _cursor = null;
    }

    void ClearStartNode()
    {
        _startNode = null;

        if (_previewPipe != null)
            _previewPipe.SetActive(false);
    }
}
