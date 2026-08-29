using UnityEngine;

/// <summary>
/// Attack + hold pose for a weapon prefab. Put <see cref="RangedWeapon"/> on the
/// weapon itself so the player can swap definitions without knowing how each gun fires.
/// </summary>
public abstract class EquippedWeapon : MonoBehaviour
{
    [Header("Hold")]
    [Tooltip("World meters from the character pivot. X = right, Y = up, Z = forward.")]
    [SerializeField] Vector3 slotOffset = new Vector3(0.68f, 1.66f, 0.06f);
    [SerializeField] Vector3 visualEuler = Vector3.zero;
    [Tooltip("Longest world-axis size of the floating weapon.")]
    [SerializeField, Min(0.1f)] float targetSize = 1.05f;
    [Tooltip("How far past the weapon center a shot starts, along the aim direction. Ignored when Muzzle Anchor is set.")]
    [SerializeField, Min(0f)] float muzzleOffset = 0.9f;
    [Tooltip("Exact muzzle point on the weapon model (for example the wand tip). Drag a child transform " +
             "from the weapon's visual here and position it on the model in the Scene view; when set, this " +
             "is used instead of the aim-direction offset above, so it tracks the model exactly at any angle.")]
    [SerializeField] Transform muzzleAnchor;
    [Tooltip("Height above the creature pivot to aim at.")]
    [SerializeField] float aimHeight = 0.7f;

    public Vector3 SlotOffset => slotOffset;
    public Vector3 VisualEuler => visualEuler;
    public float TargetSize => targetSize;
    public float MuzzleOffset => muzzleOffset;
    public Transform MuzzleAnchor => muzzleAnchor;
    public WeaponDefinition Definition { get; private set; }

    public void Bind(WeaponDefinition definition)
    {
        Definition = definition;
    }

    public abstract void Fire(Creature target, float damage, Vector3 muzzle, Transform muzzleParent, Vector3 knockFrom);

    protected void DealHit(Creature target, float damage, Vector3 knockFrom)
    {
        if (target == null || !target.IsAlive || damage <= 0f)
            return;

        target.TakeDamage(damage, knockFrom);
        if (target.IsAlive)
            Definition?.ApplyHitEffect(this, target, damage);
    }

    /// <summary>
    /// Plays this weapon's hit VFX on <paramref name="target"/>. Used for the initial shot and
    /// for follow-up on-hit effects (for example chain jumps) that strike additional creatures.
    /// </summary>
    public void PlayHitEffectVfx(Creature target, Vector3 dir)
    {
        if (target == null)
            return;

        PlayHitVfxCore(target, AimPoint(target), dir);
    }

    protected virtual void PlayHitVfxCore(Creature target, Vector3 position, Vector3 dir)
    {
    }

    /// <summary>Public entry point for on-hit effects (outside this class hierarchy) that need the same aim height as normal shots.</summary>
    public Vector3 GetAimPoint(Creature creature) => AimPoint(creature);

    protected Vector3 AimPoint(Creature creature)
    {
        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(creature.transform.position)
            : creature.transform.up;
        return creature.transform.position + up * aimHeight;
    }

    protected static Vector3 ResolveUp(Vector3 origin, Vector3 dir)
    {
        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(origin)
            : Vector3.up;
        if (Mathf.Abs(Vector3.Dot(dir, up)) <= 0.95f)
            return up;

        Vector3 alt = Vector3.Cross(dir, Vector3.right);
        if (alt.sqrMagnitude < 0.01f)
            alt = Vector3.Cross(dir, Vector3.forward);
        return alt.normalized;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor-only helper: snaps the assigned Muzzle Anchor to the exact geometric center of this
    /// weapon's visible model (the combined bounds of every renderer under it). Right-click this
    /// component's header (or the gear icon) in the Inspector and pick this from the menu — no need
    /// to eyeball local-space math by hand, especially on tilted/curled models.
    /// </summary>
    [ContextMenu("Center Muzzle Anchor On Model")]
    void CenterMuzzleAnchorOnModel()
    {
        if (muzzleAnchor == null)
        {
            Debug.LogWarning($"{name}: assign a Muzzle Anchor transform before centering it.", this);
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"{name}: no renderers found under this weapon to compute a center from.", this);
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        muzzleAnchor.position = bounds.center;
        UnityEditor.EditorUtility.SetDirty(muzzleAnchor);
        Debug.Log($"{name}: centered Muzzle Anchor on model bounds at {bounds.center}.", this);
    }
#endif
}
