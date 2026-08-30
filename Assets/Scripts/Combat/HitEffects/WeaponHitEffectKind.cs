/// <summary>
/// Which on-hit proc a weapon triggers. None for weapons that only deal their shot damage.
/// The matching settings block on <see cref="WeaponDefinition"/> (for example <see cref="WeaponDotSettings"/>)
/// holds that effect's tuning; the other blocks are ignored.
/// </summary>
public enum WeaponHitEffectKind
{
    None,
    Dot,
    ChainJump,
    AreaBlast
}
