using UnityEngine;

/// <summary>
/// Plays a particle system every <see cref="interval"/> seconds, but only once the spider has arrived at its
/// graph target (within SpiderGraphMover's arrive tolerance). Plays immediately on arrival, then repeats.
/// The particle system should have Looping and Play On Awake off, so each Play() is one burst.
/// </summary>
[RequireComponent(typeof(SpiderGraphMover))]
public class SpiderArrivalEffect : MonoBehaviour
{
    [Tooltip("Defaults to the first ParticleSystem found under this object.")]
    [SerializeField] private ParticleSystem particles;
    [Tooltip("Seconds between plays while arrived.")]
    [SerializeField, Min(0.1f)] private float interval = 3f;

    private SpiderGraphMover _mover;
    private bool _wasArrived;
    private float _nextPlayTime;

    private void Awake()
    {
        _mover = GetComponent<SpiderGraphMover>();
        if (particles == null)
            particles = GetComponentInChildren<ParticleSystem>(true);

        if (particles == null)
        {
            Debug.LogWarning("[SpiderArrivalEffect] No ParticleSystem found under this object.", this);
            enabled = false;
            return;
        }

        // Don't let Play On Awake fire it before arrival.
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Update()
    {
        if (!_mover.HasArrived)
        {
            _wasArrived = false;
            return;
        }

        if (!_wasArrived)
        {
            _wasArrived = true;
            _nextPlayTime = Time.time;
        }

        if (Time.time < _nextPlayTime) return;

        particles.Play();
        _nextPlayTime = Time.time + interval;
    }
}
