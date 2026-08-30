using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a world-space "point toward the capsule" ground marker — a flat Plane mesh aligned to
/// the planet surface, same idea as the old AttackRangeIndicator ring — and wires it as a
/// permanent child of the Player prefab. Also tags the ship capsule prefab with a findable
/// beacon. Runs automatically on editor load.
/// Menu: BackHome → Build Capsule Direction Marker
/// </summary>
public static class CapsuleDirectionMarkerSetup
{
    const string PlayerPrefabPath = "Assets/Resources/Player/Player.prefab";
    const string CapsulePrefabPath = "Assets/Resources/Ship/Capsule/ShipCapsule.prefab";
    const string MaterialPath = "Assets/Resources/Player/Navigation/CapsuleDirectionMarker.mat";
    const string MarkerName = "CapsuleDirectionMarker";

    [InitializeOnLoadMethod]
    static void AutoBuild()
    {
        EditorApplication.delayCall += RunSafe;
    }

    [MenuItem("BackHome/Build Capsule Direction Marker")]
    public static void BuildFromMenu()
    {
        RunSafe();
        EditorUtility.DisplayDialog("Capsule Direction Marker", "Rebuilt capsule direction marker.", "OK");
    }

    public static void BuildBatch()
    {
        RunSafe();
    }

    static void RunSafe()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RunSafe;
            return;
        }

        EnsureBeaconOnCapsule();
        EnsureMarkerOnPlayer();
    }

    public static bool EnsureBeaconOnCapsule()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CapsulePrefabPath);
        if (prefab == null || prefab.GetComponent<ShipCapsuleBeacon>() != null)
            return false;

        GameObject root = PrefabUtility.LoadPrefabContents(CapsulePrefabPath);
        try
        {
            if (root.GetComponent<ShipCapsuleBeacon>() == null)
                root.AddComponent<ShipCapsuleBeacon>();
            PrefabUtility.SaveAsPrefabAsset(root, CapsulePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return true;
    }

    public static bool EnsureMarkerOnPlayer()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null || playerPrefab.transform.Find(MarkerName) != null)
            return false;

        Material material = EnsureMaterial();
        if (material == null)
            return false;

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (root.transform.Find(MarkerName) == null)
            {
                BuildMarkerChild(root.transform, material);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return true;
    }

    static void BuildMarkerChild(Transform playerRoot, Material material)
    {
        var go = new GameObject(MarkerName, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(playerRoot, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = playerRoot.gameObject.layer;

        go.GetComponent<MeshFilter>().sharedMesh = LoadPlaneMesh();

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.enabled = false;

        go.AddComponent<CapsuleDirectionMarker>();
    }

    static Mesh LoadPlaneMesh()
    {
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Plane);
        try
        {
            return temp.GetComponent<MeshFilter>().sharedMesh;
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }
    }

    static Material EnsureMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null)
            return existing;

        Sprite arrowSprite = CasualHudKit.Picto("Arrow_Up");
        Texture2D texture = arrowSprite != null ? arrowSprite.texture : null;
        if (texture == null)
        {
            Debug.LogWarning("[BackHome] Arrow sprite missing — skipped capsule direction marker build.");
            return null;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            return null;

        var material = new Material(shader) { name = "CapsuleDirectionMarker" };
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);

        // Transparent unlit surface (mirrors how other runtime materials in this project
        // configure the URP Unlit surface options from script).
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
        material.SetFloat("_ZWrite", 0f);
        material.renderQueue = (int)RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        string dir = Path.GetDirectoryName(MaterialPath).Replace('\\', '/');
        EnsureFolder(dir);

        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
