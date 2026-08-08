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
/// (Tree1 / Tree2 / Tree3 / Tree4 / Tree5 / Tree6), weighted by their respective sliders. A variant
/// with no prefab assigned is skipped automatically rather than distorting the others' odds.
///
/// Menu: BackHome → Setup Nyxara Tree Streaming (adds this + wires Tree1..Tree6).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SphericalPlanet))]
public class PlanetTreeStreamer : MonoBehaviour
{
    const int Tree1Index = 0;
    const int Tree2Index = 1;
    const int Tree3Index = 2;
    const int Tree4Index = 3;
    const int Tree5Index = 4;
    const int Tree6Index = 5;
    const int PoolCount = 6;

    enum TreePick
    {
        Empty,
        Tree1,
        Tree2,
        Tree3,
        Tree4,
        Tree5,
        Tree6,
    }

    [Header("Prefabs")]
    [SerializeField] GameObject tree1Prefab;
    [SerializeField] GameObject tree2Prefab;
    [SerializeField] GameObject tree3Prefab;
    [SerializeField] GameObject tree4Prefab;
    [SerializeField] GameObject tree5Prefab;
    [SerializeField] GameObject tree6Prefab;

    [Header("Streaming Range")]
    [Tooltip("World-space radius around the player kept filled with trees. Push this out past what the camera can actually see (screen edge or planet horizon) so spawn/despawn pop-in happens off-screen instead of in view.")]
    [SerializeField, Min(4f)] float visibleRadius = 48f;
    [Tooltip("Seconds between rescans. Higher = cheaper, lower = trees keep up better with fast movement.")]
    [SerializeField, Min(0.05f)] float refreshInterval = 0.4f;

    [Header("Density")]
    [Tooltip("Master tree amount. 0 = no trees, 1 = a tree on every walkable tile. Trees are large — keep this far lower than grass density.")]
    [SerializeField, Range(0f, 1f)] float density = 0.03f;
    [Tooltip("Relative mix between the six tree variants on tiles that do get a tree (does not affect overall density). A variant with no prefab assigned is skipped automatically.")]
    [SerializeField, Min(0f)] float tree1Weight = 1f;
    [SerializeField, Min(0f)] float tree2Weight = 1f;
    [SerializeField, Min(0f)] float tree3Weight = 1f;
    [SerializeField, Min(0f)] float tree4Weight = 1f;
    [SerializeField, Min(0f)] float tree5Weight = 1f;
    [SerializeField, Min(0f)] float tree6Weight = 1f;
    [Tooltip("How far a tree can drift from its tile center.")]
    [SerializeField, Min(0f)] float jitterRadius = 1.2f;
    [SerializeField] float hover = 0.05f;

    [Header("Mobile Safety Cap")]
    [Tooltip("Hard ceiling on concurrently active trees, regardless of density/radius. Protects low-end phones even if the player stands in a dense forest.")]
    [SerializeField, Min(1)] int maxActiveTrees = 60;
    [Tooltip("Max new instances spawned per rescan, spreading instantiation cost across frames.")]
    [SerializeField, Min(1)] int maxSpawnsPerRefresh = 6;
    [Tooltip("Trees are large and prominent — shadows stay on by default, unlike grass.")]
    [SerializeField] bool disableShadows = false;
    [Tooltip("Trees should block movement — colliders stay enabled by default, unlike grass.")]
    [SerializeField] bool disableColliders = false;

    [Header("Anchor")]
    [Tooltip("Optional explicit anchor (defaults to the player, which the follow camera always keeps centered).")]
    [SerializeField] Transform anchorOverride;

    SphericalPlanet _planet;
    PlanetTileMap _tiles;
    Transform _root;
    Transform _cachedAnchor;

    readonly Dictionary<int, ActiveTree> _active = new();
    readonly HashSet<int> _desired = new();
    readonly List<int> _toDespawn = new();
    readonly List<CellPick> _toSpawn = new();
    Stack<GameObject>[] _pools;

    float _refreshTimer;
    int _longitudeBandsAtSetup;
    bool _loggedMissingPrefab;

    struct ActiveTree
    {
        public GameObject Instance;
        public int PrefabIndex;
    }

    struct CellPick
    {
        public int Lat;
        public int Lon;
        public int PrefabIndex;
        public float SqrDistance;
    }

    void Awake()
    {
        _planet = GetComponent<SphericalPlanet>();
        _tiles = GetComponent<PlanetTileMap>();

        WarnIfPrefabMissing(tree1Prefab, "Tree1");
        WarnIfPrefabMissing(tree2Prefab, "Tree2");
        WarnIfPrefabMissing(tree3Prefab, "Tree3");
        WarnIfPrefabMissing(tree4Prefab, "Tree4");
        WarnIfPrefabMissing(tree5Prefab, "Tree5");
        WarnIfPrefabMissing(tree6Prefab, "Tree6");

        var rootGo = new GameObject("TreeStream (Runtime)");
        rootGo.hideFlags = HideFlags.DontSave;
        _root = rootGo.transform;
        _root.SetParent(transform, false);

        _pools = new Stack<GameObject>[PoolCount];
        for (int i = 0; i < PoolCount; i++)
            _pools[i] = new Stack<GameObject>();
    }

    void OnEnable()
    {
        _refreshTimer = 0f;
        if (_root != null)
            _root.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (_root != null)
            _root.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (_root != null)
            Destroy(_root.gameObject);
    }

    void Update()
    {
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

    void WarnIfPrefabMissing(GameObject prefab, string label)
    {
        if (prefab == null)
            Debug.LogWarning($"[PlanetTreeStreamer] {label} Prefab is not assigned — it will be skipped. Assign it in the Inspector or re-run BackHome → Setup Nyxara Tree Streaming.", this);
    }

    bool HasAnyPrefab() => tree1Prefab != null || tree2Prefab != null || tree3Prefab != null || tree4Prefab != null || tree5Prefab != null || tree6Prefab != null;

    GameObject PrefabAt(int index)
    {
        switch (index)
        {
            case Tree1Index: return tree1Prefab;
            case Tree2Index: return tree2Prefab;
            case Tree3Index: return tree3Prefab;
            case Tree4Index: return tree4Prefab;
            case Tree5Index: return tree5Prefab;
            case Tree6Index: return tree6Prefab;
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
        Transform anchor = ResolveAnchor();
        if (anchor == null)
            return;

        _longitudeBandsAtSetup = _tiles.LongitudeBands;
        Vector3 anchorPos = anchor.position;

        if (!_tiles.WorldToCell(anchorPos, out int centerLat, out int centerLon))
            return;

        float tileSize = Mathf.Max(0.5f, _tiles.ApproximateTileWorldSize);
        int latWindow = Mathf.Clamp(
            Mathf.CeilToInt(visibleRadius / tileSize) + 1,
            1,
            Mathf.Max(1, _tiles.LatitudeBands / 2));

        float latStep = 180f / _tiles.LatitudeBands;
        float midLatDeg = -90f + (centerLat + 0.5f) * latStep;
        float cosLat = Mathf.Max(0.15f, Mathf.Abs(Mathf.Cos(midLatDeg * Mathf.Deg2Rad)));
        int lonWindow = Mathf.Clamp(
            Mathf.CeilToInt(visibleRadius / (tileSize * cosLat)) + 1,
            1,
            Mathf.Max(1, _tiles.LongitudeBands / 2));

        float sqrRadius = visibleRadius * visibleRadius;

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

                TreePick pick = PickForCell(lat, lon);
                if (pick == TreePick.Empty)
                    continue;

                if (!_tiles.TryGetCellCenter(lat, lon, out Vector3 cellCenter))
                    continue;

                float sqrDist = (cellCenter - anchorPos).sqrMagnitude;
                if (sqrDist > sqrRadius)
                    continue;

                int prefabIndex = PrefabIndexFor(pick);
                if (PrefabAt(prefabIndex) == null)
                    continue;

                int key = PackKey(lat, lon);
                _desired.Add(key);
                if (!_active.ContainsKey(key))
                    _toSpawn.Add(new CellPick { Lat = lat, Lon = lon, PrefabIndex = prefabIndex, SqrDistance = sqrDist });
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
            if (TrySpawn(pick.Lat, pick.Lon, pick.PrefabIndex))
                spawned++;
        }
    }

    TreePick PickForCell(int lat, int lon)
    {
        if (Hash01(lat, lon, 6) >= density)
            return TreePick.Empty;
        return PickVariant(lat, lon, 8);
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
        float w6 = tree6Prefab != null ? tree6Weight : 0f;

        float total = w1 + w2 + w3 + w4 + w5 + w6;
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
        roll -= w4;
        if (roll < w5)
            return TreePick.Tree5;
        return TreePick.Tree6;
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
            case TreePick.Tree6: return Tree6Index;
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
        Vector3 position = _planet.Center + pointUp * (surfaceRadius + hover);

        Vector3 normal = _tiles.ProvidesWalkSurface
            ? _tiles.GetWalkSurfaceNormal(pointUp)
            : _planet.GetTerrainNormal(pointUp);
        if (Vector3.Dot(normal, pointUp) < 0f)
            normal = -normal;

        float yaw = Hash01(lat, lon, 40) * 360f;
        Quaternion rotation = PlanetSurfacePose.RotationFromUp(normal, yaw);

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

    static string NameFor(int prefabIndex)
    {
        switch (prefabIndex)
        {
            case Tree1Index: return "Tree1";
            case Tree2Index: return "Tree2";
            case Tree3Index: return "Tree3";
            case Tree4Index: return "Tree4";
            case Tree5Index: return "Tree5";
            case Tree6Index: return "Tree6";
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

        GameObject instance = Instantiate(prefab, _root);
        PrepareInstance(instance);
        return instance;
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
    }
}
