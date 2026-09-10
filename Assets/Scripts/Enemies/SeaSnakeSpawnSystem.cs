using System.Collections.Generic;
using UnityEngine;

public class SeaSnakeSpawnSystem : MonoBehaviour
{
    [SerializeField] private SeaSnakeSpawnSettings settings;

    readonly List<GameObject> _spawned = new();

    AutomationLogicSystem _automation;
    Transform _player;
    ChunkManager _chunkManager;
    float _nextSpawnTime;

    public void Init(AutomationLogicSystem automation, Transform player, ChunkManager chunkManager)
    {
        _automation = automation;
        _player = player;
        _chunkManager = chunkManager;
        ScheduleNextSpawn();
    }

    public void Tick()
    {
        if (settings == null || settings.prefab == null || _player == null || _chunkManager == null)
            return;

        if (_automation == null || !_automation.IsMiningActive)
            return;

        if (Time.time < _nextSpawnTime)
            return;

        ScheduleNextSpawn();
        DespawnOutOfRange();

        if (_spawned.Count >= settings.maxSpawned)
            return;

        if (TryFindSpawnPosition(out Vector3 spawnPos))
            SpawnSeaSnake(spawnPos);
    }

    void ScheduleNextSpawn()
    {
        float interval = settings != null ? settings.spawnInterval : 8f;
        _nextSpawnTime = Time.time + interval;
    }

    void DespawnOutOfRange()
    {
        float maxRangeSq = settings.distanceFromPlayer * settings.distanceFromPlayer;
        Vector3 playerPos = _player.position;

        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            GameObject instance = _spawned[i];
            if (instance == null)
            {
                _spawned.RemoveAt(i);
                continue;
            }

            if ((instance.transform.position - playerPos).sqrMagnitude > maxRangeSq)
            {
                Destroy(instance);
                _spawned.RemoveAt(i);
            }
        }
    }

    bool TryFindSpawnPosition(out Vector3 spawnPos)
    {
        spawnPos = default;

        float minDist = settings.distanceFromPlayer;
        float maxDist = settings.distanceFromPlayer;
        int layerMask = settings.wallLayerMask.value != 0
            ? settings.wallLayerMask.value
            : 1 << 6;

        Vector3 origin = _player.position;

        for (int i = 0; i < settings.maxRayAttempts; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            if (dir.sqrMagnitude < 1e-6f)
                continue;
            dir.Normalize();

            if (Physics.Raycast(origin, dir, out RaycastHit hit, maxDist, layerMask))
            {
                if (hit.distance < minDist)
                    continue;

                spawnPos = hit.point;
                return true;
            }

            spawnPos = origin + dir * maxDist;
            return true;
        }

        return false;
    }

    void SpawnSeaSnake(Vector3 spawnPos)
    {
        Vector3 toPlayer = _player.position - spawnPos;
        Quaternion rotation = toPlayer.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(toPlayer.normalized, Vector3.up)
            : Quaternion.identity;

        GameObject instance = Instantiate(settings.prefab, spawnPos, rotation);
        _spawned.Add(instance);

        EnemySnakeState.GetOrCreate(instance.transform);

        RandomSteeredMover mover = instance.GetComponentInChildren<RandomSteeredMover>();
        if (mover != null)
        {
            mover.target = _player;
            mover.Init(_chunkManager, _player);
        }
    }
}
