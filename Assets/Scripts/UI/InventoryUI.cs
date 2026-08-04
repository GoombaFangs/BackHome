using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Placeholder inventory HUD. Instantiates InventoryPanel prefab and lists item counts.
/// Toggle button stays visible; ContentPanel opens/closes.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [SerializeField] GameObject inventoryPanelPrefab;
    [SerializeField] GameObject inventoryPanel;
    [SerializeField] GameObject contentPanel;
    [SerializeField] Button toggleButton;
    [SerializeField] Text contentText;
    [SerializeField] bool startOpen;

    PlayerInventory _inventory;
    bool _open;

    void Start()
    {
        PlayerInventory.EnsureExists();
        _inventory = PlayerInventory.Instance;
        EnsurePanel();
        BindInventory();
        SetOpen(startOpen);
        Refresh();
    }

    void OnDestroy()
    {
        UnbindInventory();
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

        if (contentText == null)
        {
            Transform content = root.Find("ContentPanel/Content");
            if (content != null)
                contentText = content.GetComponent<Text>();
            if (contentText == null && contentPanel != null)
            {
                Transform contentChild = contentPanel.transform.Find("Content");
                if (contentChild != null)
                    contentText = contentChild.GetComponent<Text>();
            }
        }

        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(ToggleOpen);
            toggleButton.onClick.AddListener(ToggleOpen);
        }
    }

    void BindInventory()
    {
        if (_inventory == null)
            return;

        _inventory.Changed -= Refresh;
        _inventory.Changed += Refresh;
    }

    void UnbindInventory()
    {
        if (_inventory == null)
            return;

        _inventory.Changed -= Refresh;
    }

    public void ToggleOpen()
    {
        SetOpen(!_open);
    }

    void SetOpen(bool open)
    {
        _open = open;
        if (contentPanel != null)
            contentPanel.SetActive(open);
    }

    void Refresh()
    {
        if (contentText == null)
            return;

        if (_inventory == null)
            _inventory = PlayerInventory.Instance;

        if (_inventory == null || _inventory.Slots.Count == 0)
        {
            contentText.text = "Inventory empty";
            return;
        }

        var sb = new StringBuilder(128);
        for (int i = 0; i < _inventory.Slots.Count; i++)
        {
            PlayerInventory.Slot slot = _inventory.Slots[i];
            if (slot.item == null)
                continue;

            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(slot.item.DisplayName);
            sb.Append(" x");
            sb.Append(slot.count);
        }

        contentText.text = sb.Length > 0 ? sb.ToString() : "Inventory empty";
    }
}
