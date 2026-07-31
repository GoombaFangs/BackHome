using UnityEngine;

/// <summary>
/// Wires <see cref="PlayerVitals"/> to a <see cref="VitalsBarsView"/> prefab instance
/// and keeps the bars billboarded above the player.
/// </summary>
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(PlayerVitals))]
public class WorldVitalsBars : MonoBehaviour
{
    [SerializeField] VitalsBarsView vitalsBarsPrefab;
    [SerializeField] VitalsBarsView bars;
    [SerializeField] Vector3 localOffset = new Vector3(0f, 2.15f, 0f);

    PlayerVitals _vitals;
    Camera _camera;

    void Awake()
    {
        _vitals = GetComponent<PlayerVitals>();
        EnsureBars();
    }

    void OnEnable()
    {
        if (_vitals == null)
            _vitals = GetComponent<PlayerVitals>();

        if (_vitals != null)
            _vitals.VitalsChanged += RefreshBars;

        RefreshBars();
    }

    void Start()
    {
        // Vitals finish initializing in Awake; refresh again so fills are never stuck empty.
        RefreshBars();
    }

    void OnDisable()
    {
        if (_vitals != null)
            _vitals.VitalsChanged -= RefreshBars;
    }

    void LateUpdate()
    {
        if (bars == null)
            return;

        Transform root = bars.transform;
        Vector3 lossy = transform.lossyScale;
        root.localScale = new Vector3(
            ApproxInverse(lossy.x),
            ApproxInverse(lossy.y),
            ApproxInverse(lossy.z));
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
            Debug.LogWarning("WorldVitalsBars: assign VitalsBars prefab on the Player.", this);
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
        if (bars == null || _vitals == null)
            return;

        bars.SetHealth(_vitals.HealthNormalized);
        bars.SetOxygen(_vitals.OxygenNormalized);
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
            CameraFollow follow = FindFirstObjectByType<CameraFollow>();
            if (follow != null)
                _camera = follow.GetComponent<Camera>();
        }

        if (_camera == null)
            _camera = FindFirstObjectByType<Camera>();

        return _camera;
    }
}
