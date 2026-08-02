using UnityEngine;

/// <summary>
/// Ground range ring under the player. World size is driven by <see cref="worldRadius"/>;
/// the shader draws the ring at UV distance <c>_Radius</c> from the plane center.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public class PlayerRangeIndicator : MonoBehaviour
{
    // Unity default Plane is 10x10 centered (half-extent 5). UVs span 0..1 across that.
    // Shader radius is UV distance from 0.5, so mesh radius = uvRadius * PlaneFullExtent.
    const float UnityPlaneFullExtent = 10f;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int RadiusId = Shader.PropertyToID("_Radius");
    static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    [SerializeField, Min(0.05f)] float worldRadius = 2.5f;
    [SerializeField, Range(0.05f, 0.5f)] float uvRadius = 0.45f;
    [SerializeField] Color color = new Color(1f, 1f, 0.95f, 0.65f);
    [SerializeField, Range(0.001f, 0.2f)] float thickness = 0.018f;
    [SerializeField, Range(0f, 0.1f)] float softness = 0.005f;
    [SerializeField, Range(0f, 1f)] float opacity = 1f;
    [SerializeField] float groundOffset = 0.03f;

    MeshRenderer _renderer;
    MaterialPropertyBlock _block;

    /// <summary>Combat / gameplay radius in world units (matches the visible ring).</summary>
    public float WorldRadius
    {
        get => worldRadius;
        set
        {
            worldRadius = Mathf.Max(0.05f, value);
            Apply();
        }
    }

    /// <summary>Same as <see cref="WorldRadius"/> — explicit name for combat code.</summary>
    public float GetCombatRadius() => worldRadius;

    void OnEnable()
    {
        Cache();
        Apply();
    }

    void OnValidate()
    {
        worldRadius = Mathf.Max(0.05f, worldRadius);
        uvRadius = Mathf.Clamp(uvRadius, 0.05f, 0.5f);
        thickness = Mathf.Clamp(thickness, 0.001f, 0.2f);
        softness = Mathf.Clamp(softness, 0f, 0.1f);
        opacity = Mathf.Clamp01(opacity);
        Cache();
        Apply();
    }

    void LateUpdate()
    {
        ApplyTransform();
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position;
        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(origin)
            : transform.up;

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
            Vector3 point = origin + (right * Mathf.Cos(t) + forward * Mathf.Sin(t)) * worldRadius;
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

    void Apply()
    {
        Cache();
        ApplyTransform();
        ApplyMaterial();
    }

    void ApplyTransform()
    {
        Vector3 pos = transform.localPosition;
        pos.y = groundOffset;
        transform.localPosition = pos;

        float parentScale = 1f;
        if (transform.parent != null)
        {
            Vector3 lossy = transform.parent.lossyScale;
            parentScale = Mathf.Max(0.0001f, (lossy.x + lossy.z) * 0.5f);
        }

        // worldRadius = uvRadius * PlaneFullExtent * localScale * parentScale
        float denom = parentScale * UnityPlaneFullExtent * Mathf.Max(0.05f, uvRadius);
        float local = worldRadius / denom;
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
}
