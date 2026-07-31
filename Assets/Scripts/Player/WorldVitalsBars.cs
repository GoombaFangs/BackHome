using UnityEngine;

/// <summary>
/// HP + Oxygen bars above the player head.
/// Kept as a scale-compensated child so they stay glued on flat ground and spherical planets.
/// </summary>
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(PlayerVitals))]
public class WorldVitalsBars : MonoBehaviour
{
    [SerializeField] Vector3 localOffset = new Vector3(0f, 2.15f, 0f);
    [SerializeField] Vector2 barSize = new Vector2(1.1f, 0.14f);
    [SerializeField] float barSpacing = 0.08f;
    [SerializeField] Color healthFillColor = new Color(0.25f, 0.85f, 0.35f, 1f);
    [SerializeField] Color healthLowColor = new Color(0.9f, 0.2f, 0.2f, 1f);
    [SerializeField] Color oxygenFillColor = new Color(0.25f, 0.65f, 1f, 1f);
    [SerializeField] Color backgroundColor = new Color(0.05f, 0.05f, 0.08f, 0.85f);

    PlayerVitals _vitals;
    Camera _camera;
    Transform _root;
    Transform _healthFill;
    Transform _oxygenFill;
    SpriteRenderer _healthFillSr;
    SpriteRenderer _oxygenFillSr;
    float _fillHeight;
    Sprite _whiteSprite;
    Texture2D _whiteTexture;

    void Awake()
    {
        _vitals = GetComponent<PlayerVitals>();
        _fillHeight = barSize.y * 0.75f;
        BuildBars();
    }

    void OnEnable()
    {
        if (_vitals != null)
            _vitals.VitalsChanged += RefreshBars;
        RefreshBars();
    }

    void OnDisable()
    {
        if (_vitals != null)
            _vitals.VitalsChanged -= RefreshBars;
    }

    void OnDestroy()
    {
        if (_vitals != null)
            _vitals.VitalsChanged -= RefreshBars;

        if (_root != null)
            Destroy(_root.gameObject);

        _root = null;
        _healthFill = null;
        _oxygenFill = null;
        _camera = null;

        if (_whiteSprite != null)
            Destroy(_whiteSprite);
        if (_whiteTexture != null)
            Destroy(_whiteTexture);

        _whiteSprite = null;
        _whiteTexture = null;
    }

    void LateUpdate()
    {
        if (_root == null)
            return;

        // Cancel player scale so bars keep a stable world size.
        Vector3 lossy = transform.lossyScale;
        _root.localScale = new Vector3(
            ApproxInverse(lossy.x),
            ApproxInverse(lossy.y),
            ApproxInverse(lossy.z));
        _root.localPosition = localOffset;

        Camera cam = ResolveCamera();
        if (cam != null)
            _root.rotation = cam.transform.rotation;
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

    void RefreshBars()
    {
        if (_vitals == null)
            return;

        if (_healthFill != null && _healthFillSr != null)
        {
            float t = Mathf.Clamp01(_vitals.HealthNormalized);
            ApplyFill(_healthFill, t);
            _healthFillSr.color = Color.Lerp(healthLowColor, healthFillColor, Mathf.Clamp01(t * 1.5f));
        }

        if (_oxygenFill != null)
            ApplyFill(_oxygenFill, Mathf.Clamp01(_vitals.OxygenNormalized));
    }

    void ApplyFill(Transform fill, float normalized)
    {
        float width = barSize.x * Mathf.Max(0f, normalized);
        fill.localScale = new Vector3(Mathf.Max(0.0001f, width), _fillHeight, 1f);
        fill.localPosition = new Vector3(-barSize.x * 0.5f + width * 0.5f, 0f, -0.01f);
    }

    void BuildBars()
    {
        var rootGo = new GameObject("VitalsBars");
        _root = rootGo.transform;
        _root.SetParent(transform, false);
        _root.localPosition = localOffset;
        _root.localRotation = Quaternion.identity;
        _root.localScale = Vector3.one;

        float halfGap = barSize.y * 0.5f + barSpacing * 0.5f;
        CreateBarPair("HealthBar", halfGap, healthFillColor, out _healthFill, out _healthFillSr);
        CreateBarPair("OxygenBar", -halfGap, oxygenFillColor, out _oxygenFill, out _oxygenFillSr);
    }

    void CreateBarPair(
        string name,
        float localY,
        Color fillColor,
        out Transform fillTransform,
        out SpriteRenderer fillRenderer)
    {
        var bar = new GameObject(name);
        bar.transform.SetParent(_root, false);
        bar.transform.localPosition = new Vector3(0f, localY, 0f);
        bar.transform.localRotation = Quaternion.identity;
        bar.transform.localScale = Vector3.one;

        var bg = CreateSpriteObject("Background", bar.transform, backgroundColor, 10);
        bg.transform.localPosition = Vector3.zero;
        bg.transform.localScale = new Vector3(barSize.x, barSize.y, 1f);

        var fill = CreateSpriteObject("Fill", bar.transform, fillColor, 11);
        fillTransform = fill.transform;
        fillRenderer = fill.GetComponent<SpriteRenderer>();
        ApplyFill(fillTransform, 1f);
    }

    GameObject CreateSpriteObject(string name, Transform parent, Color color, int sortingOrder)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.identity;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetWhiteSprite();
        sr.color = color;
        sr.sortingOrder = sortingOrder;

        return go;
    }

    Sprite GetWhiteSprite()
    {
        if (_whiteSprite != null)
            return _whiteSprite;

        _whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _whiteTexture.SetPixel(0, 0, Color.white);
        _whiteTexture.Apply(false, true);

        _whiteSprite = Sprite.Create(
            _whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        return _whiteSprite;
    }
}
