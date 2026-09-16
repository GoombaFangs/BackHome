using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One planet tileset: paint-able terrains plus either a wang atlas or
/// seamless splat albedos (handpainted ground that tiles and blends).
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Planet Tileset", fileName = "PlanetTileset")]
public class PlanetTileset : ScriptableObject
{
    public const int MaskCount = 16;
    public const int VisualIndexMask = 0x0FFF;
    public const int VisualTransformShift = 12;

    /// <summary>Zone id used for "Shadow Grass" path tiles — walkable terrain variant.
    /// See <see cref="IsShadowGrassZone"/>.</summary>
    public const string ShadowGrassZoneId = "shadow_grass";

    /// <summary>True when <paramref name="zoneId"/> is the Shadow Grass path zone.</summary>
    public static bool IsShadowGrassZone(string zoneId) =>
        string.Equals(zoneId, ShadowGrassZoneId, StringComparison.OrdinalIgnoreCase);

    public const int BitN = 1;
    public const int BitE = 2;
    public const int BitS = 4;
    public const int BitW = 8;

    public const int TerrainGrass = 0;
    public const int TerrainLightGrass = 0;
    public const int TerrainDirt = 1;
    public const int TerrainSand = 2;
    public const int TerrainClay = 2;
    public const int TerrainDarkGrass = 3;
    public const int SplatChannelCount = 4;

    [Serializable]
    public class Entry
    {
        public string id = "Fill_Grass";
        [Tooltip("Atlas cell column (0 = left).")]
        public int column;
        [Tooltip("Atlas cell row from top of the PNG (0 = top).")]
        public int row;
        [Tooltip("Optional pixel rect from the top-left of the PNG. Used when the sheet has gutters.")]
        public int pixelX;
        public int pixelY;
        public int pixelWidth;
        public int pixelHeight;
        [Tooltip("Terrain index at each corner of this source tile (NW, NE, SW, SE).")]
        public int cornerNW;
        public int cornerNE;
        public int cornerSW;
        public int cornerSE;
        public bool flipU;
        public bool flipV;
        public bool walkable = true;
        public string zoneId = "default";
    }

    [Serializable]
    public class Terrain
    {
        public string id = "Grass";
        public string displayName = "Grass";
        public Color previewColor = Color.green;
        public bool walkable = true;
        public string zoneId = "default";
        [Tooltip("Atlas id used when this is the base terrain, or fallback fill.")]
        public string fillAtlasId = "Fill_Grass";
        [Tooltip("Length 16. mask[i] = atlas id for neighbor bitmask i. Empty = use fillAtlasId.")]
        public string[] maskToAtlasId = new string[MaskCount];
    }

    [SerializeField] Texture2D texture;
    [SerializeField] int tileSize = 32;
    [SerializeField] Entry[] entries = Array.Empty<Entry>();
    [SerializeField] Terrain[] terrains = Array.Empty<Terrain>();
    [SerializeField] Texture2D[] splatAlbedos = Array.Empty<Texture2D>();
    [SerializeField, Min(0.1f)] float splatTiling = 1f;
    [SerializeField, Range(1f, 4f)] float splatBlendSharpness = 1.65f;

    [NonSerialized] Dictionary<int, List<int>> _lookup;

    public Texture2D Texture => texture;
    public int TileSize => tileSize;
    public int Count => entries != null ? entries.Length : 0;
    public int TerrainCount => terrains != null ? terrains.Length : 0;
    public int BaseTerrainIndex => 0;
    public int Columns => texture != null && tileSize > 0 ? texture.width / tileSize : 0;
    public int Rows => texture != null && tileSize > 0 ? texture.height / tileSize : 0;
    public bool UsesSplatBlending =>
        splatAlbedos != null && splatAlbedos.Length >= 2 && splatAlbedos[0] != null;
    public float SplatTiling => Mathf.Max(0.1f, splatTiling);
    public float SplatBlendSharpness => Mathf.Clamp(splatBlendSharpness, 1f, 4f);
    public bool HasVisualSource => UsesSplatBlending || (texture != null && Count > 0);

    public Texture2D GetSplatAlbedo(int index)
    {
        if (splatAlbedos != null && index >= 0 && index < splatAlbedos.Length && splatAlbedos[index] != null)
            return splatAlbedos[index];
        return texture;
    }

    public Color SplatWeight(int terrainIndex)
    {
        int channel = Mathf.Clamp(terrainIndex, 0, SplatChannelCount - 1);
        switch (channel)
        {
            case 0: return new Color(1f, 0f, 0f, 0f);
            case 1: return new Color(0f, 1f, 0f, 0f);
            case 2: return new Color(0f, 0f, 1f, 0f);
            default: return new Color(0f, 0f, 0f, 1f);
        }
    }

    public static int PackVisual(int entryIndex, int transform) =>
        (entryIndex & VisualIndexMask) | (transform << VisualTransformShift);

    public static void UnpackVisual(int visualIndex, out int entryIndex, out int transform)
    {
        if (visualIndex < 0)
        {
            entryIndex = 0;
            transform = 0;
            return;
        }

        entryIndex = visualIndex & VisualIndexMask;
        transform = visualIndex >> VisualTransformShift;
    }

    public void Configure(Texture2D tex, int size, Entry[] newEntries, Terrain[] newTerrains)
    {
        texture = tex;
        tileSize = Mathf.Max(1, size);
        entries = newEntries ?? Array.Empty<Entry>();
        terrains = newTerrains ?? Array.Empty<Terrain>();
        _lookup = null;
    }

    public void ConfigureSplat(Texture2D[] splats, float tiling, float blendSharpness)
    {
        splatAlbedos = splats ?? Array.Empty<Texture2D>();
        splatTiling = Mathf.Max(0.1f, tiling);
        splatBlendSharpness = Mathf.Clamp(blendSharpness, 1f, 4f);
    }

    public Entry GetEntry(int index)
    {
        if (entries == null || index < 0 || index >= entries.Length)
            return null;
        return entries[index];
    }

    public Terrain GetTerrain(int index)
    {
        if (terrains == null || index < 0 || index >= terrains.Length)
            return null;
        return terrains[index];
    }

    public int IndexOfId(string id)
    {
        if (entries == null || string.IsNullOrEmpty(id))
            return -1;
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].id == id)
                return i;
        }
        return -1;
    }

    public int DefaultVisualIndex()
    {
        Terrain t = GetTerrain(BaseTerrainIndex);
        int idx = t != null ? IndexOfId(t.fillAtlasId) : -1;
        return idx >= 0 ? idx : 0;
    }

    public int IndexOfTerrainId(string id)
    {
        if (terrains == null || string.IsNullOrEmpty(id))
            return -1;
        for (int i = 0; i < terrains.Length; i++)
        {
            if (terrains[i] != null && terrains[i].id == id)
                return i;
        }
        return -1;
    }

    string DefaultFillAtlasId()
    {
        Terrain baseTerrain = GetTerrain(BaseTerrainIndex);
        if (baseTerrain != null && !string.IsNullOrEmpty(baseTerrain.fillAtlasId))
            return baseTerrain.fillAtlasId;
        if (entries != null && entries.Length > 0 && entries[0] != null)
            return entries[0].id;
        return "Fill_Grass";
    }

    public string ResolveAtlasId(int terrainIndex, int neighborMask)
    {
        Terrain t = GetTerrain(terrainIndex);
        if (t == null)
            return DefaultFillAtlasId();

        neighborMask &= 0xF;
        if (t.maskToAtlasId != null
            && neighborMask < t.maskToAtlasId.Length
            && !string.IsNullOrEmpty(t.maskToAtlasId[neighborMask]))
            return t.maskToAtlasId[neighborMask];

        return string.IsNullOrEmpty(t.fillAtlasId) ? DefaultFillAtlasId() : t.fillAtlasId;
    }

    /// <summary>
    /// Picks a sheet tile whose four corners match the wang corners of this cell.
    /// Fill cells are chosen uniformly at random among every variant of that terrain.
    /// </summary>
    public int ResolveVisual(int nw, int ne, int sw, int se, int lat, int lon)
    {
        EnsureLookup();
        List<int> list = FindVisuals(nw, ne, sw, se);
        if (list == null || list.Count == 0)
        {
            int cnw = CollapseDirtFamily(nw);
            int cne = CollapseDirtFamily(ne);
            int csw = CollapseDirtFamily(sw);
            int cse = CollapseDirtFamily(se);
            if (cnw != nw || cne != ne || csw != sw || cse != se)
                list = FindVisuals(cnw, cne, csw, cse);
        }

        if ((list == null || list.Count == 0) && nw == ne && ne == sw && sw == se)
            list = FindVisuals(sw, sw, sw, sw);

        if (list == null || list.Count == 0)
        {
            int fill = IndexOfId(GetTerrain(sw)?.fillAtlasId);
            return fill >= 0 ? fill : 0;
        }

        int pick = StableHash(lat, lon, sw) % list.Count;
        if (pick < 0)
            pick += list.Count;
        return list[pick];
    }

    /// <summary>Corner UVs for a packed visual index (entry + rot/flip transform).</summary>
    public bool TryGetCornerUvs(int visualIndex, out Vector2 uvSW, out Vector2 uvSE, out Vector2 uvNE, out Vector2 uvNW)
    {
        uvSW = uvSE = uvNE = uvNW = default;
        UnpackVisual(visualIndex, out int entryIndex, out int transform);
        if (!TryGetRawUvBounds(entryIndex, out float u0, out float v0, out float u1, out float v1))
            return false;

        uvSW = new Vector2(u0, v0);
        uvSE = new Vector2(u1, v0);
        uvNE = new Vector2(u1, v1);
        uvNW = new Vector2(u0, v1);
        ApplyUvTransform(ref uvSW, ref uvSE, ref uvNE, ref uvNW, transform);
        return true;
    }

    public bool IsValidVisual(int visualIndex)
    {
        UnpackVisual(visualIndex, out int entryIndex, out _);
        return GetEntry(entryIndex) != null;
    }

    void EnsureLookup()
    {
        if (_lookup != null)
            return;

        _lookup = new Dictionary<int, List<int>>();
        if (entries == null)
            return;

        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            if (entry == null)
                continue;

            int key = PackCorners(entry.cornerNW, entry.cornerNE, entry.cornerSW, entry.cornerSE);
            if (!_lookup.TryGetValue(key, out List<int> list))
            {
                list = new List<int>();
                _lookup[key] = list;
            }

            if (!ListHasEntry(list, i))
                list.Add(PackVisual(i, 0));
        }

        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            if (entry == null)
                continue;
            if (entry.cornerNW == entry.cornerNE
                && entry.cornerNE == entry.cornerSW
                && entry.cornerSW == entry.cornerSE)
                continue;

            for (int transform = 1; transform < 8; transform++)
            {
                TransformCorners(
                    entry.cornerNW, entry.cornerNE, entry.cornerSW, entry.cornerSE,
                    transform, out int nw, out int ne, out int sw, out int se);
                int key = PackCorners(nw, ne, sw, se);
                if (_lookup.ContainsKey(key))
                    continue;
                _lookup[key] = new List<int> { PackVisual(i, transform) };
            }
        }
    }

    List<int> FindVisuals(int nw, int ne, int sw, int se)
    {
        if (_lookup == null)
            return null;
        _lookup.TryGetValue(PackCorners(nw, ne, sw, se), out List<int> list);
        return list;
    }

    static int PackCorners(int nw, int ne, int sw, int se) =>
        (nw & 255) | ((ne & 255) << 8) | ((sw & 255) << 16) | ((se & 255) << 24);

    static int CollapseDirtFamily(int terrain) => terrain;

    // Kept for packed visual indices saved before Tile Set 02 identity lookup.

    static bool ListHasEntry(List<int> list, int entryIndex)
    {
        for (int i = 0; i < list.Count; i++)
        {
            UnpackVisual(list[i], out int existing, out _);
            if (existing == entryIndex)
                return true;
        }

        return false;
    }

    static int StableHash(int lat, int lon, int extra)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)lat) * 16777619u;
            h = (h ^ (uint)lon) * 16777619u;
            h = (h ^ (uint)extra) * 16777619u;
            return (int)(h & 0x7fffffff);
        }
    }

    static void TransformCorners(
        int nw, int ne, int sw, int se,
        int transform,
        out int onw, out int one, out int osw, out int ose)
    {
        int rot = transform & 3;
        bool flipU = (transform & 4) != 0;
        for (int i = 0; i < rot; i++)
        {
            int nnw = sw;
            int nne = nw;
            int nsw = se;
            int nse = ne;
            nw = nnw;
            ne = nne;
            sw = nsw;
            se = nse;
        }

        if (flipU)
        {
            int t = nw;
            nw = ne;
            ne = t;
            t = sw;
            sw = se;
            se = t;
        }

        onw = nw;
        one = ne;
        osw = sw;
        ose = se;
    }

    static void ApplyUvTransform(
        ref Vector2 uvSW, ref Vector2 uvSE, ref Vector2 uvNE, ref Vector2 uvNW,
        int transform)
    {
        int rot = transform & 3;
        bool flipU = (transform & 4) != 0;
        for (int i = 0; i < rot; i++)
        {
            Vector2 nSW = uvSE;
            Vector2 nSE = uvNE;
            Vector2 nNE = uvNW;
            Vector2 nNW = uvSW;
            uvSW = nSW;
            uvSE = nSE;
            uvNE = nNE;
            uvNW = nNW;
        }

        if (flipU)
        {
            Vector2 t = uvSW;
            uvSW = uvSE;
            uvSE = t;
            t = uvNW;
            uvNW = uvNE;
            uvNE = t;
        }
    }

    bool TryGetRawUvBounds(int index, out float u0, out float v0, out float u1, out float v1)
    {
        u0 = v0 = u1 = v1 = 0f;
        Entry entry = GetEntry(index);
        if (entry == null || texture == null)
            return false;

        float texW = texture.width;
        float texH = texture.height;
        if (texW <= 0f || texH <= 0f)
            return false;

        float x;
        float yFromTop;
        float w;
        float h;
        if (entry.pixelWidth > 0 && entry.pixelHeight > 0)
        {
            x = entry.pixelX;
            yFromTop = entry.pixelY;
            w = entry.pixelWidth;
            h = entry.pixelHeight;
        }
        else
        {
            if (tileSize <= 0)
                return false;
            int cols = Columns;
            int rows = Rows;
            if (cols <= 0 || rows <= 0)
                return false;

            int col = Mathf.Clamp(entry.column, 0, cols - 1);
            int rowFromTop = Mathf.Clamp(entry.row, 0, rows - 1);
            x = col * (texW / cols);
            yFromTop = rowFromTop * (texH / rows);
            w = texW / cols;
            h = texH / rows;
        }

        // Inner rect only. Neighbor bleed is stopped by atlas edge extrusion,
        // not by shrinking the tile (that made visible grid seams).
        float a = x / texW;
        float c = (x + w) / texW;
        float rowFromBottom = texH - yFromTop - h;
        float b = rowFromBottom / texH;
        float d = (rowFromBottom + h) / texH;

        u0 = entry.flipU ? c : a;
        u1 = entry.flipU ? a : c;
        v0 = entry.flipV ? d : b;
        v1 = entry.flipV ? b : d;
        return true;
    }
}
