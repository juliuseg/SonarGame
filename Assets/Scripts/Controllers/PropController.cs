using UnityEngine;

public class PropController : MonoBehaviour
{
    private class PropellerSet
    {
        public Transform propeller;
        public Transform propellerBox;
        public Transform propellerHolder;
    }

    [Header("References")]
    [SerializeField] private SubController subController;

    [Header("Propeller Spin")]
    [Tooltip("Degrees per second per m/s of forward speed")]
    [SerializeField] private float propellerSpinMultiplier = 360f;
    [Tooltip("Extra degrees per second per rad/s of yaw/pitch angular velocity")]
    [SerializeField] private float propellerAngularVelocityMultiplier = 30f;

    [Header("Propeller Box (Yaw / Turn)")]
    [Tooltip("Local rotation offset in degrees per rad/s of yaw")]
    [SerializeField] private float boxYawRotationMultiplier = 30f;

    [Header("Propeller Holder (Pitch / Up-Down)")]
    [Tooltip("Local rotation offset in degrees per rad/s of pitch")]
    [SerializeField] private float holderPitchRotationMultiplier = 30f;

    private PropellerSet left = new PropellerSet();
    private PropellerSet right = new PropellerSet();

    private Transform searchRoot;
    
    private Quaternion _leftPropBase;
    private Quaternion _leftBoxBase;
    private Quaternion _leftHolderBase;
    private Quaternion _rightPropBase;
    private Quaternion _rightBoxBase;
    private Quaternion _rightHolderBase;
    private float _leftPropSpin;
    private float _rightPropSpin;
    private float _lastYaw;
    private float _lastPitch;
    private bool _initialized;

    void Awake()
    {
        if (subController == null)
            subController = GetComponent<SubController>();

        searchRoot = transform;

        FindPropellerSets();
        CacheBaseRotations();
    }

    void FindPropellerSets()
    {
        left = new PropellerSet();
        right = new PropellerSet();

        foreach (Transform child in searchRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child == searchRoot) continue;

            string lowerName = child.name.ToLowerInvariant();
            bool? isRight = GetSide(child, searchRoot);
            if (isRight == null) continue;

            PropellerSet set = isRight.Value ? right : left;

            if (lowerName.Contains("propholder"))
                set.propellerHolder = child;
            else if (lowerName.Contains("propbox"))
                set.propellerBox = child;
            else if (IsPropellerName(lowerName))
                set.propeller = child;
        }

        LogMissingParts("Left", left);
        LogMissingParts("Right", right);
    }

    static bool? GetSide(Transform t, Transform searchRoot)
    {
        for (Transform current = t; current != null && current != searchRoot; current = current.parent)
        {
            string lowerName = current.name.ToLowerInvariant();
            if (lowerName.Contains("right")) return true;
            if (lowerName.Contains("left")) return false;
        }

        return null;
    }

    static bool IsPropellerName(string lowerName)
    {
        if (lowerName.Contains("propbox") || lowerName.Contains("propholder"))
            return false;

        return lowerName.Contains("prop");
    }

    void LogMissingParts(string side, PropellerSet set)
    {
        if (set.propeller == null)
            Debug.LogWarning($"PropController: Could not find propeller for {side}. Expected a child name containing 'Prop' and '{side}'.", this);
        if (set.propellerBox == null)
            Debug.LogWarning($"PropController: Could not find prop box for {side}. Expected a child name containing 'PropBox' and '{side}'.", this);
        if (set.propellerHolder == null)
            Debug.LogWarning($"PropController: Could not find prop holder for {side}. Expected a child name containing 'PropHolder' and '{side}'.", this);
    }

    void CacheBaseRotations()
    {
        if (left.propeller != null) _leftPropBase = left.propeller.localRotation;
        if (left.propellerBox != null) _leftBoxBase = left.propellerBox.localRotation;
        if (left.propellerHolder != null) _leftHolderBase = left.propellerHolder.localRotation;
        if (right.propeller != null) _rightPropBase = right.propeller.localRotation;
        if (right.propellerBox != null) _rightBoxBase = right.propellerBox.localRotation;
        if (right.propellerHolder != null) _rightHolderBase = right.propellerHolder.localRotation;
    }

    void FixedUpdate()
    {
        if (subController == null) return;

        Rigidbody rb = subController.Rigidbody;
        if (rb == null) return;

        Transform subTransform = subController.transform;
        float yaw = subTransform.eulerAngles.y;
        float pitch = subTransform.eulerAngles.x;
        if (pitch > 180f) pitch -= 360f;

        if (!_initialized)
        {
            _lastYaw = yaw;
            _lastPitch = pitch;
            _initialized = true;
            return;
        }

        float dt = Time.fixedDeltaTime;
        float yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt * Mathf.Deg2Rad;
        float pitchRate = Mathf.DeltaAngle(_lastPitch, pitch) / dt * Mathf.Deg2Rad;
        _lastYaw = yaw;
        _lastPitch = pitch;

        float forwardSpeed = Vector3.Dot(rb.linearVelocity, subTransform.forward);

        ApplyPropellerSet(left, _leftPropBase, _leftBoxBase, _leftHolderBase, ref _leftPropSpin, forwardSpeed, yawRate, pitchRate, dt);
        ApplyPropellerSet(right, _rightPropBase, _rightBoxBase, _rightHolderBase, ref _rightPropSpin, forwardSpeed, yawRate, pitchRate, dt);
    }

    void ApplyPropellerSet(
        PropellerSet set,
        Quaternion propBase,
        Quaternion boxBase,
        Quaternion holderBase,
        ref float propSpin,
        float forwardSpeed,
        float yawRate,
        float pitchRate,
        float dt)
    {
        if (set.propeller != null)
        {
            float angularSpin = propellerAngularVelocityMultiplier * (yawRate + pitchRate) * Mathf.Rad2Deg;
            propSpin += (propellerSpinMultiplier * forwardSpeed + angularSpin) * dt;
            set.propeller.localRotation = propBase * Quaternion.AngleAxis(propSpin, Vector3.up);
        }

        if (set.propellerBox != null)
        {
            float boxAngle = -boxYawRotationMultiplier * yawRate * Mathf.Rad2Deg;
            set.propellerBox.localRotation = boxBase * Quaternion.AngleAxis(boxAngle, Vector3.forward);
        }

        if (set.propellerHolder != null)
        {
            float holderAngle = -holderPitchRotationMultiplier * pitchRate * Mathf.Rad2Deg;
            set.propellerHolder.localRotation = holderBase * Quaternion.AngleAxis(holderAngle, Vector3.right);
        }
    }
}
