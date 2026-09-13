using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fits tiles and the ocean sphere in the open planet scene.
/// </summary>
public static class PlanetLayoutEditor
{
    [MenuItem("BackHome/Planet Layout/Rebuild Tiles And Sea")]
    public static void RebuildTilesAndSea()
    {
        SphericalPlanet planet = Object.FindAnyObjectByType<SphericalPlanet>();
        if (planet == null)
        {
            EditorUtility.DisplayDialog(
                "Planet Layout",
                "Open a planet scene first.",
                "OK");
            return;
        }

        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        NyxaraSeaFit sea = planet.GetComponentInChildren<NyxaraSeaFit>(true);
        if (tiles != null)
            Undo.RecordObject(tiles, "Rebuild Tiles And Sea");
        if (sea != null)
            Undo.RecordObject(sea.transform, "Rebuild Tiles And Sea");

        int stripped = PlanetLayout.Rebuild(planet);
        EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
        Debug.Log(
            "[BackHome] Rebuilt tiles and ocean sphere." +
            (stripped > 0 ? " Removed " + stripped + " leftover ridge/cliff object(s)." : string.Empty));
    }

    [MenuItem("BackHome/Planet Layout/Attach Test Terrain Meshes")]
    public static void AttachTestTerrainMeshes()
    {
        SphericalPlanet planet = Object.FindAnyObjectByType<SphericalPlanet>();
        if (planet == null)
        {
            EditorUtility.DisplayDialog(
                "Planet Layout",
                "Open a planet scene first.",
                "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(planet.gameObject, "Attach Test Terrain Meshes");
        int added = PlanetTestTerrain.Attach(planet);
        EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
        Debug.Log("[BackHome] Attached " + added + " Test terrain mesh(es) from Galaxy/Nyxara/Test.");
    }

    [MenuItem("BackHome/Planet Layout/Remove Test Terrain Meshes")]
    public static void RemoveTestTerrainMeshes()
    {
        SphericalPlanet planet = Object.FindAnyObjectByType<SphericalPlanet>();
        if (planet == null || PlanetTestTerrain.FindRoot(planet) == null)
        {
            EditorUtility.DisplayDialog(
                "Planet Layout",
                "No Test terrain meshes in the open scene.",
                "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(planet.gameObject, "Remove Test Terrain Meshes");
        PlanetTestTerrain.Remove(planet);
        EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
    }

    [MenuItem("BackHome/Planet Layout/Hide Border Renderers")]
    public static void HideBorderRenderers()
    {
        SetBorderRenderers(false);
    }

    [MenuItem("BackHome/Planet Layout/Show Border Renderers")]
    public static void ShowBorderRenderers()
    {
        SetBorderRenderers(true);
    }

    static void SetBorderRenderers(bool enabled)
    {
        SphericalPlanet planet = Object.FindAnyObjectByType<SphericalPlanet>();
        if (planet == null || !PlanetBorders.HasLayout(planet))
        {
            EditorUtility.DisplayDialog(
                "Planet Layout",
                "No leftover Borders cubes in the open scene.",
                "OK");
            return;
        }

        PlanetBorders.SetSolidRenderersEnabled(planet, enabled);
        EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
    }
}
