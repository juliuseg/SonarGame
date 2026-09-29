using UnityEngine;

public class RandomSteeredMover : MonoBehaviour
{
    [Header("Kinematics")]
    public float speed = 4f;
    public float maxTurnRateDegPerSec = 90f;
    [Tooltip("Minimum angle from world up/down. 0 disables. 10 = pitch stays between 10° and 170° from up.")]
    [Range(0f, 89f)] public float pitchLimitFromVerticalDeg = 10f;

    [Header("Speed Variation")]
    public float speedMinMultiplier = 0.8f;
    public float speedMaxMultiplier = 1.2f;
    [Tooltip("How quickly speed noise changes over time.")]
    public float speedNoiseFrequency = 0.2f;

    [Header("Target Speed Boost")]
    [Tooltip("Speed multiplier at closest range (1 = no change).")]
    public float targetSpeedMultiplier = 1.3f;
    public float targetSpeedNearDist = 10f;
    public float targetSpeedFarDist = 20f;

    [Header("Body Wave")]
    [Tooltip("Side-to-side wiggle frequency in Hz.")]
    public float waveFrequency = 1.5f;
    [Tooltip("Peak yaw offset from forward heading in degrees.")]
    public float waveAmplitude = 12f;

    [Header("Random Steering")]
    [Range(0f, 1f)] public float turningStrength = 0.25f;
    public float jitterHz = 5f;

    [Header("Wall Avoidance")]
    [Tooltip("Distance at which avoidance begins (meters).")]
    public float avoidanceRadius = 1.0f;
    public float wallFollowRadius = 2.0f;
    public float slowDownFactor = 1f;
    public float wallFollowStrength = 0.5f;
    public float avoidanceTurnFactor = 1f;
    [Tooltip("Strength of the avoidance push (0–1).")]
    public float avoidanceStrength = 0.8f;

    [Header("Target Seeking")]
    public Transform target;
    public float targetBiasStrength = 0.3f;
    public float minSeekStrength = 0.5f;
    [Tooltip("Distance at which enemy locks fully onto target.")]
    public float attackRange = 5f;
    [Tooltip("Treat target as wall: actively flee when within avoidance radius.")]
    public bool avoidTarget;
    public float targetAvoidanceRadius = 1.0f;
    [Tooltip("Seconds to keep fleeing after entering the flee zone.")]
    [Min(0.1f)] public float fleeDuration = 2f;
    [Tooltip("Distance before chase can resume. 0 = targetAvoidanceRadius + 2.")]
    [Min(0f)] public float fleeExitRadius;

    [Header("Shooting")]
    public GameObject bulletPrefab;
    public float range = 5f;
    public int magazineSize = 5;
    public float reloadInterval = 1f;
    [Tooltip("Seconds to wind up before the first shot in a burst.")]
    public float windUpDuration = 0.35f;
    [Tooltip("After losing line-of-sight, mouth stays open and can resume firing for this long.")]
    public float attackGraceDuration = 0.4f;

    private ISdfSampler _chunkManager;
    private EnemySnakeState _state;

    [Header("Initial State")]
    public int seed = 0;
    private Vector3 initialDirection;

    // internal
    private Vector3 _dir;
    private Vector3 _biasDir;
    private float _nextJitterT;
    private System.Random _rng;
    private int _ammo;
    private float _nextReloadTime;
    private float _wavePhase;
    private bool _fleeing;
    private float _fleeCountdown;
    private bool _wasInFleeZone;
    private bool _ready;
    private bool _warnedNoSdf;
    private float _shootCharge;
    private float _attackGraceRemaining;

    public float WavePhase => _wavePhase;

    private bool UseSdfAvoidance => _chunkManager != null;

    const float InitialHeadingYawJitterDeg = 30f;

    void Awake()
    {
        _state = EnemySnakeState.GetOrCreate(transform);
    }

    void Start()
    {
        _chunkManager ??= ResolveSampler();
        if (!_ready)
            SetupState(target, warnNoSdf: true);
    }

    static ISdfSampler ResolveSampler()
    {
        GameServices.EnsureInitialized().TryResolve<ISdfSampler>(out var sampler);
        return sampler;
    }

    public void Init(Transform initialHeadingTarget = null)
    {
        _chunkManager = ResolveSampler();
        Transform headingTarget = initialHeadingTarget != null ? initialHeadingTarget : target;
        SetupState(headingTarget, warnNoSdf: false);
    }

    void SetupState(Transform headingTarget, bool warnNoSdf)
    {
        if (seed == 0)
            seed = UnityEngine.Random.Range(0, 1_000_000);
        _rng = new System.Random(seed);

        _dir = ClampPitch(ComputeInitialHeading(headingTarget));
        initialDirection = _dir;
        _biasDir = _dir;
        _nextJitterT = Time.time + (jitterHz > 0f ? 1f / jitterHz : 999f);
        _ammo = magazineSize;
        _nextReloadTime = Time.time + reloadInterval;
        _shootCharge = 0f;
        _attackGraceRemaining = 0f;
        PublishShootCharge();

        if (_dir.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);

        _ready = true;

        if (warnNoSdf && !UseSdfAvoidance && !_warnedNoSdf)
        {
            Debug.LogWarning($"[{name}] RandomSteeredMover running without an ISdfSampler — SDF wall avoidance disabled.");
            _warnedNoSdf = true;
        }
    }

    Vector3 ComputeInitialHeading(Transform headingTarget)
    {
        if (headingTarget == null)
        {
            Vector3 rnd = RandomUnitVector();
            return rnd.sqrMagnitude > 1e-6f ? rnd.normalized : Vector3.forward;
        }

        Vector3 toTarget = headingTarget.position - transform.position;
        if (toTarget.sqrMagnitude < 1e-6f)
            return transform.forward.sqrMagnitude > 1e-6f ? transform.forward.normalized : Vector3.forward;

        float yawOffset = (float)(_rng.NextDouble() * 2.0 * InitialHeadingYawJitterDeg - InitialHeadingYawJitterDeg);
        return (Quaternion.AngleAxis(yawOffset, Vector3.up) * toTarget.normalized).normalized;
    }


    void Update()
    {
        if (!_ready)
            SetupState(target, warnNoSdf: true);

        float dt = Time.deltaTime;

        // --- distance to target ---
        float distToTarget = target != null
            ? (target.position - transform.position).magnitude
            : float.MaxValue;

        UpdateFleeState(distToTarget, dt);

        bool inTargetAvoidanceRange = avoidTarget && target != null && _fleeing;
        bool inAttackRange = target != null && distToTarget < attackRange && !inTargetAvoidanceRange;

        // --- jitter: update bias direction periodically ---
        if (Time.time >= _nextJitterT)
        {
            _biasDir = ClampPitch(ComputeBiasDirection(distToTarget, inAttackRange, inTargetAvoidanceRange));
            _nextJitterT += jitterHz > 0f ? 1f / jitterHz : 999f;
        }

        // --- SDF sampling ---
        Vector3 avoidBias = Vector3.zero;
        Vector3 gradient = Vector3.zero;
        float sdfValue = float.MaxValue;
        bool hasSdf = false;

        if (UseSdfAvoidance)
        {
            hasSdf = _chunkManager.TryGetSDFValue(transform.position, out sdfValue);

            if (hasSdf && sdfValue < avoidanceRadius)
            {
                if (_chunkManager.TrySampleSDFGradient(transform.position, out gradient))
                {
                    float t = Mathf.Clamp01((avoidanceRadius - sdfValue) / avoidanceRadius);
                    avoidBias = gradient.normalized * avoidanceStrength * Mathf.Pow(t, 0.7f);
                }
            }
        }

        // --- target avoidance (treat target as wall) ---
        if (target != null && avoidTarget)
            HandleTargetAvoidance(ref avoidBias, ref gradient, ref sdfValue, ref hasSdf);

        // --- compute desired direction ---
        Vector3 desired = ComputeDesired(inAttackRange, inTargetAvoidanceRange, avoidBias);

        // --- wall following ---
        float turningBoost = 1f;
        if (!inAttackRange && hasSdf && gradient != Vector3.zero && sdfValue < wallFollowRadius)
        {
            float dot = Vector3.Dot(_dir.normalized, gradient.normalized);
            if (dot < 0f)
            {
                Vector3 wallTangent = Vector3.ProjectOnPlane(_dir, gradient).normalized;
                desired = Vector3.Slerp(desired, wallTangent, wallFollowStrength * -dot);
                turningBoost = 1f + (-dot) * avoidanceTurnFactor;
                Debug.DrawRay(transform.position, wallTangent * 2f, Color.cyan);
            }
        }

        desired = ClampPitch(desired);

        // --- apply turning ---
        float maxRadians = Mathf.Deg2Rad * maxTurnRateDegPerSec * dt * turningBoost;
        _dir = ClampPitch(Vector3.RotateTowards(_dir, desired, maxRadians, 0f));
        if (_dir.sqrMagnitude < 1e-9f) _dir = Vector3.forward;

        // --- speed ---
        float distToPlayer = target != null
            ? (target.position - transform.position).magnitude
            : float.MaxValue;
        float currentSpeed = ComputeSpeed(distToPlayer, hasSdf, sdfValue, avoidBias);
        // Debug.Log($"[{name}] speed: {currentSpeed:F2}");

        // --- reload ---
        if (_ammo < magazineSize && Time.time >= _nextReloadTime)
        {
            _ammo++;
            _nextReloadTime = Time.time + reloadInterval;
        }

        // --- shoot ---
        UpdateShooting(dt);

        // --- move with time-based body wave (XZ only) ---
        float speedRatio = speed > 1e-6f ? currentSpeed / speed : 1f;
        Vector3 moveDir = ApplyBodyWave(_dir, dt, speedRatio);
        transform.position += moveDir * currentSpeed * dt;
    }

    // ---- steering helpers ----

    void UpdateFleeState(float distToTarget, float dt)
    {
        if (!avoidTarget || target == null)
        {
            _fleeing = false;
            _fleeCountdown = 0f;
            _wasInFleeZone = false;
            return;
        }

        float exitRadius = fleeExitRadius > 0f
            ? fleeExitRadius
            : targetAvoidanceRadius + 2f;

        bool inFleeZone = distToTarget < targetAvoidanceRadius;
        if (inFleeZone && !_wasInFleeZone)
        {
            _fleeing = true;
            _fleeCountdown = fleeDuration;
        }

        _wasInFleeZone = inFleeZone;

        if (!_fleeing)
            return;

        if (_fleeCountdown > 0f)
            _fleeCountdown -= dt;

        if (_fleeCountdown <= 0f && distToTarget >= exitRadius)
            _fleeing = false;
    }

    private Vector3 ComputeBiasDirection(float distToTarget, bool inAttackRange, bool inTargetAvoidanceRange)
    {
        if (target == null)
            return RandomUnitVector();

        Vector3 toTarget = target.position - transform.position;
        Vector3 seek = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.zero;

        if (inTargetAvoidanceRange)
            return -seek;

        if (inAttackRange)
            return seek;

        Vector3 rnd = RandomUnitVector();
        float seekWeight = Mathf.Max(minSeekStrength, targetBiasStrength);
        float randomWeight = Mathf.Min(4f, distToTarget / 10f);
        Vector3 combined = seekWeight * seek + randomWeight * rnd;
        return combined.sqrMagnitude > 1e-6f ? combined.normalized : rnd;
    }

    private Vector3 ComputeDesired(bool inAttackRange, bool inTargetAvoidanceRange, Vector3 avoidBias)
    {
        if (inTargetAvoidanceRange && target != null)
        {
            Vector3 toTarget = target.position - transform.position;
            Vector3 away = toTarget.sqrMagnitude > 1e-6f ? (-toTarget).normalized : _dir;
            return (_dir + away + avoidBias).normalized;
        }

        if (inAttackRange && target != null)
        {
            Vector3 toTarget = target.position - transform.position;
            Vector3 seek = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : _biasDir;
            return (_dir + seek).normalized;
        }

        return (_dir + turningStrength * _biasDir + avoidBias).normalized;
    }

    private void HandleTargetAvoidance(ref Vector3 avoidBias, ref Vector3 gradient, ref float sdfValue, ref bool hasSdf)
    {
        if (!_fleeing)
            return;

        float dist = (target.position - transform.position).magnitude;
        if (dist <= 1e-6f)
            return;

        Vector3 away = (transform.position - target.position) / dist;
        float t = Mathf.Clamp01((targetAvoidanceRadius - dist) / targetAvoidanceRadius);
        avoidBias += away * avoidanceStrength * Mathf.Pow(t, 0.7f);
        gradient = away;
        sdfValue = Mathf.Min(sdfValue, dist);
        hasSdf = true;
    }

    private float ComputeSpeed(float distToPlayer, bool hasSdf, float sdfValue, Vector3 avoidBias)
    {
        float noise = Mathf.PerlinNoise(Time.time * speedNoiseFrequency, seed * 0.0001f);
        float speedMul = Mathf.Lerp(speedMinMultiplier, speedMaxMultiplier, noise);

        if (target != null && targetSpeedFarDist > targetSpeedNearDist)
        {
            float t = Mathf.InverseLerp(targetSpeedFarDist, targetSpeedNearDist, distToPlayer);
            speedMul *= Mathf.Lerp(1f, targetSpeedMultiplier, Mathf.Clamp01(t));
        }

        float result = speed * speedMul;

        if (!hasSdf || sdfValue >= avoidanceRadius || avoidBias == Vector3.zero)
            return result;

        float dot = Vector3.Dot(_dir.normalized, avoidBias.normalized);
        if (dot >= 0f) return result;

        float proximity = Mathf.Clamp01((avoidanceRadius - sdfValue) / avoidanceRadius);
        float slowFactor = 1f - Mathf.Clamp01(Mathf.Clamp01(-dot) * proximity * slowDownFactor);
        return result * slowFactor;
    }

    bool HasShotLine()
    {
        if (_ammo <= 0 || target == null || bulletPrefab == null) return false;

        return Physics.Raycast(transform.position, _dir, out RaycastHit hit, range)
               && hit.transform == target;
    }

    void UpdateShooting(float dt)
    {
        float rate = windUpDuration > 1e-6f ? 1f / windUpDuration : 100f;
        bool hasLine = HasShotLine();

        if (hasLine)
        {
            _attackGraceRemaining = attackGraceDuration;

            if (_shootCharge < 1f)
                _shootCharge = Mathf.MoveTowards(_shootCharge, 1f, rate * dt);
            else
                Fire();
        }
        else if (_shootCharge >= 1f && _attackGraceRemaining > 0f)
        {
            _attackGraceRemaining -= dt;
        }
        else if (_shootCharge > 0f)
        {
            _shootCharge = Mathf.MoveTowards(_shootCharge, 0f, rate * dt);
        }

        PublishShootCharge();
    }

    void Fire()
    {
        if (_ammo <= 0 || target == null || bulletPrefab == null) return;

        _ammo--;
        var bullet = Instantiate(bulletPrefab, transform.position, Quaternion.identity);
        if (bullet.TryGetComponent<EnemyBulletController>(out var bulletCont))
            bulletCont.Shoot(_dir.normalized);
    }

    void PublishShootCharge()
    {
        if (_state != null)
            _state.ShootCharge = _shootCharge;
    }

    Vector3 ApplyBodyWave(Vector3 forward, float dt, float speedRatio)
    {
        if (waveFrequency <= 0f || waveAmplitude <= 0f)
            return forward;

        _wavePhase += waveFrequency * speedRatio * Mathf.PI * 2f * dt;

        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 1e-6f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        float yaw = waveAmplitude * Mathf.Sin(_wavePhase);
        Vector3 waved = Quaternion.AngleAxis(yaw, Vector3.up) * flatForward;
        waved.y = forward.y;
        return waved.normalized;
    }

    // ---- utilities ----

    Vector3 ClampPitch(Vector3 dir)
    {
        if (pitchLimitFromVerticalDeg <= 0f || dir.sqrMagnitude < 1e-9f)
            return dir;

        dir = dir.normalized;
        float upDot = Vector3.Dot(dir, Vector3.up);
        float maxUpDot = Mathf.Cos(pitchLimitFromVerticalDeg * Mathf.Deg2Rad);
        float clampedDot = Mathf.Clamp(upDot, -maxUpDot, maxUpDot);
        if (Mathf.Approximately(upDot, clampedDot))
            return dir;

        Vector3 horizontal = Vector3.ProjectOnPlane(dir, Vector3.up);
        if (horizontal.sqrMagnitude < 1e-9f)
        {
            horizontal = Vector3.ProjectOnPlane(_dir.sqrMagnitude > 1e-9f ? _dir : transform.forward, Vector3.up);
            if (horizontal.sqrMagnitude < 1e-9f)
                horizontal = Vector3.forward;
        }
        horizontal.Normalize();

        float horizontalLen = Mathf.Sqrt(Mathf.Max(0f, 1f - clampedDot * clampedDot));
        return (horizontal * horizontalLen + Vector3.up * clampedDot).normalized;
    }

    Vector3 RandomUnitVector()
    {
        double u = 2.0 * _rng.NextDouble() - 1.0;
        double theta = 2.0 * Mathf.PI * _rng.NextDouble();
        double s = System.Math.Sqrt(1.0 - u * u);
        return new Vector3(
            (float)(s * System.Math.Cos(theta)),
            (float)(s * System.Math.Sin(theta)),
            (float)u);
    }

}