using UnityEditor;
using UnityEngine;

/// <summary>
/// Menu-only: reimport CasualToon and fix Nyxara materials that lost the shader.
/// </summary>
public static class CasualShaderForceImport
{
    const string ShaderPath = "Assets/Resources/Galaxy/Planets/Shaders/Casual/CasualShader.shader";
    const string ShaderName = "BackHome/CasualToon";

    [MenuItem("BackHome/Reimport Casual Shader")]
    public static void MenuReimport()
    {
        Run();
    }

    public static void Run()
    {
        if (Application.isPlaying)
            return;

        string abs = System.IO.Path.Combine(Application.dataPath, "Resources/Galaxy/Planets/Shaders/Casual/CasualShader.shader");
        if (!System.IO.File.Exists(abs))
            return;

        AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        Shader sh = Shader.Find(ShaderName);
        if (sh == null)
        {
            Debug.LogError("[BackHome] " + ShaderName + " failed to load. Check Console for shader compile errors.");
            return;
        }

        int n = 0;
        string[] folders =
        {
            "Assets/Resources/Galaxy/Planets/Nyxara/Biomes",
            "Assets/Resources/Galaxy/Planets/Nyxara/Environment",
            "Assets/Resources/Galaxy/Planets/Shaders/Casual"
        };
        string[] matGuids = AssetDatabase.FindAssets("t:Material", folders);
        for (int i = 0; i < matGuids.Length; i++)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(matGuids[i]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
                continue;
            if (mat.shader == sh)
                continue;
            mat.shader = sh;
            EditorUtility.SetDirty(mat);
            n++;
        }

        if (n > 0)
            AssetDatabase.SaveAssets();

        Debug.Log("[BackHome] CasualToon OK (" + sh.name + "), updated " + n + " material(s).");
    }
}
