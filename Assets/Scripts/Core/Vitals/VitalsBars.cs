using UnityEngine;

/// <summary>
/// Billboard HP/oxygen bars for any <see cref="IVitalsReadable"/> on the same GameObject
/// (player, creature, …).
/// </summary>
[DefaultExecutionOrder(1000)]
public class VitalsBars : MonoBehaviour
{
    [SerializeField] VitalsBarsView vitalsBarsPrefab;
    [SerializeField] VitalsBarsView bars;
    [SerializeField] Vector3 localOffset = new Vector3(0f, 2.15f, 0f);
    [Tooltip("Extra world size multiplier. SceneBootstrap can override this per scene.")]
    [SerializeField, Min(0.1f)] float worldScale = 1f;
    [SerializeField] bool hideWhenDead;
    [Tooltip("Keep bars hidden until the host takes a hit. Always on for creatures.")]
    [SerializeField] bool hideUntilDamaged;
    [SerializeField, Min(0.05f)] float visibleAfterDamage = 2.5f;
    [SerializeField, Min(0f)] float fadeDuration = 0.2f;

    IVitalsReadable _source;
    Camera _camera;
    float _revealTimer;
    float _alpha = 1f;

    public void SetWorldScale(float scale)
    {
        worldScale = Mathf.Max(0.1f, scale);
    }

    public void SetLocalOffset(Vector3 offset)
    {
        localOffset = offset;
    }

    bool HideUntilDamaged => hideUntilDamaged || _source is Creature;

    void Awake()
    {
        _source = GetComponent<IVitalsReadable>();
        EnsureBars();
        ApplyOxygenVisibility();
        if (HideUntilDamaged)
            HideBarsImmediate();
    }

    void OnEnable()
    {
        if (_source == null)
            _source = GetComponent<IVitalsReadable>();

        if (_source != null)
        {
            _source.VitalsChanged += RefreshBars;
            _source.Damaged += OnDamaged;
            _source.Died += RefreshBars;
        }

        RefreshBars();
    }

    void Start()
    {
        RefreshBars();
    }

    void OnDisable()
    {
        if (_source == null)
            return;

        _source.VitalsChanged -= RefreshBars;
        _source.Damaged -= OnDamaged;
        _source.Died -= RefreshBars;
    }

    void LateUpdate()
    {
        if (bars == null)
            return;

        Transform root = bars.transform;
        Vector3 lossy = transform.lossyScale;
        float s = worldScale;
        root.localScale = new Vector3(
            ApproxInverse(lossy.x) * s,
            ApproxInverse(lossy.y) * s,
            ApproxInverse(lossy.z) * s);
        root.localPosition = localOffset;

        Camera cam = ResolveCamera();
        if (cam != null)
            root.rotation = cam.transform.rotation;

        UpdateDamageReveal();
    }

    void EnsureBars()
    {
        if (bars != null)
            return;

        bars = GetComponentInChildren<VitalsBarsView>(true);
        if (bars != null)
            return;

        if (vitalsBarsPrefab == null)
        {
            Debug.LogWarning($"{name}: assign VitalsBars prefab on VitalsBars.", this);
            return;
        }

        bars = Instantiate(vitalsBarsPrefab, transform);
        bars.name = vitalsBarsPrefab.name;
        bars.transform.localPosition = localOffset;
        bars.transform.localRotation = Quaternion.identity;
        bars.transform.localScale = Vector3.one;
    }

    void ApplyOxygenVisibility()
    {
        if (bars == null)
            return;

        bars.SetOxygenVisible(_source != null && _source.HasOxygen);
    }

    void RefreshBars()
    {
        if (bars == null || _source == null)
            return;

        ApplyOxygenVisibility();
        bars.SetHealthValues(_source.CurrentHealth, _source.MaxHealth);

        if (_source.HasOxygen)
            bars.SetOxygenValues(_source.CurrentOxygen, _source.MaxOxygen);

        bool alive = !hideWhenDead || _source.IsAlive;
        if (!alive)
        {
            HideBarsImmediate();
            return;
        }

        if (HideUntilDamaged)
            return;

        if (!bars.gameObject.activeSelf)
            bars.gameObject.SetActive(true);
    }

    void OnDamaged(float _)
    {
        if (bars == null)
            return;

        if (HideUntilDamaged)
            RevealBars();

        bars.FlashHealthHit();
    }

    void UpdateDamageReveal()
    {
        if (!HideUntilDamaged || bars == null)
            return;

        bool alive = _source == null || !hideWhenDead || _source.IsAlive;
        if (!alive)
        {
            HideBarsImmediate();
            return;
        }

        if (_revealTimer > 0f)
            _revealTimer -= Time.deltaTime;

        float target = _revealTimer > 0f ? 1f : 0f;
        if (fadeDuration <= 0f)
            _alpha = target;
        else
            _alpha = Mathf.MoveTowards(_alpha, target, Time.deltaTime / fadeDuration);

        bool show = _alpha > 0.001f || target > 0f;
        if (bars.gameObject.activeSelf != show)
            bars.gameObject.SetActive(show);

        if (show)
            bars.SetAlpha(_alpha);
    }

    void RevealBars()
    {
        _revealTimer = visibleAfterDamage > 0.05f ? visibleAfterDamage : 2.5f;
        _alpha = 1f;
        if (!bars.gameObject.activeSelf)
            bars.gameObject.SetActive(true);
        bars.SetAlpha(1f);
    }

    void HideBarsImmediate()
    {
        _revealTimer = 0f;
        _alpha = 0f;
        if (bars == null)
            return;

        bars.SetAlpha(0f);
        if (bars.gameObject.activeSelf)
            bars.gameObject.SetActive(false);
    }

    static float ApproxInverse(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
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
}
