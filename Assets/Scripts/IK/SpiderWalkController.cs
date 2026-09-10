using UnityEngine;

/// <summary>
/// Ground-conforming spider controller. Raycasts from leg spawn points to place IK targets,
/// and keeps the body at a constant height above ground.
/// </summary>
[DefaultExecutionOrder(100)]
public class SpiderWalkController : MonoBehaviour
{
    [System.Serializable]
    public struct LegSlot
    {
        [Tooltip("Empty transform where the leg raycast starts.")]
        public Transform raycastOrigin;

        [Tooltip("IK foot target this slot drives.")]
        public Transform ikTarget;
    }

    [Header("Body")]
    [Tooltip("Desired distance from the body origin to ground along local down.")]
    public float bodyHeight = 1f;

    [Tooltip("How quickly the body height corrects toward bodyHeight.")]
    public float bodyHeightLerpSpeed = 8f;

    [Tooltip("How quickly the body up axis aligns to the ground normal under the body.")]
    public float groundNormalLerpSpeed = 6f;

    [Header("Legs")]
    public LegSlot[] legs = new LegSlot[4];

    [Tooltip("Move a leg target only when ground is farther than this from its current target.")]
    public float legStepDistance = 0.35f;

    [Tooltip("Peak lift of the step arc, along the body's local up.")]
    public float legStepArcHeight = 0.25f;

    [Tooltip("Step progress per second (1 = one second from start to landing).")]
    public float legStepSpeed = 2f;

    [Tooltip("Shift leg raycast origins along horizontal movement direction by this amount.")]
    public float raycastOriginVelocityOffset = 0.5f;

    [Tooltip("Minimum time between leg step starts. Keeps legs out of sync via round-robin.")]
    public float legStepStagger = 0.1f;

    [Header("Stand still stabilization")]
    [Tooltip("If planar movement stays below the tolerance for this long, legs re-step to targets.")]
    public float standStillTime = 2f;

    [Tooltip("Max planar movement allowed over standStillTime before counting as moving.")]
    public float standStillMoveTolerance = 0.08f;

    [Tooltip("Max rotation change in degrees over standStillTime before counting as moving.")]
    public float standStillRotateTolerance = 3f;

    [Tooltip("Skip stabilization steps when a foot is already this close to its target.")]
    public float standStillStepMinDistance = 0.01f;

    [Header("Raycast")]
    [Tooltip("Body reference used to aim leg raycasts toward a point below it.")]
    public Transform legRaycastBodyReference;

    [Tooltip("Leg rays aim toward this many units below the body reference along local down.")]
    public float legRaycastAimBelowBody = 10f;

    public float maxRaycastDistance = 10f;
    public LayerMask raycastLayers = ~0;
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    struct LegStepState
    {
        public bool stepping;
        public Vector3 from;
        public Vector3 to;
        public float t;
    }

    float _deltaTime;
    bool[] _legMissWarned;
    LegStepState[] _legSteps;
    Vector3[] _legWorldPositions;
    bool _legWorldPositionsInitialized;
    Vector3 _previousPosition;
    bool _hasPreviousPosition;
    float _globalNextStepTime;
    int _roundRobinIndex;
    bool _stepSchedulerInitialized;
    Vector3 _debugGroundNormal;
    Vector3 _debugGroundHitPoint;
    bool _hasDebugGroundHit;
    Vector3 _standStillAnchorPosition;
    Quaternion _standStillAnchorRotation;
    float _standStillAnchorTime;
    bool _standStillAnchorInitialized;
    bool _isStandingStill;

    void Start()
    {
        InitializeLegWorldPositions();
        ResetStandStillAnchor();
    }

    void ResetStandStillAnchor()
    {
        _standStillAnchorPosition = transform.position;
        _standStillAnchorRotation = transform.rotation;
        _standStillAnchorTime = Time.time;
        _standStillAnchorInitialized = true;
        _isStandingStill = false;
    }

    void UpdateStandStillState()
    {
        if (!_standStillAnchorInitialized)
        {
            ResetStandStillAnchor();
            return;
        }

        Vector3 delta = transform.position - _standStillAnchorPosition;
        float planarMove = Vector3.ProjectOnPlane(delta, transform.up).magnitude;
        float rotateDelta = Quaternion.Angle(_standStillAnchorRotation, transform.rotation);

        if (planarMove > standStillMoveTolerance || rotateDelta > standStillRotateTolerance)
        {
            ResetStandStillAnchor();
            return;
        }

        _isStandingStill = Time.time - _standStillAnchorTime >= standStillTime;
    }

    void Update()
    {
        if (!Application.isPlaying)
            return;

        _deltaTime = Time.deltaTime;
        Vector3 planarDelta = GetPlanarDelta();
        Vector3 horizontalMoveDir = planarDelta.sqrMagnitude > 1e-8f ? planarDelta.normalized : Vector3.zero;
        UpdateStandStillState();
        MaintainBodyHeight();
        UpdateLegTargets(horizontalMoveDir);
        _previousPosition = transform.position;
        _hasPreviousPosition = true;
    }

    Vector3 GetPlanarDelta()
    {
        if (!_hasPreviousPosition)
            return Vector3.zero;

        Vector3 delta = transform.position - _previousPosition;
        return Vector3.ProjectOnPlane(delta, transform.up);
    }

    Vector3 GetHorizontalMovementDirection()
    {
        Vector3 planar = GetPlanarDelta();
        return planar.sqrMagnitude > 1e-8f ? planar.normalized : Vector3.zero;
    }

    void EnsureStepScheduler()
    {
        if (_stepSchedulerInitialized || !Application.isPlaying)
            return;

        _globalNextStepTime = Time.time;
        _roundRobinIndex = 0;
        _stepSchedulerInitialized = true;
    }

    void TryStartStaggeredSteps(Vector3 horizontalMoveDir)
    {
        if (Time.time < _globalNextStepTime)
            return;

        for (int n = 0; n < legs.Length; n++)
        {
            int i = (_roundRobinIndex + n) % legs.Length;

            if (!TryStartLegStep(i, horizontalMoveDir))
                continue;

            _roundRobinIndex = (i + 1) % legs.Length;
            _globalNextStepTime = Time.time + legStepStagger;
            return;
        }
    }

    void MaintainBodyHeight()
    {
        Vector3 down = -transform.up;
        Vector3 origin = transform.position;

        if (!Physics.Raycast(origin, down, out RaycastHit hit, maxRaycastDistance, raycastLayers, triggerInteraction))
        {
            _hasDebugGroundHit = false;
            return;
        }

        _hasDebugGroundHit = true;
        _debugGroundHitPoint = hit.point;
        _debugGroundNormal = hit.normal;

        AlignToGroundNormal(hit);

        down = -transform.up;
        if (!Physics.Raycast(transform.position, down, out hit, maxRaycastDistance, raycastLayers, triggerInteraction))
            return;

        float correction = hit.distance - bodyHeight;
        if (Mathf.Abs(correction) < 1e-4f)
            return;

        float t = 1f - Mathf.Exp(-bodyHeightLerpSpeed * _deltaTime);
        transform.position -= transform.up * (correction * t);
    }

    void AlignToGroundNormal(RaycastHit hit)
    {
        Vector3 targetUp = hit.normal;
        if (targetUp.sqrMagnitude < 1e-8f)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, targetUp);
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.ProjectOnPlane(transform.right, targetUp);
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.Cross(targetUp, Vector3.right);
        if (forward.sqrMagnitude < 1e-6f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(forward.normalized, targetUp);
        float t = 1f - Mathf.Exp(-groundNormalLerpSpeed * _deltaTime);
        Quaternion newRotation = Quaternion.Slerp(transform.rotation, targetRotation, t);

        if (Quaternion.Angle(transform.rotation, newRotation) < 0.01f)
            return;

        Quaternion deltaRotation = newRotation * Quaternion.Inverse(transform.rotation);
        transform.position = hit.point + deltaRotation * (transform.position - hit.point);
        transform.rotation = newRotation;
    }

    void EnsureLegArrays()
    {
        int count = legs != null ? legs.Length : 0;
        if (_legMissWarned == null || _legMissWarned.Length != count)
            _legMissWarned = new bool[count];
        if (_legSteps == null || _legSteps.Length != count)
            _legSteps = new LegStepState[count];
        if (_legWorldPositions == null || _legWorldPositions.Length != count)
        {
            _legWorldPositions = new Vector3[count];
            _legWorldPositionsInitialized = false;
        }
    }

    void InitializeLegWorldPositions()
    {
        if (legs == null)
            return;

        EnsureLegArrays();

        for (int i = 0; i < legs.Length; i++)
        {
            if (legs[i].ikTarget == null)
                continue;

            _legWorldPositions[i] = legs[i].ikTarget.position;
        }

        _legWorldPositionsInitialized = true;
    }

    Vector3 GetLegTargetWorldPosition(int i) => _legWorldPositions[i];

    void SetLegTargetWorldPosition(int i, Vector3 worldPosition)
    {
        _legWorldPositions[i] = worldPosition;
        legs[i].ikTarget.position = worldPosition;
    }

    void MaintainPlantedLegTargets()
    {
        for (int i = 0; i < legs.Length; i++)
        {
            if (legs[i].ikTarget == null || _legSteps[i].stepping)
                continue;

            SetLegTargetWorldPosition(i, _legWorldPositions[i]);
        }
    }

    Vector3 GetLegRaycastAimPoint()
    {
        Vector3 referencePosition = legRaycastBodyReference != null
            ? legRaycastBodyReference.position
            : transform.position;

        return referencePosition - transform.up * legRaycastAimBelowBody;
    }

    bool TryLegGroundRaycast(Vector3 origin, out RaycastHit hit)
    {
        Vector3 aimPoint = GetLegRaycastAimPoint();
        Vector3 toAim = aimPoint - origin;

        if (toAim.sqrMagnitude < 1e-8f)
            return Physics.Raycast(origin, -transform.up, out hit, maxRaycastDistance, raycastLayers, triggerInteraction);

        Vector3 direction = toAim.normalized;
        float rayLength = toAim.magnitude + maxRaycastDistance;
        return Physics.Raycast(origin, direction, out hit, rayLength, raycastLayers, triggerInteraction);
    }

    bool TryGetLegRaycastDirection(Vector3 origin, out Vector3 direction, out float rayLength)
    {
        Vector3 aimPoint = GetLegRaycastAimPoint();
        Vector3 toAim = aimPoint - origin;

        if (toAim.sqrMagnitude < 1e-8f)
        {
            direction = -transform.up;
            rayLength = maxRaycastDistance;
            return false;
        }

        direction = toAim.normalized;
        rayLength = toAim.magnitude + maxRaycastDistance;
        return true;
    }

    Vector3 EvaluateStepPosition(Vector3 from, Vector3 to, float t)
    {
        Vector3 linear = Vector3.Lerp(from, to, t);
        float lift = 4f * legStepArcHeight * t * (1f - t);
        return linear + transform.up * lift;
    }

    bool TryStartLegStep(int i, Vector3 horizontalMoveDir)
    {
        ref LegSlot leg = ref legs[i];
        if (leg.raycastOrigin == null || leg.ikTarget == null)
            return false;

        ref LegStepState step = ref _legSteps[i];
        if (step.stepping)
            return false;

        Vector3 origin = leg.raycastOrigin.position + horizontalMoveDir * raycastOriginVelocityOffset;

        if (!TryLegGroundRaycast(origin, out RaycastHit hit))
        {
            if (!_legMissWarned[i])
            {
                Debug.LogWarning(
                    $"SpiderWalkController: leg {i} raycast from '{leg.raycastOrigin.name}' missed ground.",
                    leg.raycastOrigin);
                _legMissWarned[i] = true;
            }

            return false;
        }

        _legMissWarned[i] = false;

        Vector3 groundPoint = hit.point;
        Vector3 currentWorld = GetLegTargetWorldPosition(i);
        float distanceToTarget = Vector3.Distance(currentWorld, groundPoint);

        bool normalStep = distanceToTarget > legStepDistance;
        bool stabilizeStep = _isStandingStill && distanceToTarget > standStillStepMinDistance;
        if (!normalStep && !stabilizeStep)
            return false;

        step.stepping = true;
        step.from = currentWorld;
        step.to = groundPoint;
        step.t = 0f;
        SetLegTargetWorldPosition(i, currentWorld);
        return true;
    }

    void UpdateLegTargets(Vector3 horizontalMoveDir)
    {
        if (legs == null || legs.Length == 0)
            return;

        EnsureLegArrays();
        if (!_legWorldPositionsInitialized)
            InitializeLegWorldPositions();

        MaintainPlantedLegTargets();

        for (int i = 0; i < legs.Length; i++)
        {
            ref LegSlot leg = ref legs[i];
            if (leg.raycastOrigin == null || leg.ikTarget == null)
                continue;

            ref LegStepState step = ref _legSteps[i];
            if (!step.stepping)
                continue;

            step.t += legStepSpeed * _deltaTime;
            if (step.t >= 1f)
            {
                step.t = 1f;
                SetLegTargetWorldPosition(i, step.to);
                step.stepping = false;
            }
            else
            {
                SetLegTargetWorldPosition(i, EvaluateStepPosition(step.from, step.to, step.t));
            }
        }

        EnsureStepScheduler();
        TryStartStaggeredSteps(horizontalMoveDir);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying)
            return;

        Vector3 horizontalMoveDir = GetHorizontalMovementDirection();
        Vector3 originLead = horizontalMoveDir * raycastOriginVelocityOffset;
        Vector3 legAimPoint = GetLegRaycastAimPoint();

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, -transform.up * maxRaycastDistance);

        Gizmos.color = new Color(0.4f, 0.8f, 1f);
        Gizmos.DrawWireSphere(legAimPoint, 0.1f);

        if (_hasDebugGroundHit)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(_debugGroundHitPoint, 0.08f);
            Gizmos.DrawRay(_debugGroundHitPoint, _debugGroundNormal * 0.75f);
        }

        if (legs == null)
            return;

        for (int i = 0; i < legs.Length; i++)
        {
            LegSlot leg = legs[i];
            if (leg.raycastOrigin == null)
                continue;

            Vector3 origin = leg.raycastOrigin.position + originLead;
            TryGetLegRaycastDirection(origin, out Vector3 direction, out float rayLength);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(leg.raycastOrigin.position, 0.04f);
            Gizmos.color = new Color(1f, 0.6f, 0f);
            Gizmos.DrawWireSphere(origin, 0.05f);
            Gizmos.DrawLine(leg.raycastOrigin.position, origin);
            Gizmos.DrawLine(origin, legAimPoint);
            Gizmos.DrawRay(origin, direction * rayLength);

            if (leg.ikTarget == null)
                continue;

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(leg.ikTarget.position, 0.06f);
            Gizmos.DrawLine(origin, leg.ikTarget.position);
        }
    }
#endif
}
