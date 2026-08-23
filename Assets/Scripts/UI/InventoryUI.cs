using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Casual equipment + resource HUD. Toggle stays visible; ContentPanel is the full loadout window.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    const int InventoryGridCapacity = 10;

    [SerializeField] GameObject inventoryPanelPrefab;
    [SerializeField] GameObject inventoryPanel;
    [SerializeField] GameObject contentPanel;
    [SerializeField] Button toggleButton;
    [SerializeField] Button closeButton;
    [SerializeField] TMP_Text powerText;
    [SerializeField] TMP_Text heroNameText;
    [SerializeField] EquipmentSlotView[] equipmentSlots;
    [SerializeField] Transform inventoryGrid;
    [SerializeField] InventoryGridSlotView inventorySlotPrefab;
    [SerializeField] Sprite fallbackResourceIcon;
    [SerializeField] bool startOpen;

    PlayerInventory _inventory;
    PlayerVitals _vitals;
    bool _open;
    bool _boundInventory;
    bool _boundVitals;

    void Start()
    {
        PlayerInventory.EnsureExists();
        _inventory = PlayerInventory.Instance;
        EnsurePanel();
        BindInventory();
        TryBindPlayer();
        SetOpen(startOpen);
        Refresh();
    }

    void Update()
    {
        if (_vitals == null)
        {
            TryBindPlayer();
            if (_vitals != null && _open)
                Refresh();
        }
    }

    void OnDestroy()
    {
        UnbindInventory();
        UnbindPlayer();
    }

    void EnsurePanel()
    {
        if (inventoryPanel == null)
        {
            Transform existing = FindExistingPanel();
            if (existing != null)
                inventoryPanel = existing.gameObject;
        }

        if (inventoryPanel == null)
        {
            if (inventoryPanelPrefab == null)
            {
                Debug.LogWarning($"{nameof(InventoryUI)}: assign InventoryPanel prefab.", this);
                return;
            }

            Canvas canvas = GetComponentInChildren<Canvas>(true);
            Transform parent = canvas != null ? canvas.transform : transform;
            inventoryPanel = Instantiate(inventoryPanelPrefab, parent, false);
            inventoryPanel.name = "InventoryPanel";
        }

        inventoryPanel.SetActive(true);
        inventoryPanel.transform.SetAsLastSibling();
        WireReferences(inventoryPanel.transform);
    }

    Transform FindExistingPanel()
    {
        Transform direct = transform.Find("InventoryPanel");
        if (direct != null)
            return direct;

        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null)
            return canvas.transform.Find("InventoryPanel");

        return null;
    }

    void WireReferences(Transform root)
    {
        if (toggleButton == null)
        {
            Transform toggle = root.Find("ToggleButton");
            if (toggle != null)
                toggleButton = toggle.GetComponent<Button>();
        }

        if (contentPanel == null)
        {
            Transform panel = root.Find("ContentPanel");
            if (panel != null)
                contentPanel = panel.gameObject;
        }

        Transform window = root.Find("ContentPanel/Window");
        if (closeButton == null)
        {
            Transform close = window != null ? window.Find("Header/CloseButton") : null;
            if (close != null)
                closeButton = close.GetComponent<Button>();
        }

        if (powerText == null)
        {
            Transform power = window != null ? window.Find("EquipBody/Hero/Power") : null;
            if (power != null)
                powerText = power.GetComponent<TMP_Text>();
        }

        if (heroNameText == null)
        {
            Transform name = window != null ? window.Find("EquipBody/Hero/Name") : null;
            if (name != null)
                heroNameText = name.GetComponent<TMP_Text>();
        }

        if (equipmentSlots == null || equipmentSlots.Length == 0)
        {
            if (window != null)
                equipmentSlots = window.GetComponentsInChildren<EquipmentSlotView>(true);
        }

        if (inventoryGrid == null && window != null)
        {
            Transform grid = window.Find("InventoryBody/Grid");
            if (grid != null)
                inventoryGrid = grid;
        }

        if (inventorySlotPrefab == null && inventoryGrid != null)
        {
            Transform template = inventoryGrid.Find("SlotTemplate");
            if (template != null)
                inventorySlotPrefab = template.GetComponent<InventoryGridSlotView>();
        }

        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(ToggleOpen);
            toggleButton.onClick.AddListener(ToggleOpen);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
            closeButton.onClick.AddListener(Close);
        }
    }

    void BindInventory()
    {
        if (_inventory == null || _boundInventory)
            return;

        _inventory.Changed += Refresh;
        _boundInventory = true;
    }

    void UnbindInventory()
    {
        if (_inventory == null || !_boundInventory)
            return;

        _inventory.Changed -= Refresh;
        _boundInventory = false;
    }

    void TryBindPlayer()
    {
        if (_vitals != null)
            return;

        GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null)
            return;

        _vitals = playerGo.GetComponent<PlayerVitals>();
        if (_vitals == null)
            _vitals = playerGo.GetComponentInChildren<PlayerVitals>();
        if (_vitals == null)
            return;

        _vitals.VitalsChanged += Refresh;
        _vitals.LoadoutChanged += Refresh;
        _boundVitals = true;
    }

    void UnbindPlayer()
    {
        if (_vitals == null || !_boundVitals)
            return;

        _vitals.VitalsChanged -= Refresh;
        _vitals.LoadoutChanged -= Refresh;
        _boundVitals = false;
        _vitals = null;
    }

    public void ToggleOpen()
    {
        SetOpen(!_open);
    }

    public void Close()
    {
        SetOpen(false);
    }

    void SetOpen(bool open)
    {
        _open = open;
        if (contentPanel != null)
            contentPanel.SetActive(open);
        if (toggleButton != null)
            toggleButton.gameObject.SetActive(!open);
        if (open)
        {
            if (inventoryPanel != null)
                inventoryPanel.transform.SetAsLastSibling();
            Refresh();
        }
    }

    void Refresh()
    {
        if (!_open && contentPanel != null && !contentPanel.activeSelf)
            return;

        RefreshHero();
        RefreshEquipment();
        RefreshInventoryGrid();
    }

    void RefreshHero()
    {
        if (heroNameText != null)
            heroNameText.text = _vitals != null ? _vitals.DisplayName : "Hero";
        if (powerText != null)
            powerText.text = FormatPower(ComputePower());
    }

    void RefreshEquipment()
    {
        if (equipmentSlots == null)
            return;

        for (int i = 0; i < equipmentSlots.Length; i++)
        {
            EquipmentSlotView slot = equipmentSlots[i];
            if (slot == null)
                continue;

            switch (slot.Kind)
            {
                case EquipmentSlotKind.OxygenTank:
                    BindOxygen(slot);
                    break;
                case EquipmentSlotKind.Health:
                    BindHealth(slot);
                    break;
                case EquipmentSlotKind.MovementSpeed:
                    BindMoveSpeed(slot);
                    break;
                case EquipmentSlotKind.Weapon:
                    BindWeapon(slot);
                    break;
            }
        }
    }

    void BindOxygen(EquipmentSlotView slot)
    {
        float tank = _vitals != null ? _vitals.MaxOxygen : 0f;
        slot.Bind("O2 TANK", FormatStat(tank), null, new Color(0.35f, 0.85f, 1f, 1f));
    }

    void BindHealth(EquipmentSlotView slot)
    {
        float hp = _vitals != null ? _vitals.MaxHealth : 0f;
        slot.Bind("SUIT", FormatStat(hp), null, new Color(1f, 0.42f, 0.48f, 1f));
    }

    void BindMoveSpeed(EquipmentSlotView slot)
    {
        float speed = ReadMoveSpeed();
        slot.Bind("BOOTS", FormatStat(speed), null, new Color(1f, 0.82f, 0.28f, 1f));
    }

    void BindWeapon(EquipmentSlotView slot)
    {
        WeaponDefinition weapon = ReadWeapon(slot.WeaponIndex);
        Color tint = new Color(0.72f, 0.45f, 1f, 1f);
        if (weapon == null)
        {
            slot.BindEmpty("WEAPON", tint);
            return;
        }

        slot.Bind(weapon.DisplayName, FormatStat(weapon.Combat.AttackDamage), weapon.Icon, tint);
    }

    void RefreshInventoryGrid()
    {
        if (inventoryGrid == null || inventorySlotPrefab == null)
            return;

        if (_inventory == null)
            _inventory = PlayerInventory.Instance;

        int filled = _inventory != null ? _inventory.Slots.Count : 0;
        int shown = Mathf.Max(InventoryGridCapacity, filled);

        while (CountSpawnedSlots() < shown)
        {
            InventoryGridSlotView extra = Instantiate(inventorySlotPrefab, inventoryGrid);
            extra.gameObject.SetActive(true);
            extra.name = "Slot";
        }

        int slotIndex = 0;
        for (int i = 0; i < inventoryGrid.childCount; i++)
        {
            InventoryGridSlotView view = inventoryGrid.GetChild(i).GetComponent<InventoryGridSlotView>();
            if (view == null || view == inventorySlotPrefab)
                continue;

            bool active = slotIndex < shown;
            view.gameObject.SetActive(active);
            if (!active)
            {
                slotIndex++;
                continue;
            }

            if (_inventory != null && slotIndex < _inventory.Slots.Count)
            {
                PlayerInventory.Slot packed = _inventory.Slots[slotIndex];
                view.Bind(packed.item, packed.count, fallbackResourceIcon);
            }
            else
                view.Bind(null, 0, fallbackResourceIcon);

            slotIndex++;
        }
    }

    int CountSpawnedSlots()
    {
        int n = 0;
        for (int i = 0; i < inventoryGrid.childCount; i++)
        {
            InventoryGridSlotView view = inventoryGrid.GetChild(i).GetComponent<InventoryGridSlotView>();
            if (view != null && view != inventorySlotPrefab)
                n++;
        }

        return n;
    }

    WeaponDefinition ReadWeapon(int index)
    {
        if (_vitals == null || index < 0)
            return null;

        var weapons = _vitals.Weapons;
        int seen = 0;
        for (int i = 0; i < weapons.Count; i++)
        {
            if (weapons[i] == null)
                continue;
            if (seen == index)
                return weapons[i];
            seen++;
        }

        return null;
    }

    float ReadMoveSpeed()
    {
        if (_vitals == null)
            return 0f;

        PlanetWalker walker = _vitals.GetComponent<PlanetWalker>();
        if (walker != null)
            return walker.WalkSpeed;

        TouchController touch = _vitals.GetComponent<TouchController>();
        return touch != null ? touch.WalkSpeed : 0f;
    }

    float ComputePower()
    {
        if (_vitals == null)
            return 0f;

        float power = _vitals.MaxHealth + _vitals.MaxOxygen * 3f + ReadMoveSpeed() * 20f;
        var weapons = _vitals.Weapons;
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponDefinition weapon = weapons[i];
            if (weapon == null)
                continue;
            CombatStats combat = _vitals.CombatFor(weapon);
            power += combat.AttackDamage * Mathf.Max(0.5f, combat.AttackSpeed) * 8f;
        }

        return power;
    }

    static string FormatStat(float value)
    {
        if (value >= 1000f)
            return (value / 1000f).ToString("0.#") + "K";
        if (Mathf.Abs(value - Mathf.Round(value)) < 0.05f)
            return Mathf.RoundToInt(value).ToString();
        return value.ToString("0.#");
    }

    static string FormatPower(float value)
    {
        if (value >= 1000000f)
            return (value / 1000000f).ToString("0.0") + "M";
        if (value >= 1000f)
            return (value / 1000f).ToString("0.0") + "K";
        return Mathf.RoundToInt(value).ToString();
    }
}
