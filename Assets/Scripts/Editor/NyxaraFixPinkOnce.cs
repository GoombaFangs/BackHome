using UnityEditor;
using UnityEngine;

/// <summary>
/// Reassign CasualToon and rebuild Nyxara tree/prop prefabs if they turn pink.
/// Menu: BackHome → Fix Pink Nyxara Materials
/// </summary>
public static class NyxaraFixPinkOnce
{
    [MenuItem("BackHome/Fix Pink Nyxara Materials")]
    public static void MenuFix() => Fix();

    public static void Fix()
    {
        DisableFbxEmbeddedMaterials("Assets/Galaxy/Planets/Nyxara/Objects/Models");

        AssetDatabase.ImportAsset(
            "Assets/Shaders/Casual/CasualShader.shader",
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        Shader sh = Shader.Find("BackHome/CasualToon");
        if (sh == null)
            sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null)
        {
            Debug.LogError("[BackHome] No usable shader for Nyxara materials.");
            return;
        }

        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Galaxy/Planets/Nyxara/Materials" });
        int fixedMats = 0;
        for (int i = 0; i < matGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(matGuids[i]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
                continue;
            mat.shader = sh;
            mat.SetOverrideTag("RenderType", "Opaque");
            EditorUtility.SetDirty(mat);
            fixedMats++;
        }

        AssetDatabase.SaveAssets();

        NyxaraPropPrefabSetup.BuildNames(
            silent: true,
            "Tree1",
            "Tree2",
            "Tree3",
            "Tree_Emerald_Canopy",
            "Tree_Verdant_Crown",
            "Hollow_Log",
            "Emerald_Fern",
            "Rock3");

        Debug.Log($"[BackHome] Fixed pink materials: {fixedMats} mats, rebuilt tree/prop prefabs with {sh.name}.");
    }

    static void DisableFbxEmbeddedMaterials(string modelsRoot)
    {
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { modelsRoot });
        int n = 0;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                continue;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
                continue;
            if (importer.materialImportMode == ModelImporterMaterialImportMode.None)
                continue;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
            n++;
        }

        if (n > 0)
            Debug.Log($"[BackHome] Disabled embedded FBX materials on {n} models (prevents pink Standard mats).");
    }
}
