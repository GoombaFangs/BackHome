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

    static readonly int NoiseTexId = Shader.PropertyToID("_NoiseTex");
    static readonly int DangerId = Shader.PropertyToID("_Danger");
    static readonly int PulseId = Shader.PropertyToID("_Pulse");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int DistortId = Shader.PropertyToID("_Distort");
    static readonly int GlitchId = Shader.PropertyToID("_Glitch");
    static readonly int ScanlineId = Shader.PropertyToID("_Scanline");
    static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
    static readonly int ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");

    [Header("Threshold")]
    [SerializeField, Range(0.05f, 0.9f)] float oxygenThreshold = 0.2f;
    [Tooltip("Hide only after oxygen recovers this far above the threshold (avoids flicker).")]
    [SerializeField, Range(0.05f, 0.95f)] float recoverThreshold = 0.23f;

    [Header("Overlay")]
    [SerializeField] string warningText = "LOW OXYGEN WARNING";
    [SerializeField, Min(0.05f)] float fadeIn = 0.22f;
    [SerializeField, Min(0.05f)] float fadeOut = 0.35f;
    [SerializeField, Min(0.4f)] float pulseDuration = 2.4f;

    [Header("Camera")]
    [SerializeField, Min(0f)] float minSway = 0.028f;
    [SerializeField, Min(0f)] float maxSway = 0.075f;
    [SerializeField, Min(0f)] float minTunnelFov = 1.6f;
    [SerializeField, Min(0f)] float maxTunnelFov = 5.5f;

    [Header("Lighting")]
    [SerializeField, Range(0f, 1f)] float lightDim = 0.28f;
    [SerializeField, Range(0f, 1f)] float coolTint = 0.32f;

    [Header("White Screen")]
    [SerializeField, Range(0f, 1f)] float whiteAlphaMin = 0.05f;
    [SerializeField, Range(0f, 1f)] float whiteAlphaMax = 0.35f;
    [SerializeField, Min(0.05f)] float whiteFadeSpeed = 1.1f;

    [Header("HUD")]
    [SerializeField] Material textMaterial;
    [SerializeField] GameObject panel;
    [SerializeField] Text label;
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] Image whiteScreen;

    PlayerVitals _vitals;
    CameraFollow _follow;
    Material _runtime;
    Texture2D _noise;
    float _pulsePhase;
    float _visible;
    bool _wantVisible;
    bool _forcedHidden;
    float _noiseSeed;

    float _whiteAlpha;

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
            ApplyWhiteScreen(0f, instant: true);
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
        ApplyWhiteScreen(Mathf.Lerp(whiteAlphaMin, whiteAlphaMax, danger));
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
        _whiteAlpha = 0f;
        ClearCamera();
        RestoreLighting();
        ApplyWhiteScreen(0f, instant: true);
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
        _runtime.SetColor(ColorId, new Color(0.32f, 0.78f, 1f, 1f));
    }

    void ApplyWhiteScreen(float targetAlpha, bool instant = false)
    {
        EnsureWhiteScreen();
        if (whiteScreen == null)
            return;

        if (instant)
            _whiteAlpha = targetAlpha;
        else
            _whiteAlpha = Mathf.MoveTowards(_whiteAlpha, targetAlpha, Time.deltaTime * Mathf.Max(0.05f, whiteFadeSpeed));

        Color color = whiteScreen.color;
        color.a = _whiteAlpha;
        whiteScreen.color = color;
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
        float x = (Mathf.PerlinNoise(_noiseSeed, Time.time * 1.35f) - 0.5f) * 2f * magnitude;
        float y = (Mathf.PerlinNoise(Time.time * 1.1f, _noiseSeed + 17f) - 0.5f) * 1.6f * magnitude;
        y -= breath * magnitude * 0.35f;

        float hitch = Mathf.PerlinNoise(_noiseSeed + 40f, Time.time * 0.55f);
        if (hitch > 0.78f)
        {
            float spike = (hitch - 0.78f) / 0.22f;
            y -= spike * spike * magnitude * (0.8f + danger);
            x += (Mathf.PerlinNoise(Time.time * 22f, _noiseSeed) - 0.5f) * magnitude * spike;
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
        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
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

        EnsureWhiteScreen();
        EnsureMaterial();
        panel.SetActive(active);
        if (canvasGroup != null && !active)
            canvasGroup.alpha = 0f;
    }

    void EnsureWhiteScreen()
    {
        if (whiteScreen == null && panel != null)
        {
            Transform existing = panel.transform.Find("WhiteScreen");
            if (existing != null)
                whiteScreen = existing.GetComponent<Image>();
        }

        if (whiteScreen == null && panel != null)
            whiteScreen = CreateWhiteScreen(panel.transform);

        if (whiteScreen == null)
            return;

        whiteScreen.raycastTarget = false;
        whiteScreen.transform.SetAsFirstSibling();
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

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(root.transform, false);
        label = textGo.AddComponent<Text>();
        label.text = string.IsNullOrWhiteSpace(warningText) ? "LOW OXYGEN WARNING" : warningText;
        label.alignment = TextAnchor.MiddleCenter;
        label.fontSize = 44;
        label.fontStyle = FontStyle.Bold;
        label.color = new Color(0.32f, 0.78f, 1f, 1f);
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var textRect = label.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0.5f);
        textRect.anchorMax = new Vector2(1f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(0f, 120f);

        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.02f, 0.08f, 0.16f, 0.92f);
        outline.effectDistance = new Vector2(2.2f, -2.2f);
        outline.useGraphicAlpha = true;

        var shadow = textGo.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
        shadow.effectDistance = new Vector2(0f, -3f);
        shadow.useGraphicAlpha = true;

        CreateWhiteScreen(root.transform);
        root.transform.SetAsLastSibling();
        return root;
    }

    Image CreateWhiteScreen(Transform parent)
    {
        var go = new GameObject("WhiteScreen", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        go.transform.SetAsFirstSibling();

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);

        whiteScreen = go.GetComponent<Image>();
        whiteScreen.color = new Color(1f, 1f, 1f, whiteAlphaMin);
        whiteScreen.raycastTarget = false;
        return whiteScreen;
    }
}
