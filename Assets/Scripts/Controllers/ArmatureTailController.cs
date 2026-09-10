using System.Collections.Generic;
using UnityEngine;

public class ArmatureTailController : MonoBehaviour
{
    [Header("Targets")]
    public Transform leader;
    public Transform armatureRoot;
    public Transform jaw;

    [Header("Mouth")]
    public float mouthOpen;
    [Tooltip("Mouth angle when idle (ShootCharge = 0).")]
    public float baseMouthOpen;
    [Tooltip("Additional mouth open at full ShootCharge. Total = baseMouthOpen + maxMouthOpen.")]
    public float maxMouthOpen = 20f;
    [Range(0f, 1f)]
    [Tooltip("0 = jaw only, 1 = head only. Split always sums to mouthOpen.")]
    public float headMouthShare = 0.5f;

    [Header("Banking")]
    [Tooltip("Airplane-style roll into yaw turns. 0 = none. Negate to flip lean direction.")]
    public float bankStrength = 1f;

    const int MaxTrail = 512;
    const float Epsilon = 1e-8f;
    const float MinTrailStep2 = 1e-4f;
    const float MaxBank = 75f;

    Transform[] _chain;
    Transform _head;
    Transform _headEnd;
    float[] _distBack;
    Vector3[] _pos;

    Vector3 _jawLocalPos;
    Quaternion _jawLocalRot;

    readonly List<Vector3> _trail = new();

    EnemySnakeState _state;

    void Awake()
    {
        _state = EnemySnakeState.GetOrCreate(transform);
    }

    void Start()
    {
        if (leader == null || armatureRoot == null) { enabled = false; return; }
        if (!BuildChain()) { enabled = false; return; }

        int n = _chain.Length;
        _pos = new Vector3[n];

        _distBack = new float[n];
        _distBack[n - 1] = 0f;
        for (int i = n - 2; i >= 0; i--)
            _distBack[i] = _distBack[i + 1] +
                           Vector3.Distance(_chain[i].position, _chain[i + 1].position);

        if (jaw != null)
        {
            _jawLocalPos = _head.InverseTransformPoint(jaw.position);
            _jawLocalRot = Quaternion.Inverse(_head.rotation) * jaw.rotation;
        }

        _trail.Clear();
        for (int i = 0; i < n; i++)
            _trail.Add(_chain[i].position);
    }

    bool BuildChain()
    {
        var list = new List<Transform> { armatureRoot };
        Transform t = armatureRoot;

        while (t.childCount > 0)
        {
            if (t.childCount == 1) { t = t.GetChild(0); list.Add(t); continue; }

            Transform head = null;
            float best = float.MaxValue;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform c = t.GetChild(i);
                float d = (c.position - leader.position).sqrMagnitude;
                if (d < best) { best = d; head = c; }
            }
            if (head == null) return false;
            list.Add(head);
            _head = head;

            if (head.childCount == 1)
                list.Add(head.GetChild(0));

            if (jaw == null)
                for (int i = 0; i < t.childCount; i++)
                    if (t.GetChild(i) != head) { jaw = t.GetChild(i); break; }

            break;
        }

        _chain = list.ToArray();
        if (_chain.Length < 2) return false;

        _headEnd = _chain[_chain.Length - 1];
        if (_head == null)
        {
            Transform parent = _headEnd.parent;
            _head = parent != null && System.Array.IndexOf(_chain, parent) >= 0
                ? parent
                : _headEnd;
        }

        return true;
    }

    void LateUpdate()
    {
        float charge = _state != null ? _state.ShootCharge : 0f;
        mouthOpen = baseMouthOpen + maxMouthOpen * charge;

        RecordTrail();

        int n = _chain.Length;
        int bodyCount = n - (_headEnd != _head ? 2 : 1);

        _pos[n - 1] = leader.position;
        for (int i = n - 2; i >= 0; i--)
            _pos[i] = SampleTrail(_distBack[i]);

        for (int i = 0; i < bodyCount; i++)
        {
            Vector3 outDir = _pos[i + 1] - _pos[i];
            Vector3 inDir = i > 0 ? _pos[i] - _pos[i - 1] : outDir;
            _chain[i].position = _pos[i];
            _chain[i].rotation = AimRotation(outDir, BankedReference(inDir, outDir));
        }

        ApplyHeadAndJaw();
    }

    Vector3 BankedReference(Vector3 inDir, Vector3 outDir)
    {
        if (Mathf.Abs(bankStrength) < 1e-5f) return Vector3.down;

        Vector3 a = inDir; a.y = 0f;
        Vector3 b = outDir; b.y = 0f;
        if (a.sqrMagnitude < Epsilon || b.sqrMagnitude < Epsilon) return Vector3.down;

        float yaw = Vector3.SignedAngle(a, b, Vector3.up);
        float bank = Mathf.Clamp(yaw * bankStrength, -MaxBank, MaxBank);
        return Quaternion.AngleAxis(bank, outDir) * Vector3.down;
    }

    static Quaternion AimRotation(Vector3 aim, Vector3 refDown)
    {
        if (aim.sqrMagnitude < Epsilon) return Quaternion.identity;
        aim.Normalize();

        Vector3 right = Vector3.Cross(refDown, aim);
        if (right.sqrMagnitude < Epsilon)
        {
            right = Vector3.Cross(Vector3.forward, aim);
            if (right.sqrMagnitude < Epsilon) right = Vector3.Cross(Vector3.right, aim);
        }
        right.Normalize();

        Vector3 up = Vector3.Cross(aim, right);

        return Quaternion.LookRotation(up, aim);
    }

    void ApplyHeadAndJaw()
    {
        int n = _chain.Length;
        int headIdx = n - (_headEnd != _head ? 2 : 1);
        Vector3 headPos = _pos[headIdx];
        Vector3 tipPos = _pos[n - 1];
        Vector3 headAimDir = _headEnd != _head
            ? tipPos - headPos
            : headPos - _pos[headIdx - 1];

        Vector3 headIn = _headEnd != _head
            ? headPos - _pos[headIdx - 1]
            : (headIdx >= 2 ? _pos[headIdx - 1] - _pos[headIdx - 2] : headAimDir);

        Quaternion headAim = AimRotation(headAimDir, BankedReference(headIn, headAimDir));
        float headOpen = mouthOpen * headMouthShare;
        float jawOpen = mouthOpen * (1f - headMouthShare);

        _head.position = headPos;
        _head.rotation = headAim * Quaternion.AngleAxis(-headOpen, Vector3.right);

        if (_headEnd != _head)
        {
            _headEnd.position = tipPos;
            _headEnd.rotation = headAim;
        }

        if (jaw == null) return;

        jaw.position = headPos + headAim * _jawLocalPos;
        jaw.rotation = headAim * _jawLocalRot * Quaternion.AngleAxis(jawOpen, Vector3.right);
    }

    void RecordTrail()
    {
        Vector3 p = leader.position;
        if (_trail.Count == 0 ||
            (p - _trail[_trail.Count - 1]).sqrMagnitude > MinTrailStep2)
        {
            _trail.Add(p);
            if (_trail.Count > MaxTrail) _trail.RemoveAt(0);
        }
    }

    Vector3 SampleTrail(float back)
    {
        if (_trail.Count == 0) return leader.position;

        float rem = back;
        for (int i = _trail.Count - 1; i > 0; i--)
        {
            float len = Vector3.Distance(_trail[i], _trail[i - 1]);
            if (len < Epsilon) continue;
            if (rem <= len) return Vector3.Lerp(_trail[i], _trail[i - 1], rem / len);
            rem -= len;
        }
        return _trail[0];
    }
}