using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CCDSolver : MonoBehaviour
{
    public enum AxisMode { Free, Locked, Limited }

    [System.Serializable]
    public class JointLimit
    {
        public Vector3 axis = Vector3.forward;
        public Vector3 secondaryAxis = Vector3.up;

        public AxisMode xMode = AxisMode.Free;
        public float xMin = -45f;
        public float xMax = 45f;

        public AxisMode yMode = AxisMode.Free;
        public float yMin = -45f;
        public float yMax = 45f;

        public AxisMode zMode = AxisMode.Free;
        public float zMin = -45f;
        public float zMax = 45f;
    }

    public Transform[] joints;
    public Transform tip;
    public Transform target;
    public int iterations = 10;
    public float tolerance = 0.01f;

    public JointLimit[] jointLimits;

    [Header("Test")]
    [Tooltip("Scales bind-pose distance between consecutive joints and to the tip.")]
    public float lengthScale = 1f;

    Quaternion[] bindLocalRotations;
    Quaternion[] frameRotations;
    Vector3[] _bindSegmentLocalPositions;

    void Awake()
    {
        bindLocalRotations = new Quaternion[joints.Length];
        frameRotations = new Quaternion[joints.Length];

        for (int i = 0; i < joints.Length; i++)
        {
            bindLocalRotations[i] = joints[i].localRotation;

            if (jointLimits != null && jointLimits.Length > i)
            {
                Vector3 a = jointLimits[i].axis.normalized;
                Vector3 s = jointLimits[i].secondaryAxis.normalized;
                frameRotations[i] = Quaternion.LookRotation(a, s);
            }
        }

        CacheSegmentLengths();
    }

    void OnValidate() => CacheSegmentLengths();

    void CacheSegmentLengths()
    {
        if (joints == null || joints.Length == 0)
        {
            _bindSegmentLocalPositions = null;
            return;
        }

        _bindSegmentLocalPositions = new Vector3[joints.Length];
        for (int i = 0; i < joints.Length - 1; i++)
        {
            if (joints[i + 1] != null)
                _bindSegmentLocalPositions[i] = joints[i + 1].localPosition;
        }

        if (tip != null)
            _bindSegmentLocalPositions[joints.Length - 1] = tip.localPosition;
    }

    void ApplyLengthScale()
    {
        if (Mathf.Approximately(lengthScale, 1f) || _bindSegmentLocalPositions == null)
            return;

        for (int i = 0; i < joints.Length - 1; i++)
        {
            if (joints[i + 1] != null)
                joints[i + 1].localPosition = _bindSegmentLocalPositions[i] * lengthScale;
        }

        if (tip != null)
            tip.localPosition = _bindSegmentLocalPositions[joints.Length - 1] * lengthScale;
    }

    void LateUpdate()
    {
        ApplyLengthScale();

        for (int i = 0; i < iterations; i++)
        {
            if (Vector3.Distance(tip.position, target.position) < tolerance)
                break;

            for (int j = joints.Length - 1; j >= 0; j--)
            {
                Transform joint = joints[j];

                Vector3 toEnd = tip.position - joint.position;
                Vector3 toTarget = target.position - joint.position;

                Quaternion rotation = Quaternion.FromToRotation(toEnd, toTarget);
                joint.rotation = rotation * joint.rotation;

                if (jointLimits != null && jointLimits.Length > j)
                    ApplyLimit(j);
            }
        }
    }

    void ApplyLimit(int index)
    {
        Transform joint = joints[index];
        Quaternion bind = bindLocalRotations[index];
        Quaternion frame = frameRotations[index];
        JointLimit limit = jointLimits[index];

        Quaternion delta = Quaternion.Inverse(bind) * joint.localRotation;
        Quaternion deltaInFrame = Quaternion.Inverse(frame) * delta * frame;

        Vector3 euler = deltaInFrame.eulerAngles;
        euler.x = ClampAxis(euler.x, limit.xMode, limit.xMin, limit.xMax);
        euler.y = ClampAxis(euler.y, limit.yMode, limit.yMin, limit.yMax);
        euler.z = ClampAxis(euler.z, limit.zMode, limit.zMin, limit.zMax);

        Quaternion clampedInFrame = Quaternion.Euler(euler);
        Quaternion clampedDelta = frame * clampedInFrame * Quaternion.Inverse(frame);

        joint.localRotation = bind * clampedDelta;
    }

    float ClampAxis(float angle, AxisMode mode, float min, float max)
    {
        if (angle > 180f) angle -= 360f;

        switch (mode)
        {
            case AxisMode.Locked:
                return 0f;
            case AxisMode.Limited:
                return Mathf.Clamp(angle, min, max);
            default:
                return angle;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Auto Setup")]
    void AutoSetup()
    {
        if (!TryBuildChain(out Transform[] chain, out string error))
        {
            Debug.LogWarning($"CCDSolver Auto Setup failed on '{name}': {error}", this);
            return;
        }

        int jointCount = chain.Length - 1;
        Undo.RecordObject(this, "CCD Auto Setup");

        joints = new Transform[jointCount];
        for (int i = 0; i < jointCount; i++)
            joints[i] = chain[i];

        tip = chain[chain.Length - 1];

        jointLimits = new JointLimit[jointCount];
        for (int i = 0; i < jointCount; i++)
            jointLimits[i] = CreateDefaultJointLimit();

        EditorUtility.SetDirty(this);
        Debug.Log(
            $"CCDSolver Auto Setup on '{name}': {jointCount} joint(s), tip '{tip.name}'.",
            this);
    }

    static JointLimit CreateDefaultJointLimit()
    {
        return new JointLimit
        {
            axis = Vector3.forward,
            secondaryAxis = Vector3.up,
            xMode = AxisMode.Limited,
            xMin = -50f,
            xMax = 50f,
            yMode = AxisMode.Locked,
            zMode = AxisMode.Locked,
        };
    }

    bool TryBuildChain(out Transform[] chain, out string error)
    {
        chain = null;
        error = null;

        var nodes = new System.Collections.Generic.List<Transform>();
        Transform current = transform;

        while (current != null)
        {
            nodes.Add(current);

            if (current.childCount == 0)
                break;

            if (current.childCount > 1)
            {
                error =
                    $"'{current.name}' has {current.childCount} children. Expected a single-child chain.";
                return false;
            }

            current = current.GetChild(0);
        }

        if (nodes.Count < 2)
        {
            error = "Need at least two transforms (first joint + end/tip). No child chain found.";
            return false;
        }

        chain = nodes.ToArray();
        return true;
    }
#endif
}