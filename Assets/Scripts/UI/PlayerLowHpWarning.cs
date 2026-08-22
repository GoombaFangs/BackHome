using UnityEngine;

/// <summary>
/// HUD low-HP feedback: heartbeat camera shake + warning panel
/// while player health is below the threshold.
/// Lives on the Hud (like <see cref="PlayerDeathUI"/>) and binds to the player at runtime.
/// </summary>
public class PlayerLowHpWarning : MonoBehaviour
{
    const string OverlayResourcesPath = "Player/Combat/Shaders/PlayerDamageOverlay";
    const string PanelName = "LowHpWarning";

    static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int VignetteId = Shader.PropertyToID("_Vignette");
    static readonly int VignetteSoftnessId = Shader.PropertyToID("_VignetteSoftness");
    static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
    static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");

    [Header("Threshold")]
    [SerializeField, Range(0.05f, 0.9f)] float healthThreshold = 0.3f;
    [Tooltip("Hide only after HP recovers this far above the threshold (avoids flicker).")]
    [SerializeField, Range(0.05f, 0.95f)] float recoverThreshold = 0.33f;

    [Header("Heartbeat")]
    [SerializeField, Min(30f)] float minBpm = 72f;
    [SerializeField, Min(30f)] float maxBpm = 148f;
    [SerializeField, Min(0f)] float minShake = 0.04f;
    [SerializeField, Min(0f)] float maxShake = 0.09f;

    [Header("Overlay")]
    [SerializeField] Material overlayMaterial;
    [SerializeField, Min(0.05f)] float fadeIn = 0.22f;
    [SerializeField, Min(0.05f)] float fadeOut = 0.35f;
    [SerializeField, Range(0.05f, 1f)] float vignetteBase = 0.52f;
    [SerializeField, Range(0f, 1f)] float vignettePulse = 0.28f;
    [Tooltip("Seconds for one slow in-and-out of the side blood.")]
    [SerializeField, Min(1f)] float vignetteBreathDuration = 7.5f;

    [Header("HUD")]
    [SerializeField] GameObject panel;

    PlayerVitals _vitals;
    CameraFollow _follow;
    Camera _camera;
    Material _runtime;
    Transform _overlay;
    MeshRenderer _overlayRenderer;
    float _phase;
    float _vignettePhase;
    float _visible;
    bool _wantVisible;
    bool _forcedHidden;
    float _lastFov = -1f;
    float _lastAspect = -1f;
    float _noiseSeed;

    void Awake()
    {
        _noiseSeed = Random.value * 80f;
        EnsureMaterial();
        HideTextPanel();
    }

    void Start()
    {
        HideTextPanel();
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
        ClearHeartbeat();
        if (_overlay != null)
        {
            Destroy(_overlay.gameObject);
            _overlay = null;
            _overlayRenderer = null;
        }

        if (_runtime != null)
        {
            Destroy(_runtime);
            _runtime = null;
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
            ClearHeartbeat();
            SetVisualsActive(false);
            return;
        }

        float danger = DangerAmount();
        float bpm = Mathf.Lerp(minBpm, maxBpm, danger);
        float period = 60f / Mathf.Max(30f, bpm);
        _phase += Time.deltaTime / period;
        if (_phase >= 1f)
            _phase -= Mathf.Floor(_phase);

        float envelope = HeartbeatEnvelope(_phase);
        float breath = SlowBreath(_vignettePhase);
        _vignettePhase += Time.deltaTime / Mathf.Max(1f, vignetteBreathDuration);
        if (_vignettePhase >= 1f)
            _vignettePhase -= Mathf.Floor(_vignettePhase);

        ApplyCameraShake(envelope, danger);
        ApplyOverlay(breath);
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

    public void HideForCinematic()
    {
        _forcedHidden = true;
        HideImmediate();
    }

    void OnVitalsChanged()
    {
        if (_forcedHidden || _vitals == null || !_vitals.IsAlive)
        {
            _wantVisible = false;
            return;
        }

        float hp = _vitals.HealthNormalized;
        if (_wantVisible)
            _wantVisible = hp < recoverThreshold;
        else
            _wantVisible = hp < healthThreshold;

        if (_wantVisible)
        {
            EnsureOverlay();
            SetVisualsActive(true);
            if (_visible <= 0.001f)
            {
                _phase = 0f;
                _vignettePhase = 0f;
            }
        }
    }

    void HideImmediate()
    {
        _wantVisible = false;
        _visible = 0f;
        _phase = 0f;
        _vignettePhase = 0f;
        ClearHeartbeat();
        SetVisualsActive(false);
        ApplyDissolve(0f);
    }

    float DangerAmount()
    {
        if (_vitals == null || healthThreshold <= 0.001f)
            return 1f;

        return 1f - Mathf.Clamp01(_vitals.HealthNormalized / healthThreshold);
    }

    void ApplyCameraShake(float envelope, float danger)
    {
        if (_follow == null)
            _follow = FindAnyObjectByType<CameraFollow>();
        if (_follow == null && Camera.main != null)
            _follow = Camera.main.GetComponent<CameraFollow>();
        if (_follow == null)
            return;

        float magnitude = Mathf.Lerp(minShake, maxShake, danger) * _visible;
        float y = envelope * magnitude;
        float x = (Mathf.PerlinNoise(_noiseSeed, Time.time * 14f) - 0.5f) * 2f * envelope * magnitude * 0.18f;
        _follow.SetHeartbeatOffset(new Vector3(x, y, 0f));
    }

    void ClearHeartbeat()
    {
        if (_follow != null)
            _follow.SetHeartbeatOffset(Vector3.zero);
    }

    void ApplyOverlay(float envelope)
    {
        if (_runtime == null && !EnsureMaterial())
            return;

        EnsureOverlay();
        FitToCamera();
        float dissolve = (vignetteBase + envelope * vignettePulse) * _visible;
        ApplyDissolve(dissolve);
        if (_runtime != null)
            _runtime.SetFloat(OpacityId, (0.68f + envelope * 0.22f) * _visible);
    }

    void ApplyDissolve(float dissolve)
    {
        if (_runtime != null)
            _runtime.SetFloat(DissolveId, dissolve);
    }

    void SetVisualsActive(bool active)
    {
        if (_overlayRenderer != null)
            _overlayRenderer.enabled = active;
        if (_overlay != null && _overlay.gameObject != null)
            _overlay.gameObject.SetActive(active);
        if (panel != null && panel.activeSelf)
            panel.SetActive(false);
    }

    bool EnsureMaterial()
    {
        if (_runtime != null)
            return true;

        Material source = overlayMaterial;
        if (source == null)
            source = Resources.Load<Material>(OverlayResourcesPath);
        if (source == null)
            source = Resources.Load<Material>("Player/Combat/PlayerDamageOverlay");
        if (source == null)
        {
            Debug.LogWarning($"{name}: Low HP overlay material missing.", this);
            return false;
        }

        _runtime = new Material(source);
        _runtime.name = source.name + " (LowHp Runtime)";
        overlayMaterial = source;
        _runtime.SetFloat(OpacityId, 0.72f);
        _runtime.SetFloat(VignetteId, 1.35f);
        _runtime.SetFloat(VignetteSoftnessId, 0.28f);
        _runtime.SetFloat(EdgeWidthId, 0.12f);
        _runtime.SetFloat(NoiseScaleId, 7f);
        _runtime.SetColor(ColorId, new Color(0.78f, 0.04f, 0.06f, 0.95f));
        _runtime.SetColor(EdgeColorId, new Color(1.65f, 0.2f, 0.07f, 1f));
        return true;
    }

    void EnsureOverlay()
    {
        Camera cam = ResolveCamera();
        if (cam == null || _runtime == null)
            return;

        if (_overlay == null)
        {
            var go = new GameObject("LowHpWarningOverlay");
            _overlay = go.transform;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = BuildQuad();
            _overlayRenderer = go.AddComponent<MeshRenderer>();
            _overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _overlayRenderer.receiveShadows = false;
            _overlayRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _overlayRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _overlayRenderer.allowOcclusionWhenDynamic = false;
            _overlayRenderer.sharedMaterial = _runtime;
        }

        if (_overlay.parent != cam.transform)
            _overlay.SetParent(cam.transform, false);

        _overlay.localRotation = Quaternion.identity;
        FitToCamera();
    }

    void FitToCamera(bool force = false)
    {
        Camera cam = ResolveCamera();
        if (cam == null || _overlay == null)
            return;

        float fov = cam.fieldOfView;
        float aspect = cam.aspect;
        if (!force && Mathf.Abs(fov - _lastFov) < 0.01f && Mathf.Abs(aspect - _lastAspect) < 0.001f)
            return;

        _lastFov = fov;
        _lastAspect = aspect;

        float z = cam.nearClipPlane + 0.015f;
        float height = 2f * z * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        _overlay.localPosition = new Vector3(0f, 0f, z);
        _overlay.localScale = new Vector3(height * aspect, height, 1f);
    }

    void HideTextPanel()
    {
        if (panel == null)
        {
            Transform existing = transform.Find(PanelName);
            if (existing == null)
            {
                Canvas canvas = GetComponentInChildren<Canvas>(true);
                if (canvas != null)
                    existing = canvas.transform.Find(PanelName);
            }

            if (existing != null)
                panel = existing.gameObject;
        }

        if (panel != null && panel.activeSelf)
            panel.SetActive(false);
    }

    Camera ResolveCamera()
    {
        if (_camera != null)
            return _camera;

        _camera = Camera.main;
        if (_camera == null)
            _camera = FindAnyObjectByType<Camera>();
        return _camera;
    }

    static float SlowBreath(float phase)
    {
        return 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
    }

    static float HeartbeatEnvelope(float phase)
    {
        float lub = Pulse(phase, 0f, 0.10f);
        float dub = Pulse(phase, 0.16f, 0.09f) * 0.55f;
        return Mathf.Max(lub, dub);
    }

    static float Pulse(float t, float start, float width)
    {
        if (width <= 0.0001f)
            return 0f;

        float x = (t - start) / width;
        if (x < 0f || x > 1f)
            return 0f;

        return Mathf.Sin(x * Mathf.PI);
    }

    static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "LowHpWarningQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }
}
