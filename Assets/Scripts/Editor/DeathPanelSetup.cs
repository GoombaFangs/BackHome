using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the death / continue panel from GUI Pro-CasualGame.
/// Menu: BackHome → Build Death Panel
/// </summary>
public static class DeathPanelSetup
{
    const string PanelPath = "Assets/Resources/HUD/Panels/DeathPanel.prefab";

    [MenuItem("BackHome/Build Death Panel")]
    public static void BuildFromMenu()
    {
        if (Build(force: true))
            EditorUtility.DisplayDialog("Death Panel", "Rebuilt DeathPanel from GUI Pro-CasualGame.", "OK");
    }

    public static bool EnsureBuilt()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        if (prefab == null)
            return false;
        if (prefab.transform.Find("CasualKit") != null)
            return false;
        return Build(force: true);
    }

    public static bool Build(bool force)
    {
        if (Application.isPlaying)
            return false;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        if (prefab == null)
            return false;

        if (!force && prefab.transform.Find("CasualKit") != null)
            return false;

        Sprite titleFlag = CasualHudKit.TitleRed();
        Sprite button = CasualHudKit.ButtonYellow();
        Sprite glow = CasualHudKit.PopupGlow();
        Sprite skull = First(CasualHudKit.Picto("Skull"), CasualHudKit.Misc("Icon_StatsIcon_Skeleton"), CasualHudKit.Item("Pumkin"));
        TMP_FontAsset titleFont = CasualHudKit.FontOutline(120);
        TMP_FontAsset bodyFont = CasualHudKit.FontOutline(50);
        if (titleFlag == null || button == null || titleFont == null)
        {
            Debug.LogWarning("[BackHome] GUI Pro-CasualGame sprites missing — skip death panel build.");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PanelPath);
        try
        {
            Rebuild(root, titleFlag, button, glow, skull, titleFont, bodyFont);
            PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log("[BackHome] Death panel built from GUI Pro-CasualGame.");
        return true;
    }

    static Sprite First(params Sprite[] sprites)
    {
        for (int i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] != null)
                return sprites[i];
        }

        return null;
    }

    static void Rebuild(GameObject root, Sprite titleFlag, Sprite buttonSprite, Sprite glow, Sprite skull, TMP_FontAsset titleFont, TMP_FontAsset bodyFont)
    {
        root.layer = 5;
        RectTransform rootRt = root.GetComponent<RectTransform>();
        if (rootRt == null)
            rootRt = root.AddComponent<RectTransform>();
        Stretch(rootRt);

        for (int i = root.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

        var marker = new GameObject("CasualKit", typeof(RectTransform));
        marker.transform.SetParent(root.transform, false);
        marker.SetActive(false);

        GameObject dimmer = ImageGo("Dimmer", root.transform, null, new Color(0.04f, 0.02f, 0.08f, 0.78f), true);
        Stretch(dimmer.GetComponent<RectTransform>());

        GameObject glowGo = ImageGo("Glow", root.transform, glow, new Color(1f, 0.18f, 0.22f, 0.55f), false);
        RectTransform glowRt = glowGo.GetComponent<RectTransform>();
        glowRt.anchorMin = new Vector2(0.5f, 0.5f);
        glowRt.anchorMax = new Vector2(0.5f, 0.5f);
        glowRt.pivot = new Vector2(0.5f, 0.5f);
        glowRt.anchoredPosition = new Vector2(0f, 80f);
        glowRt.sizeDelta = new Vector2(980f, 720f);

        GameObject box = new GameObject("Box", typeof(RectTransform));
        box.layer = 5;
        box.transform.SetParent(root.transform, false);
        RectTransform boxRt = box.GetComponent<RectTransform>();
        boxRt.anchorMin = new Vector2(0.5f, 0.5f);
        boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.anchoredPosition = new Vector2(0f, 40f);
        boxRt.sizeDelta = new Vector2(860f, 720f);

        if (skull != null)
        {
            GameObject skullGo = ImageGo("Skull", box.transform, skull, Color.white, false);
            RectTransform skullRt = skullGo.GetComponent<RectTransform>();
            skullRt.anchorMin = new Vector2(0.5f, 1f);
            skullRt.anchorMax = new Vector2(0.5f, 1f);
            skullRt.pivot = new Vector2(0.5f, 1f);
            skullRt.anchoredPosition = new Vector2(0f, 8f);
            skullRt.sizeDelta = new Vector2(120f, 120f);
            skullGo.GetComponent<Image>().preserveAspect = true;
        }

        GameObject flag = ImageGo("TitleFlag", box.transform, titleFlag, Color.white, false);
        RectTransform flagRt = flag.GetComponent<RectTransform>();
        flagRt.anchorMin = new Vector2(0.5f, 0.62f);
        flagRt.anchorMax = new Vector2(0.5f, 0.62f);
        flagRt.pivot = new Vector2(0.5f, 0.5f);
        flagRt.anchoredPosition = Vector2.zero;
        flagRt.sizeDelta = new Vector2(760f, 180f);
        flag.GetComponent<Image>().preserveAspect = true;

        GameObject title = TmpGo("Title", flag.transform, titleFont, 72, Color.white, TextAlignmentOptions.Center, "YOU ARE DEAD");
        Stretch(title.GetComponent<RectTransform>());
        var titleTmp = title.GetComponent<TextMeshProUGUI>();
        titleTmp.enableAutoSizing = true;
        titleTmp.fontSizeMin = 36;
        titleTmp.fontSizeMax = 72;

        GameObject prompt = TmpGo("Prompt", box.transform, bodyFont, 40, Color.white, TextAlignmentOptions.Center, "Continue?");
        RectTransform promptRt = prompt.GetComponent<RectTransform>();
        promptRt.anchorMin = new Vector2(0f, 0.28f);
        promptRt.anchorMax = new Vector2(1f, 0.42f);
        promptRt.offsetMin = Vector2.zero;
        promptRt.offsetMax = Vector2.zero;

        GameObject continueBtn = ImageGo("ContinueButton", box.transform, buttonSprite, Color.white, true);
        RectTransform btnRt = continueBtn.GetComponent<RectTransform>();
        btnRt.anchorMin = new Vector2(0.5f, 0f);
        btnRt.anchorMax = new Vector2(0.5f, 0f);
        btnRt.pivot = new Vector2(0.5f, 0f);
        btnRt.anchoredPosition = new Vector2(0f, 24f);
        btnRt.sizeDelta = new Vector2(520f, 140f);

        GameObject label = TmpGo("Label", continueBtn.transform, bodyFont, 50, new Color(0.12f, 0.08f, 0.04f, 1f), TextAlignmentOptions.Center, "CONTINUE");
        Stretch(label.GetComponent<RectTransform>());

        Button button = continueBtn.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        button.colors = colors;
    }

    static GameObject ImageGo(string name, Transform parent, Sprite sprite, Color color, bool raycast)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        CasualHudKit.Apply(go.GetComponent<Image>(), sprite, color, raycast);
        return go;
    }

    static GameObject TmpGo(string name, Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align, string text)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = 5;
        go.transform.SetParent(parent, false);
        var ui = go.GetComponent<TextMeshProUGUI>();
        ui.font = font;
        ui.fontSize = size;
        ui.color = color;
        ui.alignment = align;
        ui.text = text;
        ui.raycastTarget = false;
        ui.textWrappingMode = TextWrappingModes.NoWrap;
        ui.overflowMode = TextOverflowModes.Overflow;
        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }
}
