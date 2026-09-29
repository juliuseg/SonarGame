using UnityEngine;

public class MinerAnimation : MonoBehaviour
{
    enum AnimationState
    {
        Stopped,
        RampingUp,
        Running,
        RampingDown
    }

    [Header("Targets")]
    [SerializeField] private Transform rotatorA;
    [SerializeField] private Transform rotatorB;
    [SerializeField] private Transform positionTarget;
    [SerializeField] private Transform continuousRotator;

    [Header("Rotation (local X, curve 0 → 1)")]
    [SerializeField] private float rotationAtZero;
    [SerializeField] private float rotationAtOne;

    [Header("Position (local Y, curve 0 → 1)")]
    [SerializeField] private float positionAtZero;
    [SerializeField] private float positionAtOne;

    [Header("Animation")]
    [SerializeField] private AnimationCurve animationCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("Full curve cycles per second at full speed.")]
    [SerializeField] private float speed = 1f;
    [Tooltip("Seconds to ramp from current speed up to full speed.")]
    [SerializeField] private float rampUpDuration = 1f;
    [Tooltip("Full animation cycles to finish while slowing down, plus the remaining partial cycle to reach phase 0.")]
    [SerializeField] private int rampDownIterations = 1;
    [Tooltip("Slowest speed used while finishing the stop. Speed ramps down to this, then holds until phase 0.")]
    [SerializeField] private float stopMinSpeed = 0.25f;

    [Header("Continuous Rotation")]
    [Tooltip("Local Z rotation speed in degrees per second at full speed.")]
    [SerializeField] private float continuousRotatorSpeed = 90f;

    [Header("Debug")]
    [SerializeField] private bool debugToggleAnimation;

    AnimationState _state = AnimationState.Stopped;
    float _phase;
    float _currentSpeed;

    float _rampStartTime;
    float _rampStartSpeed;

    float _stopStartSpeed;
    float _stopStartPhase;
    float _stopDecelEndPhase;
    float _stopEndPhase;
    float _continuousAngle;

    public bool IsAnimating => _state != AnimationState.Stopped;

    void Start()
    {
        if (continuousRotator != null)
            _continuousAngle = continuousRotator.localEulerAngles.z;
    }

    void Update()
    {
        if (debugToggleAnimation)
        {
            debugToggleAnimation = false;
            if (_state == AnimationState.Stopped)
                ActivateAnimation();
            else
                StopAnimation();
        }

        UpdateAnimationState();

        if (animationCurve != null)
            ApplyPose(EvaluatePhase());

        ApplyContinuousRotation();
    }

    float GetSpeedFactor()
    {
        if (speed <= 0f)
            return 0f;

        return _currentSpeed / speed;
    }

    void ApplyContinuousRotation()
    {
        if (continuousRotator == null)
            return;

        _continuousAngle += continuousRotatorSpeed * GetSpeedFactor() * Time.deltaTime;
        SetLocalRotationZ(continuousRotator, _continuousAngle);
    }

    void UpdateAnimationState()
    {
        switch (_state)
        {
            case AnimationState.RampingUp:
            {
                float u = rampUpDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01((Time.time - _rampStartTime) / rampUpDuration);

                _currentSpeed = Mathf.Lerp(_rampStartSpeed, speed, u);
                _phase += _currentSpeed * Time.deltaTime;

                if (u >= 1f)
                    _state = AnimationState.Running;
                break;
            }

            case AnimationState.Running:
                _currentSpeed = speed;
                _phase += _currentSpeed * Time.deltaTime;
                break;

            case AnimationState.RampingDown:
            {
                float totalDistance = _stopEndPhase - _stopStartPhase;
                if (totalDistance <= 0.0001f)
                {
                    _phase = 0f;
                    _currentSpeed = 0f;
                    _state = AnimationState.Stopped;
                    break;
                }

                float minSpeed = Mathf.Min(_stopStartSpeed, stopMinSpeed);
                float decelDistance = _stopDecelEndPhase - _stopStartPhase;

                if (decelDistance <= 0.0001f)
                    _currentSpeed = minSpeed;
                else if (_phase < _stopDecelEndPhase)
                    _currentSpeed = Mathf.Lerp(_stopStartSpeed, minSpeed, (_phase - _stopStartPhase) / decelDistance);
                else
                    _currentSpeed = minSpeed;

                float step = _currentSpeed * Time.deltaTime;
                float remaining = _stopEndPhase - _phase;

                if (step >= remaining)
                {
                    _phase = 0f;
                    _currentSpeed = 0f;
                    _state = AnimationState.Stopped;
                }
                else
                {
                    _phase += step;
                }

                break;
            }
        }
    }

    float EvaluatePhase()
    {
        if (_state == AnimationState.Stopped)
            return 0f;

        return Mathf.Repeat(_phase, 1f);
    }

    void ApplyPose(float phase)
    {
        float t = animationCurve.Evaluate(phase);

        float rotA = -Mathf.Lerp(rotationAtZero, rotationAtOne, t);
        float rotB = Mathf.Lerp(rotationAtZero, rotationAtOne, t);
        float posY = Mathf.Lerp(positionAtZero, positionAtOne, t);

        SetLocalRotationX(rotatorA, rotA);
        SetLocalRotationX(rotatorB, rotB);
        SetLocalPositionY(positionTarget, posY);
    }

    public void ActivateAnimation()
    {
        if (_state == AnimationState.Stopped)
        {
            _phase = 0f;
            _currentSpeed = 0f;
        }

        _rampStartTime = Time.time;
        _rampStartSpeed = _currentSpeed;
        _state = AnimationState.RampingUp;
    }

    public void StopAnimation()
    {
        if (_state == AnimationState.Stopped)
            return;

        float cyclePhase = Mathf.Repeat(_phase, 1f);
        _stopStartPhase = _phase;
        _stopStartSpeed = _currentSpeed;

        float remainingCycle = cyclePhase <= 0.0001f ? 0f : 1f - cyclePhase;
        _stopDecelEndPhase = _stopStartPhase + remainingCycle;
        _stopEndPhase = _stopStartPhase + remainingCycle + rampDownIterations;

        _state = AnimationState.RampingDown;
    }

    static void SetLocalRotationX(Transform target, float x)
    {
        if (target == null)
            return;

        Vector3 euler = target.localEulerAngles;
        euler.x = x;
        target.localEulerAngles = euler;
    }

    static void SetLocalRotationZ(Transform target, float z)
    {
        if (target == null)
            return;

        Vector3 euler = target.localEulerAngles;
        euler.z = z;
        target.localEulerAngles = euler;
    }

    static void SetLocalPositionY(Transform target, float y)
    {
        if (target == null)
            return;

        Vector3 pos = target.localPosition;
        pos.y = y;
        target.localPosition = pos;
    }
}
