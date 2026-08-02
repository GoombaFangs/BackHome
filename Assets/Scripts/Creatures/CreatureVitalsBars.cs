using UnityEngine;

/// <summary>
/// Wires a <see cref="Creature"/> to a <see cref="VitalsBarsView"/> above its head.
/// Shows health only — oxygen stays hidden for creatures.
/// </summary>
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(Creature))]
public class CreatureVitalsBars : MonoBehaviour
{
    [SerializeField] VitalsBarsView vitalsBarsPrefab;
    [SerializeField] VitalsBarsView bars;
    [SerializeField] Vector3 localOffset = new Vector3(0f, 2.2f, 0f);
    [SerializeField, Min(0.1f)] float worldScale = 1.6f;
    [SerializeField] bool hideWhenDead = true;

    Creature _creature;
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
        _creature = GetComponent<Creature>();
        EnsureBars();
        if (bars != null)
            bars.SetOxygenVisible(false);
    }

    void OnEnable()
    {
        if (_creature == null)
            _creature = GetComponent<Creature>();

        if (_creature != null)
        {
            _creature.HealthChanged += RefreshBars;
            _creature.Died += OnDied;
        }

        RefreshBars();
    }

    void Start()
    {
        RefreshBars();
    }

    void OnDisable()
    {
        if (_creature == null)
            return;

        _creature.HealthChanged -= RefreshBars;
        _creature.Died -= OnDied;
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
            Debug.LogWarning($"{name}: assign VitalsBars prefab on CreatureVitalsBars.", this);
            return;
        }

        bars = Instantiate(vitalsBarsPrefab, transform);
        bars.name = vitalsBarsPrefab.name;
        bars.transform.localPosition = localOffset;
        bars.transform.localRotation = Quaternion.identity;
        bars.transform.localScale = Vector3.one;
    }

    void RefreshBars()
    {
        if (bars == null || _creature == null)
            return;

        bars.SetOxygenVisible(false);
        bars.SetHealthValues(_creature.CurrentHealth, _creature.MaxHealth);

        bool show = !hideWhenDead || _creature.IsAlive;
        if (bars.gameObject.activeSelf != show)
            bars.gameObject.SetActive(show);
    }

    void OnDied(Creature _)
    {
        RefreshBars();
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
        {
            CameraFollow follow = FindAnyObjectByType<CameraFollow>();
            if (follow != null)
                _camera = follow.GetComponent<Camera>();
        }

        if (_camera == null)
            _camera = FindAnyObjectByType<Camera>();

        return _camera;
    }
}
