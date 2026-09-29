using System.Text;
using UnityEngine;

/// <summary>
/// Analytical 3-bone leg IK. Law-of-cosines on a bend plane, then aim each bone at the next solved joint.
/// </summary>
[DefaultExecutionOrder(1000)]
public class AnalyticalLegIk : MonoBehaviour
{
    public Transform[] bones;
    public Transform tip;
    public Transform target;
    public Transform pole;
    public bool invertBend;
    [Tooltip("Flip bend direction on the second hinge (Bone.002 / ankle).")]
    public bool invertAnkle;

    [Header("Debug")]
    [Tooltip("One full report per frame in Console. Copy between BEGIN/END when leg misbehaves.")]
    public bool debugLog;

    float[] _lengths;
    Quaternion[] _bindLocalRot;
    Vector3 _bindPlaneNormal;
    Vector3 _bindForward;
    Vector3 _lastPlaneNormal;
    bool _hasLastPlaneNormal;
    Vector3[] _joints;
    Vector3[] _hingeAxisWorld;
    Vector3 _planeNormal;
    int _rootIndex;
    int _frame;
    bool _bindIncluded;

    const string ReportBegin = "======== AnalyticalLegIk BEGIN ========";
    const string ReportEnd = "======== AnalyticalLegIk END ========";

    void Awake() => CacheBindPose();
    void Start() => CacheBindPose();

    void CacheBindPose()
    {
        if (bones == null || bones.Length < 3 || tip == null) return;

        _rootIndex = 0;
        while (_rootIndex < bones.Length - 1 &&
               Vector3.Distance(bones[_rootIndex].position, bones[_rootIndex + 1].position) < 1e-4f)
            _rootIndex++;

        if (_rootIndex + 2 >= bones.Length) return;

        int rotateCount = 3;
        int n = _rootIndex + rotateCount;
        _bindLocalRot = new Quaternion[n];
        _lengths = new float[3];
        _joints = new Vector3[4];
        _hingeAxisWorld = new Vector3[n];
        _bindIncluded = false;

        for (int i = _rootIndex; i < n; i++)
        {
            if (bones[i] == null) return;
            _bindLocalRot[i] = bones[i].localRotation;
            _hingeAxisWorld[i] = _bindPlaneNormal;
            if (Vector3.Dot(bones[i].right, _bindPlaneNormal) < 0f)
                _hingeAxisWorld[i] = -_bindPlaneNormal;
        }

        for (int s = 0; s < 3; s++)
        {
            Transform a = bones[_rootIndex + s];
            Transform b = s < 2 ? bones[_rootIndex + s + 1] : tip;
            if (b == null) return;
            _lengths[s] = Vector3.Distance(a.position, b.position);
        }

        Vector3 seg0 = bones[_rootIndex + 1].position - bones[_rootIndex].position;
        Vector3 seg1 = bones[_rootIndex + 2].position - bones[_rootIndex + 1].position;
        _bindPlaneNormal = Vector3.Cross(seg0, seg1);
        if (_bindPlaneNormal.sqrMagnitude < 1e-8f)
        {
            Vector3 poleDir = pole != null ? pole.position - bones[_rootIndex].position : Vector3.up;
            _bindPlaneNormal = Vector3.Cross(seg0, poleDir);
        }
        _bindPlaneNormal.Normalize();

        _bindForward = seg0.normalized;
        _hasLastPlaneNormal = false;
    }

    void LateUpdate()
    {
        if (bones == null || bones.Length < 3 || tip == null || target == null || _lengths == null) return;
        if (_rootIndex + 2 >= bones.Length) return;

        _frame++;

        Vector3 root = bones[_rootIndex].position;
        Vector3 rawGoal = target.position;
        Vector3 goal = ClampToReach(root, rawGoal);
        Vector3 toGoal = goal - root;

        if (!BuildPlane(root, toGoal, out Vector3 forward, out Vector3 side, out Vector3 planeNormal))
        {
            if (debugLog) LogReport("BuildPlane FAILED (goal on top of hip?)");
            return;
        }

        float x = Vector3.Dot(toGoal, forward);
        float y = Vector3.Dot(toGoal, side);
        float bendSign = invertBend ? -1f : 1f;

        SolveThreeBonePlanar(x, y, _lengths[0], _lengths[1], _lengths[2], bendSign,
            out Vector2 p1, out Vector2 p2, out Vector2 p3,
            out float a0, out float a1, out float a2);

        _joints[0] = root;
        _joints[1] = root + forward * p1.x + side * p1.y;
        _joints[2] = root + forward * p2.x + side * p2.y;
        _joints[3] = root + forward * p3.x + side * p3.y;
        _planeNormal = planeNormal;

        if (debugLog)
        {
            var applySection = new StringBuilder(512);
            ResetToBind();
            AppendApplySection(applySection, "at bind pose (before rotate)");
            ApplyRotations();
            AppendApplySection(applySection, "after rotate");
            LogFullReport(applySection, root, rawGoal, goal, forward, side, planeNormal, x, y, a0, a1, a2);
        }
        else
        {
            ApplyChain();
        }
    }

    void LogFullReport(
        StringBuilder applySection,
        Vector3 root, Vector3 rawGoal, Vector3 goal,
        Vector3 forward, Vector3 side, Vector3 planeNormal,
        float planeX, float planeY,
        float a0, float a1, float a2)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine(ReportBegin);
        sb.AppendLine($"leg={name}  frame={_frame}  time={Time.time:F3}  ikRoot={bones[_rootIndex].name} (index {_rootIndex})");

        if (!_bindIncluded)
        {
            _bindIncluded = true;
            sb.AppendLine("--- BIND (once) ---");
            for (int i = 0; i < bones.Length; i++)
            {
                Transform b = bones[i];
                float toNext = i < bones.Length - 1
                    ? Vector3.Distance(b.position, bones[i + 1].position)
                    : -1f;
                sb.AppendLine(
                    $"  bone[{i}] {b.name}  worldPos={V(b.position)}  " +
                    $"toNext={toNext:F4}  parent={(b.parent ? b.parent.name : "null")}");
            }
            sb.AppendLine($"  ik segments L0={_lengths[0]:F4} L1={_lengths[1]:F4} L2={_lengths[2]:F4} (L2=bone→tip)");
            if (tip != null)
                sb.AppendLine($"  tip {tip.name}  worldPos={V(tip.position)}");
            sb.AppendLine($"  bindForward={V(_bindForward)}  bindPlaneNormal={V(_bindPlaneNormal)}");
        }

        float rawDist = Vector3.Distance(root, rawGoal);
        float usedDist = Vector3.Distance(root, goal);
        float maxReach = _lengths[0] + _lengths[1] + _lengths[2];
        float kneeSolveDeg = a1 * Mathf.Rad2Deg;
        float greenKneeDeg = AngleBetween(_joints[1] - _joints[0], _joints[2] - _joints[1]);
        float solvedTipErr = Vector3.Distance(_joints[3], goal);

        sb.AppendLine("--- HIP ↔ TARGET ---");
        sb.AppendLine($"  ik root        {V(root)}");
        sb.AppendLine($"  target (raw)   {V(rawGoal)}");
        sb.AppendLine($"  target (used)  {V(goal)}");
        sb.AppendLine($"  distance raw   {rawDist:F4}");
        sb.AppendLine($"  distance used  {usedDist:F4}  /  maxReach {maxReach:F4}");
        sb.AppendLine($"  solved J3 err  {solvedTipErr:F4}  (should be ~0)");
        if (tip != null)
            sb.AppendLine($"  tip→target     {Vector3.Distance(tip.position, rawGoal):F4}");

        sb.AppendLine("--- PLANE ---");
        sb.AppendLine($"  normal={V(planeNormal)}  forward={V(forward)}  side={V(side)}");
        sb.AppendLine($"  targetInPlane (x,y)=({planeX:F4}, {planeY:F4})  invertBend={invertBend}  invertAnkle={invertAnkle}");
        if (Mathf.Abs(planeY) < 1e-3f)
            sb.AppendLine("  NOTE: |y|≈0 only if target lies on bind-forward line in this plane.");

        sb.AppendLine("--- SOLVER (law of cosines) ---");
        sb.AppendLine($"  angles  hip(a0)={a0 * Mathf.Rad2Deg:F1}°  knee(a1)={kneeSolveDeg:F1}°  ankle(a2)={a2 * Mathf.Rad2Deg:F1}°");
        sb.AppendLine($"  green knee bend {greenKneeDeg:F1}°  (0=straight gizmo, >30=bent gizmo)");
        sb.AppendLine($"  J0={V(_joints[0])}");
        sb.AppendLine($"  J1={V(_joints[1])}");
        sb.AppendLine($"  J2={V(_joints[2])}");
        sb.AppendLine($"  J3={V(_joints[3])}");

        sb.Append(applySection);

        sb.AppendLine("--- VERDICT ---");
        if (_lengths[0] < 1e-3f)
            sb.AppendLine("  L0≈0 — first IK bone overlaps next; skipped via ikRoot (check BIND).");
        if (kneeSolveDeg < 5f && greenKneeDeg < 5f && Mathf.Abs(planeY) < 1e-3f)
            sb.AppendLine("  Solver STRAIGHT — target on bind-forward line; offset target sideways.");
        else if (greenKneeDeg > 25f)
            sb.AppendLine("  Solver BENT (green gizmo should V-shape). If mesh straight → skin weights.");
        else if (kneeSolveDeg > 5f)
            sb.AppendLine("  Solver bending — check APPLY localRotΔ on Bone.001 / Bone.002.");

        sb.AppendLine(ReportEnd);
        Debug.Log(sb.ToString(), this);
    }

    void AppendApplySection(StringBuilder sb, string label)
    {
        sb.AppendLine($"--- APPLY {label} ---");
        int last = _rootIndex + 2;
        for (int i = _rootIndex; i <= last; i++)
        {
            Transform child = i < last ? bones[i + 1] : tip;
            Vector3 curDir = child.position - bones[i].position;
            Vector3 tgtDir = _joints[i - _rootIndex + 1] - bones[i].position;
            float curLen = curDir.magnitude;
            float tgtLen = tgtDir.magnitude;
            float ang = curLen > 1e-4f && tgtLen > 1e-4f
                ? Vector3.Angle(curDir, tgtDir)
                : -1f;
            float localDelta = Quaternion.Angle(_bindLocalRot[i], bones[i].localRotation);
            sb.AppendLine(
                $"  [{i}] {bones[i].name}  segLen={curLen:F4}  curDir={V(curDir / Mathf.Max(curLen, 1e-6f))}  " +
                $"tgtDir={V(tgtDir / Mathf.Max(tgtLen, 1e-6f))}  needRotate={ang:F1}°  localRotΔFromBind={localDelta:F1}°");
        }
    }

    void LogReport(string message)
    {
        Debug.Log($"{ReportBegin}\n{message}\n{ReportEnd}", this);
    }

    static float AngleBetween(Vector3 a, Vector3 b)
    {
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return 0f;
        return Vector3.Angle(a, b);
    }

    static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

    Vector3 ClampToReach(Vector3 root, Vector3 goal)
    {
        float reach = _lengths[0] + _lengths[1] + _lengths[2];
        Vector3 d = goal - root;
        return d.magnitude <= reach ? goal : root + d.normalized * reach;
    }

    bool BuildPlane(Vector3 root, Vector3 toGoal, out Vector3 forward, out Vector3 side, out Vector3 normal)
    {
        forward = Vector3.forward;
        side = Vector3.right;
        normal = Vector3.up;

        if (toGoal.sqrMagnitude < 1e-10f) return false;

        Vector3 poleVec = pole != null
            ? pole.position - root
            : bones[_rootIndex].parent != null ? bones[_rootIndex].parent.up : Vector3.up;

        normal = Vector3.Cross(toGoal, poleVec);
        if (normal.sqrMagnitude < 1e-8f)
            normal = Vector3.Cross(toGoal, Vector3.up);
        if (normal.sqrMagnitude < 1e-8f) return false;

        normal.Normalize();

        if (Vector3.Dot(normal, _bindPlaneNormal) < 0f)
            normal = -normal;
        if (_hasLastPlaneNormal && Vector3.Dot(normal, _lastPlaneNormal) < 0f)
            normal = -normal;

        _lastPlaneNormal = normal;
        _hasLastPlaneNormal = true;

        // Bind forward — NOT goal direction — so target can have non-zero y in the plane.
        forward = Vector3.ProjectOnPlane(_bindForward, normal).normalized;
        if (forward.sqrMagnitude < 1e-8f)
            forward = Vector3.ProjectOnPlane(toGoal, normal).normalized;
        if (forward.sqrMagnitude < 1e-8f) return false;

        side = Vector3.Cross(normal, forward).normalized;
        return true;
    }

    void ApplyChain()
    {
        ResetToBind();
        ApplyRotations();
    }

    void ResetToBind()
    {
        int last = _rootIndex + 2;
        for (int i = _rootIndex; i <= last; i++)
            bones[i].localRotation = _bindLocalRot[i];
    }

    void ApplyRotations()
    {
        int last = _rootIndex + 2;
        for (int i = _rootIndex; i <= last; i++)
        {
            Transform child = i < last ? bones[i + 1] : tip;
            Vector3 curDir = child.position - bones[i].position;
            Vector3 tgtDir = _joints[i - _rootIndex + 1] - bones[i].position;

            if (curDir.sqrMagnitude < 1e-8f || tgtDir.sqrMagnitude < 1e-8f) continue;

            if (i == last)
                ApplyHingeRotation(i, curDir, tgtDir, invertAnkle);
            else
                bones[i].rotation = Quaternion.FromToRotation(curDir, tgtDir) * bones[i].rotation;
        }
    }

    void ApplyHingeRotation(int boneIndex, Vector3 curDir, Vector3 tgtDir, bool invert)
    {
        Vector3 axis = _hingeAxisWorld[boneIndex];
        float angle = Vector3.SignedAngle(curDir, tgtDir, axis);
        if (invert)
            angle = -angle;

        bones[boneIndex].rotation = Quaternion.AngleAxis(angle, axis) * bones[boneIndex].rotation;
    }

    static void SolveThreeBonePlanar(
        float x, float y,
        float l0, float l1, float l2,
        float bendSign,
        out Vector2 j1, out Vector2 j2, out Vector2 j3,
        out float a0, out float a1, out float a2)
    {
        float d = Mathf.Sqrt(x * x + y * y);
        float maxReach = l0 + l1 + l2 - 1e-5f;
        float minReach = Mathf.Max(Mathf.Abs(l0 - l1 - l2), 1e-5f);

        if (d > maxReach) { float s = maxReach / d; x *= s; y *= s; }
        else if (d < minReach) { float s = minReach / d; x *= s; y *= s; }

        d = Mathf.Sqrt(x * x + y * y);
        float invD = d > 1e-6f ? 1f / d : 0f;
        float wx = x - l2 * x * invD;
        float wy = y - l2 * y * invD;
        float dw = Mathf.Sqrt(wx * wx + wy * wy);
        dw = Mathf.Clamp(dw, Mathf.Abs(l0 - l1) + 1e-5f, l0 + l1 - 1e-5f);

        float cosElbow = (l0 * l0 + l1 * l1 - dw * dw) / (2f * l0 * l1);
        cosElbow = Mathf.Clamp(cosElbow, -1f, 1f);
        float elbow = Mathf.PI - Mathf.Acos(cosElbow);

        float cosAlpha = (l0 * l0 + dw * dw - l1 * l1) / (2f * l0 * dw);
        cosAlpha = Mathf.Clamp(cosAlpha, -1f, 1f);
        float alpha = Mathf.Acos(cosAlpha);

        float psi = Mathf.Atan2(wy, wx);
        a0 = psi - bendSign * alpha;
        a1 = bendSign * elbow;
        a2 = Mathf.Atan2(y, x) - a0 - a1;

        j1 = new Vector2(l0 * Mathf.Cos(a0), l0 * Mathf.Sin(a0));
        j2 = j1 + new Vector2(l1 * Mathf.Cos(a0 + a1), l1 * Mathf.Sin(a0 + a1));
        j3 = j2 + new Vector2(l2 * Mathf.Cos(a0 + a1 + a2), l2 * Mathf.Sin(a0 + a1 + a2));
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (target == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(target.position, 0.05f);

        if (bones != null && bones.Length > 0 && bones[0] != null)
        {
            int root = _rootIndex;
            if (_lengths == null)
            {
                root = 0;
                while (root < bones.Length - 1 &&
                       Vector3.Distance(bones[root].position, bones[root + 1].position) < 1e-4f)
                    root++;
            }
            if (root < bones.Length)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(bones[root].position, target.position);
            }
        }

        if (_joints == null) return;
        Gizmos.color = Color.green;
        for (int i = 0; i < _joints.Length - 1; i++)
            Gizmos.DrawLine(_joints[i], _joints[i + 1]);
    }
#endif
}
