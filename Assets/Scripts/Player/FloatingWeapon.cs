using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// XP Hero-style hover weapons: sit beside the character, follow with a light lag,
/// and breathe instead of being glued to a hand bone. Up to
/// <see cref="CombatLoadout.MaxWeapons"/> guns come from
/// <see cref="PlayerVitals.Weapons"/>. How each fires lives on that prefab.
/// </summary>
[DefaultExecutionOrder(80)]
public class FloatingWeapon : MonoBehaviour
{
    const float TeleportSnapDistance = 6f;

    sealed class Slot
    {
        public WeaponDefinition Definition;
        public Transform Hover;
        public Transform Visual;
        public EquippedWeapon Attack;
        public Vector3 BaseVisualScale = Vector3.one;
        public Vector3 FollowVelocity;
        public Vector3 SmoothedSlot;
        public Quaternion SmoothedFacing = Quaternion.identity;
        public bool HasPose;
    }

    [Header("Weapon")]
    [Tooltip("Used only if PlayerStats.Weapons is empty. Prefer assigning weapons on the stats asset.")]
    [SerializeField] WeaponDefinition equipped;

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

    readonly List<Slot> _slots = new();
    readonly List<WeaponDefinition> _resolved = new();
    bool _visible = true;
    PlanetWalker _walker;
    CharacterController _controller;
    PlayerRangeCombat _combat;
    PlayerVitals _vitals;
    Camera _camera;

    public int SlotCount => _slots.Count;

    void Awake()
    {
        _walker = GetComponent<PlanetWalker>();
        _controller = GetComponent<CharacterController>();
        _combat = GetComponent<PlayerRangeCombat>();
        _vitals = GetComponent<PlayerVitals>();
        if (_vitals != null)
            _vitals.LoadoutChanged += ApplyLoadout;

        if (SceneRoles.IsSpaceshipScene())
        {
            SetVisible(false);
            return;
        }

        EnsureVisuals();
    }

    void OnDestroy()
    {
        if (_vitals != null)
            _vitals.LoadoutChanged -= ApplyLoadout;
    }

    public void ApplyLoadout()
    {
        if (SceneRoles.IsSpaceshipScene())
        {
            SetVisible(false);
            ClearHovers();
            return;
        }

        ResolveDefinitions(_resolved);
        if (Matches(_resolved))
            return;

        ClearHovers();
        EnsureVisuals();
    }

    public bool TryFire(int slot, Creature target, float damage, Vector3 knockFrom)
    {
        if (slot < 0 || slot >= _slots.Count)
            return false;

        Slot held = _slots[slot];
        if (held.Attack == null)
            return false;
        if (!TryGetMuzzle(held, slot, out Vector3 muzzle, out _))
            return false;

        held.Attack.Fire(target, damage, muzzle, held.Hover, knockFrom);
        return true;
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        for (int i = 0; i < _slots.Count; i++)
        {
            Slot held = _slots[i];
            if (held.Hover != null)
                held.Hover.gameObject.SetActive(visible);
        }
    }

    void OnEnable()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            Slot held = _slots[i];
            held.HasPose = false;
            if (held.Hover != null)
                held.Hover.gameObject.SetActive(_visible);
        }
    }

    void OnDisable()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            Slot held = _slots[i];
            if (held.Hover != null)
                held.Hover.gameObject.SetActive(false);
        }
    }

    void LateUpdate()
    {
        if (!_visible)
            return;
        if (_slots.Count == 0)
            EnsureVisuals();
        if (_slots.Count == 0)
            return;

        GetBasis(out Vector3 up, out Vector3 right, out Vector3 forward);
        Vector3 trail = TrailOffset(up);
        int count = _slots.Count;

        for (int i = 0; i < count; i++)
            UpdateSlot(_slots[i], i, count, up, right, forward, trail);
    }

    void OnDrawGizmosSelected()
    {
        GetBasis(out Vector3 up, out Vector3 right, out Vector3 forward);
        ResolveDefinitions(_resolved);
        int count = Mathf.Max(1, _resolved.Count);
        for (int i = 0; i < count; i++)
        {
            WeaponDefinition definition = i < _resolved.Count ? _resolved[i] : null;
            Vector3 slotOffset = LayoutHoldOffset(HoldSlotOffset(definition), i, count);
            Vector3 slot = transform.position
                + right * slotOffset.x
                + up * slotOffset.y
                + forward * slotOffset.z;
            Gizmos.color = new Color(0.4f, 1f, 0.55f, 0.9f);
            Gizmos.DrawWireSphere(slot, 0.08f);
            Gizmos.DrawLine(transform.position + up * 0.15f, slot);
        }
    }

    void UpdateSlot(Slot held, int index, int count, Vector3 up, Vector3 right, Vector3 forward, Vector3 trail)
    {
        if (held.Hover == null)
            return;

        Vector3 slotOffset = LayoutHoldOffset(HoldSlotOffset(held), index, count);
        Vector3 slot = transform.position
            + right * slotOffset.x
            + up * slotOffset.y
            + forward * slotOffset.z
            + trail;

        bool aiming = TryGetAimForward(index, slot, up, out Vector3 aimForward);
        Vector3 faceForward = aiming ? aimForward : forward;
        Quaternion facing = FacingRotation(up, faceForward, slot, aiming);
        float turnLag = aiming ? aimRotationLag : rotationLag;

        if (!held.HasPose || (slot - held.SmoothedSlot).sqrMagnitude > TeleportSnapDistance * TeleportSnapDistance)
        {
            held.SmoothedSlot = slot;
            held.SmoothedFacing = facing;
            held.FollowVelocity = Vector3.zero;
            held.HasPose = true;
        }
        else
        {
            held.SmoothedSlot = Vector3.SmoothDamp(held.SmoothedSlot, slot, ref held.FollowVelocity, followLag);
            float rotBlend = turnLag <= 0.0001f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / turnLag);
            held.SmoothedFacing = Quaternion.Slerp(held.SmoothedFacing, facing, rotBlend);
        }

        SampleBreath(index, up, right, forward, aiming, out Vector3 breathPos, out Quaternion breathRot, out float breathScale);
        KeepWorldScale(held.Hover);
        held.Hover.SetPositionAndRotation(
            held.SmoothedSlot + breathPos,
            held.SmoothedFacing * Quaternion.Euler(HoldVisualEuler(held)) * breathRot);

        if (held.Visual != null)
            held.Visual.localScale = held.BaseVisualScale * breathScale;
    }

    void EnsureVisuals()
    {
        if (_slots.Count > 0)
            return;

        ResolveDefinitions(_resolved);
        if (_resolved.Count == 0)
        {
            Debug.LogWarning($"{name}: FloatingWeapon has no weapon in PlayerStats.Weapons (or fallback).", this);
            return;
        }

        for (int i = 0; i < _resolved.Count; i++)
            SpawnSlot(_resolved[i], i);
    }

    void SpawnSlot(WeaponDefinition definition, int index)
    {
        if (definition == null)
            return;

        GameObject prefab = definition.Prefab;
        if (prefab == null)
        {
            _slots.Add(new Slot { Definition = definition });
            return;
        }

        var hoverGo = new GameObject(index == 0 ? "FloatingWeapon" : $"FloatingWeapon_{index + 1}");
        Transform hover = hoverGo.transform;
        hover.SetParent(transform, false);
        hover.gameObject.SetActive(_visible);
        KeepWorldScale(hover);

        GameObject instance = Instantiate(prefab, hover);
        instance.name = prefab.name;
        StripPhysics(instance);

        Transform visual = instance.transform;
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        visual.localScale = Vector3.one;
        EquippedWeapon attack = instance.GetComponent<EquippedWeapon>();
        if (attack != null)
            attack.Bind(definition);

        CenterAndFit(hover, visual, attack != null ? attack.TargetSize : 1.05f);

        _slots.Add(new Slot
        {
            Definition = definition,
            Hover = hover,
            Visual = visual,
            Attack = attack,
            BaseVisualScale = visual.localScale
        });
    }

    bool Matches(List<WeaponDefinition> definitions)
    {
        if (definitions.Count != _slots.Count)
            return false;

        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Definition != definitions[i])
                return false;
        }

        return true;
    }

    bool TryGetMuzzle(Slot held, int slot, out Vector3 position, out Vector3 forward)
    {
        if (held == null || held.Hover == null)
        {
            position = transform.position;
            forward = transform.forward;
            return false;
        }

        GetBasis(out Vector3 up, out _, out Vector3 bodyForward);
        if (TryGetAimForward(slot, held.Hover.position, up, out Vector3 aimForward))
            forward = aimForward;
        else
            forward = Vector3.ProjectOnPlane(held.SmoothedFacing * Vector3.forward, up);

        if (forward.sqrMagnitude < 0.0001f)
            forward = bodyForward;
        forward.Normalize();

        Transform anchor = held.Attack != null ? held.Attack.MuzzleAnchor : null;
        if (anchor != null)
        {
            position = anchor.position;
            return true;
        }

        float muzzleOffset = held.Attack != null ? held.Attack.MuzzleOffset : 0f;
        position = held.Hover.position + forward * muzzleOffset;
        return true;
    }

    EquippedWeapon PreviewHold(WeaponDefinition definition)
    {
        if (definition == null || definition.Prefab == null)
            return null;
        return definition.Prefab.GetComponent<EquippedWeapon>();
    }

    Vector3 HoldSlotOffset(Slot held)
    {
        if (held != null && held.Attack != null)
            return held.Attack.SlotOffset;
        return HoldSlotOffset(held != null ? held.Definition : null);
    }

    Vector3 HoldSlotOffset(WeaponDefinition definition)
    {
        EquippedWeapon hold = PreviewHold(definition);
        return hold != null ? hold.SlotOffset : Vector3.zero;
    }

    Vector3 HoldVisualEuler(Slot held)
    {
        if (held != null && held.Attack != null)
            return held.Attack.VisualEuler;
        EquippedWeapon hold = PreviewHold(held != null ? held.Definition : null);
        return hold != null ? hold.VisualEuler : Vector3.zero;
    }

    static Vector3 LayoutHoldOffset(Vector3 hold, int slot, int count)
    {
        if (count <= 1)
            return hold;

        float side = Mathf.Max(Mathf.Abs(hold.x), 0.85f) * 1.85f;
        if (count == 2)
        {
            float x = slot == 0 ? side : -side;
            return new Vector3(x, hold.y, hold.z);
        }

        switch (slot)
        {
            case 0:
                return new Vector3(side, hold.y, hold.z);
            case 1:
                return new Vector3(-side, hold.y, hold.z);
            default:
                return new Vector3(0f, hold.y, hold.z - side * 0.9f);
        }
    }

    void KeepWorldScale(Transform hover)
    {
        if (hover == null)
            return;

        Vector3 lossy = transform.lossyScale;
        hover.localScale = new Vector3(
            Inverse(lossy.x),
            Inverse(lossy.y),
            Inverse(lossy.z));
    }

    void CenterAndFit(Transform hover, Transform visual, float targetSize)
    {
        Recenter(hover, visual);
        Bounds bounds = Encapsulate(visual);
        float longest = Longest(bounds.size);
        if (longest > 0.0001f && targetSize > 0.0001f)
            visual.localScale *= targetSize / longest;
        Recenter(hover, visual);
    }

    void Recenter(Transform hover, Transform visual)
    {
        Bounds bounds = Encapsulate(visual);
        if (Longest(bounds.size) < 0.0001f)
            return;
        visual.localPosition -= hover.InverseTransformPoint(bounds.center);
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

    bool TryGetAimForward(int slot, Vector3 origin, Vector3 up, out Vector3 forward)
    {
        forward = default;
        if (_combat == null)
            _combat = GetComponent<PlayerRangeCombat>();
        if (_combat == null || !_combat.TryGetAttackAimPoint(slot, out Vector3 aimPoint))
            return false;

        Vector3 toTarget = Vector3.ProjectOnPlane(aimPoint - origin, up);
        if (toTarget.sqrMagnitude < 0.0001f)
            return false;

        forward = toTarget.normalized;
        return true;
    }

    void SampleBreath(int slot, Vector3 up, Vector3 right, Vector3 forward, bool aiming, out Vector3 pos, out Quaternion rot, out float scale)
    {
        float tau = Time.time * Mathf.PI * 2f * bobSpeed + slot * 1.7f;
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

    void ClearHovers()
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            Slot held = _slots[i];
            if (held.Hover != null)
                Destroy(held.Hover.gameObject);
        }

        _slots.Clear();
    }

    void ResolveDefinitions(List<WeaponDefinition> dest)
    {
        dest.Clear();
        if (_vitals == null)
            _vitals = GetComponent<PlayerVitals>();
        CombatLoadout.CopyClamped(_vitals != null ? _vitals.Weapons : null, dest);
        if (dest.Count == 0 && equipped != null)
            dest.Add(equipped);
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
