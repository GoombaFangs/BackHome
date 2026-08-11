using System;
using UnityEngine;

/// <summary>
/// Shared, seed-driven biome layout for a planet: partitions the sphere into a handful of
/// organic blob-shaped regions (nearest-seed / Voronoi-style, same deterministic-noise spirit as
/// <see cref="PlanetBlobAutotile.GenerateContinents"/>), and lists which tree/grass/rock/creature
/// prefabs are allowed to spawn in each one.
///
/// <see cref="PlanetGrassStreamer"/>, <see cref="PlanetTreeStreamer"/>, <see cref="PlanetRockStreamer"/>
/// and <see cref="CreatureSpawner"/> all reference the same asset instance so their region
/// boundaries line up spatially — a tile that's "Region A" for grass is also "Region A" for
/// trees/rocks/creatures.
///
/// Menu: BackHome → Setup Nyxara Environment Regions (creates + wires an example asset).
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Planet Environment Region Set", fileName = "PlanetEnvironmentRegionSet")]
public class PlanetEnvironmentRegionSet : ScriptableObject
{
    [Serializable]
    public class WeightedPrefab
    {
        public GameObject prefab;
        [Min(0f)] public float weight = 1f;
    }

    [Serializable]
    public class Region
    {
        public string name = "Region";
        [Tooltip("Tree variants allowed in this region (streamed by PlanetTreeStreamer).")]
        public WeightedPrefab[] trees = Array.Empty<WeightedPrefab>();
        [Tooltip("Grass variants allowed in this region (streamed by PlanetGrassStreamer).")]
        public WeightedPrefab[] grass = Array.Empty<WeightedPrefab>();
        [Tooltip("Rock variants allowed in this region (streamed by PlanetRockStreamer).")]
        public WeightedPrefab[] rocks = Array.Empty<WeightedPrefab>();
        [Tooltip("Creatures confined to this region's area (spawned by CreatureSpawner, additive to its global spawnEntries). Fine to leave empty — that region just has no creatures.")]
        public CreatureSpawner.SpawnEntry[] creatures = Array.Empty<CreatureSpawner.SpawnEntry>();
    }

    [Tooltip("Seed for the random blob layout. Same seed + same region count/blobs-per-region always reproduces the same boundaries.")]
    [SerializeField] int seed = 11;
    [Tooltip("Separate scattered patches per region. 1 = one big contiguous blob per region; higher = several smaller patches for a less blocky, more organic look.")]
    [SerializeField, Min(1)] int blobsPerRegion = 1;
    [SerializeField] Region[] regions = Array.Empty<Region>();

    const float MaxRegionPointJitterDegrees = 25f;
    const int MaxRegionPointAttempts = 8;

    public int RegionCount => regions != null ? regions.Length : 0;

    [NonSerialized] Vector3[] _cachedSeedDirs;
    [NonSerialized] int[] _cachedSeedRegion;
    [NonSerialized] int _cachedSeed;
    [NonSerialized] int _cachedBlobsPerRegion;
    [NonSerialized] int _cachedRegionCount;
    [NonSerialized] bool _cacheBuilt;

    /// <summary>Debug/gizmo access to the cached blob seeds — lets streamers sketch region layout
    /// in the Scene view without duplicating the seed-generation logic.</summary>
    public int DebugSeedCount
    {
        get
        {
            EnsureSeedCache();
            return _cachedSeedDirs?.Length ?? 0;
        }
    }

    public Vector3 DebugSeedDirection(int index) => _cachedSeedDirs[index];
    public int DebugSeedRegion(int index) => _cachedSeedRegion[index];

    public Region GetRegion(int index)
    {
        if (regions == null || index < 0 || index >= regions.Length)
            return null;
        return regions[index];
    }

    /// <summary>Region index owning the nearest seed point to <paramref name="up"/>, or -1 if no regions are configured.</summary>
    public int GetRegionIndex(Vector3 up)
    {
        EnsureSeedCache();
        if (_cachedSeedDirs == null || _cachedSeedDirs.Length == 0)
            return -1;

        Vector3 dir = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        int best = 0;
        float bestDot = float.NegativeInfinity;
        for (int i = 0; i < _cachedSeedDirs.Length; i++)
        {
            float dot = Vector3.Dot(dir, _cachedSeedDirs[i]);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }

        return _cachedSeedRegion[best];
    }

    /// <summary>
    /// Samples a direction that lies inside <paramref name="regionIndex"/>'s area by jittering
    /// around one of its cached blob seeds — far cheaper and more reliable than rejection-sampling
    /// the whole sphere, especially for a small region.
    /// </summary>
    public bool TryGetRandomPointInRegion(int regionIndex, System.Random rng, out Vector3 up)
    {
        up = Vector3.up;
        if (rng == null || regionIndex < 0)
            return false;

        EnsureSeedCache();
        if (_cachedSeedDirs == null || _cachedSeedDirs.Length == 0)
            return false;

        Vector3 seedDir = Vector3.zero;
        int matchCount = 0;
        for (int i = 0; i < _cachedSeedRegion.Length; i++)
        {
            if (_cachedSeedRegion[i] != regionIndex)
                continue;
            matchCount++;
            // Reservoir sampling — picks uniformly among this region's seeds in one pass.
            if (rng.Next(matchCount) == 0)
                seedDir = _cachedSeedDirs[i];
        }

        if (matchCount == 0)
            return false;

        for (int attempt = 0; attempt < MaxRegionPointAttempts; attempt++)
        {
            float angle = (float)rng.NextDouble() * MaxRegionPointJitterDegrees;
            float spin = (float)rng.NextDouble() * 360f;
            Vector3 candidate = JitterDirection(seedDir, angle, spin);
            if (GetRegionIndex(candidate) == regionIndex)
            {
                up = candidate;
                return true;
            }
        }

        // Boundary got unlucky every attempt — the seed itself is always safely inside its region.
        up = seedDir;
        return true;
    }

    /// <summary>Weighted pick among whichever entries actually have a prefab assigned; returns null
    /// if the array is empty/unassigned so a region can legitimately have none of this category.</summary>
    public static GameObject PickWeighted(WeightedPrefab[] entries, float roll01)
    {
        if (entries == null || entries.Length == 0)
            return null;

        float total = 0f;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].prefab != null)
                total += Mathf.Max(0f, entries[i].weight);
        }

        if (total <= 0f)
            return null;

        float roll = Mathf.Clamp01(roll01) * total;
        for (int i = 0; i < entries.Length; i++)
        {
            WeightedPrefab entry = entries[i];
            if (entry == null || entry.prefab == null)
                continue;

            float w = Mathf.Max(0f, entry.weight);
            if (roll < w)
                return entry.prefab;
            roll -= w;
        }

        return null;
    }

    static Vector3 JitterDirection(Vector3 dir, float angleDegrees, float spinDegrees)
    {
        Vector3 axis = Vector3.Cross(dir, Vector3.up);
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.Cross(dir, Vector3.right);
        axis = (Quaternion.AngleAxis(spinDegrees, dir) * axis).normalized;
        return (Quaternion.AngleAxis(angleDegrees, axis) * dir).normalized;
    }

    void EnsureSeedCache()
    {
        int regionCount = RegionCount;
        int clampedBlobs = Mathf.Max(1, blobsPerRegion);
        if (_cacheBuilt
            && _cachedSeed == seed
            && _cachedBlobsPerRegion == clampedBlobs
            && _cachedRegionCount == regionCount)
            return;

        BuildSeedCache(regionCount, clampedBlobs);
    }

    void BuildSeedCache(int regionCount, int blobsPerRegionClamped)
    {
        _cacheBuilt = true;
        _cachedSeed = seed;
        _cachedBlobsPerRegion = blobsPerRegionClamped;
        _cachedRegionCount = regionCount;

        if (regionCount <= 0)
        {
            _cachedSeedDirs = Array.Empty<Vector3>();
            _cachedSeedRegion = Array.Empty<int>();
            return;
        }

        int total = regionCount * blobsPerRegionClamped;
        _cachedSeedDirs = new Vector3[total];
        _cachedSeedRegion = new int[total];

        var rng = new System.Random(seed);
        int idx = 0;
        for (int r = 0; r < regionCount; r++)
        {
            for (int b = 0; b < blobsPerRegionClamped; b++)
            {
                _cachedSeedDirs[idx] = RandomDirectionOnSphere(rng);
                _cachedSeedRegion[idx] = r;
                idx++;
            }
        }
    }

    static Vector3 RandomDirectionOnSphere(System.Random rng)
    {
        double z = rng.NextDouble() * 2.0 - 1.0;
        double theta = rng.NextDouble() * Math.PI * 2.0;
        double ringRadius = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
        return new Vector3((float)(ringRadius * Math.Cos(theta)), (float)z, (float)(ringRadius * Math.Sin(theta)));
    }

    void OnValidate()
    {
        blobsPerRegion = Mathf.Max(1, blobsPerRegion);
        _cacheBuilt = false;
    }
}
