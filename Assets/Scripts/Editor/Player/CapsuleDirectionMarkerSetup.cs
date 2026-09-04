using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds two floating billboard icons (home badge + arrow) that point toward the player capsule,
/// and wires them as a permanent child of the Player prefab. Also tags the capsule prefab
/// with a findable beacon. Runs automatically on editor load.
/// Menu: BackHome → Build Capsule Direction Marker
/// </summary>
public static class CapsuleDirectionMarkerSetup
{
    const string PlayerPrefabPath = "Assets/Resources/Player/Player.prefab";
    const string CapsulePrefabPath = PlayerDiveDownCapsulePaths.AssetCapsulePrefab;
    const string NavigationFolder = "Assets/Resources/Player/Navigation";
    const string MaterialPath = NavigationFolder + "/NavigationSprite.mat";
    const string HomeSpritePath = NavigationFolder + "/HomeArrowNavigation.png";
    const string ArrowSpritePath = NavigationFolder + "/ArrowNavigation.png";
    const string MarkerName = "CapsuleDirectionMarker";
    const string HomeChildName = "HomeArrowNavigation";
    const string ArrowChildName = "ArrowNavigation";

    // Single source of truth for the tunables — kept in sync with CapsuleDirectionMarker's
    // own [SerializeField] defaults, but re-applied here every run since Unity does NOT
    // update already-serialized prefab values when a script's default changes.
    const float HomeOffset = 4.5f;
    const float ArrowOffset = 5.3f;
    const float FloatHeight = 1.5f;
    const float HomeWorldSize = 0.5f;
    const float ArrowWorldSize = 0.4f;
    const float FadeSpeed = 6f;
    const float MaxAlpha = 0.5f;
    const float HideRadius = 8f;

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
        if (prefab == null || prefab.GetComponent<PlayerCapsuleBeacon>() != null)
            return false;

        GameObject root = PrefabUtility.LoadPrefabContents(CapsulePrefabPath);
        try
        {
            if (root.GetComponent<PlayerCapsuleBeacon>() == null)
                root.AddComponent<PlayerCapsuleBeacon>();
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
        if (playerPrefab == null)
            return false;

        Sprite homeSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HomeSpritePath);
        Sprite arrowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
        if (homeSprite == null || arrowSprite == null)
        {
            Debug.LogWarning("[BackHome] Navigation sprites missing — skipped capsule direction marker build.");
            return false;
        }

        Material material = EnsureMaterial();
        if (material == null)
            return false;

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        bool changed;
        try
        {
            changed = BuildOrUpdateMarker(root.transform, material, homeSprite, arrowSprite);
            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return changed;
    }

    static bool BuildOrUpdateMarker(Transform playerRoot, Material material, Sprite homeSprite, Sprite arrowSprite)
    {
        bool changed = false;
        Transform markerRoot = playerRoot.Find(MarkerName);
        GameObject markerGo;
        if (markerRoot == null)
        {
            markerGo = new GameObject(MarkerName);
            markerGo.transform.SetParent(playerRoot, false);
            markerGo.transform.localPosition = Vector3.zero;
            markerGo.transform.localRotation = Quaternion.identity;
            markerGo.transform.localScale = Vector3.one;
            changed = true;
        }
        else
        {
            markerGo = markerRoot.gameObject;
            // Migrate away from the old ground-decal version, if present.
            var oldRenderer = markerGo.GetComponent<MeshRenderer>();
            if (oldRenderer != null)
            {
                Object.DestroyImmediate(oldRenderer);
                changed = true;
            }
            var oldFilter = markerGo.GetComponent<MeshFilter>();
            if (oldFilter != null)
            {
                Object.DestroyImmediate(oldFilter);
                changed = true;
            }
        }

        markerGo.layer = playerRoot.gameObject.layer;

        SpriteRenderer homeRenderer = EnsureIconChild(markerGo.transform, HomeChildName, homeSprite, material, sortOrder: 0, ref changed);
        SpriteRenderer arrowRenderer = EnsureIconChild(markerGo.transform, ArrowChildName, arrowSprite, material, sortOrder: 1, ref changed);

        var marker = markerGo.GetComponent<CapsuleDirectionMarker>();
        if (marker == null)
        {
            marker = markerGo.AddComponent<CapsuleDirectionMarker>();
            changed = true;
        }

        var so = new SerializedObject(marker);
        bool fieldsChanged = false;
        fieldsChanged |= AssignIfDifferent(so, "homeIcon", homeRenderer);
        fieldsChanged |= AssignIfDifferent(so, "arrowIcon", arrowRenderer);
        fieldsChanged |= AssignIfDifferent(so, "homeOffset", HomeOffset);
        fieldsChanged |= AssignIfDifferent(so, "arrowOffset", ArrowOffset);
        fieldsChanged |= AssignIfDifferent(so, "floatHeight", FloatHeight);
        fieldsChanged |= AssignIfDifferent(so, "homeWorldSize", HomeWorldSize);
        fieldsChanged |= AssignIfDifferent(so, "arrowWorldSize", ArrowWorldSize);
        fieldsChanged |= AssignIfDifferent(so, "fadeSpeed", FadeSpeed);
        fieldsChanged |= AssignIfDifferent(so, "maxAlpha", MaxAlpha);
        fieldsChanged |= AssignIfDifferent(so, "hideRadius", HideRadius);
        if (fieldsChanged)
            so.ApplyModifiedPropertiesWithoutUndo();

        return changed || fieldsChanged;
    }

    static SpriteRenderer EnsureIconChild(Transform markerRoot, string name, Sprite sprite, Material material, int sortOrder, ref bool changed)
    {
        Transform child = markerRoot.Find(name);
        GameObject go;
        if (child == null)
        {
            go = new GameObject(name, typeof(SpriteRenderer));
            go.transform.SetParent(markerRoot, false);
            changed = true;
        }
        else
        {
            go = child.gameObject;
            if (go.GetComponent<SpriteRenderer>() == null)
            {
                go.AddComponent<SpriteRenderer>();
                changed = true;
            }
        }

        go.layer = markerRoot.gameObject.layer;

        var renderer = go.GetComponent<SpriteRenderer>();
        if (renderer.sprite != sprite || renderer.sharedMaterial != material || renderer.sortingOrder != sortOrder)
            changed = true;
        renderer.sprite = sprite;
        renderer.sharedMaterial = material;
        renderer.sortingOrder = sortOrder;
        renderer.color = Color.white;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        return renderer;
    }

    static Material EnsureMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null)
            return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return null;

        var material = new Material(shader) { name = "NavigationSprite" };
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    static bool AssignIfDifferent(SerializedObject so, string propertyName, Object value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null || prop.objectReferenceValue == value)
            return false;
        prop.objectReferenceValue = value;
        return true;
    }

    static bool AssignIfDifferent(SerializedObject so, string propertyName, float value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null || Mathf.Approximately(prop.floatValue, value))
            return false;
        prop.floatValue = value;
        return true;
    }
}
