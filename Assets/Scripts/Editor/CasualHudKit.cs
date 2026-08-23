using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Loads sprites and Lilita One TMP fonts from GUI Pro-CasualGame.
/// </summary>
public static class CasualHudKit
{
    public const string Root = "Assets/AssetStore/Layer Lab/GUI Pro-CasualGame";
    const string Sprites = Root + "/ResourcesData/Sprites";
    const string Fonts = Root + "/ResourcesData/Fonts";

    public static Sprite PopupNavy() => Load(Sprites + "/Components/Popup", "Popup01_Single_Navy");
    public static Sprite PopupGlow() => Load(Sprites + "/Components/Popup", "Common_Popup_Glow");
    public static Sprite TitleBlue() => Load(Sprites + "/Components/Label", "Title_Flag01_Blue");
    public static Sprite TitleRed() => Load(Sprites + "/Components/Label", "Title_Flag01_Red");
    public static Sprite BannerNavy() => Load(Sprites + "/Components/Frame", "BannerFrame01_Single_Navy");
    public static Sprite SquareNavy() => Load(Sprites + "/Components/Button", "Button_Square06_Navy");
    public static Sprite SquareClose() => Load(Sprites + "/Components/Button", "Button_Square03_Navy");
    public static Sprite ButtonYellow() => Load(Sprites + "/Components/Button", "Button01_225_Yellow");
    public static Sprite ButtonGreen() => Load(Sprites + "/Components/Button", "Button01_225_Green");
    public static Sprite SlotEmpty() => Load(Sprites + "/Components/Frame", "ItemFrame01_Empty");
    public static Sprite SlotBlue() => Load(Sprites + "/Components/Frame", "ItemFrame01_Single_Blue");
    public static Sprite SlotRed() => Load(Sprites + "/Components/Frame", "ItemFrame01_Single_Red");
    public static Sprite SlotYellow() => Load(Sprites + "/Components/Frame", "ItemFrame01_Single_Yellow");
    public static Sprite SlotPurple() => Load(Sprites + "/Components/Frame", "ItemFrame01_Single_Purple");
    public static Sprite Pedestal() => Load(Sprites + "/Components/UI_Etc", "UserInfo01_Bottom");
    public static Sprite ActionFrameBlue() => Load(Sprites + "/Demo/Demo_Play", "Play_ActionText_Frame_Blue");
    public static Sprite ActionFrameRed() => Load(Sprites + "/Demo/Demo_Play", "Play_ActionText_Frame_Red");
    public static Sprite JoystickBg() => Load(Sprites + "/Demo/Demo_Play", "Play_Joystick_bg");
    public static Sprite JoystickHandle() => Load(Sprites + "/Demo/Demo_Play", "Play_Joystick_handle");

    public static Sprite Picto(string stem, int size = 128)
    {
        Sprite sprite = Load(Sprites + "/Components/Icon_PictoIcons/" + size, stem);
        if (sprite == null)
            sprite = Load(Sprites + "/Components/Icon_PictoIcons/" + size, "Pictoicon_" + stem);
        if (sprite == null)
            sprite = Load(Sprites + "/Components/Icon_PictoIcons/" + size, "PictoIcon_" + stem);
        if (sprite == null)
            sprite = Load(Sprites + "/Components/IconMisc", "Icon_PictoIcon_" + stem);
        return sprite;
    }

    public static Sprite Item(string stem, int size = 128)
    {
        Sprite sprite = Load(Sprites + "/Components/Icon_ItemIcons/" + size, stem);
        if (sprite == null)
            sprite = Load(Sprites + "/Components/Icon_ItemIcons/" + size, "Icon_" + stem);
        if (sprite == null)
            sprite = Load(Sprites + "/Components/Icon_ItemIcons/Original", "Icon_" + stem);
        return sprite;
    }

    public static Sprite Misc(string stem)
    {
        return Load(Sprites + "/Components/IconMisc", stem);
    }

    public static TMP_FontAsset FontSdf()
    {
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "/LilitaOne-Regular SDF.asset");
    }

    public static TMP_FontAsset FontOutline(int size)
    {
        string path = Fonts + "/LilitaOne-Regular Outline " + size + " Bitmap.asset";
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        return font != null ? font : FontSdf();
    }

    public static bool TryLoadCore(out Sprite window)
    {
        window = PopupNavy();
        return window != null && SlotEmpty() != null && FontSdf() != null;
    }

    public static Image.Type TypeOf(Sprite sprite)
    {
        if (sprite != null && sprite.border.sqrMagnitude > 1f)
            return Image.Type.Sliced;
        return Image.Type.Simple;
    }

    public static void Apply(Image image, Sprite sprite, Color color, bool raycast)
    {
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = raycast;
        image.preserveAspect = sprite != null && TypeOf(sprite) == Image.Type.Simple;
        image.type = sprite != null ? TypeOf(sprite) : Image.Type.Simple;
        image.fillCenter = true;
    }

    public static Sprite Load(string folder, string stem)
    {
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(stem))
            return null;

        string absFolder = ToAbsolute(folder);
        if (!Directory.Exists(absFolder))
            return null;

        string[] files = Directory.GetFiles(absFolder);
        for (int i = 0; i < files.Length; i++)
        {
            string file = files[i];
            if (file.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase))
                continue;

            string name = Path.GetFileNameWithoutExtension(file);
            if (!string.Equals(name, stem, System.StringComparison.OrdinalIgnoreCase))
                continue;

            string assetPath = ToAssetPath(file);
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int a = 0; a < assets.Length; a++)
            {
                if (assets[a] is Sprite sprite)
                    return sprite;
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        return null;
    }

    static string ToAbsolute(string assetPath)
    {
        if (assetPath.StartsWith("Assets/", System.StringComparison.Ordinal))
            return Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
        return assetPath;
    }

    static string ToAssetPath(string absolutePath)
    {
        string normalized = absolutePath.Replace('\\', '/');
        string data = Application.dataPath.Replace('\\', '/');
        if (normalized.StartsWith(data, System.StringComparison.OrdinalIgnoreCase))
            return "Assets" + normalized.Substring(data.Length);
        return normalized;
    }
}
