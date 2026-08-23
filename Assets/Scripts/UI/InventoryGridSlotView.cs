using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One cell in the equipment panel resource grid.
/// </summary>
public class InventoryGridSlotView : MonoBehaviour
{
    [SerializeField] Image frame;
    [SerializeField] Image icon;
    [SerializeField] TMP_Text count;
    [SerializeField] TMP_Text label;
    [SerializeField] Sprite emptyFrame;
    [SerializeField] Sprite filledFrame;

    public void Bind(ItemDefinition item, int amount, Sprite fallbackIcon)
    {
        bool hasItem = item != null && amount > 0;
        if (frame != null)
        {
            Sprite shownFrame = hasItem
                ? (filledFrame != null ? filledFrame : frame.sprite)
                : (emptyFrame != null ? emptyFrame : frame.sprite);
            if (shownFrame != null)
                ApplyFrame(frame, shownFrame);
            else
                frame.color = Color.white;
        }

        Sprite shown = hasItem ? item.Icon : null;
        if (shown == null && hasItem)
            shown = fallbackIcon;

        if (icon != null)
        {
            icon.sprite = shown;
            icon.enabled = shown != null;
            icon.color = Color.white;
            icon.preserveAspect = true;
        }

        if (count != null)
        {
            count.text = hasItem ? FormatCount(amount) : "";
            count.enabled = hasItem;
        }

        if (label != null)
        {
            label.text = hasItem ? item.DisplayName : "";
            label.enabled = hasItem && shown == null;
        }
    }

    static void ApplyFrame(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.color = Color.white;
        image.type = sprite != null && sprite.border.sqrMagnitude > 1f ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = image.type == Image.Type.Simple;
    }

    static string FormatCount(int amount)
    {
        if (amount >= 1000)
            return (amount / 1000f).ToString("0.#") + "K";
        return "x" + amount;
    }
}
