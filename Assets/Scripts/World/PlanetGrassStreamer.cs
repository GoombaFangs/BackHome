using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime grass streaming for spherical planets. Spawns a random mix of grass prefabs only in a
/// radius around the player (pooled, capped instance count) instead of pre-placing thousands of
/// static GameObjects across the whole planet. Keeps memory/CPU/GPU cost bounded on mobile no
/// matter how big the planet is — as the player walks, grass fades in ahead and returns to the
/// pool behind them.
///
/// Placement uses only analytic tile-surface math (<see cref="PlanetTileMap.GetWalkSurfaceRadius"/>)
/// — no physics raycasts — so streaming in new cells is cheap even on low-end phones.
///
/// Each walkable grass tile gets one deterministic roll: filled or empty, gated by a single
/// <see cref="density"/> knob (<see cref="GrassPick"/>). Filled tiles randomly pick one of the
/// assigned grass variants (Grass1 / Grass2 / Grass3 / Grass_Luminous_Toadstool / Hollow_Log),
/// weighted by their respective sliders.
///
/// Lives on the shared "EnvironmentManager" GameObject alongside the tree/rock streamers, not on the planet
/// itself — keeps the planet hierarchy free of manager components. Assign <see cref="planet"/>
/// explicitly, or leave it empty to auto-resolve via <see cref="SphericalPlanet.Instance"/>. The
/// spawned instances themselves are parented under the planet's "Environment" child instead (see
/// <see cref="PlanetEnvironmentRoot"/>), so streamed grass shows up nested under the planet.
///
/// Menu: BackHome → Setup Nyxara Grass Streaming (adds this + wires all grass prefabs).
/// </summary>
[DisallowMultipleComponent]
public class PlanetGrassStreamer : MonoBehaviour
{
    const int Grass1Index = 0;
    const int Grass2Index = 1;
    const int Grass3Index = 2;
    const int Grass4Index = 3;
    const int Grass5Index = 4;
    const int PoolCount = 5;

    enum GrassPick
    {
        Empty,
        Grass1,
        Grass2,
        Grass3,
        Grass4,
        Grass5,
    }

    [Header("Planet")]
    [Tooltip("Planet to stream grass onto. Leave empty to use SphericalPlanet.Instance / first in scene.")]
    [SerializeField] SphericalPlanet planet;

    [Header("Prefabs")]
    [SerializeField] GameObject grass1Prefab;
    [SerializeField] GameObject grass2Prefab;
    [SerializeField] GameObject grass3Prefab;
    [SerializeField] GameObject grass4Prefab;
    [SerializeField] GameObject grass5Prefab;

    // Legacy array — auto-migrated in Awake so existing scenes keep working.
    [SerializeField, HideInInspector] GameObject[] grassPrefabs;

    [Header("Streaming Range")]
    [Tooltip("World-space radius around the player kept filled with grass. Push this out past what the camera can actually see (screen edge or planet horizon) so spawn/despawn pop-in happens off-screen instead of in view.")]
    [SerializeField, Min(4f)] float visibleRadius = 48f;
    [Tooltip("Seconds between rescans. Higher = cheaper, lower = grass keeps up better with fast movement.")]
    [SerializeField, Min(0.05f)] float refreshInterval = 0.3f;

    [Header("Density")]
    [Tooltip("Master grass amount. 0 = bare ground, 1 = a clump on every walkable tile. This is the main knob — lower it first if there's too much grass.")]
    [SerializeField, Range(0f, 1f)] float density = 0.17f;
    [Tooltip("Relative mix between the grass variants on tiles that do get filled (does not affect overall density). A variant with no prefab assigned is skipped automatically.")]
    [SerializeField, Min(0f)] float grass1Weight = 1f;
    [SerializeField, Min(0f)] float grass2Weight = 1f;
    [SerializeField, Min(0f)] float grass3Weight = 1f;
    [SerializeField, Min(0f)] float grass4Weight = 1f;
    [SerializeField, Min(0f)] float grass5Weight = 1f;
    [Tooltip("Chance a non-empty tile gets a second overlapping clump for extra variety.")]
    [SerializeField, Range(0f, 1f)] float secondClumpChance = 0.22f;
    [Tooltip("How far a clump can drift from its tile center.")]
    [SerializeField, Min(0f)] float jitterRadius = 0.9f;
    [SerializeField] float hover = 0.05f;

    [Header("Mobile Safety Cap")]
    [Tooltip("Hard ceiling on concurrently active grass instances, regardless of density/radius. Protects low-end phones even if the player stands in a dense field.")]
    [SerializeField, Min(1)] int maxActiveGrass = 260;
    [Tooltip("Max new instances spawned per rescan, spreading instantiation cost across frames.")]
    [SerializeField, Min(1)] int maxSpawnsPerRefresh = 32;
    [Tooltip("Disable shadow casting on grass — cheap and rarely noticeable at grass scale.")]
    [SerializeField] bool disableShadows = true;

    [Header("Anchor")]
    [Tooltip("Optional explicit anchor (defaults to the player, which the follow camera always keeps centered).")]
    [SerializeField] Transform anchorOverride;

    [Header("Regions")]
    [Tooltip("Set automatically by PlanetEnvironmentManager when present. Per-region grass lists live on the region set asset.")]
    [SerializeField] PlanetEnvironmentRegionSet regionSet;

    bool _useAreaStream;
    Transform _areaAnchor;
    float _areaRadius;
    float _areaActivationRadius;
    PlanetEnvironmentRegionSet.WeightedPrefab[] _areaPrefabs;
    bool _rootCreated;

    SphericalPlanet _planet;
    PlanetTileMap _tiles;
    Transform _root;
    Transform _cachedAnchor;

    readonly Dictionary<int, ActiveClump> _active = new();
    readonly HashSet<int> _desired = new();
    readonly List<int> _toDespawn = new();
    readonly List<CellSlot> _toSpawn = new();
    Stack<GameObject>[] _pools;
    readonly Dictionary<GameObject, Stack<GameObject>> _regionPools = new();

    float _refreshTimer;
    int _longitudeBandsAtSetup;
    bool _loggedMissingPrefab;

    struct ActiveClump
    {
        public GameObject Instance;
        public int PrefabIndex;
        public GameObject RegionPrefab;
    }

    struct CellSlot
    {
        public int Lat;
        public int Lon;
        public int Slot;
        public int PrefabIndex;
        public GameObject RegionPrefab;
        public float SqrDistance;
    }

    void Awake()
    {
        _planet = ResolvePlanet();
        _tiles = _planet != null ? _planet.GetComponent<PlanetTileMap>() : null;
        if (_planet == null)
            Debug.LogWarning("[PlanetGrassStreamer] No planet assigned and none found in the scene — grass streaming disabled.", this);
        MigrateLegacyPrefabs();

        if (regionSet == null && !_useAreaStream)
        {
            WarnIfPrefabMissing(grass1Prefab, "Grass1");
            WarnIfPrefabMissing(grass2Prefab, "Grass2");
            WarnIfPrefabMissing(grass3Prefab, "Grass3");
            WarnIfPrefabMissing(grass4Prefab, "Grass_Luminous_Toadstool");
            WarnIfPrefabMissing(grass5Prefab, "Hollow_Log");
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

    /// <summary>Confines this streamer to a disk around <paramref name="anchor"/> using
    /// <paramref name="prefabs"/> instead of regionSet/legacy slots (legacy area path).</summary>
    public void ConfigureAreaStream(
        SphericalPlanet targetPlanet,
        Transform anchor,
        float worldRadius,
        PlanetEnvironmentRegionSet.WeightedPrefab[] prefabs,
        float playerActivationRadius)
    {
        planet = targetPlanet;
        _planet = targetPlanet;
        _tiles = _planet != null ? _planet.GetComponent<PlanetTileMap>() : null;
        _useAreaStream = anchor != null && worldRadius > 0.01f;
        _areaAnchor = anchor;
        _areaRadius = worldRadius;
        _areaPrefabs = prefabs;
        _areaActivationRadius = playerActivationRadius;
        regionSet = null;
        EnsureRuntimeRoot();
    }

    void EnsureRuntimeRoot()
    {
        if (_rootCreated)
            return;
        _rootCreated = true;

        if (_useAreaStream && _areaAnchor != null)
        {
            _root = transform;
            return;
        }

        var rootGo = new GameObject("GrassStream (Runtime)");
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
        if (_useAreaStream)
        {
            foreach (KeyValuePair<int, ActiveClump> pair in _active)
            {
                if (pair.Value.Instance != null)
                    Destroy(pair.Value.Instance);
            }

            _active.Clear();
            return;
        }

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

    /// <summary>Force an immediate rescan (e.g. after teleporting the player).</summary>
    [ContextMenu("Refresh Now")]
    public void ForceRefresh()
    {
        _refreshTimer = 0f;
        if (_tiles != null && _tiles.HasValidMap())
            Refresh();
    }

    void MigrateLegacyPrefabs()
    {
        if (grass1Prefab == null && grassPrefabs != null && grassPrefabs.Length > 0)
            grass1Prefab = grassPrefabs[0];
        if (grass2Prefab == null && grassPrefabs != null && grassPrefabs.Length > 1)
            grass2Prefab = grassPrefabs[1];

#if UNITY_EDITOR
        // Safety net when the scene component is missing newer serialized fields (Unity sometimes
        // drops them if the scene was saved before the script recompiled).
        const string grassFolder = "Assets/Galaxy/Planets/Nyxara/Environment/Grass";
        if (grass4Prefab == null)
            grass4Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(grassFolder + "/Grass_Luminous_Toadstool.prefab");
        if (grass5Prefab == null)
            grass5Prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(grassFolder + "/Hollow_Log.prefab");
#endif
    }

    SphericalPlanet ResolvePlanet()
    {
        if (planet != null)
            return planet;
        return SphericalPlanet.Instance != null ? SphericalPlanet.Instance : FindAnyObjectByType<SphericalPlanet>();
    }

    void WarnIfPrefabMissing(GameObject prefab, string label)
    {
        if (prefab == null)
            Debug.LogWarning($"[PlanetGrassStreamer] {label} Prefab is not assigned — it will be skipped. Assign it in the Inspector or re-run BackHome → Setup Nyxara Grass Streaming.", this);
    }

    bool HasAnyPrefab() =>
        (_useAreaStream && PlanetAreaStreamHelper.HasAnyPrefab(_areaPrefabs))
        || regionSet != null
        || grass1Prefab != null
        || grass2Prefab != null
        || grass3Prefab != null
        || grass4Prefab != null
        || grass5Prefab != null;

    GameObject PrefabAt(int index)
    {
        switch (index)
        {
            case Grass1Index: return grass1Prefab;
            case Grass2Index: return grass2Prefab;
            case Grass3Index: return grass3Prefab;
            case Grass4Index: return grass4Prefab;
            case Grass5Index: return grass5Prefab;
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

        if (_useAreaStream)
        {
            if (_areaAnchor == null)
                return;

            if (!PlanetAreaStreamHelper.IsPlayerNearArea(_planet, playerAnchor.position, _areaAnchor.position, _areaActivationRadius))
            {
                DespawnAllActive();
                return;
            }
        }

        Transform scanAnchor = _useAreaStream ? _areaAnchor : playerAnchor;
        Vector3 anchorPos = scanAnchor.position;
        float scanRadius = _useAreaStream ? _areaRadius : visibleRadius;

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
                if (!IsGrassCell(lat, lon))
                    continue;

                if (!_tiles.TryGetCellCenter(lat, lon, out Vector3 cellCenter))
                    continue;

                Vector3 up = regionSet != null || _useAreaStream ? (cellCenter - _planet.Center).normalized : default;
                int regionIndex = regionSet != null ? regionSet.GetRegionIndex(up) : -1;
                float effectiveDensity = regionSet != null
                    ? regionSet.GetEffectiveDensity(density, regionIndex, regionSet.GetGrassDensityMultiplier(regionIndex))
                    : density;

                // Master density roll — decides whether this tile grows anything at all, before
                // spending any work resolving which region/variant it'd be.
                if (Hash01(lat, lon, 6) >= effectiveDensity)
                    continue;

                float sqrDist = (cellCenter - anchorPos).sqrMagnitude;
                if (sqrDist > sqrRadius)
                    continue;

                if (_useAreaStream && !PlanetAreaStreamHelper.IsWithinDisk(_planet, _areaAnchor.position, _areaRadius, cellCenter))
                    continue;

                if (TryResolveVariant(lat, lon, up, 8, out int prefabIndex, out GameObject regionPrefab))
                    AddDesiredSlot(lat, lon, 0, prefabIndex, regionPrefab, sqrDist);

                float effectiveSecondClump = regionSet != null
                    ? Mathf.Clamp01(secondClumpChance * regionSet.GetGrassDensityMultiplier(regionIndex))
                    : secondClumpChance;
                if (Hash01(lat, lon, 7) < effectiveSecondClump
                    && TryResolveVariant(lat, lon, up, 9, out int bonusIndex, out GameObject bonusRegionPrefab))
                {
                    AddDesiredSlot(lat, lon, 1, bonusIndex, bonusRegionPrefab, sqrDist);
                }
            }
        }

        _toDespawn.Clear();
        foreach (KeyValuePair<int, ActiveClump> pair in _active)
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
            if (_active.Count >= maxActiveGrass)
                break;

            CellSlot slot = _toSpawn[i];
            bool ok = slot.RegionPrefab != null
                ? TrySpawnRegion(slot.Lat, slot.Lon, slot.Slot, slot.RegionPrefab)
                : TrySpawn(slot.Lat, slot.Lon, slot.Slot, slot.PrefabIndex);
            if (ok)
                spawned++;
        }
    }

    void AddDesiredSlot(int lat, int lon, int slot, int prefabIndex, GameObject regionPrefab, float sqrDist)
    {
        if (regionPrefab == null && PrefabAt(prefabIndex) == null)
            return;

        int key = PackKey(lat, lon, slot);
        _desired.Add(key);
        if (!_active.ContainsKey(key))
        {
            _toSpawn.Add(new CellSlot { Lat = lat, Lon = lon, Slot = slot, PrefabIndex = prefabIndex, RegionPrefab = regionPrefab, SqrDistance = sqrDist });
        }
    }

    /// <summary>Resolves the prefab to grow at (lat, lon) — from <see cref="regionSet"/>'s weighted
    /// grass list when assigned (<paramref name="up"/> must be the cell's surface direction in that
    /// case), otherwise from this streamer's own Grass1..Grass5 slots/weights.</summary>
    bool TryResolveVariant(int lat, int lon, Vector3 up, int salt, out int prefabIndex, out GameObject regionPrefab)
    {
        prefabIndex = -1;
        regionPrefab = null;

        if (_useAreaStream && PlanetAreaStreamHelper.HasAnyPrefab(_areaPrefabs))
        {
            regionPrefab = PlanetEnvironmentRegionSet.PickWeighted(_areaPrefabs, Hash01(lat, lon, salt));
            return regionPrefab != null;
        }

        if (regionSet != null)
        {
            int region = regionSet.GetRegionIndex(up);
            PlanetEnvironmentRegionSet.Region regionData = regionSet.GetRegion(region);
            regionPrefab = PlanetEnvironmentRegionSet.PickWeighted(regionData?.grass, Hash01(lat, lon, salt));
            return regionPrefab != null;
        }

        GrassPick pick = PickVariant(lat, lon, salt);
        if (pick == GrassPick.Empty)
            return false;

        prefabIndex = PrefabIndexFor(pick);
        return true;
    }

    /// <summary>Weighted pick among whichever grass variants actually have a prefab assigned.
    /// Missing variants are excluded from the roll entirely rather than falling back to another one,
    /// so the remaining variants keep their correct relative proportions.</summary>
    GrassPick PickVariant(int lat, int lon, int salt)
    {
        float w1 = grass1Prefab != null ? grass1Weight : 0f;
        float w2 = grass2Prefab != null ? grass2Weight : 0f;
        float w3 = grass3Prefab != null ? grass3Weight : 0f;
        float w4 = grass4Prefab != null ? grass4Weight : 0f;
        float w5 = grass5Prefab != null ? grass5Weight : 0f;

        float total = w1 + w2 + w3 + w4 + w5;
        if (total <= 0f)
            return GrassPick.Empty;

        float roll = Hash01(lat, lon, salt) * total;
        if (roll < w1)
            return GrassPick.Grass1;
        roll -= w1;
        if (roll < w2)
            return GrassPick.Grass2;
        roll -= w2;
        if (roll < w3)
            return GrassPick.Grass3;
        roll -= w3;
        if (roll < w4)
            return GrassPick.Grass4;
        return GrassPick.Grass5;
    }

    int PrefabIndexFor(GrassPick pick)
    {
        switch (pick)
        {
            case GrassPick.Grass1: return Grass1Index;
            case GrassPick.Grass2: return Grass2Index;
            case GrassPick.Grass3: return Grass3Index;
            case GrassPick.Grass4: return Grass4Index;
            case GrassPick.Grass5: return Grass5Index;
            default: return -1;
        }
    }

    bool IsGrassCell(int lat, int lon)
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

    bool TrySpawn(int lat, int lon, int slot, int prefabIndex)
    {
        GameObject prefab = PrefabAt(prefabIndex);
        if (prefab == null)
        {
            LogMissingPrefabOnce(prefabIndex);
            return false;
        }

        if (!TryComputePose(lat, lon, slot, out Vector3 position, out Quaternion rotation))
            return false;

        GameObject instance = Rent(prefabIndex, prefab);
        if (instance == null)
            return false;

        instance.transform.SetPositionAndRotation(position, rotation);
        instance.transform.localScale = Vector3.one;
        instance.name = NameFor(prefabIndex);
        instance.SetActive(true);

        _active[PackKey(lat, lon, slot)] = new ActiveClump { Instance = instance, PrefabIndex = prefabIndex };
        return true;
    }

    bool TrySpawnRegion(int lat, int lon, int slot, GameObject prefab)
    {
        if (prefab == null)
            return false;

        if (!TryComputePose(lat, lon, slot, out Vector3 position, out Quaternion rotation))
            return false;

        GameObject instance = RentRegion(prefab);
        if (instance == null)
            return false;

        instance.transform.SetPositionAndRotation(position, rotation);
        instance.transform.localScale = Vector3.one;
        instance.name = prefab.name;
        instance.SetActive(true);

        _active[PackKey(lat, lon, slot)] = new ActiveClump { Instance = instance, PrefabIndex = -1, RegionPrefab = prefab };
        return true;
    }

    /// <summary>Jittered surface pose for a clump at (lat, lon)/slot — shared by the legacy and
    /// region-driven spawn paths so both place/orient instances identically.</summary>
    bool TryComputePose(int lat, int lon, int slot, out Vector3 position, out Quaternion rotation)
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

        float angle = Hash01(lat, lon, 20 + slot) * Mathf.PI * 2f;
        float radius = jitterRadius * Mathf.Sqrt(Hash01(lat, lon, 30 + slot));
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

        float yaw = Hash01(lat, lon, 40 + slot) * 360f;
        rotation = PlanetSurfacePose.RotationFromUp(normal, yaw);
        return true;
    }

    static string NameFor(int prefabIndex)
    {
        switch (prefabIndex)
        {
            case Grass1Index: return "Grass1";
            case Grass2Index: return "Grass2";
            case Grass3Index: return "Grass3";
            case Grass4Index: return "Grass_Luminous_Toadstool";
            case Grass5Index: return "Hollow_Log";
            default: return "Grass";
        }
    }

    void LogMissingPrefabOnce(int prefabIndex)
    {
        if (_loggedMissingPrefab)
            return;

        _loggedMissingPrefab = true;
        Debug.LogWarning($"[PlanetGrassStreamer] Missing {NameFor(prefabIndex)} prefab — assign it on the component.", this);
    }

    void DespawnAllActive()
    {
        if (_active.Count == 0)
            return;

        _toDespawn.Clear();
        foreach (KeyValuePair<int, ActiveClump> pair in _active)
            _toDespawn.Add(pair.Key);

        for (int i = 0; i < _toDespawn.Count; i++)
            Despawn(_toDespawn[i]);
    }

    void Despawn(int key)
    {
        if (!_active.TryGetValue(key, out ActiveClump clump))
            return;

        _active.Remove(key);
        if (clump.Instance == null)
            return;

        clump.Instance.SetActive(false);
        clump.Instance.transform.SetParent(_root, false);

        if (clump.RegionPrefab != null)
            GetRegionPool(clump.RegionPrefab).Push(clump.Instance);
        else
            _pools[clump.PrefabIndex].Push(clump.Instance);
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

        GameObject instance = Instantiate(prefab, _root);
        PrepareInstance(instance);
        return instance;
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

        GameObject instance = Instantiate(prefab, _root);
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

        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;
    }

    int PackKey(int lat, int lon, int slot) => (lat * _longitudeBandsAtSetup + lon) * 2 + slot;

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

            // FNV-1a's low bits mix poorly for small, slowly-varying inputs like tile coordinates
            // (verified: it was returning near-monotonic values across neighboring tiles, badly
            // skewing every 50/50 pick in this file). Run a MurmurHash3 finalizer on top for a
            // proper full-bit avalanche before truncating to [0,1).
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
        Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.6f);
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
