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
    [Tooltip("How far past the weapon center a shot starts, along the aim direction.")]
    [SerializeField, Min(0f)] float muzzleOffset = 0.9f;
    [Tooltip("Height above the creature pivot to aim at.")]
    [SerializeField] float aimHeight = 0.7f;

    public Vector3 SlotOffset => slotOffset;
    public Vector3 VisualEuler => visualEuler;
    public float TargetSize => targetSize;
    public float MuzzleOffset => muzzleOffset;
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
            Definition?.ApplyHitEffect(target, damage);
    }

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
}
