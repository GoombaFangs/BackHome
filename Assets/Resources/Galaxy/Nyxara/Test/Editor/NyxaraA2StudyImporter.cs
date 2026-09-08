using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Editor-only fit study. Existing terrain, physics and prefab assets are untouched.
public static class NyxaraA2StudyImporter
{
    [Serializable]
    private sealed class StudyData
    {
        public float radius;
        public StudyMesh[] meshes;
    }

    [Serializable]
    private sealed class StudyMesh
    {
        public string name;
        public Vector3[] vertices;
        public int[] triangles;
        public Color color;
    }

    [MenuItem("Tools/Back Home/Import Nyxara A2 Terrain Study")]
    public static void ImportStudy()
    {
        Transform planet = Selection.activeTransform;
        if (planet == null || EditorUtility.IsPersistent(planet))
        {
            EditorUtility.DisplayDialog("Nyxara A2", "Select the PlanetNyxara scene root first.", "OK");
            return;
        }
        string path = EditorUtility.OpenFilePanel("Choose Nyxara-A2-MeshData.json", "", "json");
        if (string.IsNullOrEmpty(path)) return;
        StudyData data;
        try
        {
            data = JsonUtility.FromJson<StudyData>(File.ReadAllText(path));
            if (data == null || data.meshes == null || data.meshes.Length != 3 ||
                !Mathf.Approximately(data.radius, 75f))
                throw new InvalidDataException("Expected the supplied R75 study payload.");
            foreach (StudyMesh item in data.meshes)
            {
                if (item == null || string.IsNullOrEmpty(item.name) || item.vertices == null ||
                    item.triangles == null || item.vertices.Length == 0 || item.triangles.Length % 3 != 0)
                    throw new InvalidDataException("Invalid mesh record.");
                foreach (Vector3 v in item.vertices)
                    if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
                        float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z))
                        throw new InvalidDataException("Non-finite vertex.");
                foreach (int index in item.triangles)
                    if (index < 0 || index >= item.vertices.Length)
                        throw new InvalidDataException("Triangle index out of range.");
            }
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Nyxara A2", ex.Message, "OK");
            return;
        }
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null)
        {
            EditorUtility.DisplayDialog("Nyxara A2", "No supported Lit shader found.", "OK");
            return;
        }
        string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/NyxaraA2Study");
        AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        GameObject root = new GameObject("Nyxara_A2_Fit_Study");
        Undo.RegisterCreatedObjectUndo(root, "Import Nyxara A2 study");
        root.transform.SetParent(planet, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        for (int i = 0; i < data.meshes.Length; i++)
        {
            StudyMesh item = data.meshes[i];
            Mesh mesh = new Mesh { name = item.name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = item.vertices;
            mesh.triangles = item.triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, folder + "/Mesh_" + i + ".asset");
            Material material = new Material(shader) { name = item.name + "_Material" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", item.color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", item.color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(material, folder + "/Material_" + i + ".mat");
            GameObject child = new GameObject(item.name);
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        Debug.Log("A2 study imported as a new child. No colliders or source terrain changed. " +
            "The south cliff requires clipping/lowering existing ground to be visible. " +
            "Use the README before replacing any borders.", root);
    }
}
