using UnityEngine;

/// <summary>
/// Visual HP + Oxygen bars. Edit this on the VitalsBars prefab.
/// Uses unlit quads so fill colors stay readable in edit mode and in-game.
/// </summary>
[ExecuteAlways]
public class VitalsBarsView : MonoBehaviour
{
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("Layout")]
    [SerializeField] Vector2 barSize = new Vector2(1.1f, 0.14f);
    [SerializeField] float barSpacing = 0.08f;
    [SerializeField, Range(0.4f, 1f)] float fillHeightRatio = 0.72f;

    [Header("Colors")]
    [SerializeField] Color healthFillColor = new Color(0.25f, 0.85f, 0.35f, 1f);
    [SerializeField] Color healthLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    [SerializeField] Color oxygenFillColor = new Color(0.25f, 0.65f, 1f, 1f);
    [SerializeField] Color backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.85f);

    [Header("References")]
    [SerializeField] Transform healthBar;
    [SerializeField] Transform oxygenBar;
    [SerializeField] Transform healthFill;
    [SerializeField] Transform oxygenFill;
    [SerializeField] Renderer healthFillRenderer;
    [SerializeField] Renderer oxygenFillRenderer;
    [SerializeField] Renderer healthBgRenderer;
    [SerializeField] Renderer oxygenBgRenderer;
    [SerializeField] Material barMaterial;

    MaterialPropertyBlock _block;
    float _previewHealth = 1f;
    float _previewOxygen = 1f;

    void OnEnable()
    {
        ApplyLayout();
        SetHealth(_previewHealth);
        SetOxygen(_previewOxygen);
    }

    void OnValidate()
    {
        _previewHealth = 1f;
        _previewOxygen = 1f;
        ApplyLayout();
        SetHealth(1f);
        SetOxygen(1f);
    }

    public void SetHealth(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        _previewHealth = normalized;
        if (healthFill != null)
            ApplyFill(healthFill, normalized);
        SetRendererColor(healthFillRenderer, Color.Lerp(healthLowColor, healthFillColor, Mathf.Clamp01(normalized * 1.5f)));
    }

    public void SetOxygen(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        _previewOxygen = normalized;
        if (oxygenFill != null)
            ApplyFill(oxygenFill, normalized);
        SetRendererColor(oxygenFillRenderer, oxygenFillColor);
    }

    public void ApplyLayout()
    {
        float halfGap = barSize.y * 0.5f + barSpacing * 0.5f;

        if (healthBar != null)
            healthBar.localPosition = new Vector3(0f, halfGap, 0f);
        if (oxygenBar != null)
            oxygenBar.localPosition = new Vector3(0f, -halfGap, 0f);

        if (healthBgRenderer != null)
        {
            healthBgRenderer.transform.localPosition = Vector3.zero;
            healthBgRenderer.transform.localScale = new Vector3(barSize.x, barSize.y, 1f);
            SetRendererColor(healthBgRenderer, backgroundColor);
        }

        if (oxygenBgRenderer != null)
        {
            oxygenBgRenderer.transform.localPosition = Vector3.zero;
            oxygenBgRenderer.transform.localScale = new Vector3(barSize.x, barSize.y, 1f);
            SetRendererColor(oxygenBgRenderer, backgroundColor);
        }

        EnsureMaterials();
    }

    void ApplyFill(Transform fill, float normalized)
    {
        float fillHeight = barSize.y * fillHeightRatio;
        float width = barSize.x * Mathf.Max(0f, normalized);
        fill.localScale = new Vector3(Mathf.Max(0.0001f, width), fillHeight, 1f);
        // Keep fill in front of the background track.
        fill.localPosition = new Vector3(-barSize.x * 0.5f + width * 0.5f, 0f, -0.02f);
    }

    void EnsureMaterials()
    {
        AssignSharedMaterial(healthBgRenderer);
        AssignSharedMaterial(oxygenBgRenderer);
        AssignSharedMaterial(healthFillRenderer);
        AssignSharedMaterial(oxygenFillRenderer);
    }

    void AssignSharedMaterial(Renderer renderer)
    {
        if (renderer == null || barMaterial == null)
            return;
        if (renderer.sharedMaterial != barMaterial)
            renderer.sharedMaterial = barMaterial;
    }

    void SetRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null)
            return;

        AssignSharedMaterial(renderer);
        if (_block == null)
            _block = new MaterialPropertyBlock();

        renderer.GetPropertyBlock(_block);
        _block.SetColor(ColorId, color);
        renderer.SetPropertyBlock(_block);
    }
}
