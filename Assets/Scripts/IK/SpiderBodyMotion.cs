using UnityEngine;

/// <summary>
/// Visual body lean, glide, and damped wobble on a child transform while the root handles locomotion/IK.
/// </summary>
[DefaultExecutionOrder(200)]
public class SpiderBodyMotion : MonoBehaviour
{
    [Tooltip("Visual body under the locomotion root. Only this transform is moved/rotated.")]
    public Transform body;

    [Tooltip("Defines model forward/right for lean and glide. Defaults to Body if unset.")]
    public Transform modelFrame;

    [Tooltip("Transform used to measure velocity. Defaults to this component's transform.")]
    public Transform motionReference;

    [Header("Lean")]
    [Tooltip("Max lean angle in degrees from movement.")]
    public float maxLeanAngle = 10f;

    [Tooltip("How strongly horizontal speed maps into lean.")]
    public float leanFromSpeed = 4f;

    [Header("Glide")]
    [Tooltip("Local offset per m/s of horizontal speed. Creates forward carry when stopping.")]
    public float glideFromSpeed = 0.08f;

    [Tooltip("Max local glide offset in meters.")]
    public float maxGlideOffset = 0.35f;

    [Header("Springs")]
    public float positionSpring = 42f;
    public float positionDamping = 7f;
    public float rotationSpring = 55f;
    public float rotationDamping = 6f;

    Vector3 _bindLocalPosition;
    Quaternion _bindLocalRotation;
    Vector3 _previousReferencePosition;
    bool _hasPreviousReferencePosition;

    Vector3 _localOffset;
    Vector3 _localOffsetVelocity;
    Vector3 _leanAngles;
    Vector3 _leanAngleVelocity;

    void Awake()
    {
        if (motionReference == null)
            motionReference = transform;

        if (modelFrame == null)
            modelFrame = body;

        CacheBindPose();
    }

    void OnValidate()
    {
        if (motionReference == null)
            motionReference = transform;

        if (modelFrame == null)
            modelFrame = body;

        if (body != null && Application.isPlaying)
            CacheBindPose();
    }

    void CacheBindPose()
    {
        if (body == null)
            return;

        _bindLocalPosition = body.localPosition;
        _bindLocalRotation = body.localRotation;
    }

    void LateUpdate()
    {
        if (!Application.isPlaying || body == null || motionReference == null)
            return;

        float dt = Time.deltaTime;
        if (dt < 1e-6f)
            return;

        Vector3 planarVelocity = GetPlanarVelocity(motionReference, dt);
        if (!TryGetModelAxes(out Vector3 modelForward, out Vector3 modelRight))
        {
            Vector3 rootLocalVelocity = transform.InverseTransformDirection(planarVelocity);
            ApplyMotion(rootLocalVelocity, rootLocalVelocity, dt);
            return;
        }

        Vector3 modelVelocity = new Vector3(
            Vector3.Dot(planarVelocity, modelRight),
            0f,
            Vector3.Dot(planarVelocity, modelForward));

        Vector3 glideOffset = Vector3.ClampMagnitude(
            (modelRight * modelVelocity.x + modelForward * modelVelocity.z) * glideFromSpeed,
            maxGlideOffset);
        Vector3 rootLocalGlide = transform.InverseTransformDirection(glideOffset);

        ApplyMotion(modelVelocity, rootLocalGlide, dt);
    }

    void ApplyMotion(Vector3 modelVelocity, Vector3 rootLocalGlideOffset, float dt)
    {
        Vector3 targetLean = ComputeTargetLean(modelVelocity);

        SpringVector(ref _localOffset, ref _localOffsetVelocity, rootLocalGlideOffset, positionSpring, positionDamping, dt);
        SpringVector(ref _leanAngles, ref _leanAngleVelocity, targetLean, rotationSpring, rotationDamping, dt);

        body.localPosition = _bindLocalPosition + _localOffset;
        body.localRotation = _bindLocalRotation * Quaternion.Euler(_leanAngles);
    }

    bool TryGetModelAxes(out Vector3 modelForward, out Vector3 modelRight)
    {
        modelForward = Vector3.zero;
        modelRight = Vector3.zero;

        Transform frame = modelFrame != null ? modelFrame : body;
        if (frame == null)
            return false;

        Vector3 up = transform.up;
        modelForward = Vector3.ProjectOnPlane(frame.forward, up);
        modelRight = Vector3.ProjectOnPlane(frame.right, up);

        if (modelForward.sqrMagnitude < 1e-6f || modelRight.sqrMagnitude < 1e-6f)
            return false;

        modelForward.Normalize();
        modelRight.Normalize();
        return true;
    }

    Vector3 GetPlanarVelocity(Transform reference, float dt)
    {
        Vector3 position = reference.position;
        if (!_hasPreviousReferencePosition)
        {
            _previousReferencePosition = position;
            _hasPreviousReferencePosition = true;
            return Vector3.zero;
        }

        Vector3 delta = position - _previousReferencePosition;
        _previousReferencePosition = position;
        return Vector3.ProjectOnPlane(delta, transform.up) / dt;
    }

    Vector3 ComputeTargetLean(Vector3 modelVelocity)
    {
        float speed = new Vector2(modelVelocity.x, modelVelocity.z).magnitude;
        if (speed < 1e-4f)
            return Vector3.zero;

        float pitch = Mathf.Clamp(modelVelocity.z * leanFromSpeed, -maxLeanAngle, maxLeanAngle);
        float roll = Mathf.Clamp(-modelVelocity.x * leanFromSpeed, -maxLeanAngle, maxLeanAngle);

        float yaw = 0f;
        if (speed > 1e-4f)
        {
            float bearing = Mathf.Atan2(modelVelocity.x, modelVelocity.z) * Mathf.Rad2Deg;
            yaw = Mathf.Clamp(bearing * 0.35f, -maxLeanAngle, maxLeanAngle);
        }

        return new Vector3(pitch, yaw, roll);
    }

    static void SpringVector(
        ref Vector3 value,
        ref Vector3 velocity,
        Vector3 target,
        float spring,
        float damping,
        float dt)
    {
        Vector3 acceleration = spring * (target - value) - damping * velocity;
        velocity += acceleration * dt;
        value += velocity * dt;
    }
}
