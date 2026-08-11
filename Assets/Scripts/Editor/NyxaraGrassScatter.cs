using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor tooling for Nyxara grass. The recommended path is runtime streaming
/// (<see cref="PlanetGrassStreamer"/>) — it keeps only a small, capped number of grass instances
/// alive near the player instead of pre-placing thousands of static GameObjects across the whole
/// planet. The static <see cref="Scatter"/> path is kept only as a manual fallback for hand-authored
/// decoration on small areas; avoid it for whole-planet coverage.
/// Menu: BackHome → Setup Nyxara Grass Streaming (recommended) / Scatter Nyxara Grass (legacy, static)
/// </summary>
public static class NyxaraGrassScatter
{
    const string ScenePath = "Assets/Scenes/PlanetNyxara.unity";
    const string GrassFolder = "Assets/Galaxy/Planets/Nyxara/Environment/Grass";
    const string GrassPrefabPath = GrassFolder + "/Grass.prefab";
    const string Grass2PrefabPath = GrassFolder + "/Grass2.prefab";
    const string Grass3PrefabPath = GrassFolder + "/Grass3.prefab";
    const string Grass4PrefabPath = GrassFolder + "/Grass_Luminous_Toadstool.prefab";
    const string Grass5PrefabPath = GrassFolder + "/Hollow_Log.prefab";
    const string GrassParentName = "Grass";
    const string ObjectsRootName = "Objects";

    const float DefaultHover = 0.05f;
    const float JitterRadius = 0.95f;
    const float CellPlacementChance = 0.68f;
    const float SecondInstanceChance = 0.18f;
    const float Grass2Weight = 0.5f;

    [MenuItem("BackHome/Setup Nyxara Grass Streaming (Recommended)")]
    public static void SetupStreamingMenu()
    {
        string message = SetupStreaming();
        if (message != null)
            EditorUtility.DisplayDialog("Grass Streaming", message, "OK");
    }

    /// <summary>Unity batchmode: -executeMethod NyxaraGrassScatter.SetupStreamingBatch</summary>
    public static void SetupStreamingBatch()
    {
        try
        {
            SetupStreaming();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    static string SetupStreaming()
    {
        OpenSceneIfNeeded();

        SphericalPlanet planet = UnityEngine.Object.FindFirstObjectByType<SphericalPlanet>();
        if (planet == null)
        {
            Debug.LogError("[BackHome] Grass Streaming: no SphericalPlanet found in the scene.");
            return "No SphericalPlanet found in the scene.";
        }

        int removed = RemoveStaticGrass(planet.transform);

        GameObject grassPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GrassPrefabPath);
        GameObject grass2Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Grass2PrefabPath);
        GameObject grass3Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Grass3PrefabPath);
        GameObject grass4Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Grass4PrefabPath);
        GameObject grass5Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Grass5PrefabPath);
        if (grassPrefab == null || grass2Prefab == null || grass3Prefab == null || grass4Prefab == null || grass5Prefab == null)
        {
            string missingMessage =
                "Missing prefab(s).\n"
                + $"Grass: {(grassPrefab != null ? "OK" : "MISSING")}\n"
                + $"Grass2: {(grass2Prefab != null ? "OK" : "MISSING")}\n"
                + $"Grass3: {(grass3Prefab != null ? "OK" : "MISSING")}\n"
                + $"Grass_Luminous_Toadstool: {(grass4Prefab != null ? "OK" : "MISSING")}\n"
                + $"Hollow_Log: {(grass5Prefab != null ? "OK" : "MISSING")}";
            Debug.LogError($"[BackHome] Grass Streaming: {missingMessage}");
            return missingMessage;
        }

        Transform streamersRoot = NyxaraStreamersRoot.FindOrCreate();
        PlanetGrassStreamer streamer = streamersRoot.GetComponent<PlanetGrassStreamer>();
        bool isNew = streamer == null;
        if (isNew)
            streamer = Undo.AddComponent<PlanetGrassStreamer>(streamersRoot.gameObject);

        var so = new SerializedObject(streamer);
        so.FindProperty("planet").objectReferenceValue = planet;
        so.FindProperty("grass1Prefab").objectReferenceValue = grassPrefab;
        so.FindProperty("grass2Prefab").objectReferenceValue = grass2Prefab;
        so.FindProperty("grass3Prefab").objectReferenceValue = grass3Prefab;
        so.FindProperty("grass4Prefab").objectReferenceValue = grass4Prefab;
        so.FindProperty("grass5Prefab").objectReferenceValue = grass5Prefab;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(streamer);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        string resultMessage = $"Removed {removed} static grass objects.\n"
                                + (isNew ? "Added" : "Updated")
                                + " PlanetGrassStreamer on 'Streamers' (Grass/Grass2/Grass3/Grass_Luminous_Toadstool/Hollow_Log) — grass now streams in near the player at runtime.";
        Debug.Log($"[BackHome] {resultMessage}");
        return resultMessage;
    }

    [MenuItem("BackHome/Remove Static Nyxara Grass")]
    public static void RemoveStaticGrassMenu()
    {
        OpenSceneIfNeeded();

        SphericalPlanet planet = UnityEngine.Object.FindFirstObjectByType<SphericalPlanet>();
        if (planet == null)
            return;

        int removed = RemoveStaticGrass(planet.transform);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[BackHome] Removed {removed} static grass objects.");
    }

    static int RemoveStaticGrass(Transform planetRoot)
    {
        Transform objectsRoot = planetRoot.Find(ObjectsRootName);
        if (objectsRoot == null)
            return 0;

        Transform grassRoot = objectsRoot.Find(GrassParentName);
        if (grassRoot == null)
            return 0;

        int count = grassRoot.childCount;
        Undo.DestroyObjectImmediate(grassRoot.gameObject);
        return count;
    }

    static void OpenSceneIfNeeded()
    {
        if (!ScenePath.Equals(SceneManager.GetActiveScene().path, StringComparison.Ordinal))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    /// <summary>Legacy: places static (non-pooled) grass across the whole planet. Prefer streaming.</summary>
    [MenuItem("BackHome/Scatter Nyxara Grass (Legacy, Static)")]
    public static void ScatterMenu() => Scatter(silent: false);

    public static void Scatter(bool silent)
    {
        OpenSceneIfNeeded();

        SphericalPlanet planet = UnityEngine.Object.FindFirstObjectByType<SphericalPlanet>();
        if (planet == null)
            throw new InvalidOperationException("No SphericalPlanet found in PlanetNyxara scene.");

        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        if (tiles == null)
            throw new InvalidOperationException("Planet is missing PlanetTileMap.");

        GameObject grassPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GrassPrefabPath);
        GameObject grass2Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Grass2PrefabPath);
        if (grassPrefab == null)
            throw new InvalidOperationException($"Grass prefab not found at {GrassPrefabPath}.");
        if (grass2Prefab == null)
            throw new InvalidOperationException($"Grass2 prefab not found at {Grass2PrefabPath}.");

        var grassPrefabs = new[] { grassPrefab, grass2Prefab };

        tiles.EnsureWalkColliders();
        if (tiles.WalkMeshCollider != null && tiles.WalkMeshCollider.sharedMesh == null)
            tiles.RebuildVisuals();

        Transform grassParent = FindOrCreateGrassParent(planet.transform);
        ClearChildren(grassParent);

        int placed = PlaceGrassAcrossPlanet(planet, tiles, grassPrefabs, grassParent);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        string message = $"Scattered {placed} grass clumps (Grass + Grass2) across {planet.name}.";
        Debug.Log($"[BackHome] {message}");
        if (!silent)
            EditorUtility.DisplayDialog("Scatter Grass", message, "OK");
    }

    static Transform FindOrCreateGrassParent(Transform planetRoot)
    {
        Transform objectsRoot = planetRoot.Find(ObjectsRootName);
        if (objectsRoot == null)
            objectsRoot = PlanetSurfacePose.GetOrCreateObjectsRoot(planetRoot.GetComponent<SphericalPlanet>());

        Transform grassParent = objectsRoot.Find(GrassParentName);
        if (grassParent == null)
        {
            var go = new GameObject(GrassParentName);
            Undo.RegisterCreatedObjectUndo(go, "Create Grass Root");
            grassParent = go.transform;
            grassParent.SetParent(objectsRoot, false);
            grassParent.localPosition = Vector3.zero;
            grassParent.localRotation = Quaternion.identity;
            grassParent.localScale = Vector3.one;
        }

        return grassParent;
    }

    static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            Undo.DestroyObjectImmediate(child.gameObject);
        }
    }

    static int PlaceGrassAcrossPlanet(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        GameObject[] grassPrefabs,
        Transform grassParent)
    {
        if (grassPrefabs == null || grassPrefabs.Length == 0)
            return 0;

        PlanetTileset tileset = tiles.Tileset;
        int placed = 0;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rng = new System.Random(unchecked((int)0x7A4C91E3u));

        for (int lat = 0; lat < tiles.LatitudeBands; lat++)
        {
            for (int lon = 0; lon < tiles.LongitudeBands; lon++)
            {
                if (!IsGrassTerrain(tiles, tileset, lat, lon))
                    continue;

                if (rng.NextDouble() > CellPlacementChance)
                    continue;

                int count = 1;
                if (rng.NextDouble() < SecondInstanceChance)
                    count++;

                for (int n = 0; n < count; n++)
                {
                    if (!TryGetJitteredCellPoint(tiles, lat, lon, rng, out Vector3 worldPoint))
                        continue;

                    GameObject prefab = PickGrassPrefab(grassPrefabs, rng);
                    if (prefab == null)
                        continue;

                    float yaw = (float)(rng.NextDouble() * 360.0);
                    if (!PlanetSurfacePose.TryGetPoseFromWorldPoint(
                            planet,
                            tiles,
                            worldPoint,
                            yaw,
                            DefaultHover,
                            out Vector3 position,
                            out Quaternion rotation,
                            out _))
                    {
                        continue;
                    }

                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, grassParent);
                    if (instance == null)
                        continue;

                    Undo.RegisterCreatedObjectUndo(instance, "Scatter Grass");
                    instance.transform.SetPositionAndRotation(position, rotation);
                    instance.transform.localScale = Vector3.one;

                    PlanetSurfaceAlign align = instance.GetComponent<PlanetSurfaceAlign>();
                    if (align == null)
                        align = Undo.AddComponent<PlanetSurfaceAlign>(instance);
                    align.Configure(planet, yaw, DefaultHover);

                    string prefabName = prefab.name;
                    if (!counts.ContainsKey(prefabName))
                        counts[prefabName] = 0;
                    counts[prefabName]++;

                    placed++;
                }
            }
        }

        if (counts.Count > 0)
        {
            var parts = new List<string>(counts.Count);
            foreach (KeyValuePair<string, int> pair in counts)
                parts.Add($"{pair.Value} x {pair.Key}");
            Debug.Log($"[BackHome] Grass mix: {string.Join(", ", parts)}");
        }

        return placed;
    }

    static GameObject PickGrassPrefab(GameObject[] grassPrefabs, System.Random rng)
    {
        if (grassPrefabs.Length == 1)
            return grassPrefabs[0];

        return rng.NextDouble() < Grass2Weight ? grassPrefabs[1] : grassPrefabs[0];
    }

    static bool IsGrassTerrain(PlanetTileMap tiles, PlanetTileset tileset, int lat, int lon)
    {
        int terrainIndex = tiles.GetTerrain(lat, lon);
        if (tileset == null)
            return true;

        PlanetTileset.Terrain terrain = tileset.GetTerrain(terrainIndex);
        if (terrain == null || !terrain.walkable || PlanetTileset.IsShadowGrassZone(terrain.zoneId))
            return false;

        if (string.IsNullOrEmpty(terrain.id))
            return true;

        return terrain.id.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool TryGetJitteredCellPoint(
        PlanetTileMap tiles,
        int lat,
        int lon,
        System.Random rng,
        out Vector3 worldPoint)
    {
        worldPoint = default;
        if (!tiles.TryGetCellCenter(lat, lon, out Vector3 center))
            return false;

        SphericalPlanet planet = tiles.GetComponent<SphericalPlanet>();
        if (planet == null)
            return false;

        Vector3 up = (center - planet.Center).normalized;
        Vector3 east = Vector3.Cross(Vector3.up, up);
        if (east.sqrMagnitude < 0.0001f)
            east = Vector3.Cross(Vector3.right, up);
        east.Normalize();
        Vector3 north = Vector3.Cross(up, east).normalized;

        float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
        float radius = JitterRadius * Mathf.Sqrt((float)rng.NextDouble());
        worldPoint = center + (east * Mathf.Cos(angle) + north * Mathf.Sin(angle)) * radius;
        return true;
    }
}
