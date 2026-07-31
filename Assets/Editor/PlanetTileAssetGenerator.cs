using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Regenerates planet tile prefabs from Handpainted textures under
/// Assets/Galaxy/Planets/Textures and rebuilds Palette_PlanetA.
/// </summary>
public static class PlanetTileAssetGenerator
{
    const string TilesFolder = "Assets/Galaxy/Planets/Prefabs/Tiles";
    const string PlanetFolder = "Assets/Galaxy/Planets/Prefabs";
    const string SoFolder = "Assets/Galaxy/Planets/ScriptableObjects";
    const string TexRoot = "Assets/Galaxy/Planets/Textures";

    struct TileDef
    {
        public string TexturePath;
        public string ZoneId;
        public string Category;
    }

    // Prefab name = texture file name (without extension).
    static readonly TileDef[] TileDefs =
    {
        // Dirt (5) — default fill is dirt_clay_down (index 0)
        new TileDef { TexturePath = TexRoot + "/Dirt/dirt_clay/dirt_clay_down.png", ZoneId = "barren", Category = "dirt" },
        new TileDef { TexturePath = TexRoot + "/Dirt/dirt_claydarked/dirt_claydarked_down.png", ZoneId = "barren", Category = "dirt" },
        new TileDef { TexturePath = TexRoot + "/Dirt/dirt_lighted/dirt_lighted_down.png", ZoneId = "barren", Category = "dirt" },
        new TileDef { TexturePath = TexRoot + "/Dirt/dirt_normal/dirt_normal_down.png", ZoneId = "barren", Category = "dirt" },
        new TileDef { TexturePath = TexRoot + "/Dirt/dirt_desatured_rocks/dirt_desatured_rocks_down.png", ZoneId = "barren", Category = "dirt" },

        // Grass (5)
        new TileDef { TexturePath = TexRoot + "/Grass/Grass_normal/Grass_normal_up.png", ZoneId = "grassland", Category = "grass" },
        new TileDef { TexturePath = TexRoot + "/Grass/Grass_lighted/Grass_lighted_up.png", ZoneId = "grassland", Category = "grass" },
        new TileDef { TexturePath = TexRoot + "/Grass/Grass_darked/Grass_darked_up.png", ZoneId = "grassland", Category = "grass" },
        new TileDef { TexturePath = TexRoot + "/Grass/Grass_bluetint/Grass_bluetint_up.png", ZoneId = "grassland", Category = "grass" },
        new TileDef { TexturePath = TexRoot + "/Grass/Grass_swamp_normal/Grass_swamp_normal_up.png", ZoneId = "grassland", Category = "grass" },

        // Snow (5)
        new TileDef { TexturePath = TexRoot + "/Snow/snow_normal.png", ZoneId = "tundra", Category = "snow" },
        new TileDef { TexturePath = TexRoot + "/Snow/snow_dark.png", ZoneId = "tundra", Category = "snow" },
        new TileDef { TexturePath = TexRoot + "/Snow/snow_super_dark.png", ZoneId = "tundra", Category = "snow" },
        new TileDef { TexturePath = TexRoot + "/Snow/snow_step_001.png", ZoneId = "tundra", Category = "snow" },
        new TileDef { TexturePath = TexRoot + "/Snow/snow_dark_step_001.png", ZoneId = "tundra", Category = "snow" },
    };

    [MenuItem("BackHome/Generate Planet Tile Assets")]
    public static void GenerateAll()
    {
        EnsureFolder("Assets/Galaxy");
        EnsureFolder("Assets/Galaxy/Planets");
        EnsureFolder("Assets/Galaxy/Planets/Prefabs");
        EnsureFolder(TilesFolder);
        EnsureFolder(SoFolder);

        var created = new List<GameObject>(TileDefs.Length);
        Texture2D defaultDirt = null;

        for (int i = 0; i < TileDefs.Length; i++)
        {
            TileDef def = TileDefs[i];
            Texture2D tex = LoadTex(def.TexturePath);
            if (tex == null)
            {
                Debug.LogError($"[BackHome] Missing texture: {def.TexturePath}");
                return;
            }

            string tileName = Path.GetFileNameWithoutExtension(def.TexturePath);
            GameObject prefab = CreateOrUpdateTilePrefab(
                $"{TilesFolder}/{tileName}.prefab",
                tileName,
                tileName,
                tex,
                true,
                def.ZoneId);
            created.Add(prefab);

            if (i == 0)
                defaultDirt = tex;
        }

        CreateOrUpdateTilePrefab(
            $"{TilesFolder}/TileBase.prefab",
            "TileBase",
            "base",
            defaultDirt,
            true,
            "default");

        // Remove legacy generic names if still present.
        DeleteIfExists($"{TilesFolder}/TileDirt.prefab");
        DeleteIfExists($"{TilesFolder}/TileGrass.prefab");
        DeleteIfExists($"{TilesFolder}/TileSnow.prefab");

        PlanetTilePalette palette = CreateOrUpdatePalette($"{SoFolder}/Palette_PlanetA.asset", created);
        CreateOrUpdatePlanetPrefab($"{PlanetFolder}/PlanetBase.prefab", "PlanetBase", 40f, defaultDirt, palette);

        string planetAPath = $"{PlanetFolder}/PlanetA.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(planetAPath) == null)
        {
            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PlanetFolder}/PlanetBase.prefab");
            GameObject instance = PrefabUtility.InstantiatePrefab(basePrefab) as GameObject;
            instance.name = "PlanetA";
            PrefabUtility.SaveAsPrefabAsset(instance, planetAPath);
            Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[BackHome] Generated {created.Count} planet tiles (5 dirt / 5 grass / 5 snow). Default fill: dirt_clay_down.");
    }

    static Texture2D LoadTex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

    static void DeleteIfExists(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            AssetDatabase.DeleteAsset(path);
    }

    static GameObject CreateOrUpdateTilePrefab(
        string path,
        string name,
        string tileId,
        Texture2D albedo,
        bool walkable,
        string zoneId)
    {
        var go = new GameObject(name);
        var filter = go.AddComponent<MeshFilter>();
        var temp = GameObject.CreatePrimitive(PrimitiveType.Plane);
        filter.sharedMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temp);

        go.AddComponent<MeshRenderer>();
        var tile = go.AddComponent<PlanetTile>();
        tile.Configure(tileId, walkable, zoneId, albedo, Color.white);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    static PlanetTilePalette CreateOrUpdatePalette(string path, List<GameObject> tiles)
    {
        PlanetTilePalette palette = AssetDatabase.LoadAssetAtPath<PlanetTilePalette>(path);
        if (palette == null)
        {
            palette = ScriptableObject.CreateInstance<PlanetTilePalette>();
            AssetDatabase.CreateAsset(palette, path);
        }

        var so = new SerializedObject(palette);
        SerializedProperty entries = so.FindProperty("entries");
        entries.arraySize = tiles.Count;
        for (int i = 0; i < tiles.Count; i++)
        {
            GameObject prefab = tiles[i];
            string id = prefab != null ? prefab.name : $"tile_{i}";
            string zone = "default";
            PlanetTile tile = prefab != null ? prefab.GetComponent<PlanetTile>() : null;
            if (tile != null)
                zone = tile.ZoneId;

            SetEntry(entries.GetArrayElementAtIndex(i), id, id, prefab, true, zone);
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(palette);
        return palette;
    }

    static void SetEntry(
        SerializedProperty prop,
        string id,
        string displayName,
        GameObject prefab,
        bool walkable,
        string zoneId)
    {
        prop.FindPropertyRelative("id").stringValue = id;
        prop.FindPropertyRelative("displayName").stringValue = displayName;
        prop.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        prop.FindPropertyRelative("walkable").boolValue = walkable;
        prop.FindPropertyRelative("zoneId").stringValue = zoneId;
    }

    static void CreateOrUpdatePlanetPrefab(
        string path,
        string name,
        float radius,
        Texture2D albedo,
        PlanetTilePalette palette)
    {
        GameObject go = new GameObject(name);
        var planet = go.AddComponent<SphericalPlanet>();
        var planetSo = new SerializedObject(planet);
        planetSo.FindProperty("radius").floatValue = radius;
        planetSo.FindProperty("latitudeSegments").intValue = 20;
        planetSo.FindProperty("longitudeSegments").intValue = 28;
        planetSo.FindProperty("albedoTexture").objectReferenceValue = albedo;
        planetSo.FindProperty("tint").colorValue = Color.white;
        planetSo.FindProperty("textureTiling").floatValue = 4f;
        planetSo.FindProperty("useVisualShell").boolValue = true;
        planetSo.FindProperty("shellColorMap").objectReferenceValue = albedo;
        planetSo.FindProperty("shellNormalMap").objectReferenceValue = null;
        planetSo.FindProperty("shellHeightAmplitude").floatValue = 1.6f;
        planetSo.FindProperty("shellSmoothness").floatValue = 0.05f;
        planetSo.FindProperty("shellNormalStrength").floatValue = 0f;
        planetSo.FindProperty("shellLatitudeSegments").intValue = 24;
        planetSo.FindProperty("shellLongitudeSegments").intValue = 32;
        planetSo.ApplyModifiedPropertiesWithoutUndo();

        var map = go.AddComponent<PlanetTileMap>();
        var mapSo = new SerializedObject(map);
        mapSo.FindProperty("palette").objectReferenceValue = palette;
        mapSo.FindProperty("tilesAroundEquator").intValue = 24;
        mapSo.FindProperty("fillTileIndex").intValue = 0;
        mapSo.FindProperty("hidePlanetBaseMesh").boolValue = true;
        mapSo.FindProperty("showTileVisuals").boolValue = true;
        mapSo.FindProperty("castTileShadows").boolValue = false;
        mapSo.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
        string folderName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folderName);
    }
}
