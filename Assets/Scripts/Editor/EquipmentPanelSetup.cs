using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the equipment + resource panel from GUI Pro-CasualGame.
/// Menu: BackHome → Build Equipment Panel
/// </summary>
public static class EquipmentPanelSetup
{
    const string PanelPath = "Assets/Resources/HUD/Panels/InventoryPanel.prefab";

    struct Kit
    {
        public Sprite Window;
        public Sprite Glow;
        public Sprite TitleFlag;
        public Sprite Banner;
        public Sprite SquareNavy;
        public Sprite SquareClose;
        public Sprite SlotEmpty;
        public Sprite SlotBlue;
        public Sprite SlotRed;
        public Sprite SlotYellow;
        public Sprite SlotPurple;
        public Sprite Pedestal;
        public Sprite Bag;
        public Sprite Close;
        public Sprite Oxygen;
        public Sprite Health;
        public Sprite Boots;
        public Sprite Weapon;
        public Sprite Gem;
        public TMP_FontAsset TitleFont;
        public TMP_FontAsset BodyFont;
    }

    [MenuItem("BackHome/Build Equipment Panel")]
    public static void BuildFromMenu()
    {
        if (Build(force: true))
            EditorUtility.DisplayDialog("Equipment Panel", "Rebuilt InventoryPanel from GUI Pro-CasualGame.", "OK");
    }

    public static void BuildBatch()
    {
        Build(force: true);
    }

    public static bool EnsureBuilt()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
        if (prefab == null)
            return false;
        if (UsesCasualKit(prefab))
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

        if (!force && UsesCasualKit(prefab))
            return false;

        Kit kit = LoadKit();
        if (kit.Window == null || kit.SlotEmpty == null)
        {
            Debug.LogWarning("[BackHome] GUI Pro-CasualGame sprites missing — skip equipment panel build.");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PanelPath);
        try
        {
            Rebuild(root, kit);
            PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Debug.Log("[BackHome] Equipment panel built from GUI Pro-CasualGame.");
        return true;
    }

    static bool UsesCasualKit(GameObject prefab)
    {
        return prefab != null && prefab.transform.Find("CasualKit") != null;
    }

    static Kit LoadKit()
    {
        return new Kit
        {
            Window = CasualHudKit.PopupNavy(),
            Glow = CasualHudKit.PopupGlow(),
            TitleFlag = CasualHudKit.TitleBlue(),
            Banner = CasualHudKit.BannerNavy(),
            SquareNavy = CasualHudKit.SquareNavy(),
            SquareClose = CasualHudKit.SquareClose(),
            SlotEmpty = CasualHudKit.SlotEmpty(),
            SlotBlue = CasualHudKit.SlotBlue(),
            SlotRed = CasualHudKit.SlotRed(),
            SlotYellow = CasualHudKit.SlotYellow(),
            SlotPurple = CasualHudKit.SlotPurple(),
            Pedestal = CasualHudKit.Pedestal(),
            Bag = First(CasualHudKit.Item("Bag"), CasualHudKit.Picto("Bag"), CasualHudKit.Misc("Icon_MenuIcon02_Inventory")),
            Close = First(CasualHudKit.Picto("Close"), CasualHudKit.Misc("Icon_PictoIcon_Close")),
            Oxygen = First(CasualHudKit.Item("Potion01_Blue"), CasualHudKit.Picto("Water"), CasualHudKit.Picto("Mana")),
            Health = First(CasualHudKit.Item("Heart"), CasualHudKit.Picto("Health"), CasualHudKit.Item("Emergency_Bag")),
            Boots = First(CasualHudKit.Item("Boots"), CasualHudKit.Picto("Shoes"), CasualHudKit.Misc("Icon_MenuIcon03_Shoes_n")),
            Weapon = First(CasualHudKit.Item("Sword"), CasualHudKit.Picto("Sword"), CasualHudKit.Misc("Icon_MenuIcon03_Weapon_n")),
            Gem = First(CasualHudKit.Item("Gem01_Blue"), CasualHudKit.Item("Gem03_Diamond_Blue"), CasualHudKit.Picto("Gem_Diamond")),
            TitleFont = CasualHudKit.FontOutline(50),
            BodyFont = CasualHudKit.FontOutline(32)
        };
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

    static void Rebuild(GameObject root, Kit kit)
    {
        root.layer = 5;
        RectTransform rootRt = root.GetComponent<RectTransform>();
        if (rootRt == null)
            rootRt = root.AddComponent<RectTransform>();
        Stretch(rootRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        for (int i = root.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

        Go("CasualKit", root.transform, typeof(RectTransform)).SetActive(false);
        CreateToggle(root.transform, kit);
        CreateContent(root.transform, kit);
    }

    static void CreateToggle(Transform parent, Kit kit)
    {
        GameObject toggle = ImageGo("ToggleButton", parent, kit.SquareNavy, Color.white, raycast: true);
        RectTransform rt = toggle.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-18f, -18f);
        rt.sizeDelta = new Vector2(128f, 128f);

        GameObject icon = ImageGo("Icon", toggle.transform, kit.Bag, Color.white, raycast: false);
        RectTransform iconRt = icon.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 0.5f);
        iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = new Vector2(0f, 4f);
        iconRt.sizeDelta = new Vector2(72f, 72f);
        icon.GetComponent<Image>().preserveAspect = true;

        Button button = toggle.AddComponent<Button>();
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        button.colors = colors;
    }

    static void CreateContent(Transform parent, Kit kit)
    {
        GameObject content = Go("ContentPanel", parent, typeof(RectTransform), typeof(CanvasGroup));
        Stretch(content.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        content.GetComponent<CanvasGroup>().blocksRaycasts = true;
        content.SetActive(false);

        GameObject dimmer = ImageGo("Dimmer", content.transform, null, new Color(0.02f, 0.05f, 0.14f, 0.72f), raycast: true);
        Stretch(dimmer.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        GameObject glow = ImageGo("Glow", content.transform, kit.Glow, new Color(0.35f, 0.55f, 1f, 0.55f), raycast: false);
        RectTransform glowRt = glow.GetComponent<RectTransform>();
        glowRt.anchorMin = new Vector2(0.5f, 0.5f);
        glowRt.anchorMax = new Vector2(0.5f, 0.5f);
        glowRt.pivot = new Vector2(0.5f, 0.5f);
        glowRt.anchoredPosition = Vector2.zero;
        glowRt.sizeDelta = new Vector2(1080f, 1780f);

        GameObject window = Go("Window", content.transform, typeof(RectTransform));
        RectTransform windowRt = window.GetComponent<RectTransform>();
        windowRt.anchorMin = new Vector2(0.5f, 0.5f);
        windowRt.anchorMax = new Vector2(0.5f, 0.5f);
        windowRt.pivot = new Vector2(0.5f, 0.5f);
        windowRt.anchoredPosition = Vector2.zero;
        windowRt.sizeDelta = new Vector2(980f, 1680f);

        GameObject bg = ImageGo("Bg", window.transform, kit.Window, Color.white, raycast: true);
        Stretch(bg.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        CreateHeader(window.transform, kit);
        CreateEquipBody(window.transform, kit);
        CreateInventoryBody(window.transform, kit);
    }

    static void CreateHeader(Transform window, Kit kit)
    {
        GameObject header = Go("Header", window, typeof(RectTransform));
        RectTransform rt = header.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -36f);
        rt.sizeDelta = new Vector2(0f, 140f);

        GameObject bar = ImageGo("TitleBar", header.transform, kit.TitleFlag, Color.white, raycast: false);
        RectTransform barRt = bar.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0.5f, 0.5f);
        barRt.anchorMax = new Vector2(0.5f, 0.5f);
        barRt.pivot = new Vector2(0.5f, 0.5f);
        barRt.anchoredPosition = new Vector2(0f, 6f);
        barRt.sizeDelta = new Vector2(560f, 120f);
        bar.GetComponent<Image>().preserveAspect = true;

        GameObject title = TmpGo("Title", bar.transform, kit.TitleFont, 50, Color.white, TextAlignmentOptions.Center, "EQUIPMENT");
        Stretch(title.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        GameObject close = ImageGo("CloseButton", header.transform, kit.SquareClose, Color.white, raycast: true);
        RectTransform closeRt = close.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 0.5f);
        closeRt.anchorMax = new Vector2(1f, 0.5f);
        closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.anchoredPosition = new Vector2(-28f, 0f);
        closeRt.sizeDelta = new Vector2(96f, 96f);

        GameObject closeIcon = ImageGo("Icon", close.transform, kit.Close, Color.white, raycast: false);
        RectTransform closeIconRt = closeIcon.GetComponent<RectTransform>();
        closeIconRt.anchorMin = new Vector2(0.5f, 0.5f);
        closeIconRt.anchorMax = new Vector2(0.5f, 0.5f);
        closeIconRt.pivot = new Vector2(0.5f, 0.5f);
        closeIconRt.anchoredPosition = Vector2.zero;
        closeIconRt.sizeDelta = new Vector2(46f, 46f);
        closeIcon.GetComponent<Image>().preserveAspect = true;
        close.AddComponent<Button>();
    }

    static void CreateEquipBody(Transform window, Kit kit)
    {
        GameObject body = Go("EquipBody", window, typeof(RectTransform));
        RectTransform rt = body.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.36f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(36f, 8f);
        rt.offsetMax = new Vector2(-36f, -180f);

        GameObject left = Column("LeftSlots", body.transform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(92f, 0f), new Vector2(176f, -24f));
        CreateEquipSlot(left.transform, "OxygenSlot", EquipmentSlotKind.OxygenTank, 0, kit, kit.SlotBlue, kit.Oxygen, "O2 TANK");
        CreateEquipSlot(left.transform, "HealthSlot", EquipmentSlotKind.Health, 0, kit, kit.SlotRed, kit.Health, "SUIT");
        CreateEquipSlot(left.transform, "BootsSlot", EquipmentSlotKind.MovementSpeed, 0, kit, kit.SlotYellow, kit.Boots, "BOOTS");

        CreateHero(body.transform, kit);

        GameObject right = Column("RightSlots", body.transform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-92f, 0f), new Vector2(176f, -24f));
        CreateEquipSlot(right.transform, "WeaponSlot1", EquipmentSlotKind.Weapon, 0, kit, kit.SlotPurple, kit.Weapon, "WEAPON");
        CreateEquipSlot(right.transform, "WeaponSlot2", EquipmentSlotKind.Weapon, 1, kit, kit.SlotPurple, kit.Weapon, "WEAPON");
        CreateEquipSlot(right.transform, "WeaponSlot3", EquipmentSlotKind.Weapon, 2, kit, kit.SlotPurple, kit.Weapon, "WEAPON");
    }

    static GameObject Column(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        GameObject column = Go(name, parent, typeof(RectTransform));
        RectTransform rt = column.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var layout = column.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 18f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return column;
    }

    static void CreateHero(Transform parent, Kit kit)
    {
        GameObject hero = Go("Hero", parent, typeof(RectTransform));
        RectTransform rt = hero.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.08f);
        rt.anchorMax = new Vector2(0.5f, 0.92f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(360f, 0f);

        GameObject pedestal = ImageGo("Pedestal", hero.transform, kit.Pedestal, Color.white, raycast: false);
        RectTransform pedRt = pedestal.GetComponent<RectTransform>();
        pedRt.anchorMin = new Vector2(0.5f, 0.16f);
        pedRt.anchorMax = new Vector2(0.5f, 0.16f);
        pedRt.pivot = new Vector2(0.5f, 0.5f);
        pedRt.anchoredPosition = Vector2.zero;
        pedRt.sizeDelta = new Vector2(280f, 120f);
        pedestal.GetComponent<Image>().preserveAspect = true;

        GameObject name = TmpGo("Name", hero.transform, kit.TitleFont, 40, Color.white, TextAlignmentOptions.Center, "HERO");
        RectTransform nameRt = name.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0f, 0.72f);
        nameRt.anchorMax = new Vector2(1f, 0.84f);
        nameRt.offsetMin = Vector2.zero;
        nameRt.offsetMax = Vector2.zero;

        GameObject power = TmpGo("Power", hero.transform, kit.TitleFont, 50, new Color(1f, 0.92f, 0.38f, 1f), TextAlignmentOptions.Center, "0");
        RectTransform powerRt = power.GetComponent<RectTransform>();
        powerRt.anchorMin = new Vector2(0f, 0.84f);
        powerRt.anchorMax = new Vector2(1f, 0.98f);
        powerRt.offsetMin = Vector2.zero;
        powerRt.offsetMax = Vector2.zero;
    }

    static void CreateEquipSlot(Transform parent, string name, EquipmentSlotKind kind, int weaponIndex, Kit kit, Sprite filled, Sprite typeIcon, string defaultTitle)
    {
        GameObject slot = ImageGo(name, parent, kit.SlotEmpty, Color.white, raycast: false);
        var le = slot.AddComponent<LayoutElement>();
        le.minHeight = 168f;
        le.preferredHeight = 168f;
        le.flexibleHeight = 0f;
        slot.GetComponent<Image>().preserveAspect = true;

        GameObject icon = ImageGo("Icon", slot.transform, typeIcon, Color.white, raycast: false);
        RectTransform iconRt = icon.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 0.5f);
        iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = new Vector2(0f, 8f);
        iconRt.sizeDelta = new Vector2(84f, 84f);
        icon.GetComponent<Image>().preserveAspect = true;

        GameObject title = TmpGo("Title", slot.transform, kit.BodyFont, 18, Color.white, TextAlignmentOptions.TopLeft, defaultTitle);
        RectTransform titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 1f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.pivot = new Vector2(0f, 1f);
        titleRt.anchoredPosition = new Vector2(12f, -8f);
        titleRt.sizeDelta = new Vector2(-20f, 28f);

        GameObject value = TmpGo("Value", slot.transform, kit.TitleFont, 22, Color.white, TextAlignmentOptions.Bottom, "—");
        RectTransform valueRt = value.GetComponent<RectTransform>();
        valueRt.anchorMin = new Vector2(0f, 0f);
        valueRt.anchorMax = new Vector2(1f, 0f);
        valueRt.pivot = new Vector2(0.5f, 0f);
        valueRt.anchoredPosition = new Vector2(0f, 10f);
        valueRt.sizeDelta = new Vector2(-12f, 30f);

        var view = slot.AddComponent<EquipmentSlotView>();
        var so = new SerializedObject(view);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.FindProperty("weaponIndex").intValue = weaponIndex;
        so.FindProperty("frame").objectReferenceValue = slot.GetComponent<Image>();
        so.FindProperty("icon").objectReferenceValue = icon.GetComponent<Image>();
        so.FindProperty("typeIcon").objectReferenceValue = typeIcon;
        so.FindProperty("emptyFrame").objectReferenceValue = kit.SlotEmpty;
        so.FindProperty("filledFrame").objectReferenceValue = filled != null ? filled : kit.SlotBlue;
        so.FindProperty("title").objectReferenceValue = title.GetComponent<TMP_Text>();
        so.FindProperty("value").objectReferenceValue = value.GetComponent<TMP_Text>();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void CreateInventoryBody(Transform window, Kit kit)
    {
        GameObject body = Go("InventoryBody", window, typeof(RectTransform));
        RectTransform rt = body.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0.36f);
        rt.offsetMin = new Vector2(40f, 36f);
        rt.offsetMax = new Vector2(-40f, -8f);

        GameObject divider = ImageGo("Divider", body.transform, kit.Banner, Color.white, raycast: false);
        RectTransform divRt = divider.GetComponent<RectTransform>();
        divRt.anchorMin = new Vector2(0f, 1f);
        divRt.anchorMax = new Vector2(1f, 1f);
        divRt.pivot = new Vector2(0.5f, 1f);
        divRt.anchoredPosition = Vector2.zero;
        divRt.sizeDelta = new Vector2(0f, 72f);

        GameObject label = TmpGo("Label", divider.transform, kit.TitleFont, 32, Color.white, TextAlignmentOptions.Center, "RESOURCES");
        Stretch(label.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        GameObject grid = Go("Grid", body.transform, typeof(RectTransform));
        RectTransform gridRt = grid.GetComponent<RectTransform>();
        gridRt.anchorMin = Vector2.zero;
        gridRt.anchorMax = Vector2.one;
        gridRt.offsetMin = new Vector2(8f, 8f);
        gridRt.offsetMax = new Vector2(-8f, -84f);

        var layout = grid.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(156f, 156f);
        layout.spacing = new Vector2(14f, 14f);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 5;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.padding = new RectOffset(4, 4, 4, 4);

        CreateInventoryTemplate(grid.transform, kit);
    }

    static void CreateInventoryTemplate(Transform grid, Kit kit)
    {
        GameObject slot = ImageGo("SlotTemplate", grid, kit.SlotEmpty, Color.white, raycast: false);
        slot.GetComponent<Image>().preserveAspect = true;
        slot.SetActive(false);

        GameObject icon = ImageGo("Icon", slot.transform, kit.Gem, Color.white, raycast: false);
        RectTransform iconRt = icon.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 0.52f);
        iconRt.anchorMax = new Vector2(0.5f, 0.52f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.anchoredPosition = Vector2.zero;
        iconRt.sizeDelta = new Vector2(78f, 78f);
        icon.GetComponent<Image>().preserveAspect = true;

        GameObject count = TmpGo("Count", slot.transform, kit.TitleFont, 22, Color.white, TextAlignmentOptions.BottomRight, "x0");
        RectTransform countRt = count.GetComponent<RectTransform>();
        countRt.anchorMin = new Vector2(0f, 0f);
        countRt.anchorMax = new Vector2(1f, 0f);
        countRt.pivot = new Vector2(1f, 0f);
        countRt.anchoredPosition = new Vector2(-10f, 8f);
        countRt.sizeDelta = new Vector2(-16f, 28f);

        GameObject label = TmpGo("Label", slot.transform, kit.BodyFont, 16, Color.white, TextAlignmentOptions.Top, "");
        RectTransform labelRt = label.GetComponent<RectTransform>();
        labelRt.anchorMin = new Vector2(0f, 1f);
        labelRt.anchorMax = new Vector2(1f, 1f);
        labelRt.pivot = new Vector2(0.5f, 1f);
        labelRt.anchoredPosition = new Vector2(0f, -8f);
        labelRt.sizeDelta = new Vector2(-10f, 24f);

        var view = slot.AddComponent<InventoryGridSlotView>();
        var so = new SerializedObject(view);
        so.FindProperty("frame").objectReferenceValue = slot.GetComponent<Image>();
        so.FindProperty("icon").objectReferenceValue = icon.GetComponent<Image>();
        so.FindProperty("count").objectReferenceValue = count.GetComponent<TMP_Text>();
        so.FindProperty("label").objectReferenceValue = label.GetComponent<TMP_Text>();
        so.FindProperty("emptyFrame").objectReferenceValue = kit.SlotEmpty;
        so.FindProperty("filledFrame").objectReferenceValue = kit.SlotYellow;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static GameObject Go(string name, Transform parent, params System.Type[] components)
    {
        var go = new GameObject(name, components);
        go.layer = 5;
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject ImageGo(string name, Transform parent, Sprite sprite, Color color, bool raycast)
    {
        GameObject go = Go(name, parent, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        CasualHudKit.Apply(go.GetComponent<Image>(), sprite, color, raycast);
        return go;
    }

    static GameObject TmpGo(string name, Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align, string text)
    {
        GameObject go = Go(name, parent, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        var ui = go.GetComponent<TextMeshProUGUI>();
        ui.font = font;
        ui.fontSize = size;
        ui.color = color;
        ui.alignment = align;
        ui.text = text;
        ui.raycastTarget = false;
        ui.enableWordWrapping = false;
        ui.overflowMode = TextOverflowModes.Overflow;
        ui.richText = false;
        return go;
    }

    static void Stretch(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }
}
