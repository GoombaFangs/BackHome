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

        [Tooltip("Minimum spacing (degrees) enforced between spawned creatures of this entry. " +
            "0 (default) = use the spawner-wide default (12°). Lower values (e.g. 2-3°) allow a " +
            "tightly packed, much denser cluster — handy for a spawn-point \"den\" of many creatures.")]
        [Min(0f)]
        public float minSeparationDegrees;
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

    [Serializable]
    public struct SpawnPoint
    {
        [Tooltip("Marker Transform placed on the planet surface (e.g. an empty/mesh named 'Spawn Point'). " +
            "Creatures below spawn/respawn clustered tightly around it and are parented under this " +
            "Transform in the hierarchy for easy tracking.")]
        public Transform anchor;

        [Tooltip("Max distance (world units) from the anchor creatures can land. Small = a tight, dense cluster.")]
        [Min(0.1f)]
        public float radius;

        [Tooltip("Creatures confined to this spawn point, additive to spawnEntries.")]
        public SpawnEntry[] creatures;
    }

    /// <summary>One combined-list entry: a global row from <see cref="spawnEntries"/> (unrestricted,
    /// spawnPointIndex -1) or a row from a specific <see cref="SpawnPoint"/> (confined to a small
    /// radius around a hand-placed anchor).</summary>
    struct ResolvedEntry
    {
        public SpawnEntry entry;
        public int spawnPointIndex;
    }

    [Header("Planet")]
    [Tooltip("Planet to spawn on. Leave empty to use SphericalPlanet.Instance / first in scene.")]
    [SerializeField] SphericalPlanet planet;

    [Header("Creatures")]
    [Tooltip("One row per creature type. Same spawner can mix multiple prefabs. Spawned/respawned anywhere on the planet (no region restriction).")]
    [SerializeField] SpawnEntry[] spawnEntries = Array.Empty<SpawnEntry>();

    [Header("Spawn Points")]
    [Tooltip("Optional — hand-placed anchor markers where a specific creature list spawns confined to a small radius (a precise, dense \"den\"), additive to spawnEntries.")]
    [SerializeField] SpawnPoint[] spawnPoints = Array.Empty<SpawnPoint>();

    [Header("Timing")]
    [SerializeField] bool spawnOnStart = true;

    [Header("Presentation")]
    [Tooltip("Optional Animator state to force on spawn (e.g. idle). Leave empty to leave Animator alone.")]
    [SerializeField] string initialAnimatorState = "idle";
    [Tooltip("Parent for spawned instances. Leave empty to create a child named Creatures.")]
    [SerializeField] Transform spawnRoot;

    [Header("Loot")]
    [Tooltip("Pooled world drops on creature death. Defaults to LootDropPool on spawnRoot (Creatures).")]
    [SerializeField] LootDropPool lootPool;

    // Internal placement defaults — not exposed in the Inspector.
    const float FootOffset = 0.05f;
    const float GroundProbeDistance = 12f;
    const float MinSeparationDegrees = 12f;
    const int MaxPlacementAttempts = 200;
    const bool OnlyWalkableTiles = true;
    static readonly LayerMask GroundLayer = 1 << 3; // Ground

    readonly List<TrackedCreature> _tracked = new();
    readonly List<Vector3> _acceptedDirs = new();
    readonly List<PendingRespawn> _pendingRespawns = new();
    readonly List<ResolvedEntry> _allEntries = new();
    System.Random _spawnRng;
    LootDropPool _lootPool;

    void OnValidate()
    {
        if (spawnEntries == null)
            spawnEntries = Array.Empty<SpawnEntry>();

        for (int i = 0; i < spawnEntries.Length; i++)
            spawnEntries[i] = ClampEntry(spawnEntries[i]);

        if (spawnPoints == null)
            spawnPoints = Array.Empty<SpawnPoint>();

        for (int p = 0; p < spawnPoints.Length; p++)
        {
            SpawnPoint point = spawnPoints[p];
            point.radius = Mathf.Max(0.1f, point.radius);
            if (point.creatures != null)
            {
                for (int i = 0; i < point.creatures.Length; i++)
                    point.creatures[i] = ClampEntry(point.creatures[i]);
            }

            spawnPoints[p] = point;
        }

        if (lootPool == null && spawnRoot != null)
            lootPool = spawnRoot.GetComponent<LootDropPool>();
    }

    static SpawnEntry ClampEntry(SpawnEntry entry)
    {
        entry.count = Mathf.Max(0, entry.count);
        entry.respawnTime = Mathf.Max(0f, entry.respawnTime);
        entry.minSeparationDegrees = Mathf.Max(0f, entry.minSeparationDegrees);
        return entry;
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

        BuildAllEntries();

        int totalRequested = GetTotalRequestedCount();
        if (totalRequested <= 0)
        {
            Debug.LogWarning($"{name}: no spawn entries with count > 0.", this);
            return;
        }

        _tiles?.EnsureWalkColliders();
        EnsureSpawnRoot();
        _acceptedDirs.Clear();

        for (int e = 0; e < _allEntries.Count; e++)
        {
            SpawnEntry entry = _allEntries[e].entry;
            int spawnPointIndex = _allEntries[e].spawnPointIndex;
            if (entry.prefab == null || entry.count <= 0)
                continue;

            float minDot = ComputeMinDot(ResolveMinSeparationDegrees(entry));

            int placed = 0;
            for (int i = 0; i < entry.count; i++)
            {
                if (!TryPickSpawnDirection(minDot, spawnPointIndex, out Vector3 dir))
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

    /// <summary>Rebuilds the combined spawn-entry list: global <see cref="spawnEntries"/> rows
    /// (unrestricted) and every <see cref="spawnPoints"/> row (if any). All additive.</summary>
    void BuildAllEntries()
    {
        _allEntries.Clear();

        if (spawnEntries != null)
        {
            for (int i = 0; i < spawnEntries.Length; i++)
                _allEntries.Add(new ResolvedEntry { entry = spawnEntries[i], spawnPointIndex = -1 });
        }

        if (spawnPoints != null)
        {
            for (int p = 0; p < spawnPoints.Length; p++)
            {
                SpawnEntry[] creatures = spawnPoints[p].creatures;
                if (creatures == null)
                    continue;

                for (int i = 0; i < creatures.Length; i++)
                    _allEntries.Add(new ResolvedEntry { entry = creatures[i], spawnPointIndex = p });
            }
        }
    }

    static float ComputeMinDot(float degrees) => degrees > 0.01f
        ? Mathf.Cos(degrees * Mathf.Deg2Rad)
        : -1f;

    /// <summary>0 (unset) falls back to the spawner-wide default; a positive override lets a
    /// specific entry (e.g. a dense spawn-point cluster) pack much tighter than everything else.</summary>
    static float ResolveMinSeparationDegrees(SpawnEntry entry) =>
        entry.minSeparationDegrees > 0.01f ? entry.minSeparationDegrees : MinSeparationDegrees;

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

    void TryRespawn(PendingRespawn pending)
    {
        if (pending.entryIndex < 0 || pending.entryIndex >= _allEntries.Count)
            return;

        ResolvedEntry resolved = _allEntries[pending.entryIndex];
        if (resolved.entry.prefab == null)
            return;

        if (!TryResolvePlanet())
            return;

        EnsureSpawnRoot();
        _tiles?.EnsureWalkColliders();

        Vector3 dir;
        if (resolved.spawnPointIndex >= 0)
        {
            // Spawn-point-confined creature — re-roll a fresh point around the anchor rather than
            // reusing the exact death position, still respecting spacing/walkability below.
            float minDot = ComputeMinDot(ResolveMinSeparationDegrees(resolved.entry));
            if (!TryPickSpawnDirection(minDot, resolved.spawnPointIndex, out dir))
            {
                pending.readyAt = Time.time + 0.5f;
                _pendingRespawns.Insert(0, pending);
                return;
            }
        }
        else
        {
            dir = pending.spawnDir.sqrMagnitude > 0.0001f
                ? pending.spawnDir.normalized
                : UnityEngine.Random.onUnitSphere;
        }

        if (!TrySpawnAt(pending.entryIndex, dir))
        {
            // Placement failed — retry soon, keep this slot at the front of the queue.
            pending.readyAt = Time.time + 0.5f;
            _pendingRespawns.Insert(0, pending);
        }
    }

    bool TrySpawnAt(int entryIndex, Vector3 dir)
    {
        ResolvedEntry resolved = _allEntries[entryIndex];
        SpawnEntry entry = resolved.entry;
        if (entry.prefab == null)
            return false;

        if (!TryGetSurfacePose(dir, out Vector3 position, out Quaternion rotation))
            return false;

        Transform parent = ResolveSpawnParent(resolved);
        GameObject creature = Instantiate(entry.prefab, position, rotation, parent);
        int localIndex = parent != null ? parent.childCount - 1 : _tracked.Count;
        creature.name = $"{entry.prefab.name}_{localIndex:00}";
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

        if (tracked.entryIndex < 0 || tracked.entryIndex >= _allEntries.Count)
            return;

        float delay = Mathf.Max(0f, _allEntries[tracked.entryIndex].entry.respawnTime);
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
        Creature creature = tracked.creature;
        if (creature == null || !creature.HasStats)
            return;

        CreatureStats stats = creature.Stats;
        LootEntry[] table = stats.Loot;
        if (table == null || table.Length == 0)
            return;

        Vector3 dropPos = tracked.instance != null
            ? tracked.instance.transform.position
            : transform.position;

        EnsureLootPool();
        for (int i = 0; i < table.Length; i++)
        {
            if (!stats.TryRollLoot(i, out ItemDefinition item, out int amount))
                continue;

            _lootPool.Spawn(item, amount, dropPos);
        }
    }

    void EnsureLootPool()
    {
        if (_lootPool != null)
            return;

        if (lootPool != null)
        {
            _lootPool = lootPool;
            return;
        }

        EnsureSpawnRoot();
        if (spawnRoot != null)
        {
            _lootPool = spawnRoot.GetComponent<LootDropPool>();
            if (_lootPool == null)
                _lootPool = spawnRoot.gameObject.AddComponent<LootDropPool>();
            lootPool = _lootPool;
            return;
        }

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
        for (int i = 0; i < _allEntries.Count; i++)
        {
            if (_allEntries[i].entry.prefab != null)
                total += Mathf.Max(0, _allEntries[i].entry.count);
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

    /// <summary>Spawn-point creatures parent under their anchor; everything else uses <see cref="spawnRoot"/>.</summary>
    Transform ResolveSpawnParent(ResolvedEntry resolved)
    {
        if (resolved.spawnPointIndex >= 0
            && spawnPoints != null
            && resolved.spawnPointIndex < spawnPoints.Length
            && spawnPoints[resolved.spawnPointIndex].anchor != null)
        {
            return spawnPoints[resolved.spawnPointIndex].anchor;
        }

        EnsureSpawnRoot();
        return spawnRoot;
    }

    /// <summary>Picks a spawn direction satisfying spacing/walkability, optionally confined to a
    /// hand-placed spawn point. <paramref name="spawnPointIndex"/> &gt;= 0 samples only within that
    /// <see cref="spawnPoints"/> entry's radius around its anchor; &lt; 0 samples the whole sphere.</summary>
    bool TryPickSpawnDirection(float minDot, int spawnPointIndex, out Vector3 dir)
    {
        dir = Vector3.up;

        bool useSpawnPoint = spawnPointIndex >= 0 && spawnPoints != null && spawnPointIndex < spawnPoints.Length;
        if (useSpawnPoint)
            _spawnRng ??= new System.Random();

        SpawnPoint spawnPoint = useSpawnPoint ? spawnPoints[spawnPointIndex] : default;
        if (useSpawnPoint && spawnPoint.anchor == null)
            useSpawnPoint = false;

        for (int attempt = 0; attempt < MaxPlacementAttempts; attempt++)
        {
            Vector3 candidate;
            if (useSpawnPoint)
            {
                if (!TryGetRandomPointNearAnchor(spawnPoint.anchor, spawnPoint.radius, out candidate))
                    continue;
            }
            else
            {
                candidate = UnityEngine.Random.onUnitSphere;
            }

            if (candidate.sqrMagnitude < 0.0001f)
                continue;
            candidate.Normalize();

            if (_tiles != null && _tiles.ProvidesWalkSurface)
            {
                Vector3 probe = GetAnalyticSurfacePoint(candidate);
                if (_tiles.TryGetTile(probe, out PlanetTileMap.TileSample sample))
                {
                    if (OnlyWalkableTiles && !sample.walkable)
                        continue;
                    // Shadow Grass is a walkable path tile — keep it clear of creatures so it
                    // reads as a clean trail through the environment.
                    if (PlanetTileset.IsShadowGrassZone(sample.zoneId))
                        continue;
                }
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

    /// <summary>Samples a direction within <paramref name="worldRadius"/> world units of
    /// <paramref name="anchor"/>'s position (projected onto the planet), area-uniformly across the
    /// disk.</summary>
    bool TryGetRandomPointNearAnchor(Transform anchor, float worldRadius, out Vector3 dir)
    {
        dir = Vector3.up;
        if (anchor == null || planet == null)
            return false;

        Vector3 anchorUp = anchor.position - planet.Center;
        if (anchorUp.sqrMagnitude < 0.0001f)
            return false;
        anchorUp.Normalize();

        _spawnRng ??= new System.Random();
        float maxAngleDeg = Mathf.Asin(Mathf.Clamp01(worldRadius / Mathf.Max(0.01f, planet.Radius))) * Mathf.Rad2Deg;
        float angle = Mathf.Sqrt((float)_spawnRng.NextDouble()) * maxAngleDeg; // sqrt => uniform density across the disk area.
        float spin = (float)_spawnRng.NextDouble() * 360f;
        dir = JitterDirection(anchorUp, angle, spin);
        return true;
    }

    static Vector3 JitterDirection(Vector3 dir, float angleDegrees, float spinDegrees)
    {
        Vector3 axis = Vector3.Cross(dir, Vector3.up);
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.Cross(dir, Vector3.right);
        axis = (Quaternion.AngleAxis(spinDegrees, dir) * axis).normalized;
        return (Quaternion.AngleAxis(angleDegrees, axis) * dir).normalized;
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

        Vector3 forwardHint = UnityEngine.Random.onUnitSphere;
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
