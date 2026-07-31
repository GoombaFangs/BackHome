using System;
using UnityEngine;

/// <summary>
/// List of tile prefabs available for a planet (one palette per planet variant).
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Planet Tile Palette", fileName = "Palette_Planet")]
public class PlanetTilePalette : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string id = "grass";
        public string displayName = "Grass";
        public GameObject prefab;
        public bool walkable = true;
        public string zoneId = "default";
    }

    [SerializeField] Entry[] entries = Array.Empty<Entry>();

    public int Count => entries != null ? entries.Length : 0;

    public Entry GetEntry(int index)
    {
        if (entries == null || index < 0 || index >= entries.Length)
            return null;
        return entries[index];
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

    public bool TryGetPrefab(int index, out GameObject prefab)
    {
        prefab = null;
        Entry entry = GetEntry(index);
        if (entry == null || entry.prefab == null)
            return false;
        prefab = entry.prefab;
        return true;
    }

    public bool TryGetAlbedo(int index, out Texture2D albedo)
    {
        albedo = null;
        Entry entry = GetEntry(index);
        if (entry == null || entry.prefab == null)
            return false;

        PlanetTile tile = entry.prefab.GetComponent<PlanetTile>();
        if (tile == null || tile.Albedo == null)
            return false;

        albedo = tile.Albedo;
        return true;
    }
}
