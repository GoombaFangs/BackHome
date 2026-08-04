using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns creature prefabs across a spherical planet walk surface.
/// Place one spawner per planet (or per spawn config). Assign any planet + any prefabs.
/// When a spawned creature dies, it is re-queued individually after that entry's respawn time
/// (FIFO by death order — never batch-respawns several at once).
/// </summary>
[DefaultExecutionOrder(50)]
public class CreatureSpawner : MonoBehaviour
{
    [Serializable]
    public struct SpawnEntry
    {
        [Tooltip("Creature prefab to instantiate (e.g. Grimling).")]
        public GameObject prefab;

        [Min(0)]
        public int count;

        [Tooltip("Seconds to wait after this creature dies before respawning it.")]
        [Min(0f)]
        public float respawnTime;

        [Tooltip("Optional world loot prefab dropped when this creature dies.")]
        public GameObject lootDropPrefab;
    }

    struct TrackedCreature
    {
        public GameObject instance;
        public Creature creature;
        public int entryIndex;
        public Vector3 spawnDir;
    }

    struct PendingRespawn
    {
        public int entryIndex;
        public Vector3 spawnDir;
        public float readyAt;
    }

    [Header("Planet")]
    [Tooltip("Planet to spawn on. Leave empty to use SphericalPlanet.Instance / first in scene.")]
    [SerializeField] SphericalPlanet planet;

    [Header("Creatures")]
    [Tooltip("One row per creature type. Same spawner can mix multiple prefabs.")]
    [SerializeField] SpawnEntry[] spawnEntries = Array.Empty<SpawnEntry>();

    [Header("Timing")]
    [SerializeField] bool spawnOnStart = true;

    [Header("Presentation")]
    [Tooltip("Optional Animator state to force on spawn (e.g. idle). Leave empty to leave Animator alone.")]
    [SerializeField] string initialAnimatorState = "idle";
    [Tooltip("Parent for spawned instances. Leave empty to create a child named Creatures.")]
    [SerializeField] Transform spawnRoot;

    // Internal placement defaults — not exposed in the Inspector.
    const float FootOffset = 0.05f;
    const float GroundProbeDistance = 12f;
    const float MinSeparationDegrees = 12f;
    const int MaxPlacementAttempts = 48;
    const bool OnlyWalkableTiles = true;
    const bool RandomYaw = true;
    static readonly LayerMask GroundLayer = 1 << 3; // Ground

    readonly List<TrackedCreature> _tracked = new();
    readonly List<Vector3> _acceptedDirs = new();
    readonly List<PendingRespawn> _pendingRespawns = new();
    LootDropPool _lootPool;

    public SphericalPlanet Planet => planet;
    public int SpawnedCount => _tracked.Count;
    public int PendingRespawnCount => _pendingRespawns.Count;

    void OnValidate()
    {
        if (spawnEntries == null)
            spawnEntries = Array.Empty<SpawnEntry>();

        for (int i = 0; i < spawnEntries.Length; i++)
        {
            SpawnEntry entry = spawnEntries[i];
            entry.count = Mathf.Max(0, entry.count);
            entry.respawnTime = Mathf.Max(0f, entry.respawnTime);
            spawnEntries[i] = entry;
        }
    }

    void Start()
    {
        if (spawnOnStart)
            SpawnAll();
    }

    void Update()
    {
        if (_pendingRespawns.Count == 0)
            return;

        // One respawn per frame, in death order, only when that creature's timer is ready.
        PendingRespawn next = _pendingRespawns[0];
        if (Time.time < next.readyAt)
            return;

        _pendingRespawns.RemoveAt(0);
        TryRespawn(next);
    }

    void OnDestroy()
    {
        UnsubscribeAll();
        _pendingRespawns.Clear();
    }

    [ContextMenu("Spawn All")]
    public void SpawnAll()
    {
        ClearSpawned();

        if (!TryResolvePlanet())
        {
            Debug.LogWarning($"{name}: no SphericalPlanet assigned or found.", this);
            return;
        }

        int totalRequested = GetTotalRequestedCount();
        if (totalRequested <= 0)
        {
            Debug.LogWarning($"{name}: no spawn entries with count > 0.", this);
            return;
        }

        _tiles?.EnsureWalkColliders();
        EnsureSpawnRoot();
        _acceptedDirs.Clear();

        float minDot = MinSeparationDegrees > 0.01f
            ? Mathf.Cos(MinSeparationDegrees * Mathf.Deg2Rad)
            : -1f;

        for (int e = 0; e < spawnEntries.Length; e++)
        {
            SpawnEntry entry = spawnEntries[e];
            if (entry.prefab == null || entry.count <= 0)
                continue;

            int placed = 0;
            for (int i = 0; i < entry.count; i++)
            {
                if (!TryPickSpawnDirection(minDot, out Vector3 dir))
                {
                    Debug.LogWarning(
                        $"{name}: placed {placed}/{entry.count} of '{entry.prefab.name}' " +
                        $"(total {_tracked.Count}/{totalRequested}) — spacing or walkable filter blocked more.",
                        this);
                    break;
                }

                if (!TrySpawnAt(e, dir))
                    continue;

                placed++;
            }
        }
    }

    [ContextMenu("Clear Spawned")]
    public void ClearSpawned()
    {
        UnsubscribeAll();
        _pendingRespawns.Clear();

        for (int i = 0; i < _tracked.Count; i++)
        {
            GameObject instance = _tracked[i].instance;
            if (instance == null)
                continue;

            if (Application.isPlaying)
                Destroy(instance);
            else
                DestroyImmediate(instance);
        }

        _tracked.Clear();
        _acceptedDirs.Clear();
    }

    public void SetPlanet(SphericalPlanet target)
    {
        planet = target;
        _tiles = planet != null ? planet.GetComponent<PlanetTileMap>() : null;
    }

    void TryRespawn(PendingRespawn pending)
    {
        if (spawnEntries == null
            || pending.entryIndex < 0
            || pending.entryIndex >= spawnEntries.Length)
            return;

        SpawnEntry entry = spawnEntries[pending.entryIndex];
        if (entry.prefab == null)
            return;

        if (!TryResolvePlanet())
            return;

        EnsureSpawnRoot();
        _tiles?.EnsureWalkColliders();

        Vector3 dir = pending.spawnDir.sqrMagnitude > 0.0001f
            ? pending.spawnDir.normalized
            : UnityEngine.Random.onUnitSphere;

        if (!TrySpawnAt(pending.entryIndex, dir))
        {
            // Placement failed — retry soon, keep this slot at the front of the queue.
            pending.readyAt = Time.time + 0.5f;
            _pendingRespawns.Insert(0, pending);
        }
    }

    bool TrySpawnAt(int entryIndex, Vector3 dir)
    {
        SpawnEntry entry = spawnEntries[entryIndex];
        if (entry.prefab == null)
            return false;

        if (!TryGetSurfacePose(dir, out Vector3 position, out Quaternion rotation))
            return false;

        GameObject creature = Instantiate(entry.prefab, position, rotation, spawnRoot);
        creature.name = $"{entry.prefab.name}_{_tracked.Count:00}";
        ApplyInitialAnimatorState(creature);

        Creature creatureComp = creature.GetComponent<Creature>();
        if (creatureComp == null)
            creatureComp = creature.GetComponentInChildren<Creature>();

        var tracked = new TrackedCreature
        {
            instance = creature,
            creature = creatureComp,
            entryIndex = entryIndex,
            spawnDir = dir.normalized
        };

        if (creatureComp != null)
            creatureComp.Died += OnCreatureDied;

        _tracked.Add(tracked);
        _acceptedDirs.Add(dir.normalized);
        return true;
    }

    void OnCreatureDied(Creature creature)
    {
        if (creature == null)
            return;

        int index = -1;
        for (int i = 0; i < _tracked.Count; i++)
        {
            if (_tracked[i].creature == creature)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return;

        TrackedCreature tracked = _tracked[index];
        creature.Died -= OnCreatureDied;
        _tracked.RemoveAt(index);
        RemoveAcceptedDir(tracked.spawnDir);

        if (spawnEntries == null
            || tracked.entryIndex < 0
            || tracked.entryIndex >= spawnEntries.Length)
            return;

        float delay = Mathf.Max(0f, spawnEntries[tracked.entryIndex].respawnTime);
        _pendingRespawns.Add(new PendingRespawn
        {
            entryIndex = tracked.entryIndex,
            spawnDir = tracked.spawnDir,
            readyAt = Time.time + delay
        });

        TryDropLoot(tracked);
    }

    void TryDropLoot(TrackedCreature tracked)
    {
        SpawnEntry entry = spawnEntries[tracked.entryIndex];
        if (entry.lootDropPrefab == null)
            return;

        Vector3 dropPos = tracked.instance != null
            ? tracked.instance.transform.position
            : transform.position;

        EnsureLootPool();
        _lootPool.Spawn(entry.lootDropPrefab, dropPos);
    }

    void EnsureLootPool()
    {
        if (_lootPool != null)
            return;

        _lootPool = GetComponent<LootDropPool>();
        if (_lootPool == null)
            _lootPool = gameObject.AddComponent<LootDropPool>();
    }

    void RemoveAcceptedDir(Vector3 dir)
    {
        for (int i = 0; i < _acceptedDirs.Count; i++)
        {
            if (Vector3.Dot(_acceptedDirs[i], dir) > 0.999f)
            {
                _acceptedDirs.RemoveAt(i);
                return;
            }
        }
    }

    void UnsubscribeAll()
    {
        for (int i = 0; i < _tracked.Count; i++)
        {
            if (_tracked[i].creature != null)
                _tracked[i].creature.Died -= OnCreatureDied;
        }
    }

    int GetTotalRequestedCount()
    {
        int total = 0;
        if (spawnEntries == null)
            return 0;

        for (int i = 0; i < spawnEntries.Length; i++)
        {
            if (spawnEntries[i].prefab != null)
                total += Mathf.Max(0, spawnEntries[i].count);
        }

        return total;
    }

    PlanetTileMap _tiles;

    bool TryResolvePlanet()
    {
        if (planet == null)
        {
            planet = SphericalPlanet.Instance != null
                ? SphericalPlanet.Instance
                : FindAnyObjectByType<SphericalPlanet>();
        }

        if (planet == null)
        {
            _tiles = null;
            return false;
        }

        _tiles = planet.GetComponent<PlanetTileMap>();
        return true;
    }

    void EnsureSpawnRoot()
    {
        if (spawnRoot != null)
            return;

        Transform existing = transform.Find("Creatures");
        if (existing != null)
        {
            spawnRoot = existing;
            return;
        }

        var root = new GameObject("Creatures");
        root.transform.SetParent(transform, false);
        spawnRoot = root.transform;
    }

    bool TryPickSpawnDirection(float minDot, out Vector3 dir)
    {
        dir = Vector3.up;

        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            Vector3 candidate = UnityEngine.Random.onUnitSphere;
            if (candidate.sqrMagnitude < 0.0001f)
                continue;
            candidate.Normalize();

            if (OnlyWalkableTiles && _tiles != null && _tiles.ProvidesWalkSurface)
            {
                Vector3 probe = GetAnalyticSurfacePoint(candidate);
                if (_tiles.TryGetTile(probe, out PlanetTileMap.TileSample sample) && !sample.walkable)
                    continue;
            }

            bool farEnough = true;
            for (int i = 0; i < _acceptedDirs.Count; i++)
            {
                if (Vector3.Dot(candidate, _acceptedDirs[i]) > minDot)
                {
                    farEnough = false;
                    break;
                }
            }

            if (!farEnough)
                continue;

            dir = candidate;
            return true;
        }

        return false;
    }

    bool TryGetSurfacePose(Vector3 directionFromCenter, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        Vector3 radial = directionFromCenter.normalized;
        _tiles?.EnsureWalkColliders();

        Vector3 up = radial;
        if (!TryStickToCollider(radial, out position, out up))
        {
            position = GetAnalyticSurfacePoint(radial);
            up = _tiles != null && _tiles.ProvidesWalkSurface
                ? _tiles.GetWalkSurfaceNormal(radial)
                : planet.GetTerrainNormal(radial);
        }

        if (Vector3.Dot(up, radial) < 0f)
            up = -up;

        Vector3 forwardHint = RandomYaw ? UnityEngine.Random.onUnitSphere : Vector3.forward;
        Vector3 forward = Vector3.ProjectOnPlane(forwardHint, up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(Vector3.right, up);

        rotation = Quaternion.LookRotation(forward.normalized, up);
        return true;
    }

    Vector3 GetAnalyticSurfacePoint(Vector3 directionFromCenter)
    {
        if (_tiles != null && _tiles.ProvidesWalkSurface)
            return _tiles.GetWalkSurfacePoint(directionFromCenter, FootOffset);

        return planet.GetSurfacePoint(directionFromCenter, FootOffset);
    }

    float GetFallbackSurfaceRadius(Vector3 radial)
    {
        if (_tiles != null && _tiles.ProvidesWalkSurface)
            return _tiles.GetWalkSurfaceRadius(radial);
        return planet.GetTerrainRadius(radial);
    }

    bool TryStickToCollider(Vector3 radial, out Vector3 feetPosition, out Vector3 normal)
    {
        feetPosition = default;
        normal = radial;

        float castStart = GetFallbackSurfaceRadius(radial) + Mathf.Max(4f, GroundProbeDistance * 0.5f);
        Vector3 origin = planet.Center + radial * castStart;
        float maxDist = castStart + 2f;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            -radial,
            maxDist,
            GroundLayer,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        float bestDist = float.MaxValue;
        bool found = false;
        RaycastHit best = default;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;

            if (col.transform != planet.transform && !col.transform.IsChildOf(planet.transform))
                continue;

            if (hits[i].distance < bestDist)
            {
                bestDist = hits[i].distance;
                best = hits[i];
                found = true;
            }
        }

        if (!found)
            return false;

        normal = best.normal.sqrMagnitude > 0.001f ? best.normal.normalized : radial;
        if (Vector3.Dot(normal, radial) < 0f)
            normal = -normal;

        feetPosition = best.point + normal * FootOffset;
        return true;
    }

    void ApplyInitialAnimatorState(GameObject creature)
    {
        if (string.IsNullOrWhiteSpace(initialAnimatorState))
            return;

        Animator animator = creature.GetComponentInChildren<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        animator.Play(initialAnimatorState, 0, 0f);
        animator.Update(0f);
    }
}
