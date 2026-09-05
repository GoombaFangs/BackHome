using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime tree streaming for spherical planets — same streaming/pooling approach as
/// <see cref="PlanetGrassStreamer"/>, tuned for trees instead of grass: only one tree per tile (no
/// overlapping clumps), density stays far lower since a single tree already reads as "occupied"
/// ground, and colliders/shadows stay enabled by default since trees are large, solid, and
/// visually prominent (unlike grass blades where disabling both is basically free and unnoticed).
///
/// Each walkable tile gets one deterministic roll: empty, or one of the assigned tree variants
/// (Tree1 / Tree2 / Tree3 / Tree4 / Tree5), weighted by their respective sliders. A variant
/// with no prefab assigned is skipped automatically rather than distorting the others' odds.
///
/// Lives on the shared "EnvironmentManager" GameObject alongside the grass/rock streamers, not on the planet
/// itself — keeps the planet hierarchy free of manager components. Assign <see cref="planet"/>
/// explicitly, or leave it empty to auto-resolve via <see cref="SphericalPlanet.Instance"/>. The
/// spawned instances themselves are parented under the planet's "Environment" child instead (see
/// <see cref="PlanetEnvironmentRoot"/>), so streamed trees show up nested under the planet.
///
/// Menu: BackHome → Setup Nyxara Tree Streaming (adds this + wires Tree1..Tree5).
/// </summary>
[DisallowMultipleComponent]
public class PlanetTreeStreamer : MonoBehaviour
{
    const int Tree1Index = 0;
    const int Tree2Index = 1;
    const int Tree3Index = 2;
    const int Tree4Index = 3;
    const int Tree5Index = 4;
    const int PoolCount = 5;

    enum TreePick
    {
        Empty,
        Tree1,
        Tree2,
        Tree3,
        Tree4,
        Tree5,
    }

    [Header("Planet")]
    [Tooltip("Planet to stream trees onto. Leave empty to use SphericalPlanet.Instance / first in scene.")]
    [SerializeField] SphericalPlanet planet;

    [Header("Prefabs")]
    [SerializeField] GameObject tree1Prefab;
    [SerializeField] GameObject tree2Prefab;
    [SerializeField] GameObject tree3Prefab;
    [SerializeField] GameObject tree4Prefab;
    [SerializeField] GameObject tree5Prefab;

    [Header("Streaming Range")]
    [Tooltip("World-space radius around the player kept filled with trees. Push this out past what the camera can actually see (screen edge or planet horizon) so spawn/despawn pop-in happens off-screen instead of in view.")]
    [SerializeField, Min(4f)] float visibleRadius = 48f;
    [Tooltip("Seconds between rescans. Higher = cheaper, lower = trees keep up better with fast movement.")]
    [SerializeField, Min(0.05f)] float refreshInterval = 0.4f;

    [Header("Density")]
    [Tooltip("Master tree amount. 0 = no trees, 1 = a tree on every walkable tile. Trees are large — keep this far lower than grass density.")]
    [SerializeField, Range(0f, 1f)] float density = 0.075f;
    [Tooltip("Relative mix between the five tree variants on tiles that do get a tree (does not affect overall density). A variant with no prefab assigned is skipped automatically.")]
    [SerializeField, Min(0f)] float tree1Weight = 1f;
    [SerializeField, Min(0f)] float tree2Weight = 1f;
    [SerializeField, Min(0f)] float tree3Weight = 1f;
    [SerializeField, Min(0f)] float tree4Weight = 1f;
    [SerializeField, Min(0f)] float tree5Weight = 1f;
    [Tooltip("How far a tree can drift from its tile center.")]
    [SerializeField, Min(0f)] float jitterRadius = 1.2f;
    [SerializeField] float hover = 0.05f;

    [Header("Mobile Safety Cap")]
    [Tooltip("Hard ceiling on concurrently active trees, regardless of density/radius. Protects low-end phones even if the player stands in a dense forest.")]
    [SerializeField, Min(1)] int maxActiveTrees = 110;
    [Tooltip("Max new instances spawned per rescan, spreading instantiation cost across frames.")]
    [SerializeField, Min(1)] int maxSpawnsPerRefresh = 10;
    [Tooltip("Trees are large and prominent — shadows stay on by default, unlike grass.")]
    [SerializeField] bool disableShadows = false;
    [Tooltip("Trees should block movement — colliders stay enabled by default, unlike grass.")]
    [SerializeField] bool disableColliders = false;

    [Header("Anchor")]
    [Tooltip("Optional explicit anchor (defaults to the player, which the follow camera always keeps centered).")]
    [SerializeField] Transform anchorOverride;

    [Header("Regions")]
    [Tooltip("Set automatically by PlanetEnvironmentManager when present. Per-region tree lists live on the region set asset.")]
    [SerializeField] PlanetEnvironmentRegionSet regionSet;

    bool _rootCreated;

    SphericalPlanet _planet;
    PlanetTileMap _tiles;
    Transform _root;
    Transform _cachedAnchor;

    readonly Dictionary<int, ActiveTree> _active = new();
    readonly HashSet<int> _desired = new();
    readonly List<int> _toDespawn = new();
    readonly List<CellPick> _toSpawn = new();
    Stack<GameObject>[] _pools;
    readonly Dictionary<GameObject, Stack<GameObject>> _regionPools = new();

    float _refreshTimer;
    int _longitudeBandsAtSetup;
    bool _loggedMissingPrefab;

    struct ActiveTree
    {
        public GameObject Instance;
        public int PrefabIndex;
        public GameObject RegionPrefab;
    }

    struct CellPick
    {
        public int Lat;
        public int Lon;
        public int PrefabIndex;
        public GameObject RegionPrefab;
        public float SqrDistance;
    }

    void Awake()
    {
        _planet = ResolvePlanet();
        _tiles = _planet != null ? _planet.GetComponent<PlanetTileMap>() : null;
        if (_planet == null)
            Debug.LogWarning("[PlanetTreeStreamer] No planet assigned and none found in the scene — tree streaming disabled.", this);

        if (regionSet == null)
        {
            WarnIfPrefabMissing(tree1Prefab, "Tree1");
            WarnIfPrefabMissing(tree2Prefab, "Tree2");
            WarnIfPrefabMissing(tree3Prefab, "Tree3");
            WarnIfPrefabMissing(tree4Prefab, "Tree4");
            WarnIfPrefabMissing(tree5Prefab, "Tree5");
        }

        _pools = new Stack<GameObject>[PoolCount];
        for (int i = 0; i < PoolCount; i++)
            _pools[i] = new Stack<GameObject>();
    }

    void Start()
    {
        EnsureRuntimeRoot();
    }

    /// <summary>Called by <see cref="PlanetEnvironmentManager"/> — single source for planet + regionSet.</summary>
    public void ConfigureFromManager(SphericalPlanet targetPlanet, PlanetEnvironmentRegionSet set)
    {
        if (targetPlanet != null)
        {
            planet = targetPlanet;
            _planet = targetPlanet;
            _tiles = _planet.GetComponent<PlanetTileMap>();
        }

        regionSet = set;
    }

    void EnsureRuntimeRoot()
    {
        if (_rootCreated)
            return;
        _rootCreated = true;

        var rootGo = new GameObject("TreeStream (Runtime)");
        rootGo.hideFlags = HideFlags.DontSave;
        _root = rootGo.transform;
        _root.SetParent(PlanetEnvironmentRoot.FindOrCreate(_planet != null ? _planet : ResolvePlanet(), transform), false);
    }

    void OnEnable()
    {
        _refreshTimer = 0f;
        if (_root != null && _root != transform)
            _root.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (_root != null && _root != transform)
            _root.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (_root != null)
            Destroy(_root.gameObject);
    }

    void Update()
    {
        EnsureRuntimeRoot();
        if (!HasAnyPrefab())
            return;
        if (_tiles == null || !_tiles.HasValidMap() || _tiles.Tileset == null)
            return;

        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer > 0f)
            return;
        _refreshTimer = refreshInterval;

        Refresh();
    }

    /// <summary>Queue a rescan on the next Update (e.g. after teleporting the player).</summary>
    [ContextMenu("Refresh Now")]
    public void ForceRefresh()
    {
        _refreshTimer = 0f;
    }

    void WarnIfPrefabMissing(GameObject prefab, string label)
    {
        if (prefab == null)
            Debug.LogWarning($"[PlanetTreeStreamer] {label} Prefab is not assigned — it will be skipped. Assign it in the Inspector or re-run BackHome → Setup Nyxara Tree Streaming.", this);
    }

    SphericalPlanet ResolvePlanet()
    {
        if (planet != null)
            return planet;
        return SphericalPlanet.Instance != null ? SphericalPlanet.Instance : FindAnyObjectByType<SphericalPlanet>();
    }

    bool HasAnyPrefab() =>
        regionSet != null
        || tree1Prefab != null
        || tree2Prefab != null
        || tree3Prefab != null
        || tree4Prefab != null
        || tree5Prefab != null;

    GameObject PrefabAt(int index)
    {
        switch (index)
        {
            case Tree1Index: return tree1Prefab;
            case Tree2Index: return tree2Prefab;
            case Tree3Index: return tree3Prefab;
            case Tree4Index: return tree4Prefab;
            case Tree5Index: return tree5Prefab;
            default: return null;
        }
    }

    Transform ResolveAnchor()
    {
        if (anchorOverride != null)
            return anchorOverride;

        if (_cachedAnchor != null)
            return _cachedAnchor;

        PlanetWalker walker = FindAnyObjectByType<PlanetWalker>();
        if (walker != null)
        {
            _cachedAnchor = walker.transform;
            return _cachedAnchor;
        }

        if (Camera.main != null)
        {
            _cachedAnchor = Camera.main.transform;
            return _cachedAnchor;
        }

        return null;
    }

    void Refresh()
    {
        EnsureRuntimeRoot();

        Transform playerAnchor = ResolveAnchor();
        if (playerAnchor == null)
            return;

        Vector3 anchorPos = playerAnchor.position;
        float scanRadius = visibleRadius;

        _longitudeBandsAtSetup = _tiles.LongitudeBands;
        if (!_tiles.WorldToCell(anchorPos, out int centerLat, out int centerLon))
            return;

        float tileSize = Mathf.Max(0.5f, _tiles.ApproximateTileWorldSize);
        int latWindow = Mathf.Clamp(
            Mathf.CeilToInt(scanRadius / tileSize) + 1,
            1,
            Mathf.Max(1, _tiles.LatitudeBands / 2));

        float latStep = 180f / _tiles.LatitudeBands;
        float midLatDeg = -90f + (centerLat + 0.5f) * latStep;
        float cosLat = Mathf.Max(0.15f, Mathf.Abs(Mathf.Cos(midLatDeg * Mathf.Deg2Rad)));
        int lonWindow = Mathf.Clamp(
            Mathf.CeilToInt(scanRadius / (tileSize * cosLat)) + 1,
            1,
            Mathf.Max(1, _tiles.LongitudeBands / 2));

        float sqrRadius = scanRadius * scanRadius;

        _desired.Clear();
        _toSpawn.Clear();

        int latMin = Mathf.Max(0, centerLat - latWindow);
        int latMax = Mathf.Min(_tiles.LatitudeBands - 1, centerLat + latWindow);

        for (int lat = latMin; lat <= latMax; lat++)
        {
            for (int lonOffset = -lonWindow; lonOffset <= lonWindow; lonOffset++)
            {
                int lon = Mod(centerLon + lonOffset, _tiles.LongitudeBands);
                if (!IsTreeCell(lat, lon))
                    continue;

                if (!_tiles.TryGetCellCenter(lat, lon, out Vector3 cellCenter))
                    continue;

                if (PlanetEnvironmentExclusion.IsExcluded(_planet, cellCenter))
                    continue;

                Vector3 up = regionSet != null ? (cellCenter - _planet.Center).normalized : default;
                int regionIndex = regionSet != null ? regionSet.GetRegionIndex(up) : -1;
                float effectiveDensity = regionSet != null
                    ? regionSet.GetEffectiveDensity(density, regionIndex, regionSet.GetTreeDensityMultiplier(regionIndex))
                    : density;

                // Master density roll — decides whether this tile grows anything at all, before
                // spending any work resolving which region/variant it'd be.
                if (Hash01(lat, lon, 6) >= effectiveDensity)
                    continue;

                float sqrDist = (cellCenter - anchorPos).sqrMagnitude;
                if (sqrDist > sqrRadius)
                    continue;

                if (!TryResolveVariant(lat, lon, up, 8, out int prefabIndex, out GameObject regionPrefab))
                    continue;

                int key = PackKey(lat, lon);
                _desired.Add(key);
                if (!_active.ContainsKey(key))
                    _toSpawn.Add(new CellPick { Lat = lat, Lon = lon, PrefabIndex = prefabIndex, RegionPrefab = regionPrefab, SqrDistance = sqrDist });
            }
        }

        _toDespawn.Clear();
        foreach (KeyValuePair<int, ActiveTree> pair in _active)
        {
            if (!_desired.Contains(pair.Key))
                _toDespawn.Add(pair.Key);
        }

        for (int i = 0; i < _toDespawn.Count; i++)
            Despawn(_toDespawn[i]);

        if (_toSpawn.Count > 1)
            _toSpawn.Sort((a, b) => a.SqrDistance.CompareTo(b.SqrDistance));

        int spawned = 0;
        for (int i = 0; i < _toSpawn.Count && spawned < maxSpawnsPerRefresh; i++)
        {
            if (_active.Count >= maxActiveTrees)
                break;

            CellPick pick = _toSpawn[i];
            bool ok = pick.RegionPrefab != null
                ? TrySpawnRegion(pick.Lat, pick.Lon, pick.RegionPrefab)
                : TrySpawn(pick.Lat, pick.Lon, pick.PrefabIndex);
            if (ok)
                spawned++;
        }
    }

    /// <summary>Resolves the prefab to grow at (lat, lon) — from <see cref="regionSet"/>'s weighted
    /// tree list when assigned (<paramref name="up"/> must be the cell's surface direction in that
    /// case), otherwise from this streamer's own Tree1..Tree5 slots/weights.</summary>
    bool TryResolveVariant(int lat, int lon, Vector3 up, int salt, out int prefabIndex, out GameObject regionPrefab)
    {
        prefabIndex = -1;
        regionPrefab = null;

        if (regionSet != null)
        {
            int region = regionSet.GetRegionIndex(up);
            PlanetEnvironmentRegionSet.Region regionData = regionSet.GetRegion(region);
            regionPrefab = PlanetEnvironmentRegionSet.PickWeighted(regionData?.trees, Hash01(lat, lon, salt));
            return regionPrefab != null;
        }

        TreePick pick = PickVariant(lat, lon, salt);
        if (pick == TreePick.Empty)
            return false;

        prefabIndex = PrefabIndexFor(pick);
        return true;
    }

    /// <summary>Weighted pick among whichever tree variants actually have a prefab assigned.
    /// Missing variants are excluded from the roll entirely rather than falling back to another
    /// one, so the remaining variants keep their correct relative proportions.</summary>
    TreePick PickVariant(int lat, int lon, int salt)
    {
        float w1 = tree1Prefab != null ? tree1Weight : 0f;
        float w2 = tree2Prefab != null ? tree2Weight : 0f;
        float w3 = tree3Prefab != null ? tree3Weight : 0f;
        float w4 = tree4Prefab != null ? tree4Weight : 0f;
        float w5 = tree5Prefab != null ? tree5Weight : 0f;

        float total = w1 + w2 + w3 + w4 + w5;
        if (total <= 0f)
            return TreePick.Empty;

        float roll = Hash01(lat, lon, salt) * total;
        if (roll < w1)
            return TreePick.Tree1;
        roll -= w1;
        if (roll < w2)
            return TreePick.Tree2;
        roll -= w2;
        if (roll < w3)
            return TreePick.Tree3;
        roll -= w3;
        if (roll < w4)
            return TreePick.Tree4;
        return TreePick.Tree5;
    }

    int PrefabIndexFor(TreePick pick)
    {
        switch (pick)
        {
            case TreePick.Tree1: return Tree1Index;
            case TreePick.Tree2: return Tree2Index;
            case TreePick.Tree3: return Tree3Index;
            case TreePick.Tree4: return Tree4Index;
            case TreePick.Tree5: return Tree5Index;
            default: return -1;
        }
    }

    bool IsTreeCell(int lat, int lon)
    {
        PlanetTileset tileset = _tiles.Tileset;
        if (tileset == null)
            return false;

        int terrainIndex = _tiles.GetTerrain(lat, lon);
        PlanetTileset.Terrain terrain = tileset.GetTerrain(terrainIndex);
        if (terrain == null || !terrain.walkable)
            return false;

        return !string.IsNullOrEmpty(terrain.id)
               && terrain.id.IndexOf("Grass", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    bool TrySpawn(int lat, int lon, int prefabIndex)
    {
        GameObject prefab = PrefabAt(prefabIndex);
        if (prefab == null)
        {
            LogMissingPrefabOnce(prefabIndex);
            return false;
        }

        if (!TryComputePose(lat, lon, out Vector3 position, out Quaternion rotation))
            return false;

        if (PlanetEnvironmentExclusion.IsExcluded(_planet, position))
            return false;

        GameObject instance = Rent(prefabIndex, prefab);
        if (instance == null)
            return false;

        instance.transform.SetPositionAndRotation(position, rotation);
        instance.transform.localScale = Vector3.one;
        instance.name = NameFor(prefabIndex);
        instance.SetActive(true);

        _active[PackKey(lat, lon)] = new ActiveTree { Instance = instance, PrefabIndex = prefabIndex };
        return true;
    }

    bool TrySpawnRegion(int lat, int lon, GameObject prefab)
    {
        if (prefab == null)
            return false;

        if (!TryComputePose(lat, lon, out Vector3 position, out Quaternion rotation))
            return false;

        if (PlanetEnvironmentExclusion.IsExcluded(_planet, position))
            return false;

        GameObject instance = RentRegion(prefab);
        if (instance == null)
            return false;

        instance.transform.SetPositionAndRotation(position, rotation);
        instance.transform.localScale = Vector3.one;
        instance.name = prefab.name;
        instance.SetActive(true);

        _active[PackKey(lat, lon)] = new ActiveTree { Instance = instance, PrefabIndex = -1, RegionPrefab = prefab };
        return true;
    }

    /// <summary>Jittered surface pose for a tree at (lat, lon) — shared by the named-prefab and
    /// region-driven spawn paths so both place/orient instances identically.</summary>
    bool TryComputePose(int lat, int lon, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        if (!_tiles.TryGetCellCenter(lat, lon, out Vector3 cellCenter))
            return false;

        Vector3 up = (cellCenter - _planet.Center).normalized;
        Vector3 east = Vector3.Cross(Vector3.up, up);
        if (east.sqrMagnitude < 0.0001f)
            east = Vector3.Cross(Vector3.right, up);
        east.Normalize();
        Vector3 north = Vector3.Cross(up, east).normalized;

        float angle = Hash01(lat, lon, 20) * Mathf.PI * 2f;
        float radius = jitterRadius * Mathf.Sqrt(Hash01(lat, lon, 30));
        Vector3 jittered = cellCenter + (east * Mathf.Cos(angle) + north * Mathf.Sin(angle)) * radius;

        Vector3 pointUp = (jittered - _planet.Center).normalized;
        float surfaceRadius = _tiles.ProvidesWalkSurface
            ? _tiles.GetWalkSurfaceRadius(pointUp)
            : _planet.GetTerrainRadius(pointUp);
        position = _planet.Center + pointUp * (surfaceRadius + hover);

        Vector3 normal = _tiles.ProvidesWalkSurface
            ? _tiles.GetWalkSurfaceNormal(pointUp)
            : _planet.GetTerrainNormal(pointUp);
        if (Vector3.Dot(normal, pointUp) < 0f)
            normal = -normal;

        float yaw = Hash01(lat, lon, 40) * 360f;
        rotation = PlanetSurfacePose.RotationFromUp(normal, yaw);
        return true;
    }

    static string NameFor(int prefabIndex)
    {
        switch (prefabIndex)
        {
            case Tree1Index: return "Tree1";
            case Tree2Index: return "Tree2";
            case Tree3Index: return "Tree3";
            case Tree4Index: return "Tree4";
            case Tree5Index: return "Tree5";
            default: return "Tree";
        }
    }

    void LogMissingPrefabOnce(int prefabIndex)
    {
        if (_loggedMissingPrefab)
            return;

        _loggedMissingPrefab = true;
        Debug.LogWarning($"[PlanetTreeStreamer] Missing {NameFor(prefabIndex)} prefab — assign it on the component.", this);
    }

    void Despawn(int key)
    {
        if (!_active.TryGetValue(key, out ActiveTree tree))
            return;

        _active.Remove(key);
        if (tree.Instance == null)
            return;

        tree.Instance.SetActive(false);
        tree.Instance.transform.SetParent(_root, false);

        if (tree.RegionPrefab != null)
            GetRegionPool(tree.RegionPrefab).Push(tree.Instance);
        else
            _pools[tree.PrefabIndex].Push(tree.Instance);
    }

    GameObject Rent(int prefabIndex, GameObject prefab)
    {
        Stack<GameObject> pool = _pools[prefabIndex];
        while (pool.Count > 0)
        {
            GameObject pooled = pool.Pop();
            if (pooled != null)
                return pooled;
        }

        return CreateInstance(prefab);
    }

    GameObject RentRegion(GameObject prefab)
    {
        Stack<GameObject> pool = GetRegionPool(prefab);
        while (pool.Count > 0)
        {
            GameObject pooled = pool.Pop();
            if (pooled != null)
                return pooled;
        }

        return CreateInstance(prefab);
    }

    GameObject CreateInstance(GameObject prefab)
    {
        // Instantiate unparented so the clone can finish Awake before we parent it.
        // Instantiate(prefab, parent) SendMessages OnTransformChildrenChanged during that Awake.
        GameObject instance = Instantiate(prefab);
        instance.transform.SetParent(_root, false);
        PrepareInstance(instance);
        return instance;
    }

    Stack<GameObject> GetRegionPool(GameObject prefab)
    {
        if (!_regionPools.TryGetValue(prefab, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>();
            _regionPools[prefab] = pool;
        }

        return pool;
    }

    void PrepareInstance(GameObject instance)
    {
        PlanetSurfaceAlign align = instance.GetComponent<PlanetSurfaceAlign>();
        if (align != null)
            align.enabled = false;

        if (disableShadows)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }
        }

        if (disableColliders)
        {
            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }
    }

    int PackKey(int lat, int lon) => lat * _longitudeBandsAtSetup + lon;

    static int Mod(int value, int modulus)
    {
        int m = value % modulus;
        return m < 0 ? m + modulus : m;
    }

    static float Hash01(int lat, int lon, int salt)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)lat) * 16777619u;
            h = (h ^ (uint)lon) * 16777619u;
            h = (h ^ (uint)salt) * 16777619u;

            // Plain FNV-1a's low bits mix poorly for small, slowly-varying inputs like tile
            // coordinates — a MurmurHash3-style finalizer gives a proper full-bit avalanche
            // before truncating to [0,1). (Same fix as PlanetGrassStreamer.Hash01.)
            h ^= h >> 16;
            h *= 0x85ebca6bu;
            h ^= h >> 13;
            h *= 0xc2b2ae35u;
            h ^= h >> 16;

            return (h & 0x00FFFFFFu) / (float)0x01000000u;
        }
    }

    void OnDrawGizmosSelected()
    {
        Transform anchor = anchorOverride != null ? anchorOverride : _cachedAnchor;
        Vector3 center = anchor != null ? anchor.position : transform.position;
        Gizmos.color = new Color(0.35f, 0.55f, 0.25f, 0.6f);
        Gizmos.DrawWireSphere(center, visibleRadius);

        DrawRegionGizmos();
    }

    /// <summary>Sketches each region's blob seeds on the planet surface so the layout (tuned via
    /// regionSet's seed/blobsPerRegion) can be eyeballed in the Scene view.</summary>
    void DrawRegionGizmos()
    {
        if (regionSet == null)
            return;

        SphericalPlanet planetForGizmo = _planet != null ? _planet : ResolvePlanet();
        if (planetForGizmo == null)
            return;

        int regionCount = Mathf.Max(1, regionSet.RegionCount);
        int seedCount = regionSet.DebugSeedCount;
        for (int i = 0; i < seedCount; i++)
        {
            int region = regionSet.DebugSeedRegion(i);
            Gizmos.color = Color.HSVToRGB((region % regionCount) / (float)regionCount, 0.75f, 1f);
            Vector3 point = planetForGizmo.Center + regionSet.DebugSeedDirection(i) * planetForGizmo.Radius;
            Gizmos.DrawSphere(point, Mathf.Max(0.5f, planetForGizmo.Radius * 0.02f));
        }
    }
}
