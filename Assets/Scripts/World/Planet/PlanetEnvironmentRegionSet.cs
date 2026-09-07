using System;
using UnityEngine;

/// <summary>
/// Shared, seed-driven biome layout for a planet: partitions the sphere into a handful of
/// organic blob-shaped regions (nearest-seed / Voronoi-style, same deterministic-noise spirit as
/// <see cref="PlanetBlobAutotile.GenerateContinents"/>), and lists which tree/grass/rock prefabs
/// belong in each one.
///
/// Creatures are configured separately via <see cref="CreatureSpawner"/> (spawnEntries / spawnPoints)
/// — this asset is environment-only.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Planet Environment Region Set", fileName = "PlanetEnvironmentRegionSet")]
public class PlanetEnvironmentRegionSet : ScriptableObject
{
    [Serializable]
    public class WeightedPrefab
    {
        public GameObject prefab;
        [Tooltip("Relative mix between variants in the same category (does not control overall amount — use the region's tree/grass/rock Density fields for that).")]
        [Min(0f)] public float weight = 1f;
    }

    [Serializable]
    public class Region
    {
        public string name = "Region";
        [Tooltip("Tree variants allowed in this region.")]
        public WeightedPrefab[] trees = Array.Empty<WeightedPrefab>();
        [Tooltip("Grass variants allowed in this region.")]
        public WeightedPrefab[] grass = Array.Empty<WeightedPrefab>();
        [Tooltip("Rock variants allowed in this region.")]
        public WeightedPrefab[] rocks = Array.Empty<WeightedPrefab>();

        [Header("Amount (per region)")]
        [Tooltip("Multiplier on global tree density in this region. 0 = no trees, 1 = same as global, 2 = twice as many.")]
        [Min(0f)] public float treeDensity = 1f;
        [Tooltip("Multiplier on global grass density in this region.")]
        [Min(0f)] public float grassDensity = 1f;
        [Tooltip("Multiplier on global rock density in this region.")]
        [Min(0f)] public float rockDensity = 1f;
    }

    [Tooltip("Seed for the random blob layout. Same seed + same region count/blobs-per-region always reproduces the same boundaries.")]
    [SerializeField] int seed = 11;
    [Tooltip("Separate scattered patches per region. 1 = one big contiguous blob per region; higher = several smaller patches for a less blocky, more organic look.")]
    [SerializeField, Min(1)] int blobsPerRegion = 1;
    [SerializeField] Region[] regions = Array.Empty<Region>();

    public int RegionCount => regions != null ? regions.Length : 0;

    [NonSerialized] Vector3[] _cachedSeedDirs;
    [NonSerialized] int[] _cachedSeedRegion;
    [NonSerialized] int _cachedSeed;
    [NonSerialized] int _cachedBlobsPerRegion;
    [NonSerialized] int _cachedRegionCount;
    [NonSerialized] bool _cacheBuilt;

    /// <summary>Debug/gizmo access to the cached blob seeds — lets tools sketch region layout
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

    /// <summary>Effective spawn density for a category: global density × this region's multiplier (clamped 0–1).</summary>
    public float GetEffectiveDensity(float streamerDensity, int regionIndex, float regionMultiplier)
    {
        if (regionIndex < 0)
            return streamerDensity;

        return Mathf.Clamp01(streamerDensity * Mathf.Max(0f, regionMultiplier));
    }

    public float GetTreeDensityMultiplier(int regionIndex) => GetRegion(regionIndex)?.treeDensity ?? 1f;
    public float GetGrassDensityMultiplier(int regionIndex) => GetRegion(regionIndex)?.grassDensity ?? 1f;
    public float GetRockDensityMultiplier(int regionIndex) => GetRegion(regionIndex)?.rockDensity ?? 1f;

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
        if (regions != null)
        {
            for (int i = 0; i < regions.Length; i++)
            {
                if (regions[i] == null)
                    continue;

                regions[i].treeDensity = Mathf.Max(0f, regions[i].treeDensity);
                regions[i].grassDensity = Mathf.Max(0f, regions[i].grassDensity);
                regions[i].rockDensity = Mathf.Max(0f, regions[i].rockDensity);
            }
        }

        _cacheBuilt = false;
    }
}
