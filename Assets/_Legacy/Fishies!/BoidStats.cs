using UnityEngine;

[CreateAssetMenu(fileName = "BoidStats", menuName = "Own/BoidStats")]
public class BoidStats : ScriptableObject
{
    public float simulationSpeed;

    public float speed;
    public float maxAcceleration;
    public float cohesion;
    public float separation;
    public float alignment;
    public float objectAvoidance;
    public float boundaryPadding;
    public float neighborRadius;



}
