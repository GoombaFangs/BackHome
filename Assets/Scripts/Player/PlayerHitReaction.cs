using System.Collections;
using UnityEngine;

/// <summary>
/// Damage flinch: the instant <see cref="PlayerVitals.Damaged"/> fires, plays a short ~0.15s slice
/// (0.5s-0.65s) of Starbot_Animation_dying (the "Hit" Animator state, which reuses the exact same
/// clip as "Dying", entering it already offset ~0.5s in - see PlayerModelSwapTool.WireHitStateIntoController)
/// as a quick reaction, then hands control back to normal locomotion automatically: this script
/// only ever sets the "Hit" bool true/false, it never has to pick which locomotion state to return
/// to - the Animator's own Run/Idle1/Idle2 Any State transitions (guarded with "Hit == false")
/// immediately resume once "Hit" clears. If the same hit kills the player, "Dead" always wins over
/// "Hit" in the Animator (see PlayerDeathUI), so a killing blow's flinch can never fight the death
/// animation. On top of that, a <see cref="hitCooldown"/> (measured from the moment a reaction
/// starts, not from when it ends) blocks any further reaction from starting while it's active - so
/// rapid repeated damage (e.g. several creatures hitting the player within the same second) can't
/// spam/restart the flinch over and over, which looked messy. Damage itself is never blocked by the
/// cooldown, only the flinch animation is.
///
/// Also spawns the "GettingDamageVfx" particle burst (Assets/Resources/Player/VFX/GettingDamageVfx.prefab,
/// loaded via Resources so no manual Inspector wiring is needed) on every single damage instance -
/// unlike the flinch animation, the VFX is never gated by <see cref="hitCooldown"/>, since it's a
/// short one-shot burst on its own and stacking a couple of them when hit rapidly reads fine.
///
/// Lives on the Player prefab itself, alongside PlayerVitals and the Animator.
/// </summary>
[RequireComponent(typeof(PlayerVitals))]
public class PlayerHitReaction : MonoBehaviour
{
    static readonly int HitId = Animator.StringToHash("Hit");

    const string GettingDamageVfxResourcePath = "Player/VFX/GettingDamageVfx";

    [Tooltip("How much of Starbot_Animation_dying to show (starting ~0.5s into the clip - see PlayerModelSwapTool.WireHitStateIntoController) before returning to normal locomotion.")]
    [SerializeField, Min(0.05f)] float hitDuration = 0.15f;

    [Tooltip("Minimum time between two reactions, measured from the start of the previous one. While this is running out, further damage still hurts the player as normal, it just won't retrigger/restart the flinch animation.")]
    [SerializeField, Min(0f)] float hitCooldown = 0.4f;

    [Tooltip("Height above the player's feet (transform.position), along the local planet 'up', that GettingDamageVfx is spawned at.")]
    [SerializeField] float vfxHeight = 1f;

    [Tooltip("How long after spawning GettingDamageVfx gets destroyed. Should comfortably cover the burst's own duration + the longest particle lifetime in it.")]
    [SerializeField, Min(0.1f)] float vfxLifetime = 2f;

    PlayerVitals _vitals;
    Animator _animator;
    Coroutine _routine;
    float _lastReactionStartTime = float.NegativeInfinity;
    GameObject _gettingDamageVfxPrefab;
    bool _loadedGettingDamageVfxPrefab;

    void Awake()
    {
        _vitals = GetComponent<PlayerVitals>();
        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();
    }

    void OnEnable()
    {
        if (_vitals != null)
            _vitals.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (_vitals != null)
            _vitals.Damaged -= OnDamaged;

        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
    }

    void OnDamaged(float amount)
    {
        SpawnGettingDamageVfx();

        if (_animator == null)
            return;

        if (Time.time - _lastReactionStartTime < hitCooldown)
            return; // Still on cooldown from the previous flinch - let the current animation state be.

        _lastReactionStartTime = Time.time;

        if (_routine != null)
            StopCoroutine(_routine);
        _routine = StartCoroutine(HitReactionRoutine());
    }

    IEnumerator HitReactionRoutine()
    {
        _animator.SetBool(HitId, true);
        yield return new WaitForSeconds(hitDuration);
        _animator.SetBool(HitId, false);
        _routine = null;
    }

    void SpawnGettingDamageVfx()
    {
        if (!_loadedGettingDamageVfxPrefab)
        {
            _gettingDamageVfxPrefab = Resources.Load<GameObject>(GettingDamageVfxResourcePath);
            _loadedGettingDamageVfxPrefab = true;
        }

        if (_gettingDamageVfxPrefab == null)
            return;

        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(transform.position)
            : transform.up;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up);
        Vector3 position = transform.position + up * vfxHeight;

        GameObject fx = Instantiate(_gettingDamageVfxPrefab, position, rotation);
        fx.name = _gettingDamageVfxPrefab.name;

        ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            if (!systems[i].isPlaying)
                systems[i].Play(true);
        }

        Destroy(fx, vfxLifetime);
    }
}
