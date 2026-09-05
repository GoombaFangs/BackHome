using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Center-screen "LOW OXYGEN WARNING" while oxygen is at or below 20%.
/// Frosted UI shader on the text, plus hypoxic camera sway and a cooler, dimmer light.
/// Lives on the Hud and binds to the player at runtime.
/// </summary>
public class PlayerLowOxygenWarning : MonoBehaviour
{
    const string PanelName = "LowOxygenWarning";
    const string MaterialResourcesPath = "HUD/LowOxygenWarning";
    const string OverlayResourcesPath = "HUD/LowOxygenOverlay";

    static readonly int NoiseTexId = Shader.PropertyToID("_NoiseTex");
    static readonly int DangerId = Shader.PropertyToID("_Danger");
    static readonly int PulseId = Shader.PropertyToID("_Pulse");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int DistortId = Shader.PropertyToID("_Distort");
    static readonly int GlitchId = Shader.PropertyToID("_Glitch");
    static readonly int ScanlineId = Shader.PropertyToID("_Scanline");
    static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
    static readonly int ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");
    static readonly int FrostAmountId = Shader.PropertyToID("_FrostAmount");
    static readonly int VignetteAmountId = Shader.PropertyToID("_VignetteAmount");

    static Sprite _frameSprite;

    [Header("Threshold")]
    [SerializeField, Range(0.05f, 0.9f)] float oxygenThreshold = 0.2f;
    [Tooltip("Hide only after oxygen recovers this far above the threshold (avoids flicker).")]
    [SerializeField, Range(0.05f, 0.95f)] float recoverThreshold = 0.23f;

    [Header("Overlay")]
    [Tooltip("First letter of each word is rendered larger (small-caps style) automatically.")]
    [SerializeField] string warningText = "WARNING - OXYGEN LEVEL CRITICAL";
    [SerializeField, Min(0.05f)] float fadeIn = 0.22f;
    [SerializeField, Min(0.05f)] float fadeOut = 0.35f;
    [SerializeField, Min(0.4f)] float pulseDuration = 2.4f;

    [Header("Camera")]
    [SerializeField, Min(0f)] float minSway = 0.012f;
    [SerializeField, Min(0f)] float maxSway = 0.032f;
    [SerializeField, Min(0f)] float minTunnelFov = 1.6f;
    [SerializeField, Min(0f)] float maxTunnelFov = 5.5f;

    [Header("Lighting")]
    [SerializeField, Range(0f, 1f)] float lightDim = 0.28f;
    [SerializeField, Range(0f, 1f)] float coolTint = 0.32f;

    [Header("Frost Overlay")]
    [Tooltip("Blurs the real screen behind the HUD (camera going out of focus), instead of a flat white flash or a fake fog texture.")]
    [SerializeField, Range(0f, 1f)] float frostAmountMin = 0.28f;
    [SerializeField, Range(0f, 1f)] float frostAmountMax = 0.95f;
    [SerializeField, Min(0.05f)] float overlayFadeSpeed = 1.1f;
    [Tooltip("Overlay image alpha at zero danger (warning just started).")]
    [SerializeField, Range(0f, 1f)] float overlayColorAlphaMin = 0f;
    [Tooltip("Overlay image alpha at max danger (no oxygen left).")]
    [SerializeField, Range(0f, 1f)] float overlayColorAlphaMax = 0.32f;

    [Header("Vignette")]
    [Tooltip("Dark edge falloff that tightens as oxygen drops, for a tunnel-vision suffocation feel.")]
    [SerializeField, Range(0f, 1f)] float vignetteAmountMin = 0.22f;
    [SerializeField, Range(0f, 1f)] float vignetteAmountMax = 0.92f;

    [Header("HUD")]
    [SerializeField] Material textMaterial;
    [SerializeField] GameObject panel;
    [SerializeField] Text label;
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] Material overlayMaterial;
    [SerializeField] Image overlay;
    [SerializeField] Image frame;

    PlayerVitals _vitals;
    CameraFollow _follow;
    Material _runtime;
    Texture2D _noise;
    float _pulsePhase;
    float _visible;
    bool _wantVisible;
    bool _forcedHidden;
    float _noiseSeed;

    Material _overlayRuntime;
    float _frostAlpha;
    float _vignetteAlpha;
    float _overlayColorAlpha;

    bool _lightingCaptured;
    AmbientMode _capturedAmbientMode;
    Color _capturedAmbientSky;
    Color _capturedAmbientEquator;
    Color _capturedAmbientGround;
    Color _capturedAmbientFlat;
    float _capturedAmbientIntensity;
    Light _sun;
    float _capturedSunIntensity;
    Color _capturedSunColor;

    void Awake()
    {
        _noiseSeed = Random.value * 80f;
        EnsurePanel(active: false);
    }

    void Start()
    {
        EnsurePanel(active: false);
        TryBindPlayer();
    }

    void OnDisable()
    {
        UnbindPlayer();
        HideImmediate();
    }

    void OnDestroy()
    {
        UnbindPlayer();
        ClearCamera();
        RestoreLighting();
        if (_runtime != null)
        {
            Destroy(_runtime);
            _runtime = null;
        }

        if (_noise != null)
        {
            Destroy(_noise);
            _noise = null;
        }

        if (_overlayRuntime != null)
        {
            Destroy(_overlayRuntime);
            _overlayRuntime = null;
        }
    }

    void Update()
    {
        if (_vitals == null)
            TryBindPlayer();
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        float target = _wantVisible ? 1f : 0f;
        float fade = _wantVisible ? fadeIn : fadeOut;
        _visible = Mathf.MoveTowards(_visible, target, Time.deltaTime / Mathf.Max(0.05f, fade));

        if (_visible <= 0.001f && !_wantVisible)
        {
            ClearCamera();
            RestoreLighting();
            ResetOverlay();
            SetPanelActive(false);
            return;
        }

        float danger = DangerAmount();
        float period = Mathf.Lerp(pulseDuration, pulseDuration * 0.45f, danger);
        _pulsePhase += Time.deltaTime / Mathf.Max(0.35f, period);
        if (_pulsePhase >= 1f)
            _pulsePhase -= Mathf.Floor(_pulsePhase);

        float breath = 0.5f - 0.5f * Mathf.Cos(_pulsePhase * Mathf.PI * 2f);
        ApplyLabel(breath, danger);
        ApplyOverlay(breath, danger);
        ApplyCamera(breath, danger);
        ApplyLighting(_visible * Mathf.Lerp(0.4f, 1f, danger));
    }

    public void HideForCinematic()
    {
        _forcedHidden = true;
        HideImmediate();
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

        _vitals.VitalsChanged += OnVitalsChanged;
        _vitals.Died += HideImmediate;
        OnVitalsChanged();
    }

    void UnbindPlayer()
    {
        if (_vitals == null)
            return;

        _vitals.VitalsChanged -= OnVitalsChanged;
        _vitals.Died -= HideImmediate;
        _vitals = null;
    }

    void OnVitalsChanged()
    {
        if (_forcedHidden || _vitals == null || !_vitals.IsAlive || !_vitals.HasOxygen || _vitals.IsOnSpaceship)
        {
            _wantVisible = false;
            return;
        }

        float oxygen = _vitals.OxygenNormalized;
        if (_wantVisible)
            _wantVisible = oxygen < recoverThreshold;
        else
            _wantVisible = oxygen <= oxygenThreshold;

        if (_wantVisible)
        {
            EnsurePanel(active: true);
            EnsureMaterial();
            CaptureLightingIfNeeded();
            SetPanelActive(true);
            if (_visible <= 0.001f)
                _pulsePhase = 0f;
        }
    }

    void HideImmediate()
    {
        _wantVisible = false;
        _visible = 0f;
        _pulsePhase = 0f;
        ClearCamera();
        RestoreLighting();
        ResetOverlay();
        SetPanelActive(false);
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    float DangerAmount()
    {
        if (_vitals == null || oxygenThreshold <= 0.001f)
            return 1f;

        return 1f - Mathf.Clamp01(_vitals.OxygenNormalized / oxygenThreshold);
    }

    void ApplyLabel(float breath, float danger)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = _visible;

        if (label != null)
        {
            float scale = 1f + breath * Mathf.Lerp(0.02f, 0.05f, danger);
            label.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        if (_runtime == null && !EnsureMaterial())
            return;

        BindRuntimeMaterial();
        _runtime.SetFloat(DangerId, danger * _visible);
        _runtime.SetFloat(PulseId, breath);
        _runtime.SetFloat(DistortId, 0f);
        _runtime.SetFloat(GlitchId, 0f);
        _runtime.SetFloat(ScanlineId, Mathf.Lerp(0.08f, 0.18f, danger));
        _runtime.SetFloat(NoiseStrengthId, Mathf.Lerp(0.28f, 0.48f, danger));
        _runtime.SetFloat(ScrollSpeedId, Mathf.Lerp(0.06f, 0.12f, danger));
        _runtime.SetColor(ColorId, new Color(0.9f, 0.95f, 1f, 1f));
    }

    void ApplyOverlay(float breath, float danger)
    {
        EnsureOverlay();
        if (overlay == null || (_overlayRuntime == null && !EnsureOverlayMaterial()))
            return;

        BindOverlayMaterial();

        // Note: overall panel fade (fade in/out) is handled by canvasGroup.alpha, so danger alone
        // drives these targets - multiplying by _visible again would double-dampen the effect.
        float frostTarget = Mathf.Lerp(frostAmountMin, frostAmountMax, danger);
        float vignetteTarget = Mathf.Lerp(vignetteAmountMin, vignetteAmountMax, danger);
        float colorAlphaTarget = Mathf.Lerp(overlayColorAlphaMin, overlayColorAlphaMax, danger);
        float rate = Time.deltaTime * Mathf.Max(0.05f, overlayFadeSpeed);
        _frostAlpha = Mathf.MoveTowards(_frostAlpha, frostTarget, rate);
        _vignetteAlpha = Mathf.MoveTowards(_vignetteAlpha, vignetteTarget, rate);
        _overlayColorAlpha = Mathf.MoveTowards(_overlayColorAlpha, colorAlphaTarget, rate);

        _overlayRuntime.SetFloat(FrostAmountId, _frostAlpha);
        _overlayRuntime.SetFloat(VignetteAmountId, _vignetteAlpha);
        _overlayRuntime.SetFloat(PulseId, breath);
        overlay.color = new Color(1f, 1f, 1f, _overlayColorAlpha);
    }

    void ResetOverlay()
    {
        _frostAlpha = 0f;
        _vignetteAlpha = 0f;
        _overlayColorAlpha = 0f;
        if (overlay != null)
            overlay.color = new Color(1f, 1f, 1f, 0f);

        if (_overlayRuntime == null)
            return;

        _overlayRuntime.SetFloat(FrostAmountId, 0f);
        _overlayRuntime.SetFloat(VignetteAmountId, 0f);
        _overlayRuntime.SetFloat(PulseId, 0f);
    }

    void ApplyCamera(float breath, float danger)
    {
        if (_follow == null)
            _follow = FindAnyObjectByType<CameraFollow>();
        if (_follow == null && Camera.main != null)
            _follow = Camera.main.GetComponent<CameraFollow>();
        if (_follow == null)
            return;

        float magnitude = Mathf.Lerp(minSway, maxSway, danger) * _visible;
        float x = (Mathf.PerlinNoise(_noiseSeed, Time.time * 0.9f) - 0.5f) * 2f * magnitude;
        float y = (Mathf.PerlinNoise(Time.time * 0.75f, _noiseSeed + 17f) - 0.5f) * magnitude;
        y -= breath * magnitude * 0.2f;

        // Rare, gentle "swoon" hitch rather than a frequent jarring spike.
        float hitch = Mathf.PerlinNoise(_noiseSeed + 40f, Time.time * 0.35f);
        if (hitch > 0.88f)
        {
            float spike = (hitch - 0.88f) / 0.12f;
            y -= spike * spike * magnitude * (0.3f + danger * 0.3f);
            x += (Mathf.PerlinNoise(Time.time * 12f, _noiseSeed) - 0.5f) * magnitude * spike * 0.5f;
        }

        _follow.SetOxygenOffset(new Vector3(x, y, 0f));
        _follow.SetOxygenFovDelta(-Mathf.Lerp(minTunnelFov, maxTunnelFov, danger) * _visible);
    }

    void ClearCamera()
    {
        if (_follow == null)
            return;

        _follow.SetOxygenOffset(Vector3.zero);
        _follow.SetOxygenFovDelta(0f);
    }

    void CaptureLightingIfNeeded()
    {
        if (_lightingCaptured)
            return;

        _capturedAmbientMode = RenderSettings.ambientMode;
        _capturedAmbientSky = RenderSettings.ambientSkyColor;
        _capturedAmbientEquator = RenderSettings.ambientEquatorColor;
        _capturedAmbientGround = RenderSettings.ambientGroundColor;
        _capturedAmbientFlat = RenderSettings.ambientLight;
        _capturedAmbientIntensity = RenderSettings.ambientIntensity;
        _sun = ResolveSun();
        if (_sun != null)
        {
            _capturedSunIntensity = _sun.intensity;
            _capturedSunColor = _sun.color;
        }

        _lightingCaptured = true;
    }

    void ApplyLighting(float amount)
    {
        if (!_lightingCaptured)
            return;

        amount = Mathf.Clamp01(amount);
        Color cool = new Color(0.58f, 0.76f, 1f);
        float dim = 1f - lightDim * amount;

        if (_capturedAmbientMode == AmbientMode.Trilight)
        {
            RenderSettings.ambientSkyColor = TintToward(_capturedAmbientSky, cool, coolTint * amount) * dim;
            RenderSettings.ambientEquatorColor = TintToward(_capturedAmbientEquator, cool, coolTint * amount) * dim;
            RenderSettings.ambientGroundColor = _capturedAmbientGround * (1f - lightDim * amount * 1.15f);
        }
        else
        {
            RenderSettings.ambientLight = TintToward(_capturedAmbientFlat, cool, coolTint * amount) * dim;
        }

        RenderSettings.ambientIntensity = Mathf.Lerp(_capturedAmbientIntensity, _capturedAmbientIntensity * (1f - lightDim), amount);

        if (_sun != null)
        {
            _sun.intensity = Mathf.Lerp(_capturedSunIntensity, _capturedSunIntensity * (1f - lightDim * 0.75f), amount);
            _sun.color = TintToward(_capturedSunColor, cool, coolTint * amount * 0.7f);
        }
    }

    void RestoreLighting()
    {
        if (!_lightingCaptured)
            return;

        RenderSettings.ambientSkyColor = _capturedAmbientSky;
        RenderSettings.ambientEquatorColor = _capturedAmbientEquator;
        RenderSettings.ambientGroundColor = _capturedAmbientGround;
        RenderSettings.ambientLight = _capturedAmbientFlat;
        RenderSettings.ambientIntensity = _capturedAmbientIntensity;
        if (_sun != null)
        {
            _sun.intensity = _capturedSunIntensity;
            _sun.color = _capturedSunColor;
        }

        _sun = null;
        _lightingCaptured = false;
    }

    static Light ResolveSun()
    {
        if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled)
            return RenderSettings.sun;

        Light best = null;
        float bestIntensity = -1f;
        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light == null || light.type != LightType.Directional || !light.enabled)
                continue;
            if (light.intensity > bestIntensity)
            {
                best = light;
                bestIntensity = light.intensity;
            }
        }

        return best;
    }

    static Color TintToward(Color source, Color tint, float amount)
    {
        Color cooled = new Color(source.r * tint.r, source.g * tint.g, source.b * tint.b, source.a);
        return Color.Lerp(source, cooled, Mathf.Clamp01(amount));
    }

    void SetPanelActive(bool active)
    {
        if (panel != null && panel.activeSelf != active)
            panel.SetActive(active);
    }

    void EnsurePanel(bool active)
    {
        if (panel == null)
        {
            Transform existing = FindExistingPanel();
            if (existing != null)
                panel = existing.gameObject;
        }

        if (panel == null)
            panel = CreatePanel();

        if (panel == null)
            return;

        if (canvasGroup == null)
            canvasGroup = panel.GetComponent<CanvasGroup>();
        if (label == null)
        {
            Transform labelTransform = panel.transform.Find("Label");
            if (labelTransform != null)
                label = labelTransform.GetComponent<Text>();
        }

        EnsureOverlay();
        EnsureFrame();
        EnsureMaterial();
        EnsureLabelText();
        panel.SetActive(active);
        if (canvasGroup != null && !active)
            canvasGroup.alpha = 0f;
    }

    void EnsureOverlay()
    {
        if (overlay == null && panel != null)
        {
            Transform existing = panel.transform.Find("FrostOverlay");
            if (existing != null)
                overlay = existing.GetComponent<Image>();
        }

        if (overlay == null && panel != null)
            overlay = CreateOverlay(panel.transform);

        if (overlay == null)
            return;

        overlay.raycastTarget = false;
        overlay.transform.SetAsFirstSibling();
        EnsureOverlayMaterial();
        BindOverlayMaterial();
    }

    void EnsureFrame()
    {
        if (frame == null && panel != null)
        {
            Transform existing = panel.transform.Find("Frame");
            if (existing != null)
                frame = existing.GetComponent<Image>();
        }

        if (frame == null && panel != null)
            frame = CreateFrame(panel.transform);

        if (frame == null)
            return;

        frame.raycastTarget = false;
        Sprite sprite = EnsureFrameSprite();
        if (frame.sprite != sprite)
            frame.sprite = sprite;
        frame.type = Image.Type.Sliced;
        frame.color = Color.white;
    }

    void EnsureLabelText()
    {
        if (label == null)
            return;

        label.supportRichText = true;
        string source = string.IsNullOrWhiteSpace(warningText) ? "WARNING - OXYGEN LEVEL CRITICAL" : warningText;
        string styled = BuildSmallCapsRichText(source, label.fontSize > 0 ? label.fontSize : 36);
        if (label.text != styled)
            label.text = styled;
    }

    /// <summary>
    /// Wraps the first letter of each word in a larger &lt;size&gt; tag, e.g. "WARNING" -&gt;
    /// big-W + small-ARNING, matching a small-caps military-warning look.
    /// </summary>
    public static string BuildSmallCapsRichText(string text, int baseFontSize, float leadScale = 1.35f)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        int leadSize = Mathf.Max(baseFontSize + 1, Mathf.RoundToInt(baseFontSize * leadScale));
        var sb = new StringBuilder(text.Length + 32);
        bool atWordStart = true;
        foreach (char c in text)
        {
            if (char.IsLetter(c) && atWordStart)
            {
                sb.Append("<size=").Append(leadSize).Append('>').Append(c).Append("</size>");
                atWordStart = false;
            }
            else
            {
                sb.Append(c);
                atWordStart = char.IsWhiteSpace(c) || c == '-';
            }
        }

        return sb.ToString();
    }

    bool EnsureMaterial()
    {
        if (_runtime != null)
        {
            BindRuntimeMaterial();
            return true;
        }

        Material source = textMaterial;
        if (source == null)
            source = Resources.Load<Material>(MaterialResourcesPath);
        if (source == null)
        {
            Shader shader = Shader.Find("BackHome/Hud/LowOxygenWarning");
            if (shader != null)
                source = new Material(shader);
        }

        if (source == null)
            return false;

        textMaterial = source;
        _runtime = new Material(source);
        _runtime.name = source.name + " (Runtime)";
        if (_noise == null)
            _noise = CreateFrostNoise(128);
        _runtime.SetTexture(NoiseTexId, _noise);
        BindRuntimeMaterial();
        return true;
    }

    void BindRuntimeMaterial()
    {
        if (label == null || _runtime == null)
            return;
        if (label.material != _runtime)
            label.material = _runtime;
    }

    bool EnsureOverlayMaterial()
    {
        if (_overlayRuntime != null)
        {
            BindOverlayMaterial();
            return true;
        }

        Material source = overlayMaterial;
        if (source == null)
            source = Resources.Load<Material>(OverlayResourcesPath);
        if (source == null)
        {
            Shader shader = Shader.Find("BackHome/Hud/LowOxygenOverlay");
            if (shader != null)
                source = new Material(shader);
        }

        if (source == null)
            return false;

        overlayMaterial = source;
        _overlayRuntime = new Material(source);
        _overlayRuntime.name = source.name + " (Runtime)";
        BindOverlayMaterial();
        return true;
    }

    void BindOverlayMaterial()
    {
        if (overlay == null || _overlayRuntime == null)
            return;
        if (overlay.material != _overlayRuntime)
            overlay.material = _overlayRuntime;
    }

    static Texture2D CreateFrostNoise(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGB24, false)
        {
            name = "LowOxygenFrostNoise",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.065f, y * 0.065f);
                float n2 = Mathf.PerlinNoise(x * 0.17f + 19f, y * 0.17f + 7f);
                float n3 = Mathf.PerlinNoise(x * 0.39f + 51f, y * 0.39f + 23f);
                pixels[y * size + x] = new Color(n, n2, n3, 1f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }

    Transform FindExistingPanel()
    {
        Transform direct = transform.Find(PanelName);
        if (direct != null)
            return direct;

        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null)
            return canvas.transform.Find(PanelName);

        return null;
    }

    GameObject CreatePanel()
    {
        Canvas canvas = GetComponentInChildren<Canvas>(true);
        Transform parent = canvas != null ? canvas.transform : transform;

        var root = new GameObject(PanelName);
        root.transform.SetParent(parent, false);

        var rect = root.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        canvasGroup = root.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        canvasGroup.alpha = 0f;

        CreateFrame(root.transform);

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(root.transform, false);
        label = textGo.AddComponent<Text>();
        label.alignment = TextAnchor.MiddleCenter;
        label.fontSize = 36;
        label.fontStyle = FontStyle.Bold;
        label.color = new Color(0.92f, 0.95f, 1f, 1f);
        label.raycastTarget = false;
        label.supportRichText = true;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.text = BuildSmallCapsRichText(
            string.IsNullOrWhiteSpace(warningText) ? "WARNING - OXYGEN LEVEL CRITICAL" : warningText,
            label.fontSize);

        var textRect = label.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0.5f);
        textRect.anchorMax = new Vector2(1f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(0f, 90f);

        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.02f, 0.08f, 0.16f, 0.92f);
        outline.effectDistance = new Vector2(2.2f, -2.2f);
        outline.useGraphicAlpha = true;

        var shadow = textGo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
        shadow.effectDistance = new Vector2(0f, -3f);
        shadow.useGraphicAlpha = true;

        CreateOverlay(root.transform);
        root.transform.SetAsLastSibling();
        return root;
    }

    Image CreateOverlay(Transform parent)
    {
        var go = new GameObject("FrostOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.transform.SetAsFirstSibling();

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);

        overlay = go.GetComponent<Image>();
        overlay.color = new Color(1f, 1f, 1f, 0f);
        overlay.raycastTarget = false;
        return overlay;
    }

    Image CreateFrame(Transform parent)
    {
        var go = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(860f, 108f);

        frame = go.GetComponent<Image>();
        frame.raycastTarget = false;
        return frame;
    }

    /// <summary>
    /// Thin light border + dark translucent fill, rounded corners - a plain sci-fi warning
    /// banner look, generated once and 9-sliced so it scales cleanly to any panel size.
    /// </summary>
    public static Sprite EnsureFrameSprite()
    {
        if (_frameSprite != null)
            return _frameSprite;

        const int size = 64;
        const float radius = 16f;
        const float thickness = 3f;

        var borderColor = new Color(0.85f, 0.92f, 1f, 0.9f);
        var fillColor = new Color(0.04f, 0.06f, 0.09f, 0.5f);
        var half = new Vector2(size * 0.5f, size * 0.5f);

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "LowOxygenFrameTex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f) - half;
                float outerDist = RoundedBoxSdf(p, half, radius);
                float innerDist = RoundedBoxSdf(p, half - new Vector2(thickness, thickness), Mathf.Max(0f, radius - thickness));

                float outerCoverage = Mathf.Clamp01(0.5f - outerDist);
                float innerCoverage = Mathf.Clamp01(0.5f - innerDist);
                float ringCoverage = Mathf.Clamp01(outerCoverage - innerCoverage);

                float aFill = fillColor.a * innerCoverage;
                float aBorder = borderColor.a * ringCoverage;
                float outAlpha = aBorder + aFill * (1f - aBorder);

                Color outColor;
                if (outAlpha > 0.0001f)
                {
                    float r = (borderColor.r * aBorder + fillColor.r * aFill * (1f - aBorder)) / outAlpha;
                    float g = (borderColor.g * aBorder + fillColor.g * aFill * (1f - aBorder)) / outAlpha;
                    float b = (borderColor.b * aBorder + fillColor.b * aFill * (1f - aBorder)) / outAlpha;
                    outColor = new Color(r, g, b, outAlpha);
                }
                else
                {
                    outColor = new Color(0f, 0f, 0f, 0f);
                }

                pixels[y * size + x] = outColor;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);

        float border = radius + thickness + 2f;
        _frameSprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        _frameSprite.name = "LowOxygenFrame";
        return _frameSprite;
    }

    static float RoundedBoxSdf(Vector2 p, Vector2 halfSize, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x) - halfSize.x + radius, Mathf.Abs(p.y) - halfSize.y + radius);
        float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
        return Mathf.Min(Mathf.Max(q.x, q.y), 0f) + outside - radius;
    }
}
