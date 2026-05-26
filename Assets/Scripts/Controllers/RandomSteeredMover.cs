using UnityEngine;

/// TODO:
/// Make the sampling on GPU to match the mesh. 

public class RandomSteeredMover : MonoBehaviour
{
    [Header("Kinematics")]
    public float speed = 4f;
    public float maxTurnRateDegPerSec = 90f;

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
    [Tooltip("Treat target as wall: steer away when within avoidance radius.")]
    public bool avoidTarget;
    public float targetAvoidanceRadius = 1.0f;
    [Tooltip("When first avoiding target, idle for this many seconds and ignore target.")]
    public float idleDuration = 2f;

    [Header("Shooting")]
    public GameObject bulletPrefab;
    public float range = 5f;
    public int magazineSize = 5;
    public float reloadInterval = 1f;

    [Header("SDF")]
    private ChunkManager _chunkManager;

    [Header("Initial State")]
    public int seed = 0;
    private Vector3 initialDirection;

    // internal
    private Vector3 _dir;
    private Vector3 _biasDir;
    private float _nextJitterT;
    private System.Random _rng;
    public bool idleing;
    private float _idleCountdown;
    private bool _wasInTargetAvoidanceRange;
    private int _ammo;
    private float _nextReloadTime;

    public void Init(ChunkManager chunkManager)
    {
        _chunkManager = chunkManager;

        if (seed == 0)
            seed = UnityEngine.Random.Range(0, 1_000_000);
        _rng = new System.Random(seed);
    }

    void Start()
    {
        if (_chunkManager == null)
        {
            Debug.LogError("ChunkManager not found");
            return;
        }

        initialDirection = RandomUnitVector();
        _dir = initialDirection.sqrMagnitude > 1e-6f ? initialDirection.normalized : Vector3.forward;
        _biasDir = RandomUnitVector();
        _nextJitterT = Time.time + (jitterHz > 0f ? 1f / jitterHz : 999f);
        _ammo = magazineSize;
        _nextReloadTime = Time.time + reloadInterval;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // --- resolve effective target ---
        Transform effectiveTarget = (idleing || target == null) ? null : target;
        if (idleing)
        {
            _idleCountdown -= dt;
            if (_idleCountdown <= 0f) idleing = false;
        }

        // --- distance to target ---
        float distToTarget = effectiveTarget != null
            ? (effectiveTarget.position - transform.position).magnitude
            : float.MaxValue;
        bool inAttackRange = effectiveTarget != null && distToTarget < attackRange;

        // --- jitter: update bias direction periodically ---
        if (Time.time >= _nextJitterT)
        {
            _biasDir = ComputeBiasDirection(effectiveTarget, distToTarget, inAttackRange);
            _nextJitterT += jitterHz > 0f ? 1f / jitterHz : 999f;
        }

        // --- SDF sampling ---
        Vector3 avoidBias = Vector3.zero;
        Vector3 gradient = Vector3.zero;
        float sdfValue = float.MaxValue;
        bool hasSdf = _chunkManager.TryGetSDFValue(transform.position, out sdfValue);

        if (hasSdf && sdfValue < avoidanceRadius)
        {
            if (_chunkManager.TrySampleSDFGradient(transform.position, out gradient))
            {
                float t = Mathf.Clamp01((avoidanceRadius - sdfValue) / avoidanceRadius);
                avoidBias = gradient.normalized * avoidanceStrength * Mathf.Pow(t, 0.7f);
            }
        }

        // --- target avoidance (treat target as wall) ---
        if (target != null && avoidTarget)
            HandleTargetAvoidance(ref avoidBias, ref gradient, ref sdfValue, ref hasSdf);

        // --- compute desired direction ---
        Vector3 desired = ComputeDesired(effectiveTarget, distToTarget, inAttackRange, avoidBias);

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

        // --- apply turning ---
        float maxRadians = Mathf.Deg2Rad * maxTurnRateDegPerSec * dt * turningBoost;
        _dir = Vector3.RotateTowards(_dir, desired, maxRadians, 0f);
        if (_dir.sqrMagnitude < 1e-9f) _dir = Vector3.forward;

        // --- speed slowdown near walls ---
        float currentSpeed = ComputeSpeed(hasSdf, sdfValue, avoidBias);

        // --- reload ---
        if (_ammo < magazineSize && Time.time >= _nextReloadTime)
        {
            _ammo++;
            _nextReloadTime = Time.time + reloadInterval;
        }

        // --- shoot ---
        TryShoot();

        // --- move ---
        transform.position += _dir * currentSpeed * dt;
    }

    // ---- steering helpers ----

    private Vector3 ComputeBiasDirection(Transform effectiveTarget, float distToTarget, bool inAttackRange)
    {
        if (effectiveTarget == null)
            return RandomUnitVector();

        Vector3 toTarget = effectiveTarget.position - transform.position;
        Vector3 seek = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.zero;

        if (inAttackRange)
            return seek; // pure seek in attack range

        // outside attack range: blend seek with random, but guarantee minSeekStrength
        Vector3 rnd = RandomUnitVector();
        float seekWeight = Mathf.Max(minSeekStrength, targetBiasStrength);
        float randomWeight = Mathf.Min(4f, distToTarget / 10f);
        Vector3 combined = seekWeight * seek + randomWeight * rnd;
        return combined.sqrMagnitude > 1e-6f ? combined.normalized : rnd;
    }

    private Vector3 ComputeDesired(Transform effectiveTarget, float distToTarget, bool inAttackRange, Vector3 avoidBias)
    {
        if (inAttackRange)
        {
            // in attack range: micro-adjust every frame toward target, ignore everything else
            Vector3 toTarget = effectiveTarget.position - transform.position;
            Vector3 seek = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : _biasDir;
            return (_dir + seek).normalized; // smooth micro-correction each frame
        }

        return (_dir + turningStrength * _biasDir + avoidBias).normalized;
    }

    private void HandleTargetAvoidance(ref Vector3 avoidBias, ref Vector3 gradient, ref float sdfValue, ref bool hasSdf)
    {
        float dist = (target.position - transform.position).magnitude;
        bool inRange = dist < targetAvoidanceRadius;

        if (inRange)
        {
            if (!idleing && !_wasInTargetAvoidanceRange)
            {
                idleing = true;
                _idleCountdown = idleDuration;
            }
            else
            {
                Vector3 toTarget = target.position - transform.position;
                if (dist > 1e-6f)
                {
                    Vector3 away = -toTarget / dist;
                    float t = Mathf.Clamp01((targetAvoidanceRadius - dist) / targetAvoidanceRadius);
                    avoidBias += away * avoidanceStrength * Mathf.Pow(t, 0.7f);
                    gradient = away;
                    sdfValue = Mathf.Min(sdfValue, dist);
                    hasSdf = true;
                }
            }
            _wasInTargetAvoidanceRange = true;
        }
        else if (!idleing)
        {
            _wasInTargetAvoidanceRange = false;
        }
    }

    private float ComputeSpeed(bool hasSdf, float sdfValue, Vector3 avoidBias)
    {
        if (!hasSdf || sdfValue >= avoidanceRadius || avoidBias == Vector3.zero)
            return speed;

        float dot = Vector3.Dot(_dir.normalized, avoidBias.normalized);
        if (dot >= 0f) return speed;

        float proximity = Mathf.Clamp01((avoidanceRadius - sdfValue) / avoidanceRadius);
        float slowFactor = 1f - Mathf.Clamp01(Mathf.Clamp01(-dot) * proximity * slowDownFactor);
        return speed * slowFactor;
    }

    private void TryShoot()
    {
        if (_ammo <= 0 || target == null || bulletPrefab == null) return;

        if (Physics.Raycast(transform.position, _dir, out RaycastHit hit, range) && hit.transform == target)
        {
            _ammo--;
            var bullet = Instantiate(bulletPrefab, transform.position, Quaternion.identity);
            if (bullet.TryGetComponent<EnemyBulletController>(out var bulletCont))
                bulletCont.Shoot(_dir.normalized);
        }
    }

    // ---- utilities ----

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

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Vector3 p = transform.position;
        Vector3 v = (Application.isPlaying ? _dir : initialDirection.normalized) * Mathf.Max(0.5f, speed * 0.25f);
        Gizmos.DrawLine(p, p + v);
        Gizmos.DrawSphere(p + v, 0.03f);
    }
#endif
}