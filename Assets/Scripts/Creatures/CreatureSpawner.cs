using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns creature prefabs across a spherical planet walk surface.
/// Place one spawner per planet (or per spawn config). Assign any planet + any prefabs.
/// Combat / AI is out of scope — this only handles placement and orientation.
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

    readonly List<GameObject> _spawned = new();
    readonly List<Vector3> _acceptedDirs = new();
    PlanetTileMap _tiles;

    public SphericalPlanet Planet => planet;
    public IReadOnlyList<GameObject> Spawned => _spawned;
    public int SpawnedCount => _spawned.Count;

    void OnValidate()
    {
        if (spawnEntries == null)
            spawnEntries = Array.Empty<SpawnEntry>();

        for (int i = 0; i < spawnEntries.Length; i++)
        {
            SpawnEntry entry = spawnEntries[i];
            entry.count = Mathf.Max(0, entry.count);
            spawnEntries[i] = entry;
        }
    }

    void Start()
    {
        if (spawnOnStart)
            SpawnAll();
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
                        $"(total {_spawned.Count}/{totalRequested}) — spacing or walkable filter blocked more.",
                        this);
                    break;
                }

                if (!TryGetSurfacePose(dir, out Vector3 position, out Quaternion rotation))
                    continue;

                GameObject creature = Instantiate(entry.prefab, position, rotation, spawnRoot);
                creature.name = $"{entry.prefab.name}_{_spawned.Count:00}";
                ApplyInitialAnimatorState(creature);
                _spawned.Add(creature);
                _acceptedDirs.Add(dir);
                placed++;
            }
        }
    }

    [ContextMenu("Clear Spawned")]
    public void ClearSpawned()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] == null)
                continue;

            if (Application.isPlaying)
                Destroy(_spawned[i]);
            else
                DestroyImmediate(_spawned[i]);
        }

        _spawned.Clear();
        _acceptedDirs.Clear();
    }

    public void SetPlanet(SphericalPlanet target)
    {
        planet = target;
        _tiles = planet != null ? planet.GetComponent<PlanetTileMap>() : null;
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
