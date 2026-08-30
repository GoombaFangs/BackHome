using UnityEngine;

/// <summary>
/// Ground arrow marker, physically in the world — not UI. Lies flat on the planet surface near
/// the player's feet and continuously yaws to point toward the ship capsule. Same ground-alignment
/// approach as the old attack-range ring (a Plane mesh aligned to the planet's up vector), except
/// this one also yaws to face a direction instead of just sitting flat.
/// Hidden outside the planet scene, or once the player is already close to the capsule.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public class CapsuleDirectionMarker : MonoBehaviour
{
    // Unity default Plane is 10x10 centered, lying flat with normal +Y and V (texture-up) along +Z —
    // exactly the axis LookRotation's "forward" maps to, so forward = ground heading, up = surface normal.
    const float PlaneFullExtent = 10f;

    static readonly int ColorId = Shader.PropertyToID("_BaseColor");

    [Header("Look")]
    [SerializeField, Min(0.1f)] float worldSize = 1.6f;
    [SerializeField] float groundOffset = 0.04f;
    [SerializeField] Color color = new Color(1f, 1f, 1f, 0.92f);
    [SerializeField] float fadeSpeed = 6f;

    [Header("Hide")]
    [Tooltip("Hide once the player is within this world distance of the capsule.")]
    [SerializeField, Min(0.1f)] float hideRadius = 6f;

    MeshRenderer _renderer;
    MaterialPropertyBlock _block;
    Transform _owner;
    Transform _capsule;
    float _alpha;

    void OnEnable()
    {
        _renderer = GetComponent<MeshRenderer>();
        _block = new MaterialPropertyBlock();
        _owner = transform.parent != null ? transform.parent : transform;
        _alpha = 0f;
        ApplyMaterial();
    }

    void LateUpdate()
    {
        float targetAlpha = 0f;
        if (SceneRoles.IsPlanetScene() && TryResolveCapsule() && TryOrient())
            targetAlpha = 1f;

        _alpha = Mathf.MoveTowards(_alpha, targetAlpha, fadeSpeed * Time.deltaTime);
        ApplyMaterial();
        if (_renderer != null)
            _renderer.enabled = _alpha > 0.01f;
    }

    bool TryResolveCapsule()
    {
        if (_capsule == null && ShipCapsuleBeacon.Instance != null)
            _capsule = ShipCapsuleBeacon.Instance.transform;
        return _owner != null && _capsule != null;
    }

    bool TryOrient()
    {
        Vector3 ownerPos = _owner.position;
        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(ownerPos)
            : _owner.up;

        Vector3 toCapsule = Vector3.ProjectOnPlane(_capsule.position - ownerPos, up);
        if (toCapsule.magnitude <= hideRadius)
            return false;

        Vector3 dir = toCapsule.normalized;
        transform.position = ownerPos + up * groundOffset;
        transform.rotation = Quaternion.LookRotation(dir, up);

        float worldScale = worldSize / PlaneFullExtent;
        Vector3 parentLossy = _owner.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(parentLossy.x));
        float sy = Mathf.Max(0.0001f, Mathf.Abs(parentLossy.y));
        float sz = Mathf.Max(0.0001f, Mathf.Abs(parentLossy.z));
        transform.localScale = new Vector3(worldScale / sx, worldScale / sy, worldScale / sz);
        return true;
    }

    void ApplyMaterial()
    {
        if (_renderer == null || _block == null)
            return;

        _renderer.GetPropertyBlock(_block);
        Color c = color;
        c.a *= _alpha;
        _block.SetColor(ColorId, c);
        _renderer.SetPropertyBlock(_block);
    }
}
