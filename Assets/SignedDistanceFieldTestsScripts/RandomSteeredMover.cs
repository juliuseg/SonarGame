using UnityEngine;


/// TODO:
/// Make the sampling on GPU to match the mesh. 


public class RandomSteeredMover : MonoBehaviour
{
    [Header("Kinematics")]
    public float speed = 4f;
    public float maxTurnRateDegPerSec = 90f;

    [Header("Random Steering")]
    [Range(0f, 1f)] public float turningStrenght = 0.25f;
    public float jitterHz = 5f;

    [Header("Wall Avoidance")]
    [Tooltip("Distance at which avoidance begins (meters).")]
    public float avoidanceRadius = 1.0f;
    public float wallFollowRadius = 2.0f;
    public float slowDownFactor = 1f; 
    public float wallFollowStrength = 0.5f; // new: how much to steer along the wall
    public float avoidanceTurnFactor = 1f; 

    [Tooltip("Strength of the avoidance push (0–1).")]
    public float avoidanceStrength = 0.8f;

    [Header("SDF")]
    private ChunkManager _chunkManager;

    [Header("Target Seeking")]
    public Transform target;
    public GameObject bulletPrefab;
    public float range = 5;
    public int magazineSize = 5;
    public float reloadInterval = 1f;
    public float targetBiasStrength = 0.3f;
    [Tooltip("Treat target as wall: steer away when within avoidance radius.")]
    public bool avoidTarget;
    public float targetAvoidanceRadius = 1.0f;
    [Tooltip("When first avoiding target, idle for this many seconds and ignore target.")]
    public float idleDuration = 2f;

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
        
        if (seed == 0) {
            seed = UnityEngine.Random.Range(0, 1_000_000);
        }
        _rng = new System.Random(seed);
    }

    void Start()
    {
        // build the SDF brick using MCSettings
        if (_chunkManager == null)
        {
            Debug.LogError("Chunkmanager not found");
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

        Transform effectiveTarget = (idleing || target == null) ? null : target;
        if (idleing)
        {
            _idleCountdown -= dt;
            if (_idleCountdown <= 0f) idleing = false;
        }

        // --- random steering ---
        if (Time.time >= _nextJitterT)
        {
            if (effectiveTarget == null)
            {
                _biasDir = RandomUnitVector();
            }
            else
            {
                Vector3 rnd = RandomUnitVector();
                Vector3 toTarget = effectiveTarget.position - transform.position;
                Vector3 seek = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : Vector3.zero;
                Vector3 combined = targetBiasStrength * seek + rnd * Mathf.Min(4f, toTarget.magnitude / 10f);
                _biasDir = combined.sqrMagnitude > 1e-6f ? combined.normalized : rnd;
            }

            _nextJitterT += jitterHz > 0f ? 1f / jitterHz : 999f;
        }

        // --- compute avoidance bias from SDF ---
        Vector3 avoidBias = Vector3.zero;
        Vector3 gradient = Vector3.zero;
        float sdfValue = float.MaxValue;
        bool hasSdf = _chunkManager.TryGetSDFValue(transform.position, out sdfValue);
        // if (hasSdf) {
        //     Debug.Log($"SDF Value {sdfValue}");
        // }

        if (hasSdf && sdfValue < avoidanceRadius)
        {
            if (_chunkManager.TrySampleSDFGradient(transform.position, out gradient))
            {
                float t = Mathf.Clamp01((avoidanceRadius - sdfValue) / avoidanceRadius);
                float biasStrength = Mathf.Pow(t, 0.7f);
                avoidBias = gradient.normalized * avoidanceStrength * biasStrength;
            }
        }

        // --- treat target as wall when avoidTarget ---
        bool inTargetAvoidanceRange = target != null && avoidTarget && (target.position - transform.position).magnitude < targetAvoidanceRadius;
        if (inTargetAvoidanceRange)
        {
            if (!idleing && !_wasInTargetAvoidanceRange)
            {
                idleing = true;
                _idleCountdown = idleDuration;
                _wasInTargetAvoidanceRange = true;
            }
            else
            {
                _wasInTargetAvoidanceRange = true;
                Vector3 toTarget = target.position - transform.position;
                float distToTarget = toTarget.magnitude;
                if (distToTarget > 1e-6f)
                {
                    Vector3 awayFromTarget = -toTarget / distToTarget;
                    float t = Mathf.Clamp01((targetAvoidanceRadius - distToTarget) / targetAvoidanceRadius);
                    float biasStrength = Mathf.Pow(t, 0.7f);
                    avoidBias += awayFromTarget * avoidanceStrength * biasStrength;

                    gradient = awayFromTarget;
                    sdfValue = Mathf.Min(sdfValue, distToTarget);
                    hasSdf = true;
                }
            }
        }
        else if (!idleing)
        {
            _wasInTargetAvoidanceRange = false;
        }

        // --- desired heading base ---
        Vector3 desired = (_dir + turningStrenght * _biasDir + avoidBias).normalized;

        // --- wall following if heading into wall ---
        float turningBoost = 1f; // dynamic turn rate multiplier
        if (hasSdf && gradient != Vector3.zero && sdfValue < wallFollowRadius)
        {
            float dot = Vector3.Dot(_dir.normalized, gradient.normalized);
            if (dot < 0f)
            {
                Vector3 wallTangent = Vector3.ProjectOnPlane(_dir, gradient).normalized;
                desired = Vector3.Slerp(desired, wallTangent, wallFollowStrength * -dot);
                turningBoost = 1f + (-dot) * avoidanceTurnFactor; // scale turn speed when facing wall
                Debug.DrawRay(transform.position, wallTangent * 2f, Color.cyan);
                // Debug.Log($"Wall follow active: dot={dot:F3}, turningBoost={turningBoost:F2}");
            }
        }

        // --- limit turning with boosted rate ---
        float maxRadians = Mathf.Deg2Rad * maxTurnRateDegPerSec * dt * turningBoost;
        _dir = Vector3.RotateTowards(_dir, desired, maxRadians, 0f);
        if (_dir.sqrMagnitude < 1e-9f) _dir = Vector3.forward;

        // --- slowdown based on avoidance direction ---
        float currentSpeed = speed;
        if (hasSdf && sdfValue < avoidanceRadius && avoidBias != Vector3.zero)
        {
            float dot = Vector3.Dot(_dir.normalized, avoidBias.normalized);
            if (dot < 0f)
            {
                float proximity = Mathf.Clamp01((avoidanceRadius - sdfValue) / avoidanceRadius);
                float baseSlow = Mathf.Clamp01(-dot) * proximity * slowDownFactor;
                float slowFactor = 1f - Mathf.Clamp01(baseSlow);
                currentSpeed *= slowFactor;

                // Debug.Log($"Slowing down: dot={dot:F3}, proximity={proximity:F3}, slowDownFactor={slowDownFactor:F2}, slowFactor={slowFactor:F3}, newSpeed={currentSpeed:F3}");
            }
        }

        if (sdfValue < 0)
        {
            // Debug.Log($"SDF Value {sdfValue}, avoidBias {avoidBias}, _dir {_dir}, speedMod {speed/currentSpeed}");
        }

        // --- reload ---
        if (_ammo < magazineSize && Time.time >= _nextReloadTime)
        {
            _ammo++;
            _nextReloadTime = Time.time + reloadInterval;
        }

        // --- raycast toward heading ---
        if (_ammo > 0 && target != null && bulletPrefab != null && Physics.Raycast(transform.position, _dir, out RaycastHit hit, range) && hit.transform == target)
        {
            _ammo--;
            var bullet = Instantiate(bulletPrefab, transform.position, Quaternion.identity);
            if (bullet.TryGetComponent<EnemyBulletController>(out var bulletCont)) bulletCont.Shoot(_dir.normalized);
        }

        // --- move ---
        transform.position += _dir * currentSpeed * dt;
    }





    Vector3 RandomUnitVector()
    {
        double u = 2.0 * _rng.NextDouble() - 1.0;
        double theta = 2.0 * Mathf.PI * _rng.NextDouble();
        double s = System.Math.Sqrt(1.0 - u * u);
        return new Vector3((float)(s * System.Math.Cos(theta)),
                           (float)(s * System.Math.Sin(theta)),
                           (float)u);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.white;
        Vector3 p = Application.isPlaying ? transform.position : transform.position;
        Vector3 v = (Application.isPlaying ? _dir : initialDirection.normalized) * Mathf.Max(0.5f, speed * 0.25f);
        Gizmos.DrawLine(p, p + v);
        Gizmos.DrawSphere(p + v, 0.03f);
    }
#endif
}

