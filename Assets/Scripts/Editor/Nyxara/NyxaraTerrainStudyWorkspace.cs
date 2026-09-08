using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates and tears down the A2 ridge/cliff study workspace without touching play scenes.
/// </summary>
public static class NyxaraTerrainStudyWorkspace
{
    const string StudyScenePath = NyxaraTerrainStudySession.StudyScenePath;
    const string SourceScenePath = NyxaraTerrainStudySession.SourceScenePath;
    const string SnapshotPath = NyxaraTerrainStudySession.SnapshotAssetPath;
    public const string TerrainPrefabPath = "Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2Terrain.prefab";

    [MenuItem("BackHome/Nyxara Terrain Study/Open A2 Study Scene")]
    public static void OpenStudyScene()
    {
        if (!File.Exists(StudyScenePath))
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Study scene is missing:\n" + StudyScenePath,
                "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorSceneManager.OpenScene(StudyScenePath, OpenSceneMode.Single);
        NyxaraTerrainStudySession session = EnsureSessionInOpenScene();
        if (session != null)
        {
            session.ApplyPlanToTileMap();
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        }

        Debug.Log("[BackHome] Opened Nyxara A2 terrain study. Do not Apply Prefab onto PlanetNyxara.");
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Open Original PlanetNyxara Scene")]
    public static void OpenOriginalScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Apply A2 Work Plan In Open Scene")]
    public static void ApplyPlanInOpenScene()
    {
        if (IsOriginalPlaySceneOpen())
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "The original PlanetNyxara scene is open. Apply the work plan only in PlanetNyxaraTerrainStudy.",
                "OK");
            return;
        }

        NyxaraTerrainStudySession session = EnsureSessionInOpenScene();
        if (session == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Open PlanetNyxaraTerrainStudy. The original play scene is never modified.",
                "OK");
            return;
        }

        Undo.RecordObject(session, "Apply A2 Work Plan");
        if (session.TileMap != null)
            Undo.RecordObject(session.TileMap, "Apply A2 Work Plan");
        session.ApplyPlanToTileMap();
        EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Remove A2 Study Extras (Revert)")]
    public static void RevertStudyExtras()
    {
        if (SceneManager.GetActiveScene().path == SourceScenePath)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "The original PlanetNyxara scene is already open. Study extras live only in the study scene.",
                "OK");
            return;
        }

        NyxaraTerrainStudySession[] sessions = Object.FindObjectsByType<NyxaraTerrainStudySession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (sessions.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "No NyxaraA2TerrainStudy object in the open scene.",
                "OK");
            return;
        }

        foreach (NyxaraTerrainStudySession session in sessions)
        {
            if (session == null)
                continue;
            Undo.RecordObject(session, "Revert A2 Terrain Study");
            if (session.TileMap != null)
                Undo.RecordObject(session.TileMap, "Revert A2 Terrain Study");
            session.ClearPlanFromTileMap();
            Undo.DestroyObjectImmediate(session.gameObject);
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid())
            EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("[BackHome] Removed A2 terrain study extras. Open PlanetNyxara.unity for the original level.");
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Rebuild A2 Boundary Overlay")]
    public static void RebuildBoundaryOverlay()
    {
        if (IsOriginalPlaySceneOpen())
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Open PlanetNyxaraTerrainStudy first. The original play scene is never modified.",
                "OK");
            return;
        }

        NyxaraTerrainStudySession session = Object.FindAnyObjectByType<NyxaraTerrainStudySession>();
        if (session == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "No NyxaraA2TerrainStudy object in the open scene.",
                "OK");
            return;
        }

        Undo.RecordObject(session, "Rebuild A2 Boundary Overlay");
        session.RebuildBoundaryOverlay();
        EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        Debug.Log("[BackHome] Rebuilt A2 boundary overlay from colliders and walk surface.");
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Save A2 Boundary Overlay JSON")]
    public static void SaveBoundaryOverlayJson()
    {
        NyxaraTerrainStudySession session = Object.FindAnyObjectByType<NyxaraTerrainStudySession>();
        if (session == null || session.BoundaryOverlay == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Rebuild the overlay in PlanetNyxaraTerrainStudy first.",
                "OK");
            return;
        }

        if (!session.BoundaryOverlay.HasSamples)
            session.RebuildBoundaryOverlay();

        string json = JsonUtility.ToJson(session.BoundaryOverlay, true);
        File.WriteAllText(NyxaraTerrainStudySession.BoundaryOverlayAssetPath, json);
        AssetDatabase.Refresh();
        Object asset = AssetDatabase.LoadAssetAtPath<TextAsset>(NyxaraTerrainStudySession.BoundaryOverlayAssetPath);
        if (asset != null)
        {
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        Debug.Log("[BackHome] Saved " + NyxaraTerrainStudySession.BoundaryOverlayAssetPath);
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Bake A2 North Ridge")]
    public static void BakeNorthRidge()
    {
        if (IsOriginalPlaySceneOpen())
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Open PlanetNyxaraTerrainStudy first. The original play scene is never modified.",
                "OK");
            return;
        }

        NyxaraTerrainStudySession session = Object.FindAnyObjectByType<NyxaraTerrainStudySession>();
        if (session == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "No NyxaraA2TerrainStudy object in the open scene.",
                "OK");
            return;
        }

        session.BindPlanet();
        if (session.BoundaryOverlay == null || !session.BoundaryOverlay.HasSamples)
            session.RebuildBoundaryOverlay();

        NyxaraA2NorthRidge ridge = EnsureNorthRidgeObject(session);
        if (ridge == null || session.Planet == null)
            return;

        PlanetTileMap tileMap = session.TileMap;
        float walk = tileMap != null
            ? tileMap.GetWalkSurfaceRadius(PlanetTileMap.StudyLonLatToDirection(35f, 25f))
            : session.Planet.Radius;
        var settings = NyxaraA2NorthRidgeMeshBuilder.FromPlan(session.Plan, session.BoundaryOverlay, walk);
        Mesh mesh = NyxaraA2NorthRidgeMeshBuilder.Build(settings);
        string report = NyxaraA2NorthRidgeMeshBuilder.Describe(settings, mesh);

        string folder = "Assets/Resources/Galaxy/Nyxara/Terrain/A2";
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Galaxy/Nyxara/Terrain"))
            AssetDatabase.CreateFolder("Assets/Resources/Galaxy/Nyxara", "Terrain");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Resources/Galaxy/Nyxara/Terrain", "A2");

        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(NyxaraA2NorthRidge.MeshAssetPath);
        if (saved == null)
        {
            AssetDatabase.CreateAsset(mesh, NyxaraA2NorthRidge.MeshAssetPath);
            saved = mesh;
        }
        else
        {
            EditorUtility.CopySerialized(mesh, saved);
            Object.DestroyImmediate(mesh);
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(NyxaraA2NorthRidge.MaterialAssetPath);
        EditorUtility.SetDirty(saved);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Undo.RecordObject(ridge, "Bake A2 North Ridge");
        ridge.SetBaked(saved, material, report);
        EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        Selection.activeGameObject = ridge.gameObject;
        Debug.Log("[BackHome] " + report);
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Bake A2 South Cliff")]
    public static void BakeSouthCliff()
    {
        if (IsOriginalPlaySceneOpen())
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Open PlanetNyxaraTerrainStudy first. The original play scene is never modified.",
                "OK");
            return;
        }

        NyxaraTerrainStudySession session = Object.FindAnyObjectByType<NyxaraTerrainStudySession>();
        if (session == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "No NyxaraA2TerrainStudy object in the open scene.",
                "OK");
            return;
        }

        session.BindPlanet();
        if (session.BoundaryOverlay == null || !session.BoundaryOverlay.HasSamples)
            session.RebuildBoundaryOverlay();
        else
            session.ApplyPlanToTileMap();

        NyxaraA2SouthCliff cliff = EnsureSouthCliffObject(session);
        if (cliff == null || session.Planet == null)
            return;

        PlanetTileMap tileMap = session.TileMap;
        float walk = tileMap != null
            ? tileMap.GetWalkSurfaceRadius(PlanetTileMap.StudyLonLatToDirection(35f, -12f))
            : session.Planet.Radius;
        var settings = NyxaraA2SouthCliffMeshBuilder.FromPlan(session.Plan, walk);
        Mesh mesh = NyxaraA2SouthCliffMeshBuilder.Build(settings);
        string report = NyxaraA2SouthCliffMeshBuilder.Describe(settings, mesh);

        string folder = "Assets/Resources/Galaxy/Nyxara/Terrain/A2";
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Galaxy/Nyxara/Terrain"))
            AssetDatabase.CreateFolder("Assets/Resources/Galaxy/Nyxara", "Terrain");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Resources/Galaxy/Nyxara/Terrain", "A2");

        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(NyxaraA2SouthCliff.MeshAssetPath);
        if (saved == null)
        {
            AssetDatabase.CreateAsset(mesh, NyxaraA2SouthCliff.MeshAssetPath);
            saved = mesh;
        }
        else
        {
            EditorUtility.CopySerialized(mesh, saved);
            Object.DestroyImmediate(mesh);
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(NyxaraA2SouthCliff.MaterialAssetPath);
        EditorUtility.SetDirty(saved);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Undo.RecordObject(cliff, "Bake A2 South Cliff");
        cliff.SetBaked(saved, material, report);
        EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        Selection.activeGameObject = cliff.gameObject;
        Debug.Log("[BackHome] " + report);
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Bake Full Ring Ridge And Cliff")]
    public static void BakeFullRing()
    {
        if (IsOriginalPlaySceneOpen())
        {
            EditorUtility.DisplayDialog(
                "Nyxara Terrain Study",
                "Open PlanetNyxaraTerrainStudy first. The original play scene is never modified.",
                "OK");
            return;
        }

        NyxaraTerrainStudySession session = Object.FindAnyObjectByType<NyxaraTerrainStudySession>();
        if (session == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara Terrain Study",
                "No NyxaraA2TerrainStudy object in the open scene.",
                "OK");
            return;
        }

        Undo.RecordObject(session, "Bake Full Ring");
        if (session.TileMap != null)
            Undo.RecordObject(session.TileMap, "Bake Full Ring");
        session.EnableFullRing();
        BakeNorthRidge();
        BakeSouthCliff();
        BakeLipWalls();
        session.SetHideCoveredPlaceholderWallRenderers(true);
        EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        Debug.Log(
            "[BackHome] Baked full-ring ridge and cliff. Walk band is kinematic (no lip/Border physics). Play Mode, Game View 9:16. Do not Apply Prefab onto PlanetNyxara.");
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Bake Invisible Lip Walls")]
    public static void BakeLipWalls()
    {
        if (IsOriginalPlaySceneOpen())
        {
            EditorUtility.DisplayDialog(
                "Nyxara Terrain Study",
                "Open PlanetNyxaraTerrainStudy first. The original play scene is never modified.",
                "OK");
            return;
        }

        NyxaraTerrainStudySession session = Object.FindAnyObjectByType<NyxaraTerrainStudySession>();
        if (session == null)
            return;

        session.BindPlanet();
        session.EnsureLipWalls();
        NyxaraA2LipWalls walls = session.GetComponentInChildren<NyxaraA2LipWalls>(true);
        if (walls == null)
            return;

        MeshFilter filter = walls.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null)
            return;

        string folder = "Assets/Resources/Galaxy/Nyxara/Terrain/A2";
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Galaxy/Nyxara/Terrain"))
            AssetDatabase.CreateFolder("Assets/Resources/Galaxy/Nyxara", "Terrain");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Resources/Galaxy/Nyxara/Terrain", "A2");

        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(NyxaraA2LipWalls.MeshAssetPath);
        if (saved == null)
        {
            Mesh copy = Object.Instantiate(mesh);
            copy.name = NyxaraA2LipWallMeshBuilder.MeshName;
            AssetDatabase.CreateAsset(copy, NyxaraA2LipWalls.MeshAssetPath);
            saved = copy;
        }
        else
        {
            saved.Clear();
            saved.indexFormat = mesh.indexFormat;
            saved.vertices = mesh.vertices;
            saved.triangles = mesh.triangles;
            saved.normals = mesh.normals;
            saved.RecalculateBounds();
        }

        EditorUtility.SetDirty(saved);
        AssetDatabase.SaveAssets();
        walls.SetBaked(saved);
        EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        Debug.Log("[BackHome] Baked lip wall mesh (collider stays off — kinematic walk band).");
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Hide A2 Placeholder Wall Renderers")]
    public static void HideA2PlaceholderWallRenderers()
    {
        SetA2PlaceholderWallRenderers(true);
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Show A2 Placeholder Wall Renderers")]
    public static void ShowA2PlaceholderWallRenderers()
    {
        SetA2PlaceholderWallRenderers(false);
    }

    static void SetA2PlaceholderWallRenderers(bool hide)
    {
        if (IsOriginalPlaySceneOpen())
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Open PlanetNyxaraTerrainStudy first. The original play scene is never modified.",
                "OK");
            return;
        }

        NyxaraTerrainStudySession session = Object.FindAnyObjectByType<NyxaraTerrainStudySession>();
        if (session == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "No NyxaraA2TerrainStudy object in the open scene.",
                "OK");
            return;
        }

        Undo.RecordObject(session, hide ? "Hide A2 Placeholder Wall Renderers" : "Show A2 Placeholder Wall Renderers");
        session.SetHideCoveredPlaceholderWallRenderers(hide);
        EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Place Player In A2 (Game Camera)")]
    public static void PlacePlayerInA2()
    {
        SphericalPlanet planet = Object.FindAnyObjectByType<SphericalPlanet>();
        PlanetWalker walker = Object.FindAnyObjectByType<PlanetWalker>();
        if (planet == null || walker == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Need PlanetNyxara and the player in the open scene. Enter Play Mode in PlanetNyxaraTerrainStudy, then run this again.\n\n" +
                "Game View follows CameraFollow on the player (height 22 / back 9.5). Rotating the Scene camera does not change Game View.",
                "OK");
            return;
        }

        Transform a2 = planet.transform.Find("Areas/A2");
        if (a2 == null)
        {
            Transform areas = planet.transform.Find("Areas");
            if (areas != null)
                a2 = areas.Find("A2");
        }

        if (a2 == null)
        {
            EditorUtility.DisplayDialog("Nyxara A2 Study", "Could not find Areas/A2 under the planet.", "OK");
            return;
        }

        Vector3 dir = a2.position - planet.Center;
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.forward;
        dir.Normalize();
        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        float radius = tiles != null ? tiles.GetWalkSurfaceRadius(dir) : planet.Radius;
        Vector3 point = planet.Center + dir * radius;

        Undo.RecordObject(walker.transform, "Place Player In A2");
        walker.transform.position = point;
        walker.EnsureWalkingOnPlanet();
        Selection.activeGameObject = walker.gameObject;
        Debug.Log("[BackHome] Player snapped to the A2 walk surface. Check Game View 9:16 — not Scene View.");
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Save A2 Terrain Prefab")]
    public static void SaveA2TerrainPrefab()
    {
        if (IsOriginalPlaySceneOpen())
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Save the A2 terrain prefab from PlanetNyxaraTerrainStudy only. The play scene is not modified.",
                "OK");
            return;
        }

        NyxaraTerrainStudySession session = Object.FindAnyObjectByType<NyxaraTerrainStudySession>();
        if (session == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Open PlanetNyxaraTerrainStudy first.",
                "OK");
            return;
        }

        session.BindPlanet();
        NyxaraA2NorthRidge ridge = EnsureNorthRidgeObject(session);
        NyxaraA2SouthCliff cliff = EnsureSouthCliffObject(session);
        session.EnsureLipWalls();
        NyxaraA2LipWalls lips = session.GetComponentInChildren<NyxaraA2LipWalls>(true);
        if (ridge == null || cliff == null)
            return;

        string folder = "Assets/Resources/Galaxy/Nyxara/Terrain/A2";
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Galaxy/Nyxara/Terrain"))
            AssetDatabase.CreateFolder("Assets/Resources/Galaxy/Nyxara", "Terrain");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Resources/Galaxy/Nyxara/Terrain", "A2");

        var root = new GameObject("NyxaraA2Terrain");
        try
        {
            CloneIdentityChild(ridge.gameObject, root.transform);
            CloneIdentityChild(cliff.gameObject, root.transform);
            if (lips != null)
                CloneIdentityChild(lips.gameObject, root.transform);
            PrefabUtility.SaveAsPrefabAsset(root, TerrainPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        Object prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainPrefabPath);
        if (prefab != null)
        {
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        Debug.Log("[BackHome] Saved " + TerrainPrefabPath +
                  ". Parent under PlanetNyxara at identity TRS. Do not add this prefab to other scenes.");
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Compare Layout To Snapshot")]
    public static void CompareLayoutToSnapshot()
    {
        if (!File.Exists(SnapshotPath))
        {
            EditorUtility.DisplayDialog("Nyxara A2 Study", "Snapshot missing:\n" + SnapshotPath, "OK");
            return;
        }

        SphericalPlanet planet = Object.FindAnyObjectByType<SphericalPlanet>();
        if (planet == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Open PlanetNyxara or PlanetNyxaraTerrainStudy first.",
                "OK");
            return;
        }

        bool playScene = SceneManager.GetActiveScene().path == SourceScenePath;
        var report = new StringBuilder();
        if (!playScene)
        {
            int removed = RemoveAddedLayoutCopies(planet);
            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
                report.AppendLine(
                    "Removed " + removed +
                    " scene-added Area/Border copies. Prefab originals (including B at 18.7, 8, -53.5 scale 60) were kept.");
            }
        }

        string json = File.ReadAllText(SnapshotPath);
        int fail = 0;
        fail += CompareNamedChildren(planet.transform.Find("Areas"), json, true, report);
        fail += CompareNamedChildren(planet.transform.Find("Borders"), json, false, report);

        PlanetTileMap tileMap = planet.GetComponent<PlanetTileMap>();
        if (playScene && tileMap != null && tileMap.WorkPlan != null && tileMap.WorkPlan.enabled)
        {
            fail++;
            report.AppendLine("FAIL workPlan.enabled is true in the play scene. Production must leave it off.");
        }
        else if (!playScene && tileMap != null && tileMap.WorkPlan != null && tileMap.WorkPlan.enabled)
            report.AppendLine("OK study workPlan.enabled (pit on this instance only).");
        else if (playScene)
            report.AppendLine("OK play-scene workPlan is off or unset.");

        bool studyInBuild = false;
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene != null && scene.path == StudyScenePath && scene.enabled)
                studyInBuild = true;
        }

        if (studyInBuild)
        {
            fail++;
            report.AppendLine("FAIL PlanetNyxaraTerrainStudy is in EditorBuildSettings.");
        }
        else
            report.AppendLine("OK study scene is not in the build list.");

        string summary = fail == 0
            ? "Layout matches the stage-1 snapshot (areas, walls, build list)."
            : fail + " layout check(s) failed. See the Console.";
        Debug.Log("[BackHome] A2 layout compare\n" + report);
        EditorUtility.DisplayDialog("Nyxara A2 Layout", summary + "\n\n" + report, "OK");
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Remove Duplicate Area And Wall Copies")]
    public static void RemoveDuplicateLayoutCopiesMenu()
    {
        if (IsOriginalPlaySceneOpen())
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "The play scene is not modified. Open PlanetNyxaraTerrainStudy first.",
                "OK");
            return;
        }

        SphericalPlanet planet = Object.FindAnyObjectByType<SphericalPlanet>();
        if (planet == null)
        {
            EditorUtility.DisplayDialog("Nyxara A2 Study", "Open PlanetNyxaraTerrainStudy first.", "OK");
            return;
        }

        int removed = RemoveAddedLayoutCopies(planet);
        if (removed > 0)
            EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
        EditorUtility.DisplayDialog(
            "Nyxara A2 Study",
            removed == 0
                ? "No extra Area/Border copies under the planet prefab."
                : "Removed " + removed + " scene-added copies. Save the study scene.",
            "OK");
    }

    [MenuItem("BackHome/Nyxara Terrain Study/Select Layout Snapshot")]
    public static void SelectSnapshot()
    {
        Object snapshot = AssetDatabase.LoadAssetAtPath<TextAsset>(SnapshotPath);
        if (snapshot == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara A2 Study",
                "Snapshot not found:\n" + SnapshotPath,
                "OK");
            return;
        }

        Selection.activeObject = snapshot;
        EditorGUIUtility.PingObject(snapshot);
    }

    static bool IsOriginalPlaySceneOpen()
    {
        return SceneManager.GetActiveScene().path == SourceScenePath;
    }

    static NyxaraTerrainStudySession EnsureSessionInOpenScene()
    {
        if (IsOriginalPlaySceneOpen())
            return null;

        NyxaraTerrainStudySession existing = Object.FindAnyObjectByType<NyxaraTerrainStudySession>();
        if (existing != null)
        {
            existing.BindPlanet();
            existing.SnapToPlanetLocalIdentity();
            return existing;
        }

        if (SceneManager.GetActiveScene().path != StudyScenePath)
            return null;

        SphericalPlanet planet = Object.FindAnyObjectByType<SphericalPlanet>();
        if (planet == null)
            return null;

        var go = new GameObject(NyxaraTerrainStudySession.RootName);
        Undo.RegisterCreatedObjectUndo(go, "Create A2 Terrain Study");
        go.transform.SetParent(planet.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        NyxaraTerrainStudySession session = Undo.AddComponent<NyxaraTerrainStudySession>(go);
        session.BindPlanet();
        session.SnapToPlanetLocalIdentity();
        session.ApplyPlanToTileMap();
        return session;
    }

    static NyxaraA2NorthRidge EnsureNorthRidgeObject(NyxaraTerrainStudySession session)
    {
        if (session == null)
            return null;

        NyxaraA2NorthRidge existing = session.GetComponentInChildren<NyxaraA2NorthRidge>(true);
        if (existing != null)
        {
            existing.transform.SetParent(session.transform, false);
            existing.transform.localPosition = Vector3.zero;
            existing.transform.localRotation = Quaternion.identity;
            existing.transform.localScale = Vector3.one;
            existing.gameObject.layer = NyxaraTerrainCollision.GroundLayerIndex;
            return existing;
        }

        var go = new GameObject(NyxaraA2NorthRidge.RootName);
        Undo.RegisterCreatedObjectUndo(go, "Create A2 North Ridge");
        go.transform.SetParent(session.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.AddComponent<MeshFilter>();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
        go.layer = NyxaraTerrainCollision.GroundLayerIndex;
        go.AddComponent<MeshCollider>();
        return Undo.AddComponent<NyxaraA2NorthRidge>(go);
    }

    static NyxaraA2SouthCliff EnsureSouthCliffObject(NyxaraTerrainStudySession session)
    {
        if (session == null)
            return null;

        NyxaraA2SouthCliff existing = session.GetComponentInChildren<NyxaraA2SouthCliff>(true);
        if (existing != null)
        {
            existing.transform.SetParent(session.transform, false);
            existing.transform.localPosition = Vector3.zero;
            existing.transform.localRotation = Quaternion.identity;
            existing.transform.localScale = Vector3.one;
            existing.gameObject.layer = NyxaraTerrainCollision.GroundLayerIndex;
            return existing;
        }

        var go = new GameObject(NyxaraA2SouthCliff.RootName);
        Undo.RegisterCreatedObjectUndo(go, "Create A2 South Cliff");
        go.transform.SetParent(session.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.AddComponent<MeshFilter>();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
        go.layer = NyxaraTerrainCollision.GroundLayerIndex;
        go.AddComponent<MeshCollider>();
        return Undo.AddComponent<NyxaraA2SouthCliff>(go);
    }

    static void CloneIdentityChild(GameObject source, Transform parent)
    {
        GameObject clone = Object.Instantiate(source, parent, false);
        clone.name = source.name;
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
    }

    static int CompareNamedChildren(Transform group, string json, bool expectTrigger, StringBuilder report)
    {
        int fail = 0;
        if (group == null)
        {
            report.AppendLine("FAIL missing " + (expectTrigger ? "Areas" : "Borders") + " under the planet.");
            return 1;
        }

        var seen = new HashSet<string>();
        int compared = 0;
        for (int i = 0; i < group.childCount; i++)
        {
            Transform child = group.GetChild(i);
            if (child == null)
                continue;
            if (PrefabUtility.IsAddedGameObjectOverride(child.gameObject))
            {
                fail++;
                report.AppendLine("FAIL extra scene copy still present: " + child.name);
                continue;
            }

            if (!TryReadSnapshotPose(json, child.name, out Vector3 pos, out Vector3 scale, out bool isTrigger))
                continue;

            if (!seen.Add(child.name))
            {
                fail++;
                report.AppendLine("FAIL duplicate " + child.name + " under " + group.name + ".");
                continue;
            }

            compared++;
            Vector3 delta = child.localPosition - pos;
            Vector3 scaleDelta = child.localScale - scale;
            var collider = child.GetComponent<BoxCollider>();
            bool triggerOk = collider != null && collider.isTrigger == expectTrigger && collider.isTrigger == isTrigger;
            bool poseOk = delta.sqrMagnitude < 0.0001f && scaleDelta.sqrMagnitude < 0.0001f;
            if (poseOk && triggerOk)
            {
                report.AppendLine("OK " + child.name);
                continue;
            }

            fail++;
            report.AppendLine(
                "FAIL " + child.name +
                " pos " + child.localPosition + " vs " + pos +
                " scale " + child.localScale + " vs " + scale +
                " trigger " + (collider != null && collider.isTrigger) + " vs " + isTrigger);
        }

        int expected = expectTrigger ? 9 : 24;
        if (compared != expected)
        {
            fail++;
            report.AppendLine(
                "FAIL expected " + expected +
                (expectTrigger ? " areas" : " walls") +
                " in the snapshot compare, got " + compared + ".");
        }

        return fail;
    }

    static int RemoveAddedLayoutCopies(SphericalPlanet planet)
    {
        if (planet == null)
            return 0;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Remove duplicate Nyxara layout copies");
        int removed = 0;
        removed += RemoveAddedDuplicatesUnder(planet.transform.Find("Areas"));
        removed += RemoveAddedDuplicatesUnder(planet.transform.Find("Borders"));
        Undo.CollapseUndoOperations(group);
        if (removed > 0)
            Debug.Log("[BackHome] Removed " + removed + " scene-added Area/Border copies. Prefab originals kept.");
        return removed;
    }

    static int RemoveAddedDuplicatesUnder(Transform group)
    {
        if (group == null)
            return 0;

        int removed = 0;
        for (int i = group.childCount - 1; i >= 0; i--)
        {
            Transform child = group.GetChild(i);
            if (child == null || !PrefabUtility.IsAddedGameObjectOverride(child.gameObject))
                continue;

            bool hasOriginal = false;
            for (int j = 0; j < group.childCount; j++)
            {
                Transform other = group.GetChild(j);
                if (other != null &&
                    other != child &&
                    other.name == child.name &&
                    !PrefabUtility.IsAddedGameObjectOverride(other.gameObject))
                {
                    hasOriginal = true;
                    break;
                }
            }

            if (!hasOriginal)
                continue;

            Undo.DestroyObjectImmediate(child.gameObject);
            removed++;
        }

        return removed;
    }

    static bool TryReadSnapshotPose(string json, string name, out Vector3 pos, out Vector3 scale, out bool isTrigger)
    {
        pos = default;
        scale = default;
        isTrigger = false;
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(name))
            return false;

        var match = Regex.Match(
            json,
            "\"name\"\\s*:\\s*\"" + Regex.Escape(name) +
            "\"\\s*,\\s*\"localPosition\"\\s*:\\s*\\[([^\\]]+)\\]\\s*,\\s*\"localRotation\"\\s*:\\s*\\[[^\\]]+\\]\\s*,\\s*\"localScale\"\\s*:\\s*\\[([^\\]]+)\\].*?\"isTrigger\"\\s*:\\s*(true|false)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;

        if (!TryParseVec3(match.Groups[1].Value, out pos) || !TryParseVec3(match.Groups[2].Value, out scale))
            return false;
        isTrigger = match.Groups[3].Value == "true";
        return true;
    }

    static bool TryParseVec3(string csv, out Vector3 value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(csv))
            return false;
        string[] parts = csv.Split(',');
        if (parts.Length != 3)
            return false;
        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
            return false;
        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            return false;
        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            return false;
        value = new Vector3(x, y, z);
        return true;
    }
}
