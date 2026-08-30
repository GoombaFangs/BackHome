using UnityEngine;

/// <summary>
/// Two floating billboard icons near the player that point toward the ship capsule — a "home"
/// badge close to the player and an arrow further out along the same direction, both always
/// facing the camera (the arrow also rolls in-plane to point at the capsule).
/// Physically in the world, not UI. Hidden outside the planet scene, or once the player is
/// already close to the capsule.
/// </summary>
public class CapsuleDirectionMarker : MonoBehaviour
{
    [Header("Icons")]
    [SerializeField] SpriteRenderer homeIcon;
    [SerializeField] SpriteRenderer arrowIcon;

    [Header("Placement")]
    [Tooltip("How far from the player (toward the capsule) the home badge floats.")]
    [SerializeField, Min(0f)] float homeOffset = 4.5f;
    [Tooltip("How far from the player (toward the capsule, beyond the home badge) the arrow floats.")]
    [SerializeField, Min(0f)] float arrowOffset = 5.3f;
    [SerializeField] float floatHeight = 1.5f;
    [SerializeField, Min(0.05f)] float homeWorldSize = 0.5f;
    [SerializeField, Min(0.05f)] float arrowWorldSize = 0.4f;
    [SerializeField] float fadeSpeed = 6f;
    [Range(0f, 1f)] [SerializeField] float maxAlpha = 0.5f;

    [Header("Hide")]
    [Tooltip("Hide once the player is within this world distance of the capsule.")]
    [SerializeField, Min(0.1f)] float hideRadius = 8f;

    Transform _owner;
    Transform _capsule;
    Camera _camera;
    float _alpha;

    void OnEnable()
    {
        _owner = transform.parent != null ? transform.parent : transform;
        _alpha = 0f;
        FitToSize(homeIcon, homeWorldSize);
        FitToSize(arrowIcon, arrowWorldSize);
        ApplyAlpha();
    }

    void LateUpdate()
    {
        if (_camera == null)
            _camera = Camera.main;

        float targetAlpha = 0f;
        if (_camera != null && SceneRoles.IsPlanetScene() && TryResolveCapsule() && TryPlace())
            targetAlpha = maxAlpha;

        _alpha = Mathf.MoveTowards(_alpha, targetAlpha, fadeSpeed * Time.deltaTime);
        ApplyAlpha();

        bool visible = _alpha > 0.01f;
        if (homeIcon != null)
            homeIcon.enabled = visible;
        if (arrowIcon != null)
            arrowIcon.enabled = visible;
    }

    bool TryResolveCapsule()
    {
        if (_capsule == null && ShipCapsuleBeacon.Instance != null)
            _capsule = ShipCapsuleBeacon.Instance.transform;
        return _owner != null && _capsule != null;
    }

    bool TryPlace()
    {
        Vector3 ownerPos = _owner.position;
        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(ownerPos)
            : _owner.up;

        Vector3 toCapsule = Vector3.ProjectOnPlane(_capsule.position - ownerPos, up);
        if (toCapsule.magnitude <= hideRadius)
            return false;

        Vector3 dir = toCapsule.normalized;
        Vector3 basePos = ownerPos + up * floatHeight;

        if (homeIcon != null)
            PlaceBillboard(homeIcon.transform, basePos + dir * homeOffset, dir, roll: false);
        if (arrowIcon != null)
            PlaceBillboard(arrowIcon.transform, basePos + dir * arrowOffset, dir, roll: true);

        return true;
    }

    void PlaceBillboard(Transform t, Vector3 worldPos, Vector3 dir, bool roll)
    {
        t.position = worldPos;

        Vector3 fwd = worldPos - _camera.transform.position;
        if (fwd.sqrMagnitude < 0.0001f)
            fwd = _camera.transform.forward;
        Quaternion billboard = Quaternion.LookRotation(fwd.normalized, _camera.transform.up);

        if (!roll)
        {
            t.rotation = billboard;
            return;
        }

        // Roll around the camera-facing axis so the arrow (which points along local -X by
        // default) points toward the capsule as projected onto the screen plane.
        float screenX = Vector3.Dot(dir, _camera.transform.right);
        float screenY = Vector3.Dot(dir, _camera.transform.up);
        float angle = Mathf.Atan2(screenY, screenX) * Mathf.Rad2Deg + 180f;
        t.rotation = billboard * Quaternion.Euler(0f, 0f, angle);
    }

    void ApplyAlpha()
    {
        SetAlpha(homeIcon, _alpha);
        SetAlpha(arrowIcon, _alpha);
    }

    static void SetAlpha(SpriteRenderer renderer, float alpha)
    {
        if (renderer == null)
            return;

        Color c = renderer.color;
        c.a = alpha;
        renderer.color = c;
    }

    static void FitToSize(SpriteRenderer renderer, float worldSize)
    {
        if (renderer == null || renderer.sprite == null)
            return;

        Vector2 boundsSize = renderer.sprite.bounds.size;
        float largest = Mathf.Max(boundsSize.x, boundsSize.y, 0.0001f);
        float scale = worldSize / largest;
        renderer.transform.localScale = new Vector3(scale, scale, scale);
    }
}
