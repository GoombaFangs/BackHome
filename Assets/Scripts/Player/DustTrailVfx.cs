using UnityEngine;

/// <summary>
/// Gates the DustTrail's emission to only happen while actually running - every other particle
/// setting (lifetime, size, shape, mesh, material, forces, curves, ...) is left alone, authored
/// directly on the ParticleSystem/ParticleSystemRenderer right here in Player.prefab and tunable
/// by hand in the Inspector like any normal particle system.
///
/// If the parent has a <see cref="PlanetWalker"/> or <see cref="TouchController"/>, gating uses
/// their <c>MotionAmount</c> (0..1, clamped to 1 exactly at their own run threshold) - this is
/// exact, not a guess, and works correctly both on flat scenes and the spherical planet. Without
/// either, it falls back to an Animator "MotionSpeed" float if present, or just always emits
/// (whatever's authored on the Emission module runs as-is) so it's still usable on other objects.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class DustTrailVfx : MonoBehaviour
{
    [Header("Run Gate")]
    [Tooltip("If on, only emits while running (see class summary for how that's detected). If " +
        "off, always emits - whatever's authored on the Emission module runs as-is.")]
    [SerializeField] bool onlyEmitWhileRunning = true;
    [Tooltip("Threshold against PlanetWalker/TouchController's MotionAmount (which clamps to 1 " +
        "exactly at their own run threshold) or, lacking those, the Animator's \"MotionSpeed\".")]
    [SerializeField, Range(0f, 1f)] float runThreshold = 0.9f;

    ParticleSystem _ps;
    ParticleSystem.EmissionModule _emission;
    PlanetWalker _planetWalker;
    TouchController _touchController;
    Animator _animator;
    int _animIDMotionSpeed;
    bool _running;

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        _emission = _ps.emission;
        _planetWalker = GetComponentInParent<PlanetWalker>();
        _touchController = GetComponentInParent<TouchController>();
        _animator = GetComponentInParent<Animator>();
        _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        SetRunning(!onlyEmitWhileRunning || !HasRunSignal());
    }

    void Update()
    {
        bool running = !onlyEmitWhileRunning || !HasRunSignal() || GetMotionAmount() >= runThreshold;
        if (running != _running)
            SetRunning(running);
    }

    bool HasRunSignal() => _planetWalker != null || _touchController != null || _animator != null;

    float GetMotionAmount()
    {
        // PlanetWalker disables TouchController (and vice versa) depending on scene, so at most
        // one of these is actually driving movement at a time - prefer whichever is enabled.
        if (_planetWalker != null && _planetWalker.isActiveAndEnabled)
            return _planetWalker.MotionAmount;
        if (_touchController != null && _touchController.isActiveAndEnabled)
            return _touchController.MotionAmount;
        if (_animator != null)
            return _animator.GetFloat(_animIDMotionSpeed);
        return 1f;
    }

    void SetRunning(bool running)
    {
        _running = running;
        _emission.enabled = running;
    }
}
