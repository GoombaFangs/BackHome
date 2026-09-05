using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime rock streaming for spherical planets — same pooled streaming approach as
/// <see cref="PlanetTreeStreamer"/> / <see cref="PlanetGrassStreamer"/>, tuned for rocks:
/// one rock per walkable grass tile (when the density roll succeeds), weighted random mix among
/// Rock / Rock2 / Rock3 / Rock4, colliders and shadows on by default.
///
/// Lives on the shared "EnvironmentManager" GameObject alongside the grass/tree streamers, not on the planet
/// itself — keeps the planet hierarchy free of manager components. Assign <see cref="planet"/>
/// explicitly, or leave it empty to auto-resolve via <see cref="SphericalPlanet.Instance"/>. The
/// spawned instances themselves are parented under the planet's "Environment" child instead (see
/// <see cref="PlanetEnvironmentRoot"/>), so streamed rocks show up nested under the planet.
///
/// Menu: BackHome → Setup Nyxara Rock Streaming (adds this + wires Rock..Rock4).
/// </summary>
[DisallowMultipleComponent]
public class PlanetRockStreamer : MonoBehaviour
{
    const int Rock1Index = 0;
    const int Rock2Index = 1;
    const int Rock3Index = 2;
    const int Rock4Index = 3;
    const int PoolCount = 4;

    enum RockPick
    {
        Empty,
        Rock1,
        Rock2,
        Rock3,
        Rock4,
    }

    [Header("Planet")]
    [Tooltip("Planet to stream rocks onto. Leave empty to use SphericalPlanet.Instance / first in scene.")]
    [SerializeField] SphericalPlanet planet;

    [Header("Prefabs")]
    [SerializeField] GameObject rock1Prefab;
    [SerializeField] GameObject rock2Prefab;
    [SerializeField] GameObject rock3Prefab;
    [SerializeField] GameObject rock4Prefab;

    [Header("Streaming Range")]
    [Tooltip("World-space radius around the player kept filled with rocks. Push past the visible horizon so spawn/despawn happens off-screen.")]
    [SerializeField, Min(4f)] float visibleRadius = 48f;
    [Tooltip("Seconds between rescans. Higher = cheaper, lower = rocks keep up better with fast movement.")]
    [SerializeField, Min(0.05f)] float refreshInterval = 0.4f;

    [Header("Density")]
    [Tooltip("Master rock amount. 0 = none, 1 = a rock on every eligible tile.")]
    [SerializeField, Range(0f, 1f)] float density = 0.02f;
    [Tooltip("Relative mix between the four rock variants on tiles that do get a rock.")]
    [SerializeField, Min(0f)] float rock1Weight = 1f;
    [SerializeField, Min(0f)] float rock2Weight = 1f;
    [SerializeField, Min(0f)] float rock3Weight = 1f;
    [SerializeField, Min(0f)] float rock4Weight = 1f;
    [Tooltip("How far a rock can drift from its tile center.")]
    [SerializeField, Min(0f)] float jitterRadius = 0.9f;
    [SerializeField] float hover = 0.05f;

    [Header("Mobile Safety Cap")]
    [SerializeField, Min(1)] int maxActiveRocks = 55;
    [SerializeField, Min(1)] int maxSpawnsPerRefresh = 6;
    [SerializeField] bool disableShadows = false;
    [SerializeField] bool disableColliders = false;

    [Header("Anchor")]
    [SerializeField] Transform anchorOverride;

    [Header("Regions")]
    [Tooltip("Set automatically by PlanetEnvironmentManager when present. Per-region rock lists live on the region set asset.")]
    [SerializeField] PlanetEnvironmentRegionSet regionSet;

    bool _rootCreated;

    SphericalPlanet _planet;
    PlanetTileMap _tiles;
    Transform _root;
    Transform _cachedAnchor;

    readonly Dictionary<int, ActiveRock> _active = new();
    readonly HashSet<int> _desired = new();
    readonly List<int> _toDespawn = new();
    readonly List<CellPick> _toSpawn = new();
    Stack<GameObject>[] _pools;
    readonly Dictionary<GameObject, Stack<GameObject>> _regionPools = new();

    float _refreshTimer;
    int _longitudeBandsAtSetup;
    bool _loggedMissingPrefab;

    struct ActiveRock
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
            Debug.LogWarning("[PlanetRockStreamer] No planet assigned and none found in the scene — rock streaming disabled.", this);

        if (regionSet == null)
        {
            WarnIfPrefabMissing(rock1Prefab, "Rock");
            WarnIfPrefabMissing(rock2Prefab, "Rock2");
            WarnIfPrefabMissing(rock3Prefab, "Rock3");
            WarnIfPrefabMissing(rock4Prefab, "Rock4");
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

        var rootGo = new GameObject("RockStream (Runtime)");
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
            Debug.LogWarning($"[PlanetRockStreamer] {label} Prefab is not assigned — it will be skipped. Assign it in the Inspector or re-run BackHome → Setup Nyxara Rock Streaming.", this);
    }

    SphericalPlanet ResolvePlanet()
    {
        if (planet != null)
            return planet;
        return SphericalPlanet.Instance != null ? SphericalPlanet.Instance : FindAnyObjectByType<SphericalPlanet>();
    }

    bool HasAnyPrefab() =>
        regionSet != null
        || rock1Prefab != null
        || rock2Prefab != null
        || rock3Prefab != null
        || rock4Prefab != null;

    GameObject PrefabAt(int index)
    {
        switch (index)
        {
            case Rock1Index: return rock1Prefab;
            case Rock2Index: return rock2Prefab;
            case Rock3Index: return rock3Prefab;
            case Rock4Index: return rock4Prefab;
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
                if (!IsRockCell(lat, lon))
                    continue;

                if (!_tiles.TryGetCellCenter(lat, lon, out Vector3 cellCenter))
                    continue;

                if (PlanetEnvironmentExclusion.IsExcluded(_planet, cellCenter))
                    continue;

                Vector3 up = regionSet != null ? (cellCenter - _planet.Center).normalized : default;
                int regionIndex = regionSet != null ? regionSet.GetRegionIndex(up) : -1;
                float effectiveDensity = regionSet != null
                    ? regionSet.GetEffectiveDensity(density, regionIndex, regionSet.GetRockDensityMultiplier(regionIndex))
                    : density;

                // Master density roll — decides whether this tile grows anything at all, before
                // spending any work resolving which region/variant it'd be. Salt 15 decorrelates
                // this roll from grass (6) and trees on the same tile.
                if (Hash01(lat, lon, 15) >= effectiveDensity)
                    continue;

                float sqrDist = (cellCenter - anchorPos).sqrMagnitude;
                if (sqrDist > sqrRadius)
                    continue;

                if (!TryResolveVariant(lat, lon, up, 17, out int prefabIndex, out GameObject regionPrefab))
                    continue;

                int key = PackKey(lat, lon);
                _desired.Add(key);
                if (!_active.ContainsKey(key))
                    _toSpawn.Add(new CellPick { Lat = lat, Lon = lon, PrefabIndex = prefabIndex, RegionPrefab = regionPrefab, SqrDistance = sqrDist });
            }
        }

        _toDespawn.Clear();
        foreach (KeyValuePair<int, ActiveRock> pair in _active)
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
            if (_active.Count >= maxActiveRocks)
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
    /// rock list when assigned (<paramref name="up"/> must be the cell's surface direction in that
    /// case), otherwise from this streamer's own Rock1..Rock4 slots/weights.</summary>
    bool TryResolveVariant(int lat, int lon, Vector3 up, int salt, out int prefabIndex, out GameObject regionPrefab)
    {
        prefabIndex = -1;
        regionPrefab = null;

        if (regionSet != null)
        {
            int region = regionSet.GetRegionIndex(up);
            PlanetEnvironmentRegionSet.Region regionData = regionSet.GetRegion(region);
            regionPrefab = PlanetEnvironmentRegionSet.PickWeighted(regionData?.rocks, Hash01(lat, lon, salt));
            return regionPrefab != null;
        }

        RockPick pick = PickVariant(lat, lon, salt);
        if (pick == RockPick.Empty)
            return false;

        prefabIndex = PrefabIndexFor(pick);
        return true;
    }

    RockPick PickVariant(int lat, int lon, int salt)
    {
        float w1 = rock1Prefab != null ? rock1Weight : 0f;
        float w2 = rock2Prefab != null ? rock2Weight : 0f;
        float w3 = rock3Prefab != null ? rock3Weight : 0f;
        float w4 = rock4Prefab != null ? rock4Weight : 0f;

        float total = w1 + w2 + w3 + w4;
        if (total <= 0f)
            return RockPick.Empty;

        float roll = Hash01(lat, lon, salt) * total;
        if (roll < w1)
            return RockPick.Rock1;
        roll -= w1;
        if (roll < w2)
            return RockPick.Rock2;
        roll -= w2;
        if (roll < w3)
            return RockPick.Rock3;
        return RockPick.Rock4;
    }

    int PrefabIndexFor(RockPick pick)
    {
        switch (pick)
        {
            case RockPick.Rock1: return Rock1Index;
            case RockPick.Rock2: return Rock2Index;
            case RockPick.Rock3: return Rock3Index;
            case RockPick.Rock4: return Rock4Index;
            default: return -1;
        }
    }

    bool IsRockCell(int lat, int lon)
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

        _active[PackKey(lat, lon)] = new ActiveRock { Instance = instance, PrefabIndex = prefabIndex };
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

        _active[PackKey(lat, lon)] = new ActiveRock { Instance = instance, PrefabIndex = -1, RegionPrefab = prefab };
        return true;
    }

    /// <summary>Jittered surface pose for a rock at (lat, lon) — shared by the named-prefab and
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

        float angle = Hash01(lat, lon, 50) * Mathf.PI * 2f;
        float radius = jitterRadius * Mathf.Sqrt(Hash01(lat, lon, 51));
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

        float yaw = Hash01(lat, lon, 52) * 360f;
        rotation = PlanetSurfacePose.RotationFromUp(normal, yaw);
        return true;
    }

    static string NameFor(int prefabIndex)
    {
        switch (prefabIndex)
        {
            case Rock1Index: return "Rock";
            case Rock2Index: return "Rock2";
            case Rock3Index: return "Rock3";
            case Rock4Index: return "Rock4";
            default: return "Rock";
        }
    }

    void LogMissingPrefabOnce(int prefabIndex)
    {
        if (_loggedMissingPrefab)
            return;

        _loggedMissingPrefab = true;
        Debug.LogWarning($"[PlanetRockStreamer] Missing {NameFor(prefabIndex)} prefab — assign it on the component.", this);
    }

    void Despawn(int key)
    {
        if (!_active.TryGetValue(key, out ActiveRock rock))
            return;

        _active.Remove(key);
        if (rock.Instance == null)
            return;

        rock.Instance.SetActive(false);
        rock.Instance.transform.SetParent(_root, false);

        if (rock.RegionPrefab != null)
            GetRegionPool(rock.RegionPrefab).Push(rock.Instance);
        else
            _pools[rock.PrefabIndex].Push(rock.Instance);
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
        Gizmos.color = new Color(0.5f, 0.45f, 0.4f, 0.6f);
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
