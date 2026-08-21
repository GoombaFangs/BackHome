using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click Nyxara continent bake for the open scene.
/// </summary>
public static class NyxaraContinentBaker
{
    [MenuItem("BackHome/Generate Nyxara Continents")]
    public static void GenerateInOpenScene()
    {
        PlanetTileMap map = Object.FindAnyObjectByType<PlanetTileMap>();
        if (map == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara Continents",
                "Open a scene with a PlanetTileMap first.",
                "OK");
            return;
        }

        if (map.Tileset == null)
        {
            EditorUtility.DisplayDialog(
                "Nyxara Continents",
                "Run BackHome → Import Nyxara Tileset first.",
                "OK");
            return;
        }

        Undo.RecordObject(map, "Generate Nyxara Continents");
        if (!map.HasValidMap())
            map.SetTilesAroundEquator(map.TilesAroundEquator, refillWithBase: true);

        map.FillTerrain(map.Tileset.BaseTerrainIndex);
        PlanetBlobAutotile.GenerateContinents(map, seed: 11);
        EditorUtility.SetDirty(map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        if (map.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
        map.RebuildVisuals();
        Debug.Log("[BackHome] Generated Nyxara continents.");
    }

    [MenuItem("BackHome/Resolve Nyxara Autotile")]
    public static void ResolveInOpenScene()
    {
        PlanetTileMap map = Object.FindAnyObjectByType<PlanetTileMap>();
        if (map == null || map.Tileset == null)
        {
            EditorUtility.DisplayDialog("Nyxara Autotile", "Need PlanetTileMap with a tileset.", "OK");
            return;
        }

        Undo.RecordObject(map, "Resolve Nyxara Autotile");
        PlanetBlobAutotile.ResolveAll(map);
        EditorUtility.SetDirty(map);
        PrefabUtility.RecordPrefabInstancePropertyModifications(map);
        if (map.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
    }
}
