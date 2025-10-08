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

    [Tooltip("Strength of the avoidance push (0–1).")]
    public float avoidanceStrength = 0.8f;

    [Header("SDF Brick Settings")]
    public MCSettings mcSettings; // assign in inspector
    public float voxelSize = 0.25f;
    public int brickSize = 32;

    [Header("Target Seeking")]
    public Transform target;
    public float targetBiasStrength = 0.3f;

    [Header("Initial State")]
    public int seed = 12345;
    private Vector3 initialDirection;

    // internal
    private Vector3 _dir;
    private Vector3 _biasDir;
    private float _nextJitterT;
    private System.Random _rng;
    private SDFBrick sdfBrick;

    void Awake()
    {
        _rng = new System.Random(seed);
    }

    void Start()
    {
        // build the SDF brick using MCSettings
        sdfBrick = new SDFBrick(mcSettings, voxelSize, brickSize, isoLevelOffset: 0.00f);

        initialDirection = RandomUnitVector();
        _dir = initialDirection.sqrMagnitude > 1e-6f ? initialDirection.normalized : Vector3.forward;
        _biasDir = RandomUnitVector();
        _nextJitterT = Time.time + (jitterHz > 0f ? 1f / jitterHz : 999f);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // Random steering update
        if (Time.time >= _nextJitterT)
        {
            if (target == null){
                Debug.LogError("No target, using random steering");
                _biasDir = RandomUnitVector();
            } else{
            
                Vector3 rnd = RandomUnitVector();
                Vector3 seek = Vector3.zero;
                
                Vector3 toTarget = target.position - transform.position;

                if (toTarget.sqrMagnitude > 1e-6f)
                {
                    seek = toTarget.normalized;
                }
                
                Vector3 combined = targetBiasStrength * seek + rnd*Mathf.Min(4f, (toTarget.magnitude/10f));
                // Debug.Log("Combined: " + combined);
                _biasDir = combined.sqrMagnitude > 1e-6f ? combined.normalized : rnd;
            }
            
            _nextJitterT += jitterHz > 0f ? 1f / jitterHz : 999f;
        }

        // --- compute avoidance bias from SDF ---
        Vector3 avoidBias = Vector3.zero;
        float s = sdfBrick.Sample(transform.position);
        if (s < avoidanceRadius)
        {
            // numeric gradient
            float h = 0.05f;
            float sx = sdfBrick.Sample(transform.position + new Vector3(h, 0, 0)) - sdfBrick.Sample(transform.position - new Vector3(h, 0, 0));
            float sy = sdfBrick.Sample(transform.position + new Vector3(0, h, 0)) - sdfBrick.Sample(transform.position - new Vector3(0, h, 0));
            float sz = sdfBrick.Sample(transform.position + new Vector3(0, 0, h)) - sdfBrick.Sample(transform.position - new Vector3(0, 0, h));
            Vector3 grad = new Vector3(sx, sy, sz).normalized;
            avoidBias = grad * avoidanceStrength * Mathf.Clamp01(0.5f+(avoidanceRadius - s) / avoidanceRadius);

        }
        Debug.Log("SDF: " + s + " Avoid Bias: " + avoidBias);

        // desired heading: random + avoidance
        Vector3 desired = (_dir + turningStrenght * _biasDir + avoidBias).normalized;

        // limit turning
        float maxRadians = Mathf.Deg2Rad * maxTurnRateDegPerSec * dt;
        _dir = Vector3.RotateTowards(_dir, desired, maxRadians, 0f);
        if (_dir.sqrMagnitude < 1e-9f) _dir = Vector3.forward;

        // move
        transform.position += _dir * speed * dt;
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

