using TMPro;
using UnityEngine;

/// <summary>
/// Billboard HP / oxygen art. Frame, fills, and labels are authored on the prefab;
/// runtime only changes fill width and label text.
/// </summary>
public class VitalsBarsView : MonoBehaviour
{
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    const int LabelSortingOrder = 50;

    struct AuthoredFill
    {
        public bool valid;
        public Vector3 localPosition;
        public Vector3 localScale;
        public Vector2 spriteSize;
        public bool sliced;
    }

    [Header("Bars")]
    [SerializeField] bool showOxygen = true;

    [Header("Value Labels")]
    [SerializeField] bool showValueLabels = true;
    [SerializeField] Color labelColor = new Color(1f, 1f, 1f, 0.95f);

    [Header("Colors")]
    [SerializeField] Color healthFillColor = new Color(0.25f, 0.85f, 0.35f, 1f);
    [SerializeField] Color healthLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    [SerializeField] Color healthHitFlashColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] Color oxygenFillColor = new Color(0.25f, 0.65f, 1f, 1f);
    [SerializeField, Min(0.01f)] float hitFlashDuration = 0.12f;

    [Header("References")]
    [SerializeField] Renderer frameRenderer;
    [SerializeField] Transform healthFill;
    [SerializeField] Transform oxygenFill;
    [SerializeField] Renderer healthFillRenderer;
    [SerializeField] Renderer oxygenFillRenderer;
    [SerializeField] Material barMaterial;
    [SerializeField] Material fillMaterial;
    [SerializeField] TMP_Text healthLabel;
    [SerializeField] TMP_Text oxygenLabel;

    MaterialPropertyBlock _block;
    AuthoredFill _healthAuthored;
    AuthoredFill _oxygenAuthored;
    float _previewHealth = 1f;
    float _previewOxygen = 1f;
    float _healthCurrent = 1f;
    float _healthMax = 1f;
    float _oxygenCurrent = 1f;
    float _oxygenMax = 1f;
    float _hitFlashTimer;
    float _alpha = 1f;

    void Awake()
    {
        CaptureAuthoredLayout();
    }

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
        CaptureAuthoredLayout();
        EnsureValueLabels();
        EnsureMaterials();
        ApplyOxygenVisibility();
        RefreshValueLabels();
    }

    public void SetOxygenVisible(bool visible)
    {
        showOxygen = visible;
        ApplyOxygenVisibility();
        RefreshValueLabels();
    }

    public void SetHealth(float normalized)
    {
        normalized = Mathf.Clamp01(normalized);
        _previewHealth = normalized;
        ApplyFill(ref _healthAuthored, healthFill, healthFillRenderer, normalized);

        if (_hitFlashTimer <= 0f)
            ApplyHealthFillColor(normalized);
    }

    public void SetAlpha(float alpha)
    {
        _alpha = Mathf.Clamp01(alpha);
        SetRendererColor(frameRenderer, Color.white);
        if (_hitFlashTimer > 0f)
            SetRendererColor(healthFillRenderer, healthHitFlashColor);
        else
            ApplyHealthFillColor(_previewHealth);
        SetRendererColor(oxygenFillRenderer, oxygenFillColor);
        ApplyLabelAlpha();
    }

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

        ApplyFill(ref _oxygenAuthored, oxygenFill, oxygenFillRenderer, normalized);
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
        ApplyOxygenVisibility();
        EnsureMaterials();
    }

    void ApplyOxygenVisibility()
    {
        if (oxygenFill != null)
            oxygenFill.gameObject.SetActive(showOxygen);
        if (oxygenLabel != null)
            oxygenLabel.gameObject.SetActive(showValueLabels && showOxygen);
    }

    void CaptureAuthoredLayout()
    {
        CaptureFill(ref _healthAuthored, healthFill, healthFillRenderer);
        CaptureFill(ref _oxygenAuthored, oxygenFill, oxygenFillRenderer);
    }

    static void CaptureFill(ref AuthoredFill authored, Transform fill, Renderer renderer)
    {
        if (authored.valid || fill == null)
            return;

        authored.localPosition = fill.localPosition;
        authored.localScale = fill.localScale;
        authored.spriteSize = Vector2.one;
        authored.sliced = false;

        if (renderer is SpriteRenderer spriteRenderer)
        {
            authored.sliced = spriteRenderer.drawMode == SpriteDrawMode.Sliced
                || spriteRenderer.drawMode == SpriteDrawMode.Tiled;
            if (authored.sliced)
                authored.spriteSize = spriteRenderer.size;
            else if (spriteRenderer.sprite != null)
                authored.spriteSize = spriteRenderer.sprite.bounds.size;
        }

        authored.spriteSize.x = Mathf.Max(0.0001f, authored.spriteSize.x);
        authored.spriteSize.y = Mathf.Max(0.0001f, authored.spriteSize.y);
        authored.valid = true;
    }

    void EnsureValueLabels()
    {
        if (healthLabel == null)
            healthLabel = FindLabel("HealthValue");
        if (oxygenLabel == null)
            oxygenLabel = FindLabel("OxygenValue");

        EnsureWorldSpaceLabel(healthLabel);
        EnsureWorldSpaceLabel(oxygenLabel);
    }

    static void EnsureWorldSpaceLabel(TMP_Text label)
    {
        if (label == null)
            return;

        Canvas canvas = label.GetComponent<Canvas>();
        if (canvas == null)
            canvas = label.gameObject.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = LabelSortingOrder;
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal
            | AdditionalCanvasShaderChannels.Tangent;
    }

    TMP_Text FindLabel(string objectName)
    {
        Transform[] children = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == objectName)
                return children[i].GetComponent<TMP_Text>();
        }

        return null;
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
        ApplyLabelColor(healthLabel);
        healthLabel.ForceMeshUpdate();
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
        ApplyLabelColor(oxygenLabel);
        oxygenLabel.ForceMeshUpdate();
    }

    static string FormatValue(float current, float max)
    {
        int cur = Mathf.Max(0, Mathf.RoundToInt(current));
        int mx = Mathf.Max(0, Mathf.RoundToInt(max));
        return $"{cur}/{mx}";
    }

    void ApplyFill(ref AuthoredFill authored, Transform fill, Renderer renderer, float normalized)
    {
        if (fill == null)
            return;

        CaptureFill(ref authored, fill, renderer);
        if (!authored.valid)
            return;

        normalized = Mathf.Clamp01(normalized);
        Vector3 position = authored.localPosition;
        Vector3 scale = authored.localScale;
        float fullWidth = authored.spriteSize.x * Mathf.Abs(scale.x);
        float width = Mathf.Max(0.0001f, fullWidth * normalized);

        fill.localPosition = new Vector3(
            position.x - fullWidth * 0.5f + width * 0.5f,
            position.y,
            position.z);

        if (renderer is SpriteRenderer spriteRenderer && authored.sliced)
        {
            fill.localScale = scale;
            spriteRenderer.size = new Vector2(
                Mathf.Max(0.0001f, authored.spriteSize.x * normalized),
                authored.spriteSize.y);
            return;
        }

        fill.localScale = new Vector3(
            scale.x * Mathf.Max(0.0001f, normalized),
            scale.y,
            scale.z);
    }

    void EnsureMaterials()
    {
        AssignSharedMaterial(frameRenderer, barMaterial);
        Material fill = fillMaterial != null ? fillMaterial : barMaterial;
        AssignSharedMaterial(healthFillRenderer, fill);
        AssignSharedMaterial(oxygenFillRenderer, fill);
    }

    void AssignSharedMaterial(Renderer renderer, Material material)
    {
        if (renderer == null || material == null)
            return;
        if (renderer.sharedMaterial != material)
            renderer.sharedMaterial = material;
        if (renderer is SpriteRenderer spriteRenderer)
            BindSpriteTexture(spriteRenderer);
    }

    Material FillMaterial()
    {
        return fillMaterial != null ? fillMaterial : barMaterial;
    }

    void BindSpriteTexture(SpriteRenderer spriteRenderer)
    {
        if (spriteRenderer.sprite == null)
            return;

        if (_block == null)
            _block = new MaterialPropertyBlock();

        spriteRenderer.GetPropertyBlock(_block);
        _block.SetTexture(MainTexId, spriteRenderer.sprite.texture);
        spriteRenderer.SetPropertyBlock(_block);
    }

    void SetRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null)
            return;

        AssignSharedMaterial(renderer, renderer == frameRenderer ? barMaterial : FillMaterial());
        color.a *= _alpha;

        if (renderer is SpriteRenderer spriteRenderer)
        {
            spriteRenderer.color = color;
            BindSpriteTexture(spriteRenderer);
            return;
        }

        if (_block == null)
            _block = new MaterialPropertyBlock();

        renderer.GetPropertyBlock(_block);
        _block.SetColor(ColorId, color);
        renderer.SetPropertyBlock(_block);
    }

    void ApplyLabelAlpha()
    {
        ApplyLabelColor(healthLabel);
        ApplyLabelColor(oxygenLabel);
    }

    void ApplyLabelColor(TMP_Text label)
    {
        if (label == null)
            return;

        Color color = labelColor;
        color.a *= _alpha;
        label.color = color;
    }
}
