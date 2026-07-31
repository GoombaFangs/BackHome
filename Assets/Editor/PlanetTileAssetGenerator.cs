using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Regenerates / refreshes planet tile assets. Prefer using existing prefabs;
/// this menu helps recreate them if missing.
/// </summary>
public static class PlanetTileAssetGenerator
{
    const string TilesFolder = "Assets/Galaxy/Planets/Prefabs/Tiles";
    const string PlanetFolder = "Assets/Galaxy/Planets/Prefabs";
    const string SoFolder = "Assets/Galaxy/Planets/ScriptableObjects";

    [MenuItem("BackHome/Generate Planet Tile Assets")]
    public static void GenerateAll()
    {
        EnsureFolder("Assets/Galaxy");
        EnsureFolder("Assets/Galaxy/Planets");
        EnsureFolder("Assets/Galaxy/Planets/Prefabs");
        EnsureFolder(TilesFolder);
        EnsureFolder(SoFolder);

        const string Pack = "Assets/AssetStore/Handpainted_Grass_and_Ground_Textures/Textures";
        Texture2D dirt = LoadTex($"{Pack}/Dirt/dirt_clay/dirt_clay_down.png");
        Texture2D grass = LoadTex($"{Pack}/Grass/Grass_normal/Grass_normal_up.png");
        Texture2D snow = LoadTex($"{Pack}/Snow/snow_normal.png");

        if (grass == null || dirt == null || snow == null)
        {
            Debug.LogError(
                "[BackHome] Missing Handpainted_Grass_and_Ground_Textures. " +
                "Expected under Assets/AssetStore/Handpainted_Grass_and_Ground_Textures.");
            return;
        }

        // Default planet surface = clay dirt; grass/snow are paint options.
        CreateOrUpdateTilePrefab($"{TilesFolder}/TileBase.prefab", "TileBase", "base", dirt, true, "default");
        GameObject tileDirt = CreateOrUpdateTilePrefab($"{TilesFolder}/TileDirt.prefab", "TileDirt", "dirt", dirt, true, "barren");
        GameObject tileGrass = CreateOrUpdateTilePrefab($"{TilesFolder}/TileGrass.prefab", "TileGrass", "grass", grass, true, "grassland");
        GameObject tileSnow = CreateOrUpdateTilePrefab($"{TilesFolder}/TileSnow.prefab", "TileSnow", "snow", snow, true, "tundra");

        PlanetTilePalette palette = CreateOrUpdatePalette(
            $"{SoFolder}/Palette_PlanetA.asset",
            tileDirt,
            tileGrass,
            tileSnow);

        CreateOrUpdatePlanetPrefab($"{PlanetFolder}/PlanetBase.prefab", "PlanetBase", 40f, dirt, palette);

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
        Debug.Log("[BackHome] Planet tile assets ready. Select PlanetA in the scene, use PlanetTileMap inspector to Fill/Paint.");
    }

    static Texture2D LoadTex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

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

    static PlanetTilePalette CreateOrUpdatePalette(string path, GameObject dirt, GameObject grass, GameObject snow)
    {
        PlanetTilePalette palette = AssetDatabase.LoadAssetAtPath<PlanetTilePalette>(path);
        if (palette == null)
        {
            palette = ScriptableObject.CreateInstance<PlanetTilePalette>();
            AssetDatabase.CreateAsset(palette, path);
        }

        var so = new SerializedObject(palette);
        SerializedProperty entries = so.FindProperty("entries");
        entries.arraySize = 3;
        SetEntry(entries.GetArrayElementAtIndex(0), "dirt", "Dirt", dirt, true, "barren");
        SetEntry(entries.GetArrayElementAtIndex(1), "grass", "Grass", grass, true, "grassland");
        SetEntry(entries.GetArrayElementAtIndex(2), "snow", "Snow", snow, true, "tundra");
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
        // Casual low-poly shell: handpainted color, soft hills, no photoreal normals.
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
