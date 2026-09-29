using UnityEngine;

public static class BezierPipeMeshBuilder
{
    public static float EstimateLength(
        PipeToolSettings settings,
        Vector3 startPosition,
        Vector3 startTangent,
        Vector3 endPosition,
        Vector3 endTangent)
    {
        GetControlPoints(
            settings,
            startPosition,
            startTangent,
            endPosition,
            endTangent,
            out Vector3 p0,
            out Vector3 p1,
            out Vector3 p2,
            out Vector3 p3,
            out Vector3 p4,
            out Vector3 p5);

        return EstimateCurveLength(p0, p1, p2, p3, p4, p5);
    }

    public static float Build(
        Transform pipeTransform,
        Mesh mesh,
        MeshRenderer meshRenderer,
        PipeToolSettings settings,
        Vector3 startPosition,
        Vector3 startTangent,
        Vector3 endPosition,
        Vector3 endTangent,
        Vector3 startRingUp,
        Vector3 endRingUp,
        Material materialOverride = null)
    {
        GetControlPoints(
            settings,
            startPosition,
            startTangent,
            endPosition,
            endTangent,
            out Vector3 p0,
            out Vector3 p1,
            out Vector3 p2,
            out Vector3 p3,
            out Vector3 p4,
            out Vector3 p5);

        float curveLength = EstimateCurveLength(p0, p1, p2, p3, p4, p5);
        int segments = settings.baseSegments + Mathf.RoundToInt(curveLength * settings.segmentsPerUnit);
        segments = Mathf.Max(segments, 1);

        int rings = segments + 1;
        var worldVerts = new Vector3[rings * settings.sides];
        var centers = new Vector3[rings];
        var tangents = new Vector3[rings];
        var rFrames = new Vector3[rings];
        var sFrames = new Vector3[rings];

        Vector3 t0 = SampleTangent(0f, p0, p1, p2, p3, p4, p5);
        Vector3 r0 = GetRingReferenceAxis(t0, startRingUp);

        Vector3 r = r0;
        for (int i = 0; i < rings; i++)
        {
            float t = (float)i / segments;
            centers[i] = SampleBezier(t, p0, p1, p2, p3, p4, p5);
            tangents[i] = SampleTangent(t, p0, p1, p2, p3, p4, p5);

            if (i > 0)
                r = (r - Vector3.Dot(r, tangents[i]) * tangents[i]).normalized;

            rFrames[i] = r;
            sFrames[i] = Vector3.Cross(tangents[i], r).normalized;
        }

        Vector3 tangentEnd = tangents[segments];
        Vector3 rEndTarget = GetRingReferenceAxis(tangentEnd, endRingUp);
        float twistAtEnd = SignedAngleAroundAxis(rFrames[segments], rEndTarget, tangentEnd);

        float ringAngleOffset = Mathf.PI / settings.sides;

        for (int i = 0; i < rings; i++)
        {
            float twistCorrection = twistAtEnd * (i / (float)segments);
            Vector3 rCorrected = rFrames[i];
            Vector3 sCorrected = sFrames[i];
            RotateFrameAroundAxis(tangents[i], twistCorrection, ref rCorrected, ref sCorrected);

            for (int j = 0; j < settings.sides; j++)
            {
                float angle = (float)j / settings.sides * Mathf.PI * 2f + ringAngleOffset;
                Vector3 outward = (Mathf.Cos(angle) * rCorrected + Mathf.Sin(angle) * sCorrected).normalized;

                worldVerts[i * settings.sides + j] = centers[i] + outward * settings.radius;
            }
        }

        int faceCount = segments * settings.sides;
        var verts = new Vector3[faceCount * 4];
        var normals = new Vector3[faceCount * 4];
        var tris = new int[faceCount * 6];

        int faceIndex = 0;
        for (int i = 0; i < segments; i++)
        for (int j = 0; j < settings.sides; j++)
        {
            Vector3 w0 = worldVerts[i * settings.sides + j];
            Vector3 w1 = worldVerts[i * settings.sides + (j + 1) % settings.sides];
            Vector3 w2 = worldVerts[(i + 1) * settings.sides + (j + 1) % settings.sides];
            Vector3 w3 = worldVerts[(i + 1) * settings.sides + j];

            Vector3 faceNormal = Vector3.Cross(w1 - w0, w3 - w0).normalized;
            Vector3 faceCenter = (w0 + w1 + w2 + w3) * 0.25f;
            if (Vector3.Dot(faceNormal, faceCenter - centers[i]) < 0f)
                faceNormal = -faceNormal;

            int baseVertex = faceIndex * 4;
            int baseTriangle = faceIndex * 6;

            verts[baseVertex] = pipeTransform.InverseTransformPoint(w0);
            verts[baseVertex + 1] = pipeTransform.InverseTransformPoint(w1);
            verts[baseVertex + 2] = pipeTransform.InverseTransformPoint(w2);
            verts[baseVertex + 3] = pipeTransform.InverseTransformPoint(w3);

            Vector3 localNormal = pipeTransform.InverseTransformDirection(faceNormal);
            normals[baseVertex] = localNormal;
            normals[baseVertex + 1] = localNormal;
            normals[baseVertex + 2] = localNormal;
            normals[baseVertex + 3] = localNormal;

            tris[baseTriangle] = baseVertex;
            tris[baseTriangle + 1] = baseVertex + 1;
            tris[baseTriangle + 2] = baseVertex + 3;
            tris[baseTriangle + 3] = baseVertex + 1;
            tris[baseTriangle + 4] = baseVertex + 2;
            tris[baseTriangle + 5] = baseVertex + 3;

            faceIndex++;
        }

        mesh.Clear();
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = tris;

        if (meshRenderer != null)
        {
            if (materialOverride != null)
                meshRenderer.sharedMaterial = materialOverride;
            else
                meshRenderer.sharedMaterial = curveLength < settings.maxBuildableLenght ? settings.material : settings.material_error;
        }

        return curveLength;
    }

    static void GetClampedTangentLengths(
        PipeToolSettings settings,
        Vector3 startPosition,
        Vector3 startTangent,
        Vector3 endPosition,
        Vector3 endTangent,
        out float startLen,
        out float endLen)
    {
        startLen = startTangent.magnitude;
        endLen = endTangent.magnitude;

        float straightDist = Vector3.Distance(startPosition, endPosition);
        float maxHandleLen = straightDist * settings.maxTangentFraction * 0.5f;

        startLen = Mathf.Min(startLen, maxHandleLen);
        endLen = Mathf.Min(endLen, maxHandleLen);
    }

    static void GetControlPoints(
        PipeToolSettings settings,
        Vector3 startPosition,
        Vector3 startTangent,
        Vector3 endPosition,
        Vector3 endTangent,
        out Vector3 p0,
        out Vector3 p1,
        out Vector3 p2,
        out Vector3 p3,
        out Vector3 p4,
        out Vector3 p5)
    {
        p0 = startPosition;
        p5 = endPosition;

        Vector3 startDir = startTangent.normalized;
        Vector3 endDir = endTangent.normalized;

        GetClampedTangentLengths(settings, startPosition, startTangent, endPosition, endTangent, out float startLen, out float endLen);

        p1 = p0 + startDir * startLen;
        p4 = p5 + endDir * endLen;

        Vector3 chord = p4 - p1;
        Vector3 chordDir = chord.sqrMagnitude > 0.0001f ? chord.normalized : startDir;

        p2 = p1 + chordDir * startLen;
        p3 = p4 - chordDir * endLen;
    }

    static Vector3 SampleBezier(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, Vector3 p5)
    {
        float u = 1 - t;
        return u * u * u * u * u * p0
             + 5 * u * u * u * u * t * p1
             + 10 * u * u * u * t * t * p2
             + 10 * u * u * t * t * t * p3
             + 5 * u * t * t * t * t * p4
             + t * t * t * t * t * p5;
    }

    static Vector3 SampleTangent(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, Vector3 p5)
    {
        float u = 1 - t;
        Vector3 d = 5 * (
            u * u * u * u * (p1 - p0)
            + 4 * u * u * u * t * (p2 - p1)
            + 6 * u * u * t * t * (p3 - p2)
            + 4 * u * t * t * t * (p4 - p3)
            + t * t * t * t * (p5 - p4)
        );
        return d.normalized;
    }

    static Vector3 GetRingReferenceAxis(Vector3 curveTangent, Vector3 ringUp)
    {
        Vector3 r = Vector3.Cross(curveTangent, ringUp);
        if (r.sqrMagnitude < 0.001f)
            r = Vector3.Cross(curveTangent, Vector3.right);
        return r.normalized;
    }

    static float SignedAngleAroundAxis(Vector3 from, Vector3 to, Vector3 axis)
    {
        return Mathf.Atan2(Vector3.Dot(Vector3.Cross(from, to), axis), Vector3.Dot(from, to));
    }

    static void RotateFrameAroundAxis(Vector3 axis, float angle, ref Vector3 r, ref Vector3 s)
    {
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        Vector3 rRotated = cos * r + sin * s;
        Vector3 sRotated = -sin * r + cos * s;
        r = rRotated;
        s = sRotated;
    }

    static float EstimateCurveLength(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, Vector3 p5)
    {
        const int steps = 10;
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
}
