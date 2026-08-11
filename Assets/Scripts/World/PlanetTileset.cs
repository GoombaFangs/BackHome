using System;
using UnityEngine;

/// <summary>
/// One planet tileset: atlas texture + named tile rects + paint-able terrains with autotile masks.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Planet Tileset", fileName = "PlanetTileset")]
public class PlanetTileset : ScriptableObject
{
    public const int MaskCount = 16;

    /// <summary>Zone id used for "Shadow Grass" path tiles — walkable terrain variant.
    /// See <see cref="IsShadowGrassZone"/>.</summary>
    public const string ShadowGrassZoneId = "shadow_grass";

    /// <summary>True when <paramref name="zoneId"/> is the Shadow Grass path zone.</summary>
    public static bool IsShadowGrassZone(string zoneId) =>
        string.Equals(zoneId, ShadowGrassZoneId, StringComparison.OrdinalIgnoreCase);

    // Bit layout: N=1, E=2, S=4, W=8
    public const int BitN = 1;
    public const int BitE = 2;
    public const int BitS = 4;
    public const int BitW = 8;

    [Serializable]
    public class Entry
    {
        public string id = "Fill_Grass";
        [Tooltip("Atlas cell column (0 = left).")]
        public int column;
        [Tooltip("Atlas cell row from top of the PNG (0 = top).")]
        public int row;
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

    public Texture2D Texture => texture;
    public int TileSize => tileSize;
    public int Count => entries != null ? entries.Length : 0;
    public int TerrainCount => terrains != null ? terrains.Length : 0;
    public int BaseTerrainIndex => 0;
    public int Columns => texture != null && tileSize > 0 ? texture.width / tileSize : 0;
    public int Rows => texture != null && tileSize > 0 ? texture.height / tileSize : 0;

    public void Configure(Texture2D tex, int size, Entry[] newEntries, Terrain[] newTerrains)
    {
        texture = tex;
        tileSize = Mathf.Max(1, size);
        entries = newEntries ?? Array.Empty<Entry>();
        terrains = newTerrains ?? Array.Empty<Terrain>();
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

    public string ResolveAtlasId(int terrainIndex, int neighborMask)
    {
        Terrain t = GetTerrain(terrainIndex);
        if (t == null)
            return "Fill_Grass";

        if (terrainIndex == BaseTerrainIndex)
            return string.IsNullOrEmpty(t.fillAtlasId) ? "Fill_Grass" : t.fillAtlasId;

        neighborMask &= 0xF;
        if (t.maskToAtlasId != null
            && neighborMask < t.maskToAtlasId.Length
            && !string.IsNullOrEmpty(t.maskToAtlasId[neighborMask]))
            return t.maskToAtlasId[neighborMask];

        return string.IsNullOrEmpty(t.fillAtlasId) ? "Fill_ShadowGrass" : t.fillAtlasId;
    }

    /// <summary>Corner UVs for a cell, honoring flipU/flipV.</summary>
    public bool TryGetCornerUvs(int index, out Vector2 uvSW, out Vector2 uvSE, out Vector2 uvNE, out Vector2 uvNW)
    {
        uvSW = uvSE = uvNE = uvNW = default;
        if (!TryGetRawUvBounds(index, out float u0, out float v0, out float u1, out float v1))
            return false;

        uvSW = new Vector2(u0, v0);
        uvSE = new Vector2(u1, v0);
        uvNE = new Vector2(u1, v1);
        uvNW = new Vector2(u0, v1);
        return true;
    }

    bool TryGetRawUvBounds(int index, out float u0, out float v0, out float u1, out float v1)
    {
        u0 = v0 = u1 = v1 = 0f;
        Entry entry = GetEntry(index);
        if (entry == null || texture == null || tileSize <= 0)
            return false;

        int cols = Columns;
        int rows = Rows;
        if (cols <= 0 || rows <= 0)
            return false;

        int col = Mathf.Clamp(entry.column, 0, cols - 1);
        int rowFromTop = Mathf.Clamp(entry.row, 0, rows - 1);
        int rowFromBottom = rows - 1 - rowFromTop;

        float a = col / (float)cols;
        float b = rowFromBottom / (float)rows;
        float c = (col + 1) / (float)cols;
        float d = (rowFromBottom + 1) / (float)rows;
        float padU = 0.5f / texture.width;
        float padV = 0.5f / texture.height;
        a += padU;
        b += padV;
        c -= padU;
        d -= padV;

        u0 = entry.flipU ? c : a;
        u1 = entry.flipU ? a : c;
        v0 = entry.flipV ? d : b;
        v1 = entry.flipV ? b : d;
        return true;
    }
}
