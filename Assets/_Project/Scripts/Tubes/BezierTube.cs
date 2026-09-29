using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BezierTube : MonoBehaviour
{
    [Header("Bezier")]
    public Transform startPoint;
    public Transform endPoint;
    public Vector3 startTangent;
    public Vector3 endTangent;

    [Header("Tube")]
    public float radius = 0.1f;
    public int sides = 8;

    [Header("Segments")]
    public int baseSegments = 5;
    public float segmentsPerUnit = 2f;

    [Header("Tangent Clamping")]
    [Tooltip("Max fraction of estimated curve length the total control point chain can occupy")]
    public float maxTangentFraction = 0.8f;

    [Header("Material")]
    public Material material;
    public Material material_error;

    public float maxBuildableLenght;

    private Mesh mesh;

    void Start()
    {
        GetComponent<MeshRenderer>().material = material;
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
        GenerateMesh();
    }

    void Update()
    {
        GenerateMesh();
    }

    void OnValidate()
    {
        if (mesh != null) GenerateMesh();
    }

    // Returns clamped tangent lengths so the control point chain doesn't exceed maxTangentFraction of curve length
    void GetClampedTangentLengths(out float startLen, out float endLen)
    {
        startLen = startTangent.magnitude;
        endLen   = endTangent.magnitude;

        // Estimate curve length as straight-line distance (cheap, good enough for clamping)
        float straightDist = Vector3.Distance(startPoint.position, endPoint.position);

        // Total chain: startTangent + its normal step + endTangent + its normal step = 4 * tangent lengths (worst case)
        float maxTotal = straightDist * maxTangentFraction;
        float currentTotal = (startLen + endLen) * 2f;

        if (currentTotal > maxTotal)
        {
            float scale = maxTotal / currentTotal;
            startLen *= scale;
            endLen   *= scale;
        }
    }

    void GetControlPoints(out Vector3 p0, out Vector3 p1, out Vector3 p2,
                          out Vector3 p3, out Vector3 p4, out Vector3 p5)
    {
        p0 = startPoint.position;
        p5 = endPoint.position;

        Vector3 startDir = startTangent.normalized;
        Vector3 endDir   = endTangent.normalized;

        GetClampedTangentLengths(out float startLen, out float endLen);

        p1 = p0 + startDir * startLen;
        p4 = p5 + endDir   * endLen;

        // p2: from p1 toward p4, clamped to perpendicular plane if angle > 90
        Vector3 toP4 = p4 - p1;
        Vector3 dir2;
        if (Vector3.Dot(toP4.normalized, startDir) >= 0f)
            dir2 = toP4.normalized;
        else
        {
            Vector3 proj = toP4 - Vector3.Dot(toP4, startDir) * startDir;
            dir2 = proj.normalized;
        }
        p2 = p1 + dir2 * startLen;

        // p3: from p4 toward p2, clamped to perpendicular plane if angle > 90
        Vector3 toP2 = p2 - p4;
        Vector3 dir3;
        if (Vector3.Dot(toP2.normalized, endDir) >= 0f)
            dir3 = toP2.normalized;
        else
        {
            Vector3 proj = toP2 - Vector3.Dot(toP2, endDir) * endDir;
            dir3 = proj.normalized;
        }
        p3 = p4 + dir3 * endLen;
    }

    Vector3 SampleBezier(float t, Vector3 p0, Vector3 p1, Vector3 p2,
                                  Vector3 p3, Vector3 p4, Vector3 p5)
    {
        float u = 1 - t;
        return u*u*u*u*u*p0
             + 5*u*u*u*u*t*p1
             + 10*u*u*u*t*t*p2
             + 10*u*u*t*t*t*p3
             + 5*u*t*t*t*t*p4
             + t*t*t*t*t*p5;
    }

    Vector3 SampleTangent(float t, Vector3 p0, Vector3 p1, Vector3 p2,
                                   Vector3 p3, Vector3 p4, Vector3 p5)
    {
        float u = 1 - t;
        Vector3 d = 5 * (
              u*u*u*u*(p1-p0)
            + 4*u*u*u*t*(p2-p1)
            + 6*u*u*t*t*(p3-p2)
            + 4*u*t*t*t*(p4-p3)
            + t*t*t*t*(p5-p4)
        );
        return d.normalized;
    }

    float EstimateCurveLength(Vector3 p0, Vector3 p1, Vector3 p2,
                              Vector3 p3, Vector3 p4, Vector3 p5)
    {
        // Sample 10 points and sum chord lengths
        int steps = 10;
        float len = 0f;
        Vector3 prev = SampleBezier(0f, p0, p1, p2, p3, p4, p5);
        for (int i = 1; i <= steps; i++)
        {
            Vector3 curr = SampleBezier((float)i / steps, p0, p1, p2, p3, p4, p5);
            len += Vector3.Distance(prev, curr);
            prev = curr;
        }
        return len;
    }

    void GenerateMesh()
    {
        if (startPoint == null || endPoint == null) return;

        GetControlPoints(out Vector3 p0, out Vector3 p1, out Vector3 p2,
                         out Vector3 p3, out Vector3 p4, out Vector3 p5);

        float curveLength = EstimateCurveLength(p0, p1, p2, p3, p4, p5);
        int segments = baseSegments + Mathf.RoundToInt(curveLength * segmentsPerUnit);
        segments = Mathf.Max(segments, 1);

        int rings = segments + 1;
        Vector3[] verts   = new Vector3[rings * sides];
        Vector3[] normals = new Vector3[rings * sides];
        int[]     tris    = new int[segments * sides * 6];

        // Seed rotation minimizing frame
        Vector3 t0 = SampleTangent(0f, p0, p1, p2, p3, p4, p5);
        Vector3 r0 = Vector3.Cross(t0, Vector3.up);
        if (r0.sqrMagnitude < 0.001f) r0 = Vector3.Cross(t0, Vector3.right);
        r0.Normalize();

        Vector3 r = r0;

        for (int i = 0; i < rings; i++)
        {
            float   t       = (float)i / segments;
            Vector3 center  = SampleBezier(t, p0, p1, p2, p3, p4, p5);
            Vector3 tangent = SampleTangent(t, p0, p1, p2, p3, p4, p5);

            if (i > 0)
                r = (r - Vector3.Dot(r, tangent) * tangent).normalized;

            Vector3 s = Vector3.Cross(tangent, r).normalized;

            for (int j = 0; j < sides; j++)
            {
                float   angle   = (float)j / sides * Mathf.PI * 2f;
                Vector3 outward = (Mathf.Cos(angle) * r + Mathf.Sin(angle) * s).normalized;

                verts  [i * sides + j] = transform.InverseTransformPoint(center + outward * radius);
                normals[i * sides + j] = transform.InverseTransformDirection(outward);
            }
        }

        int ti = 0;
        for (int i = 0; i < segments; i++)
        for (int j = 0; j < sides; j++)
        {
            int curr     = i       * sides + j;
            int next     = i       * sides + (j + 1) % sides;
            int currNext = (i + 1) * sides + j;
            int nextNext = (i + 1) * sides + (j + 1) % sides;

            tris[ti++] = curr;     tris[ti++] = next;     tris[ti++] = currNext;
            tris[ti++] = next;     tris[ti++] = nextNext; tris[ti++] = currNext;
        }

        mesh.Clear();
        mesh.vertices  = verts;
        mesh.normals   = normals;
        mesh.triangles = tris;

        if (curveLength < maxBuildableLenght)
        {
            GetComponent<MeshRenderer>().material = material;
        }
        else
        {
            GetComponent<MeshRenderer>().material = material_error;
        }

        
    }
}