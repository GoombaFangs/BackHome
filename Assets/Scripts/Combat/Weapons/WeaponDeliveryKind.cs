/// <summary>
/// How a <see cref="RangedWeapon"/> shot actually travels from muzzle to target.
/// </summary>
public enum WeaponDeliveryKind
{
    /// <summary>Instant beam — muzzle and hit VFX play immediately, no travel time.</summary>
    Hitscan,
    /// <summary>An actual bullet GameObject travels to the target (see <see cref="WeaponProjectileSettings"/>).</summary>
    Projectile
}
