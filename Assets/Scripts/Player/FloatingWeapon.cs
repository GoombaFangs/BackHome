using UnityEngine;

/// <summary>
/// XP Hero-style hover weapon: sits beside the character, follows with a light lag,
/// and breathes instead of being glued to a hand bone.
/// </summary>
[DefaultExecutionOrder(80)]
public class FloatingWeapon : MonoBehaviour
{
    const string DefaultResourcePath = "Player/Combat/Weapons/Stinger/Stinger";
    const float TeleportSnapDistance = 6f;

    [Header("Visual")]
    [SerializeField] GameObject weaponPrefab;
    [Tooltip("World meters from the character pivot. X = right, Y = up, Z = forward.")]
    [SerializeField] Vector3 slotOffset = new Vector3(0.68f, 1.66f, 0.06f);
    [SerializeField] Vector3 visualEuler = Vector3.zero;
    [Tooltip("Longest world-axis size of the floating weapon.")]
    [SerializeField, Min(0.1f)] float targetSize = 1.05f;

    [Header("Follow")]
    [SerializeField, Min(0f)] float followLag = 0.08f;
    [SerializeField, Min(0f)] float rotationLag = 0.11f;
    [Tooltip("How far the weapon trails behind while moving.")]
    [SerializeField, Min(0f)] float moveTrail = 0.14f;
    [Tooltip("0 = face character forward, 1 = tilt toward the camera.")]
    [SerializeField, Range(0f, 1f)] float cameraTilt = 0.28f;
    [Tooltip("How quickly the weapon turns to face an attacked creature.")]
    [SerializeField, Min(0f)] float aimRotationLag = 0.05f;

    [Header("Breath")]
    [SerializeField, Min(0f)] float bobHeight = 0.055f;
    [Tooltip("Main hover cycle in Hz. Keep slow — this is idle breathing, not a bounce.")]
    [SerializeField, Min(0.05f)] float bobSpeed = 0.42f;
    [SerializeField, Min(0f)] float swayAmount = 0.03f;
    [SerializeField] float yawSway = 7f;
    [SerializeField] float rollSway = 5f;
    [SerializeField, Range(0f, 0.08f)] float scalePulse = 0.018f;

    Transform _hover;
    Transform _visual;
    Vector3 _baseVisualScale = Vector3.one;
    Vector3 _followVelocity;
    Vector3 _smoothedSlot;
    Quaternion _smoothedFacing = Quaternion.identity;
    bool _hasPose;
    PlanetWalker _walker;
    CharacterController _controller;
    PlayerRangeCombat _combat;
    Camera _camera;

    void Awake()
    {
        _walker = GetComponent<PlanetWalker>();
        _controller = GetComponent<CharacterController>();
        _combat = GetComponent<PlayerRangeCombat>();
        EnsureVisual();
    }

    void OnEnable()
    {
        _hasPose = false;
        if (_hover != null)
            _hover.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (_hover != null)
            _hover.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (_hover == null)
            EnsureVisual();
        if (_hover == null)
            return;

        GetBasis(out Vector3 up, out Vector3 right, out Vector3 forward);
        Vector3 slot = transform.position
            + right * slotOffset.x
            + up * slotOffset.y
            + forward * slotOffset.z
            + TrailOffset(up);

        bool aiming = TryGetAimForward(slot, up, out Vector3 aimForward);
        Vector3 faceForward = aiming ? aimForward : forward;
        Quaternion facing = FacingRotation(up, faceForward, slot, aiming);
        float turnLag = aiming ? aimRotationLag : rotationLag;

        if (!_hasPose || (slot - _smoothedSlot).sqrMagnitude > TeleportSnapDistance * TeleportSnapDistance)
        {
            _smoothedSlot = slot;
            _smoothedFacing = facing;
            _followVelocity = Vector3.zero;
            _hasPose = true;
        }
        else
        {
            _smoothedSlot = Vector3.SmoothDamp(_smoothedSlot, slot, ref _followVelocity, followLag);
            float rotBlend = turnLag <= 0.0001f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / turnLag);
            _smoothedFacing = Quaternion.Slerp(_smoothedFacing, facing, rotBlend);
        }

        SampleBreath(up, right, forward, aiming, out Vector3 breathPos, out Quaternion breathRot, out float breathScale);
        KeepWorldScale();
        _hover.SetPositionAndRotation(
            _smoothedSlot + breathPos,
            _smoothedFacing * Quaternion.Euler(visualEuler) * breathRot);

        if (_visual != null)
            _visual.localScale = _baseVisualScale * breathScale;
    }

    void OnDrawGizmosSelected()
    {
        GetBasis(out Vector3 up, out Vector3 right, out Vector3 forward);
        Vector3 slot = transform.position
            + right * slotOffset.x
            + up * slotOffset.y
            + forward * slotOffset.z;
        Gizmos.color = new Color(0.4f, 1f, 0.55f, 0.9f);
        Gizmos.DrawWireSphere(slot, 0.08f);
        Gizmos.DrawLine(transform.position + up * 0.15f, slot);
    }

    void EnsureVisual()
    {
        if (_hover != null)
            return;

        GameObject prefab = weaponPrefab;
        if (prefab == null)
            prefab = Resources.Load<GameObject>(DefaultResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"{name}: FloatingWeapon has no visual. Assign weaponPrefab or add {DefaultResourcePath}.", this);
            return;
        }

        var hoverGo = new GameObject("FloatingWeapon");
        _hover = hoverGo.transform;
        _hover.SetParent(transform, false);
        KeepWorldScale();

        GameObject instance = Instantiate(prefab, _hover);
        instance.name = prefab.name;
        StripPhysics(instance);

        _visual = instance.transform;
        _visual.localPosition = Vector3.zero;
        _visual.localRotation = Quaternion.identity;
        _visual.localScale = Vector3.one;

        CenterAndFit(_visual);
        _baseVisualScale = _visual.localScale;
    }

    void KeepWorldScale()
    {
        if (_hover == null)
            return;

        Vector3 lossy = transform.lossyScale;
        _hover.localScale = new Vector3(
            Inverse(lossy.x),
            Inverse(lossy.y),
            Inverse(lossy.z));
    }

    void CenterAndFit(Transform visual)
    {
        Recenter(visual);
        Bounds bounds = Encapsulate(visual);
        float longest = Longest(bounds.size);
        if (longest > 0.0001f)
            visual.localScale *= targetSize / longest;
        Recenter(visual);
    }

    void Recenter(Transform visual)
    {
        Bounds bounds = Encapsulate(visual);
        if (Longest(bounds.size) < 0.0001f)
            return;
        visual.localPosition -= _hover.InverseTransformPoint(bounds.center);
    }

    Vector3 TrailOffset(Vector3 up)
    {
        if (moveTrail <= 0f)
            return Vector3.zero;

        Vector3 velocity = Vector3.zero;
        float amount = 0f;
        if (_walker != null && _walker.IsWalkingOnPlanet)
        {
            velocity = _walker.PlanarVelocity;
            amount = _walker.MotionAmount;
        }
        else if (_controller != null)
        {
            velocity = Vector3.ProjectOnPlane(_controller.velocity, up);
            amount = Mathf.Clamp01(velocity.magnitude / 6f);
        }

        if (velocity.sqrMagnitude < 0.01f || amount <= 0.001f)
            return Vector3.zero;

        return -velocity.normalized * (moveTrail * amount);
    }

    Quaternion FacingRotation(Vector3 up, Vector3 forward, Vector3 slot, bool aiming)
    {
        Vector3 face = forward;
        if (!aiming)
        {
            Camera cam = ResolveCamera();
            if (cam != null && cameraTilt > 0f)
            {
                Vector3 toCam = Vector3.ProjectOnPlane(cam.transform.position - slot, up);
                if (toCam.sqrMagnitude > 0.0001f)
                    face = Vector3.Slerp(forward, toCam.normalized, cameraTilt).normalized;
            }
        }

        if (face.sqrMagnitude < 0.0001f)
            face = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (face.sqrMagnitude < 0.0001f)
            face = Vector3.forward;

        return Quaternion.LookRotation(face, up);
    }

    bool TryGetAimForward(Vector3 slot, Vector3 up, out Vector3 forward)
    {
        forward = default;
        if (_combat == null)
            _combat = GetComponent<PlayerRangeCombat>();
        if (_combat == null || !_combat.TryGetAttackAimPoint(out Vector3 aimPoint))
            return false;

        Vector3 toTarget = Vector3.ProjectOnPlane(aimPoint - slot, up);
        if (toTarget.sqrMagnitude < 0.0001f)
            return false;

        forward = toTarget.normalized;
        return true;
    }

    void SampleBreath(Vector3 up, Vector3 right, Vector3 forward, bool aiming, out Vector3 pos, out Quaternion rot, out float scale)
    {
        float tau = Time.time * Mathf.PI * 2f * bobSpeed;
        float bob = Mathf.Sin(tau) * bobHeight + Mathf.Sin(tau * 1.37f + 0.9f) * (bobHeight * 0.28f);
        float sway = Mathf.Sin(tau * 0.73f + 0.4f) * swayAmount + Mathf.Cos(tau * 1.11f) * (swayAmount * 0.35f);
        float fwd = Mathf.Sin(tau * 0.61f + 1.2f) * (swayAmount * 0.45f);
        float rotScale = aiming ? 0.22f : 1f;

        pos = up * bob + right * sway + forward * fwd;
        rot = Quaternion.Euler(
            Mathf.Sin(tau * 0.44f + 0.3f) * (yawSway * 0.35f * rotScale),
            Mathf.Sin(tau * 0.52f) * yawSway * rotScale,
            Mathf.Sin(tau * 0.68f + 1.1f) * rollSway * rotScale);
        scale = 1f + Mathf.Sin(tau) * scalePulse;
    }

    void GetBasis(out Vector3 up, out Vector3 right, out Vector3 forward)
    {
        up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(transform.position)
            : transform.up;
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.up;
        else
            up.Normalize();

        forward = Vector3.ProjectOnPlane(transform.forward, up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(transform.right, up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        right = Vector3.Cross(up, forward);
        if (right.sqrMagnitude < 0.0001f)
            right = transform.right;
        else
            right.Normalize();
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

    static void StripPhysics(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            Destroy(colliders[i]);

        Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
            Destroy(bodies[i]);
    }

    static Bounds Encapsulate(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        bool has = false;
        Bounds bounds = new Bounds(root.position, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i].enabled)
                continue;
            if (!has)
            {
                bounds = renderers[i].bounds;
                has = true;
            }
            else
                bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    static float Longest(Vector3 size)
    {
        return Mathf.Max(size.x, Mathf.Max(size.y, size.z));
    }

    static float Inverse(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }
}
