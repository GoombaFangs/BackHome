using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shared procedural building blocks for the ShipCapsule crash VFX (<see cref="ShipFireTrail"/>,
/// <see cref="ShipCrashImpact"/>, <see cref="ShipReentryGlow"/>). Everything here is generated in
/// code - no texture/material/mesh assets required - so every effect stays a drop-in component.
/// </summary>
public static class ShipVfxUtility
{
    static Texture2D _softDotTexture;
    static Mesh _cubeMesh;

    /// <summary>Small procedural soft-edged circle. Falloff is baked into both RGB and alpha so
    /// it reads as a round glow whether the shader blends additively or via normal alpha.</summary>
    public static Texture2D GetSoftDotTexture()
    {
        if (_softDotTexture != null)
            return _softDotTexture;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ShipVfx_SoftDot (Generated)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxDist = center.magnitude;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                float falloff = Mathf.Clamp01(1f - Mathf.SmoothStep(0f, 1f, dist));
                falloff = Mathf.Pow(falloff, 1.4f);
                byte channel = (byte)Mathf.RoundToInt(falloff * 255f);
                pixels[y * size + x] = new Color32(channel, channel, channel, channel);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        _softDotTexture = tex;
        return tex;
    }

    /// <summary>
    /// Builds an unlit particle material (URP Particles/Unlit, falling back to Sprites/Default)
    /// with vertex-color-driven tint. Blend state (additive vs normal alpha) is forced directly
    /// via Src/DstBlend so it's correct regardless of which shader variant was found.
    /// </summary>
    public static Material BuildParticleMaterial(Texture2D texture, bool additive, string name)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Transparent");

        Material mat = new Material(shader) { name = name };

        if (mat.HasProperty("_BaseMap"))
            mat.SetTexture("_BaseMap", texture);
        else if (mat.HasProperty("_MainTex"))
            mat.SetTexture("_MainTex", texture);

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        else if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);

        if (mat.HasProperty("_ColorMode"))
            mat.SetFloat("_ColorMode", 0f); // multiply base map by vertex color
        if (mat.HasProperty("_Surface"))
            mat.SetFloat("_Surface", 1f); // transparent
        if (mat.HasProperty("_ZWrite"))
            mat.SetFloat("_ZWrite", 0f);

        int srcBlend = additive ? (int)BlendMode.One : (int)BlendMode.SrcAlpha;
        int dstBlend = additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha;
        if (mat.HasProperty("_SrcBlend"))
            mat.SetInt("_SrcBlend", srcBlend);
        if (mat.HasProperty("_DstBlend"))
            mat.SetInt("_DstBlend", dstBlend);

        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetShaderPassEnabled("ShadowCaster", false);
        return mat;
    }

    /// <summary>Opaque lit material with a baked-in tint (mesh particles don't reliably read
    /// per-particle color on plain Lit/Standard shaders).</summary>
    public static Material BuildOpaqueTintedMaterial(Color tint, string name)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        Material mat = new Material(shader) { name = name };
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", tint);
        else if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", tint);
        return mat;
    }

    /// <summary>Built-in cube mesh, fetched via a throwaway primitive - no mesh asset needed.</summary>
    public static Mesh GetCubeMesh()
    {
        if (_cubeMesh != null)
            return _cubeMesh;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying)
            Object.Destroy(temp);
        else
            Object.DestroyImmediate(temp);
        return _cubeMesh;
    }

    /// <summary>Builds a fade gradient - RGB (not just alpha) trends to black so it fades
    /// correctly even under a forced additive blend that ignores destination alpha.</summary>
    public static Gradient BuildFadeGradient(bool fadeToBlack)
    {
        Gradient gradient = new Gradient();
        Color endColor = fadeToBlack ? Color.black : Color.white;
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(endColor, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 0.85f), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    /// <summary>Combined world-space bounds of every renderer under root - used to auto-fit
    /// effects to whatever the actual ShipCapsule model's size turns out to be.</summary>
    public static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            bounds = new Bounds(root.position, Vector3.one);
            return false;
        }

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }
}
