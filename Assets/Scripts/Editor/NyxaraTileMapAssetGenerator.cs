using UnityEditor;
using UnityEngine;

/// <summary>
/// Legacy menu — redirects to the tileset importer.
/// </summary>
public static class NyxaraTileMapAssetGenerator
{
    [MenuItem("BackHome/Generate Nyxara TileMap Assets")]
    public static void Generate()
    {
        bool ok = EditorUtility.DisplayDialog(
            "Nyxara Tiles",
            "Prefab-per-tile generation is replaced by the tileset importer.\n\nRun Import Nyxara Tileset?",
            "Import Tileset",
            "Cancel");
        if (ok)
            NyxaraTileAtlasImporter.Import();
    }
}
