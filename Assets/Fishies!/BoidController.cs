using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BoidController : MonoBehaviour
{
    public BoidStats boidStats;
    public GameObject boidPrefab;
    public int boidCount;
    public Vector3 boidAreaMin, boidAreaMax;


    public int averageGroupSize;
    public float initialGroupRadius;

    Transform[] boidT;
    Boid[] boidC;                 // cache components
    int[] neighborsIdx;           // scratch buffer (indices)
    // optional: per-frame neighbor bools to avoid list allocs
    bool[] isNeighbor;

    void Start()
    {
        boidT = new Transform[boidCount];
        boidC = new Boid[boidCount];
        neighborsIdx = new int[boidCount];
        isNeighbor = new bool[boidCount];

        for (int i = 0; i < boidCount; i++) {
            var go = Instantiate(boidPrefab, transform);
            boidT[i] = go.transform;
            boidC[i] = go.GetComponent<Boid>();
        }
        Restart();
    }

    void Update()
    {
        float dt = Time.deltaTime * boidStats.simulationSpeed;
        float r2 = boidStats.neighborRadius * boidStats.neighborRadius;

        for (int i = 0; i < boidCount; i++)
        {
            Vector3 p = boidT[i].position;

            // walls (no normalization, no allocs)
            Vector3 walls = ComputeWallForce(p, boidAreaMin, boidAreaMax, boidStats.boundaryPadding);

            // neighbors (reuse buffers)
            int nCount = 0;
            for (int j = 0; j < boidCount; j++) {
                if (i == j) continue;
                Vector3 d = boidT[j].position - p;
                if (d.sqrMagnitude < r2) neighborsIdx[nCount++] = j;
            }

            if (nCount == 0) {
                Vector3 uA = boidStats.objectAvoidance * walls;
                Vector3 vA = Vector3.ClampMagnitude(boidC[i].velocity + uA * dt, boidStats.speed * boidStats.simulationSpeed);
                boidC[i].velocity = vA;
                boidT[i].position += vA * dt;
                continue;
            }

            // cohesion
            Vector3 com = Vector3.zero;
            for (int k = 0; k < nCount; k++) com += boidT[neighborsIdx[k]].position;
            com /= nCount;
            Vector3 cohesion = (com - p).normalized;

            // alignment (use cached velocities)
            Vector3 avgV = Vector3.zero;
            for (int k = 0; k < nCount; k++) avgV += boidC[neighborsIdx[k]].velocity;
            avgV /= nCount;
            Vector3 alignment = avgV; // steering term is fine as-is in your setup

            // separation
            Vector3 separation = Vector3.zero;
            const float eps = 1e-4f;
            for (int k = 0; k < nCount; k++) {
                Vector3 d = p - boidT[neighborsIdx[k]].position;
                separation += d / (d.sqrMagnitude + eps);
            }
            separation /= nCount;

            // integrate
            Vector3 u = boidStats.cohesion * cohesion +
                        boidStats.alignment * alignment +
                        boidStats.separation * separation +
                        boidStats.objectAvoidance * walls;

            u = Vector3.ClampMagnitude(u, boidStats.maxAcceleration * boidStats.simulationSpeed);
            Vector3 v = Vector3.ClampMagnitude(boidC[i].velocity + u * dt, boidStats.speed * boidStats.simulationSpeed);
            boidC[i].velocity = v;
            boidT[i].position += v * dt;
        }
    }

    static Vector3 ComputeWallForce(Vector3 p, Vector3 min, Vector3 max, float pad)
    {
        Vector3 w = Vector3.zero;
        if (p.x < min.x + pad) w.x += (min.x + pad - p.x) / pad;
        if (p.x > max.x - pad) w.x -= (p.x - (max.x - pad)) / pad;
        if (p.y < min.y + pad) w.y += (min.y + pad - p.y) / pad;
        if (p.y > max.y - pad) w.y -= (p.y - (max.y - pad)) / pad;
        if (p.z < min.z + pad) w.z += (min.z + pad - p.z) / pad;
        if (p.z > max.z - pad) w.z -= (p.z - (max.z - pad)) / pad;
        return Vector3.ClampMagnitude(w, 1f);
    }

    void Restart()
    {
        // Start by getting group sizes 
        int[] groups = new int[boidCount];
        int currentGroup = 0;
        for (int i = 0; i < boidCount; i++)
        {
            groups[i] = currentGroup;
            if (Random.Range(0.0f, 1.0f) < 1.0f/averageGroupSize)
            {
                currentGroup++;
            }
        }
        int totalGroups = currentGroup + 1; // Because we have 0 based indexing and use group for length.
        print("totalGroups "+totalGroups);

        int[] groupSizes = new int[totalGroups];
        for (int i = 0; i < boidCount; i++)
        {
            groupSizes[groups[i]]++;
        }

        // Get group positions
        Vector3[] groupPositions = new Vector3[totalGroups];
        float[] groupRadii = new float[totalGroups];
        Vector3[] groupVelocities = new Vector3[totalGroups];
        for (int i = 0; i < totalGroups; i++)
        {
            groupPositions[i] = GetRandomPosition();
            groupRadii[i] = groupSizes[i]/((float)averageGroupSize) * initialGroupRadius;
            print("groupRadii[i] "+groupRadii[i] + " groupSizes[i] "+groupSizes[i] + " averageGroupSize "+averageGroupSize + " initialGroupRadius "+initialGroupRadius);
            groupVelocities[i] = boidStats.simulationSpeed * boidStats.speed * Random.Range(0.5f, 1.0f) * Random.insideUnitSphere;
        }

        // Assign boids to groups
        for (int i = 0; i < boidCount; i++)
        {
            Transform boid = boidT[i];
            boid.position = groupPositions[groups[i]] + Random.insideUnitSphere*groupRadii[groups[i]];
            print("boid.position "+boid.position + " groupPositions[groups[i]] "+groupPositions[groups[i]] + " groupRadii[groups[i]] "+groupRadii[groups[i]]);
            boid.GetComponent<Boid>().velocity = groupVelocities[groups[i]];
        }

        // for (int i = 0; i < boidCount; i++)
        // {
        //     Transform boid = boidT[i];
        //     boid.position = GetRandomPosition();
        //     // Set random velocity
        //     Vector3 dir = Random.onUnitSphere;
        //     boidTransforms[i].GetComponent<Boid>().velocity = dir*boidStats.speed;
        // }
    }
    Vector3 GetRandomPosition()
    {
        return new Vector3(Random.Range(boidAreaMin.x, boidAreaMax.x), Random.Range(boidAreaMin.y, boidAreaMax.y), Random.Range(boidAreaMin.z, boidAreaMax.z));
    }
}
