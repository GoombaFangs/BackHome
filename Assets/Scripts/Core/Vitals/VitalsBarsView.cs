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
    [SerializeField] bool showOxygen = true;

    [Header("Value Labels")]
    [SerializeField] bool showValueLabels = true;
    [SerializeField] float labelOffsetX = 0.68f;
    [SerializeField] float labelCharacterSize = 0.065f;
    [SerializeField] Color labelColor = new Color(1f, 1f, 1f, 0.95f);

    [Header("Colors")]
    [SerializeField] Color healthFillColor = new Color(0.25f, 0.85f, 0.35f, 1f);
    [SerializeField] Color healthLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    [SerializeField] Color healthHitFlashColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] Color oxygenFillColor = new Color(0.25f, 0.65f, 1f, 1f);
    [SerializeField] Color backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.85f);
    [SerializeField, Min(0.01f)] float hitFlashDuration = 0.12f;

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
    [SerializeField] TextMesh healthLabel;
    [SerializeField] TextMesh oxygenLabel;

    MaterialPropertyBlock _block;
    float _previewHealth = 1f;
    float _previewOxygen = 1f;
    float _healthCurrent = 1f;
    float _healthMax = 1f;
    float _oxygenCurrent = 1f;
    float _oxygenMax = 1f;
    float _hitFlashTimer;

    void Update()
    {
        if (_hitFlashTimer <= 0f)
            return;

        _hitFlashTimer -= Time.deltaTime;
        if (_hitFlashTimer <= 0f)
        {
            _hitFlashTimer = 0f;
            ApplyHealthFillColor(_previewHealth);
        }
    }

    void OnEnable()
    {
        EnsureValueLabels();
        ApplyLayout();
        SetHealth(_previewHealth);
        SetOxygen(_previewOxygen);
        RefreshValueLabels();
    }

    void OnValidate()
    {
        _previewHealth = 1f;
        _previewOxygen = 1f;
        EnsureValueLabels();
        ApplyLayout();
        SetHealth(1f);
        SetOxygen(1f);
        if (_healthMax <= 0f)
            _healthMax = 1f;
        if (_oxygenMax <= 0f)
            _oxygenMax = 1f;
        RefreshValueLabels();
    }

    public void SetOxygenVisible(bool visible)
    {
        showOxygen = visible;
        ApplyLayout();
        RefreshValueLabels();
    }

    /// <summary>
    /// Sets fill from normalized 0-1. Prefer <see cref="SetHealthValues"/> when you have real HP numbers.
    /// </summary>
    public void SetHealth(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        _previewHealth = normalized;
        if (healthFill != null)
            ApplyFill(healthFill, normalized);

        if (_hitFlashTimer <= 0f)
            ApplyHealthFillColor(normalized);
    }

    /// <summary>Brief white flash on the health fill when taking a hit.</summary>
    public void FlashHealthHit()
    {
        _hitFlashTimer = hitFlashDuration;
        SetRendererColor(healthFillRenderer, healthHitFlashColor);
    }

    void ApplyHealthFillColor(float normalized)
    {
        SetRendererColor(
            healthFillRenderer,
            Color.Lerp(healthLowColor, healthFillColor, Mathf.Clamp01(normalized * 1.5f)));
    }

    /// <summary>
    /// Sets fill + label from current/max HP (from PlayerStats / CreatureStats).
    /// </summary>
    public void SetHealthValues(float current, float max)
    {
        _healthMax = Mathf.Max(0f, max);
        _healthCurrent = Mathf.Clamp(current, 0f, _healthMax > 0f ? _healthMax : current);
        float normalized = _healthMax > 0f ? _healthCurrent / _healthMax : 0f;
        SetHealth(normalized);
        RefreshHealthLabel();
    }

    public void SetOxygen(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        _previewOxygen = normalized;
        if (!showOxygen)
            return;

        if (oxygenFill != null)
            ApplyFill(oxygenFill, normalized);
        SetRendererColor(oxygenFillRenderer, oxygenFillColor);
    }

    public void SetOxygenValues(float current, float max)
    {
        _oxygenMax = Mathf.Max(0f, max);
        _oxygenCurrent = Mathf.Clamp(current, 0f, _oxygenMax > 0f ? _oxygenMax : current);
        float normalized = _oxygenMax > 0f ? _oxygenCurrent / _oxygenMax : 0f;
        SetOxygen(normalized);
        RefreshOxygenLabel();
    }

    public void ApplyLayout()
    {
        if (oxygenBar != null)
            oxygenBar.gameObject.SetActive(showOxygen);

        if (showOxygen)
        {
            float halfGap = barSize.y * 0.5f + barSpacing * 0.5f;
            if (healthBar != null)
                healthBar.localPosition = new Vector3(0f, halfGap, 0f);
            if (oxygenBar != null)
                oxygenBar.localPosition = new Vector3(0f, -halfGap, 0f);
        }
        else if (healthBar != null)
        {
            healthBar.localPosition = Vector3.zero;
        }

        if (healthBgRenderer != null)
        {
            healthBgRenderer.transform.localPosition = Vector3.zero;
            healthBgRenderer.transform.localScale = new Vector3(barSize.x, barSize.y, 1f);
            SetRendererColor(healthBgRenderer, backgroundColor);
        }

        if (showOxygen && oxygenBgRenderer != null)
        {
            oxygenBgRenderer.transform.localPosition = Vector3.zero;
            oxygenBgRenderer.transform.localScale = new Vector3(barSize.x, barSize.y, 1f);
            SetRendererColor(oxygenBgRenderer, backgroundColor);
        }

        PositionValueLabels();
        EnsureMaterials();
    }

    void EnsureValueLabels()
    {
        // Labels live on the VitalsBars prefab — never spawn them at runtime.
        if (healthLabel == null && healthBar != null)
        {
            Transform t = healthBar.Find("HealthValue");
            if (t != null)
                healthLabel = t.GetComponent<TextMesh>();
        }

        if (oxygenLabel == null && oxygenBar != null)
        {
            Transform t = oxygenBar.Find("OxygenValue");
            if (t != null)
                oxygenLabel = t.GetComponent<TextMesh>();
        }

        PositionValueLabels();
    }

    void PositionValueLabels()
    {
        // Place just to the right of the bar track.
        float x = barSize.x * 0.5f + Mathf.Max(0.02f, labelOffsetX * 0.12f);

        if (healthLabel != null)
        {
            healthLabel.characterSize = labelCharacterSize;
            healthLabel.color = labelColor;
            healthLabel.transform.localPosition = new Vector3(x, 0f, -0.03f);
            healthLabel.transform.localRotation = Quaternion.identity;
            healthLabel.gameObject.SetActive(showValueLabels);
        }

        if (oxygenLabel != null)
        {
            oxygenLabel.characterSize = labelCharacterSize;
            oxygenLabel.color = labelColor;
            oxygenLabel.transform.localPosition = new Vector3(x, 0f, -0.03f);
            oxygenLabel.transform.localRotation = Quaternion.identity;
            oxygenLabel.gameObject.SetActive(showValueLabels && showOxygen);
        }
    }

    void RefreshValueLabels()
    {
        RefreshHealthLabel();
        RefreshOxygenLabel();
    }

    void RefreshHealthLabel()
    {
        if (healthLabel == null)
            return;

        healthLabel.gameObject.SetActive(showValueLabels);
        if (!showValueLabels)
            return;

        healthLabel.text = FormatValue(_healthCurrent, _healthMax);
    }

    void RefreshOxygenLabel()
    {
        if (oxygenLabel == null)
            return;

        bool visible = showValueLabels && showOxygen;
        oxygenLabel.gameObject.SetActive(visible);
        if (!visible)
            return;

        oxygenLabel.text = FormatValue(_oxygenCurrent, _oxygenMax);
    }

    static string FormatValue(float current, float max)
    {
        int cur = Mathf.Max(0, Mathf.RoundToInt(current));
        int mx = Mathf.Max(0, Mathf.RoundToInt(max));
        return $"{cur}/{mx}";
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
