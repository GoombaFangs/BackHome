using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum EquipmentSlotKind
{
    OxygenTank,
    Health,
    MovementSpeed,
    Weapon
}

/// <summary>
/// One loadout cell on the equipment panel: gear (O2 / HP / boots) or a weapon slot.
/// </summary>
public class EquipmentSlotView : MonoBehaviour
{
    [SerializeField] EquipmentSlotKind kind;
    [SerializeField] int weaponIndex;
    [SerializeField] Image frame;
    [SerializeField] Image icon;
    [SerializeField] Sprite typeIcon;
    [SerializeField] Sprite emptyFrame;
    [SerializeField] Sprite filledFrame;
    [SerializeField] TMP_Text title;
    [SerializeField] TMP_Text value;

    public EquipmentSlotKind Kind => kind;
    public int WeaponIndex => weaponIndex;

    public void Bind(string slotTitle, string slotValue, Sprite slotIcon, Color frameTint)
    {
        if (title != null)
            title.text = slotTitle ?? "";
        if (value != null)
            value.text = slotValue ?? "";

        if (frame != null)
        {
            if (filledFrame != null)
                ApplyFrame(frame, filledFrame);
            else
                frame.color = Color.white;
        }

        if (icon == null)
            return;

        Sprite shown = slotIcon != null ? slotIcon : typeIcon;
        if (kind == EquipmentSlotKind.Weapon && slotIcon == null && !string.IsNullOrEmpty(slotValue) && slotValue != "—")
            shown = null;

        icon.sprite = shown;
        icon.enabled = shown != null;
        icon.color = Color.white;
        icon.preserveAspect = true;
        _ = frameTint;
    }

    static void ApplyFrame(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.color = Color.white;
        image.type = sprite != null && sprite.border.sqrMagnitude > 1f ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = image.type == Image.Type.Simple;
    }

    public void BindEmpty(string slotTitle, Color frameTint)
    {
        Bind(slotTitle, "—", null, frameTint);
        if (frame != null && emptyFrame != null)
            ApplyFrame(frame, emptyFrame);

        if (icon != null && typeIcon != null)
        {
            icon.sprite = typeIcon;
            icon.enabled = true;
            icon.color = new Color(1f, 1f, 1f, 0.4f);
        }
    }
}
