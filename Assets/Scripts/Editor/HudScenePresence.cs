using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Keeps the HUD authored in playable scenes (and complete inside Hud.prefab)
/// so Canvas / inventory / death layout is visible without entering Play Mode.
/// </summary>
public static class HudScenePresence
{
    const string HudPrefabPath = "Assets/Resources/HUD/Hud.prefab";
    const string InventoryPanelPath = "Assets/Resources/HUD/Panels/InventoryPanel.prefab";
    const string DeathPanelPath = "Assets/Resources/HUD/Panels/DeathPanel.prefab";

    [InitializeOnLoadMethod]
    static void AutoPlace()
    {
        EditorApplication.delayCall += RunSafe;
    }

    [MenuItem("BackHome/Place HUD In Open Scenes")]
    public static void PlaceFromMenu()
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

        SetupHudPrefab();
        PlaceHudInLoadedScenes();
    }

    static void SetupHudPrefab()
    {
        GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
        GameObject inventoryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryPanelPath);
        GameObject deathPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DeathPanelPath);
        if (hudPrefab == null || inventoryPrefab == null || deathPrefab == null)
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        try
        {
            Canvas canvas = root.GetComponentInChildren<Canvas>(true);
            if (canvas == null)
                return;

            Transform canvasTransform = canvas.transform;
            bool changed = false;

            GameObject inventory = FindChild(canvasTransform, "InventoryPanel");
            if (inventory == null)
            {
                inventory = (GameObject)PrefabUtility.InstantiatePrefab(inventoryPrefab, canvasTransform);
                inventory.name = "InventoryPanel";
                StretchFull(inventory.GetComponent<RectTransform>());
                changed = true;
            }

            GameObject death = FindChild(canvasTransform, "DeathPanel");
            if (death == null)
            {
                death = (GameObject)PrefabUtility.InstantiatePrefab(deathPrefab, canvasTransform);
                death.name = "DeathPanel";
                StretchFull(death.GetComponent<RectTransform>());
                death.SetActive(false);
                changed = true;
            }
            else if (death.activeSelf)
            {
                death.SetActive(false);
                changed = true;
            }

            var inventoryUi = root.GetComponent<InventoryUI>();
            if (inventoryUi != null && inventory != null)
            {
                var so = new SerializedObject(inventoryUi);
                SerializedProperty panelProp = so.FindProperty("inventoryPanel");
                if (panelProp != null && panelProp.objectReferenceValue != inventory)
                {
                    panelProp.objectReferenceValue = inventory;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }

            if (EnsureLowHpOnHud(root, canvasTransform))
                changed = true;
            if (EnsureLowOxygenOnHud(root, canvasTransform))
                changed = true;
            if (EnsureDamageOverlayOnHud(root))
                changed = true;

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void PlaceHudInLoadedScenes()
    {
        GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
        if (hudPrefab == null)
            return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;
            if (!HasBootstrap(scene) || FindHud(scene) != null)
                continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, scene);
            instance.name = "Hud";
            Undo.RegisterCreatedObjectUndo(instance, "Place HUD");
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    static bool HasBootstrap(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<SceneBootstrap>(true) != null)
                return true;
        }

        return false;
    }

    static GameObject FindHud(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Hud")
                return root;
            if (root.GetComponentInChildren<PlayerDeathUI>(true) != null)
                return root;
            if (root.GetComponentInChildren<InventoryUI>(true) != null)
                return root;
            if (root.GetComponentInChildren<PlayerLowHpWarning>(true) != null)
                return root;
            if (root.GetComponentInChildren<PlayerLowOxygenWarning>(true) != null)
                return root;
            if (root.GetComponentInChildren<PlayerDamageOverlay>(true) != null)
                return root;
        }

        return null;
    }

    static bool EnsureLowHpOnHud(GameObject root, Transform canvasTransform)
    {
        bool changed = false;
        var warning = root.GetComponent<PlayerLowHpWarning>();
        if (warning == null)
        {
            warning = root.AddComponent<PlayerLowHpWarning>();
            changed = true;
        }

        GameObject panel = FindChild(canvasTransform, "LowHpWarning");
        if (panel != null)
        {
            Object.DestroyImmediate(panel);
            changed = true;
        }

        var so = new SerializedObject(warning);
        changed |= AssignIfDifferent(so, "panel", null);
        changed |= AssignIfDifferent(so, "label", null);
        changed |= AssignIfDifferent(so, "canvasGroup", null);
        if (changed)
            so.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    static bool EnsureLowOxygenOnHud(GameObject root, Transform canvasTransform)
    {
        bool changed = false;
        var warning = root.GetComponent<PlayerLowOxygenWarning>();
        if (warning == null)
        {
            warning = root.AddComponent<PlayerLowOxygenWarning>();
            changed = true;
        }

        GameObject panel = FindChild(canvasTransform, "LowOxygenWarning");
        if (panel == null)
        {
            panel = CreateLowOxygenPanel(canvasTransform);
            changed = true;
        }

        Text label = null;
        Transform labelTransform = panel.transform.Find("Label");
        if (labelTransform != null)
            label = labelTransform.GetComponent<Text>();

        Image white = null;
        Transform whiteTransform = panel.transform.Find("WhiteScreen");
        if (whiteTransform != null)
            white = whiteTransform.GetComponent<Image>();

        if (white == null)
        {
            var whiteGo = new GameObject("WhiteScreen", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            whiteGo.transform.SetParent(panel.transform, false);
            whiteGo.transform.SetAsFirstSibling();
            StretchFull(whiteGo.GetComponent<RectTransform>());
            white = whiteGo.GetComponent<Image>();
            white.color = new Color(1f, 1f, 1f, 0.05f);
            white.raycastTarget = false;
            changed = true;
        }

        var group = panel.GetComponent<CanvasGroup>();
        var so = new SerializedObject(warning);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/HUD/LowOxygenWarning.mat");
        changed |= AssignIfDifferent(so, "textMaterial", mat);
        changed |= AssignIfDifferent(so, "panel", panel);
        changed |= AssignIfDifferent(so, "label", label);
        changed |= AssignIfDifferent(so, "canvasGroup", group);
        changed |= AssignIfDifferent(so, "whiteScreen", white);
        if (changed)
            so.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    static GameObject CreateLowOxygenPanel(Transform canvasTransform)
    {
        var panel = new GameObject("LowOxygenWarning", typeof(RectTransform), typeof(CanvasGroup));
        panel.transform.SetParent(canvasTransform, false);
        StretchFull(panel.GetComponent<RectTransform>());

        var group = panel.GetComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 1f;

        var textGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        textGo.transform.SetParent(panel.transform, false);

        var text = textGo.GetComponent<Text>();
        text.text = "LOW OXYGEN WARNING";
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 44;
        text.fontStyle = FontStyle.Bold;
        text.color = new Color(0.28f, 0.72f, 1f, 1f);
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var textRect = text.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0.5f);
        textRect.anchorMax = new Vector2(1f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(0f, 120f);

        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.02f, 0.08f, 0.16f, 0.92f);
        outline.effectDistance = new Vector2(2.2f, -2.2f);
        outline.useGraphicAlpha = true;

        var shadow = textGo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
        shadow.effectDistance = new Vector2(0f, -3f);
        shadow.useGraphicAlpha = true;

        panel.SetActive(false);
        return panel;
    }

    static bool EnsureDamageOverlayOnHud(GameObject root)
    {
        if (root.GetComponent<PlayerDamageOverlay>() != null)
            return false;

        root.AddComponent<PlayerDamageOverlay>();
        return true;
    }

    static bool AssignIfDifferent(SerializedObject so, string propertyName, Object value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null || prop.objectReferenceValue == value)
            return false;

        prop.objectReferenceValue = value;
        return true;
    }

    static GameObject FindChild(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
                return child.gameObject;
        }

        return null;
    }

    static void StretchFull(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.localPosition = Vector3.zero;
    }
}
