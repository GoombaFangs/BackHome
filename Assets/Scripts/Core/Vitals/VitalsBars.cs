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

    IVitalsReadable _source;
    Camera _camera;

    public float WorldScale => worldScale;
    public Vector3 LocalOffset => localOffset;

    public void SetWorldScale(float scale)
    {
        worldScale = Mathf.Max(0.1f, scale);
    }

    public void SetLocalOffset(Vector3 offset)
    {
        localOffset = offset;
    }

    void Awake()
    {
        _source = GetComponent<IVitalsReadable>();
        EnsureBars();
        ApplyOxygenVisibility();
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

        bool show = !hideWhenDead || _source.IsAlive;
        if (bars.gameObject.activeSelf != show)
            bars.gameObject.SetActive(show);
    }

    void OnDamaged()
    {
        if (bars != null)
            bars.FlashHealthHit();
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
