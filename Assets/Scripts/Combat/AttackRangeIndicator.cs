using UnityEngine;

/// <summary>
/// Ground attack-range ring. Works under player or creature — radius comes from
/// parent <see cref="PlayerVitals"/> / <see cref="Creature"/> AttackRange (or <see cref="SetRadius"/>).
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public class AttackRangeIndicator : MonoBehaviour
{
    // Unity default Plane is 10x10 centered. UVs span 0..1 across that.
    // Shader radius is UV distance from 0.5, so mesh radius = uvRadius * PlaneFullExtent.
    const float UnityPlaneFullExtent = 10f;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int RadiusId = Shader.PropertyToID("_Radius");
    static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    [Header("Look")]
    [SerializeField, Range(0.05f, 0.5f)] float uvRadius = 0.45f;
    [SerializeField] Color color = new Color(1f, 1f, 0.95f, 0.65f);
    [SerializeField, Range(0.001f, 0.2f)] float thickness = 0.018f;
    [SerializeField, Range(0f, 0.1f)] float softness = 0.005f;
    [SerializeField, Range(0f, 1f)] float opacity = 1f;
    [SerializeField] float groundOffset = 0.03f;
    [SerializeField] bool alignToPlanet = true;

    [Header("Radius")]
    [Tooltip("Used only when no parent stats and SetRadius was never called.")]
    [SerializeField, Min(0.05f)] float fallbackRadius = 5f;

    MeshRenderer _renderer;
    MaterialPropertyBlock _block;
    float _worldRadius = 5f;
    float _forcedRadius = -1f;

    /// <summary>Current ring radius in world units.</summary>
    public float WorldRadius => _worldRadius;

    public float GetCombatRadius() => _worldRadius;

    /// <summary>Force a radius (skips parent stats). Pass &lt;= 0 to clear.</summary>
    public void SetRadius(float worldRadius)
    {
        _forcedRadius = worldRadius > 0f ? worldRadius : -1f;
        SyncRadius();
        Apply();
    }

    public void SetColor(Color newColor)
    {
        color = newColor;
        ApplyMaterial();
    }

    void OnEnable()
    {
        Cache();
        SyncRadius();
        Apply();
    }

    void OnValidate()
    {
        uvRadius = Mathf.Clamp(uvRadius, 0.05f, 0.5f);
        thickness = Mathf.Clamp(thickness, 0.001f, 0.2f);
        softness = Mathf.Clamp(softness, 0f, 0.1f);
        opacity = Mathf.Clamp01(opacity);
        fallbackRadius = Mathf.Max(0.05f, fallbackRadius);
        Cache();
        SyncRadius();
        Apply();
    }

    void LateUpdate()
    {
        SyncRadius();
        ApplyTransform();
        ApplyMaterial();
    }

    void OnDrawGizmosSelected()
    {
        SyncRadius();
        Vector3 origin = transform.position;
        Vector3 up = GetUp(origin);

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.85f);
        const int segments = 48;
        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= segments; i++)
        {
            float t = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 right = Vector3.Cross(up, Vector3.forward);
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.Cross(up, Vector3.right);
            right.Normalize();
            Vector3 forward = Vector3.Cross(right, up).normalized;
            Vector3 point = origin + (right * Mathf.Cos(t) + forward * Mathf.Sin(t)) * _worldRadius;
            if (i > 0)
                Gizmos.DrawLine(prev, point);
            prev = point;
        }
    }

    void Cache()
    {
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_block == null)
            _block = new MaterialPropertyBlock();
    }

    void SyncRadius()
    {
        if (_forcedRadius > 0f)
        {
            _worldRadius = _forcedRadius;
            return;
        }

        PlayerVitals vitals = GetComponentInParent<PlayerVitals>();
        if (vitals != null && vitals.AttackRange > 0f)
        {
            _worldRadius = vitals.AttackRange;
            return;
        }

        Creature creature = GetComponentInParent<Creature>();
        if (creature != null && creature.AttackRange > 0f)
        {
            _worldRadius = creature.AttackRange;
            return;
        }

        _worldRadius = fallbackRadius;
    }

    void Apply()
    {
        Cache();
        ApplyTransform();
        ApplyMaterial();
    }

    void ApplyTransform()
    {
        Transform owner = transform.parent != null ? transform.parent : transform;
        Vector3 ownerPos = owner.position;
        Vector3 up = GetUp(ownerPos);

        if (alignToPlanet)
        {
            transform.position = ownerPos + up * groundOffset;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, up);

            float worldScale = _worldRadius / (UnityPlaneFullExtent * Mathf.Max(0.05f, uvRadius));
            Vector3 parentLossy = owner.lossyScale;
            float sx = Mathf.Max(0.0001f, Mathf.Abs(parentLossy.x));
            float sy = Mathf.Max(0.0001f, Mathf.Abs(parentLossy.y));
            float sz = Mathf.Max(0.0001f, Mathf.Abs(parentLossy.z));
            transform.localScale = new Vector3(worldScale / sx, worldScale / sy, worldScale / sz);
            return;
        }

        Vector3 pos = transform.localPosition;
        pos.y = groundOffset;
        transform.localPosition = pos;

        float parentScale = 1f;
        if (transform.parent != null)
        {
            Vector3 lossy = transform.parent.lossyScale;
            parentScale = Mathf.Max(0.0001f, (lossy.x + lossy.z) * 0.5f);
        }

        float denom = parentScale * UnityPlaneFullExtent * Mathf.Max(0.05f, uvRadius);
        float local = _worldRadius / denom;
        transform.localScale = new Vector3(local, local, local);
    }

    void ApplyMaterial()
    {
        if (_renderer == null)
            return;

        _renderer.GetPropertyBlock(_block);
        _block.SetColor(ColorId, color);
        _block.SetFloat(RadiusId, uvRadius);
        _block.SetFloat(ThicknessId, thickness);
        _block.SetFloat(SoftnessId, softness);
        _block.SetFloat(OpacityId, opacity);
        _renderer.SetPropertyBlock(_block);
    }

    Vector3 GetUp(Vector3 worldPosition)
    {
        if (alignToPlanet && SphericalPlanet.Instance != null)
            return SphericalPlanet.Instance.GetUpAt(worldPosition);

        if (transform.parent != null)
            return transform.parent.up;

        return transform.up;
    }
}
