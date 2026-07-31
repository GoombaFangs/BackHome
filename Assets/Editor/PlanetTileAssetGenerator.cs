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

        // Intentionally left empty for now: we don't want hard dependency on the old handpainted pack.
        Texture2D grass = null;
        Texture2D dirt = null;
        Texture2D snow = null;

        CreateOrUpdateTilePrefab($"{TilesFolder}/TileBase.prefab", "TileBase", "base", grass, true, "default");
        GameObject tileGrass = CreateOrUpdateTilePrefab($"{TilesFolder}/Tile_Grass.prefab", "Tile_Grass", "grass", grass, true, "grassland");
        GameObject tileDirt = CreateOrUpdateTilePrefab($"{TilesFolder}/Tile_Dirt.prefab", "Tile_Dirt", "dirt", dirt, true, "barren");
        GameObject tileSnow = CreateOrUpdateTilePrefab($"{TilesFolder}/Tile_Snow.prefab", "Tile_Snow", "snow", snow, true, "tundra");

        PlanetTilePalette palette = CreateOrUpdatePalette(
            $"{SoFolder}/Palette_PlanetA.asset",
            tileGrass,
            tileDirt,
            tileSnow);

        CreateOrUpdatePlanetPrefab($"{PlanetFolder}/PlanetBase.prefab", "PlanetBase", 40f, grass, palette);

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

    static PlanetTilePalette CreateOrUpdatePalette(string path, GameObject grass, GameObject dirt, GameObject snow)
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
        SetEntry(entries.GetArrayElementAtIndex(0), "grass", "Grass", grass, true, "grassland");
        SetEntry(entries.GetArrayElementAtIndex(1), "dirt", "Dirt", dirt, true, "barren");
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
        planetSo.FindProperty("albedoTexture").objectReferenceValue = albedo;
        planetSo.FindProperty("tint").colorValue = Color.white;
        planetSo.FindProperty("textureTiling").floatValue = 8f;
        planetSo.ApplyModifiedPropertiesWithoutUndo();

        var map = go.AddComponent<PlanetTileMap>();
        var mapSo = new SerializedObject(map);
        mapSo.FindProperty("palette").objectReferenceValue = palette;
        mapSo.FindProperty("tilesAroundEquator").intValue = 48;
        mapSo.FindProperty("fillTileIndex").intValue = 0;
        mapSo.FindProperty("hidePlanetBaseMesh").boolValue = true;
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
