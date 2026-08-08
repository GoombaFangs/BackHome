using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates URP Lit materials + ready prefabs for every FBX under Nyxara/Models.
/// Menu: BackHome → Setup All Nyxara Prop Prefabs
/// </summary>
public static class NyxaraPropPrefabSetup
{
    const string Root = "Assets/Galaxy/Planets/Nyxara";
    const string ModelsRoot = Root + "/Objects/Models";
    const string MaterialsRoot = Root + "/Materials";
    const string PrefabsRoot = Root + "/Objects/Prefabs";
    const string GroundLayerName = "Ground";

    [MenuItem("BackHome/Setup All Nyxara Prop Prefabs")]
    public static void BuildAllMenu() => BuildAll(silent: false);

    /// <summary>Unity batchmode: -executeMethod NyxaraPropPrefabSetup.BuildNewPropsBatch</summary>
    public static void BuildNewPropsBatch()
    {
        try
        {
            BuildMissing(silent: true);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    public static void BuildAll(bool silent) => BuildJobs(DiscoverModels(), silent);

    /// <summary>Builds only models that do not yet have a prefab under PrefabsRoot.</summary>
    public static void BuildMissing(bool silent)
    {
        EnsureFolder(PrefabsRoot);
        var jobs = DiscoverModels()
            .Where(j => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsRoot}/{j.PrefabName}.prefab") == null)
            .ToList();
        if (jobs.Count == 0)
        {
            Debug.Log("[BackHome] No missing Nyxara prop prefabs.");
            if (!silent)
                EditorUtility.DisplayDialog("Nyxara Props", "All discovered models already have prefabs.", "OK");
            return;
        }

        BuildJobs(jobs, silent);
    }

    [MenuItem("BackHome/Setup Missing Nyxara Prop Prefabs")]
    public static void BuildMissingMenu() => BuildMissing(silent: false);

    public static void BuildNames(bool silent, params string[] prefabNames)
    {
        if (prefabNames == null || prefabNames.Length == 0)
        {
            BuildAll(silent);
            return;
        }

        var wanted = new HashSet<string>(prefabNames, StringComparer.OrdinalIgnoreCase);
        var jobs = DiscoverModels().Where(j => wanted.Contains(j.PrefabName)).ToList();
        if (jobs.Count == 0)
        {
            Debug.LogWarning("[BackHome] No matching Nyxara models for: " + string.Join(", ", prefabNames));
            return;
        }

        BuildJobs(jobs, silent);
    }

    static void BuildJobs(List<ModelJob> jobs, bool silent)
    {
        EnsureFolder(MaterialsRoot);
        EnsureFolder(PrefabsRoot);

        if (jobs.Count == 0)
        {
            if (!silent)
                EditorUtility.DisplayDialog("Nyxara Props", $"No FBX models found under:\n{ModelsRoot}", "OK");
            return;
        }

        int ok = 0;
        try
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                ModelJob job = jobs[i];
                if (!silent)
                {
                    EditorUtility.DisplayProgressBar(
                        "Nyxara Prop Prefabs",
                        $"{job.PrefabName} ({i + 1}/{jobs.Count})",
                        (i + 0.5f) / jobs.Count);
                }

                if (BuildOne(job))
                    ok++;
            }
        }
        finally
        {
            if (!silent)
                EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (!silent)
        {
            EditorUtility.DisplayDialog(
                "Nyxara Props",
                $"Created/updated {ok}/{jobs.Count} prefabs in:\n{PrefabsRoot}",
                "OK");
        }
        Debug.Log($"[BackHome] Nyxara prop prefabs: {ok}/{jobs.Count} ready in {PrefabsRoot}");
    }

    struct ModelJob
    {
        public string ModelName;
        public string PrefabName;
        public string Folder;
        public string FbxPath;
        public string AlbedoPath;
        public string NormalPath;
        public string MetallicPath;
        public string RoughnessPath;
        public string EmissionPath;
    }

    static List<ModelJob> DiscoverModels()
    {
        var jobs = new List<ModelJob>();
        if (!AssetDatabase.IsValidFolder(ModelsRoot))
            return jobs;

        string[] folders = AssetDatabase.GetSubFolders(ModelsRoot);
        Array.Sort(folders, StringComparer.OrdinalIgnoreCase);

        foreach (string folder in folders)
        {
            string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { folder });
            var fbxPaths = new List<string>();
            for (int i = 0; i < fbxGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(fbxGuids[i]);
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    fbxPaths.Add(path);
            }

            // Longer names first so Rock2 wins over Rock when matching textures.
            fbxPaths.Sort((a, b) =>
                Path.GetFileNameWithoutExtension(b).Length.CompareTo(Path.GetFileNameWithoutExtension(a).Length));

            var claimedTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] allPng = Directory.Exists(ToAbsolute(folder))
                ? Directory.GetFiles(ToAbsolute(folder), "*.png")
                    .Select(p => folder + "/" + Path.GetFileName(p).Replace('\\', '/'))
                    .ToArray()
                : Array.Empty<string>();

            foreach (string fbxPath in fbxPaths)
            {
                string modelName = Path.GetFileNameWithoutExtension(fbxPath);
                var owned = allPng
                    .Where(p => !claimedTextures.Contains(p) && TextureBelongsToModel(Path.GetFileName(p), modelName))
                    .ToList();

                foreach (string p in owned)
                    claimedTextures.Add(p);

                ClassifyTextures(owned, out string albedo, out string normal, out string metallic, out string roughness, out string emission);

                // Fallback: if this is the only fbx in folder, take any remaining albedo-like texture.
                if (string.IsNullOrEmpty(albedo) && fbxPaths.Count == 1)
                {
                    ClassifyTextures(allPng.ToList(), out albedo, out normal, out metallic, out roughness, out emission);
                }

                jobs.Add(new ModelJob
                {
                    ModelName = modelName,
                    PrefabName = modelName,
                    Folder = folder,
                    FbxPath = fbxPath,
                    AlbedoPath = albedo,
                    NormalPath = normal,
                    MetallicPath = metallic,
                    RoughnessPath = roughness,
                    EmissionPath = emission
                });
            }
        }

        // Restore stable alphabetical order for UI progress.
        jobs.Sort((a, b) => string.CompareOrdinal(a.PrefabName, b.PrefabName));
        return jobs;
    }

    static bool TextureBelongsToModel(string fileName, string modelName)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(modelName))
            return false;

        // Prefer exact prefix: ModelName_... or ModelNametexture...
        if (fileName.StartsWith(modelName + "_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.StartsWith(modelName + "texture", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.StartsWith(modelName + ".", StringComparison.OrdinalIgnoreCase))
            return true;

        // Avoid Rock matching Rock2 textures.
        return false;
    }

    static void ClassifyTextures(
        List<string> paths,
        out string albedo,
        out string normal,
        out string metallic,
        out string roughness,
        out string emission)
    {
        albedo = normal = metallic = roughness = emission = null;
        foreach (string path in paths)
        {
            string n = Path.GetFileName(path).ToLowerInvariant();
            if (n.Contains("emission"))
                emission = path;
            else if (n.Contains("normal"))
                normal = path;
            else if (n.Contains("metallic") || n.Contains("metalness"))
                metallic = path;
            else if (n.Contains("roughness") || n.Contains("rough"))
                roughness = path;
            else if (n.Contains("texture") || n.Contains("albedo") || n.Contains("basecolor") || n.Contains("diffuse"))
            {
                if (albedo == null)
                    albedo = path;
            }
        }
    }

    static bool BuildOne(ModelJob job)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(job.FbxPath);
        if (source == null)
        {
            Debug.LogWarning($"[BackHome] Missing FBX: {job.FbxPath}");
            return false;
        }

        ConfigureTextureImports(job);
        Texture2D mask = BuildMaskIfNeeded(job);
        Material mat = BuildMaterial(job, mask);
        return BuildPrefab(job, mat) != null;
    }

    static void ConfigureTextureImports(ModelJob job)
    {
        SetTextureImport(job.AlbedoPath, TextureImporterType.Default, sRGB: true, readable: false);
        SetTextureImport(job.NormalPath, TextureImporterType.NormalMap, sRGB: false, readable: false);
        SetTextureImport(job.EmissionPath, TextureImporterType.Default, sRGB: true, readable: false);
        SetTextureImport(job.MetallicPath, TextureImporterType.Default, sRGB: false, readable: true);
        SetTextureImport(job.RoughnessPath, TextureImporterType.Default, sRGB: false, readable: true);
    }

    static void SetTextureImport(string path, TextureImporterType type, bool sRGB, bool readable)
    {
        if (string.IsNullOrEmpty(path))
            return;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;
        bool dirty = false;
        if (importer.textureType != type) { importer.textureType = type; dirty = true; }
        if (importer.sRGBTexture != sRGB) { importer.sRGBTexture = sRGB; dirty = true; }
        if (importer.isReadable != readable) { importer.isReadable = readable; dirty = true; }
        if (importer.anisoLevel < 4) { importer.anisoLevel = 4; dirty = true; }
        if (dirty)
            importer.SaveAndReimport();
    }

    static Texture2D BuildMaskIfNeeded(ModelJob job)
    {
        string maskPath = $"{MaterialsRoot}/{job.PrefabName}_Mask.png";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
        if (existing != null)
        {
            // Keep sources non-readable.
            SetTextureImport(job.MetallicPath, TextureImporterType.Default, sRGB: false, readable: false);
            SetTextureImport(job.RoughnessPath, TextureImporterType.Default, sRGB: false, readable: false);
            return existing;
        }

        if (string.IsNullOrEmpty(job.MetallicPath) && string.IsNullOrEmpty(job.RoughnessPath))
            return null;

        // Ensure readable for packing.
        SetTextureImport(job.MetallicPath, TextureImporterType.Default, sRGB: false, readable: true);
        SetTextureImport(job.RoughnessPath, TextureImporterType.Default, sRGB: false, readable: true);

        Texture2D metallic = string.IsNullOrEmpty(job.MetallicPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<Texture2D>(job.MetallicPath);
        Texture2D roughness = string.IsNullOrEmpty(job.RoughnessPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<Texture2D>(job.RoughnessPath);

        Texture2D source = metallic != null ? metallic : roughness;
        if (source == null)
            return null;

        int w = source.width;
        int h = source.height;
        var mask = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
        Color32[] mPix = metallic != null ? ResizeOrGet(metallic, w, h) : null;
        Color32[] rPix = roughness != null ? ResizeOrGet(roughness, w, h) : null;
        var outPix = new Color32[w * h];

        for (int i = 0; i < outPix.Length; i++)
        {
            byte metal = mPix != null ? mPix[i].r : (byte)0;
            byte smooth = 128;
            if (rPix != null)
                smooth = (byte)(255 - rPix[i].r);
            outPix[i] = new Color32(metal, metal, metal, smooth);
        }

        mask.SetPixels32(outPix);
        mask.Apply(true, false);
        byte[] png = mask.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(mask);

        string abs = ToAbsolute(maskPath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, png);
        AssetDatabase.ImportAsset(maskPath);

        var maskImporter = AssetImporter.GetAtPath(maskPath) as TextureImporter;
        if (maskImporter != null)
        {
            maskImporter.textureType = TextureImporterType.Default;
            maskImporter.sRGBTexture = false;
            maskImporter.mipmapEnabled = true;
            maskImporter.isReadable = false;
            maskImporter.SaveAndReimport();
        }

        SetTextureImport(job.MetallicPath, TextureImporterType.Default, sRGB: false, readable: false);
        SetTextureImport(job.RoughnessPath, TextureImporterType.Default, sRGB: false, readable: false);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
    }

    static Color32[] ResizeOrGet(Texture2D tex, int w, int h)
    {
        if (tex.width == w && tex.height == h)
            return tex.GetPixels32();

        // Bilinear sample into target size.
        var result = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                Color c = tex.GetPixelBilinear(u, v);
                result[y * w + x] = c;
            }
        }
        return result;
    }

    static Material BuildMaterial(ModelJob job, Texture2D mask)
    {
        string matPath = $"{MaterialsRoot}/{job.PrefabName}.mat";
        Shader shader = Shader.Find("BackHome/CasualToon");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        else
        {
            mat.shader = shader;
        }

        Texture2D albedo = LoadTex(job.AlbedoPath);
        Texture2D emission = LoadTex(job.EmissionPath);

        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", albedo);
        if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", albedo);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);

        // Casual toon defaults (XP Heroes style).
        if (mat.HasProperty("_ShadeSteps"))
            mat.SetFloat("_ShadeSteps", 3f);
        if (mat.HasProperty("_ShadeSoftness"))
            mat.SetFloat("_ShadeSoftness", 0.35f);
        if (mat.HasProperty("_ShadeFloor"))
            mat.SetFloat("_ShadeFloor", 0.48f);
        if (mat.HasProperty("_Saturation"))
            mat.SetFloat("_Saturation", 1.12f);
        if (mat.HasProperty("_Contrast"))
            mat.SetFloat("_Contrast", 1.05f);
        if (mat.HasProperty("_ShadowTint"))
            mat.SetColor("_ShadowTint", new Color(0.62f, 0.68f, 0.92f, 1f));
        if (mat.HasProperty("_KeyTint"))
            mat.SetColor("_KeyTint", new Color(1f, 0.98f, 0.94f, 1f));
        if (mat.HasProperty("_SpecularColor"))
            mat.SetColor("_SpecularColor", new Color(1f, 1f, 1f, 0.55f));
        if (mat.HasProperty("_SpecularSize"))
            mat.SetFloat("_SpecularSize", 0.22f);
        if (mat.HasProperty("_SpecularSoftness"))
            mat.SetFloat("_SpecularSoftness", 0.06f);
        if (mat.HasProperty("_RimColor"))
            mat.SetColor("_RimColor", new Color(0.85f, 0.95f, 1f, 0.35f));
        if (mat.HasProperty("_RimPower"))
            mat.SetFloat("_RimPower", 3.5f);
        if (mat.HasProperty("_RimLightMask"))
            mat.SetFloat("_RimLightMask", 0.75f);
        if (mat.HasProperty("_AmbientStrength"))
            mat.SetFloat("_AmbientStrength", 0.35f);
        if (mat.HasProperty("_AmbientTint"))
            mat.SetColor("_AmbientTint", new Color(0.55f, 0.62f, 0.85f, 1f));

        if (emission != null && mat.HasProperty("_EmissionMap"))
        {
            mat.SetTexture("_EmissionMap", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            mat.SetColor("_EmissionColor", new Color(1.8f, 1.8f, 1.8f, 1f));
        }
        else
        {
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", Color.black);
            if (mat.HasProperty("_EmissionMap"))
                mat.SetTexture("_EmissionMap", null);
        }

        // Plants/leaves often need double-sided.
        if (mat.HasProperty("_Cull"))
            mat.SetFloat("_Cull", 0f);

        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D LoadTex(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static GameObject BuildPrefab(ModelJob job, Material mat)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(job.FbxPath);
        if (source == null)
            return null;

        string prefabPath = $"{PrefabsRoot}/{job.PrefabName}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            AssetDatabase.DeleteAsset(prefabPath);

        // Also remove legacy name without underscores if present (BioluminescentBloom).
        string legacy = $"{PrefabsRoot}/{job.PrefabName.Replace("_", "")}.prefab";
        if (!string.Equals(legacy, prefabPath, StringComparison.Ordinal) &&
            AssetDatabase.LoadAssetAtPath<GameObject>(legacy) != null)
            AssetDatabase.DeleteAsset(legacy);

        string rootLegacy = $"{Root}/{job.PrefabName.Replace("_", "")}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(rootLegacy) != null)
            AssetDatabase.DeleteAsset(rootLegacy);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        var root = new GameObject(job.PrefabName);
        instance.transform.SetParent(root.transform, false);
        instance.name = "Model";
        instance.transform.localPosition = Vector3.zero;
        // Match existing Nyxara prop prefabs (FBX cm → Unity units + Blender-style axis).
        instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        instance.transform.localScale = Vector3.one * 100f;

        if (root.GetComponent<PlanetSurfaceAlign>() == null)
            root.AddComponent<PlanetSurfaceAlign>();

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var mats = renderers[i].sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
                mats[m] = mat;
            renderers[i].sharedMaterials = mats;
            renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderers[i].receiveShadows = true;
        }

        Bounds localBounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;
        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            Mesh mesh = filters[i].sharedMesh;
            if (mesh == null)
                continue;

            Bounds mb = mesh.bounds;
            Transform t = filters[i].transform;
            Vector3 worldCenter = t.TransformPoint(mb.center);
            Vector3 lossy = t.lossyScale;
            var wb = new Bounds(
                worldCenter,
                new Vector3(mb.size.x * Mathf.Abs(lossy.x), mb.size.y * Mathf.Abs(lossy.y), mb.size.z * Mathf.Abs(lossy.z)));

            Vector3 c = root.transform.InverseTransformPoint(wb.center);
            Vector3 rs = root.transform.lossyScale;
            Vector3 s = new Vector3(
                wb.size.x / Mathf.Max(0.0001f, Mathf.Abs(rs.x)),
                wb.size.y / Mathf.Max(0.0001f, Mathf.Abs(rs.y)),
                wb.size.z / Mathf.Max(0.0001f, Mathf.Abs(rs.z)));

            if (!hasBounds)
            {
                localBounds = new Bounds(c, s);
                hasBounds = true;
            }
            else
            {
                localBounds.Encapsulate(new Bounds(c, s));
            }
        }

        if (hasBounds && localBounds.size.sqrMagnitude > 0.0001f)
        {
            NyxaraPropColliderSetup.ApplyColliders(root, destroyImmediate: false);
        }
        else
        {
            NyxaraPropColliderSetup.ApplyColliders(root, destroyImmediate: false);
            if (root.GetComponentInChildren<Collider>(true) == null)
            {
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.5f, 0f);
                box.size = new Vector3(1f, 1f, 1f);
            }
        }

        int ground = LayerMask.NameToLayer(GroundLayerName);
        if (ground >= 0)
            SetLayerRecursive(root, ground);

        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Undo.DestroyObjectImmediate(root);
        return prefabAsset;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        Transform t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i).gameObject, layer);
    }

    static void EnsureFolder(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder))
            return;

        // If the folder already exists on disk (e.g. broken .meta), CreateFolder
        // would spawn "Materials 1", "Materials 2", ... — never do that.
        string abs = ToAbsolute(assetFolder);
        if (Directory.Exists(abs))
        {
            Debug.LogWarning(
                $"[BackHome] Folder exists on disk but AssetDatabase rejects it (likely bad .meta GUID): {assetFolder}");
            return;
        }

        string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
        string name = Path.GetFileName(assetFolder);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            return;
        if (!AssetDatabase.IsValidFolder(parent) && !Directory.Exists(ToAbsolute(parent)))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    static string ToAbsolute(string assetPath)
    {
        if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            assetPath.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, assetPath));
        return assetPath;
    }
}
