using StarterAssets;
using UnityEngine;

/// <summary>
/// Locks the live Player during the crash cinematic (see PlayerCrashIntro). Animation plays on
/// the nested dive FBX via PlayerDiveAnimation — not on this Animator, whose rig does not match
/// those clips.
/// </summary>
[DefaultExecutionOrder(-200)]
public class PlayerLandIntro : MonoBehaviour
{
    Animator _animator;
    RuntimeAnimatorController _controller;
    PlanetWalker _planetWalker;
    TouchController _touchController;
    CharacterController _characterController;
    PlayerRangeCombat _combat;
    PlayerVitals _vitals;
    VitalsBars _vitalsBars;
    FloatingWeapon _floatingWeapon;
    StarterAssetsInputs _input;
    bool _cinematic;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();
        _planetWalker = GetComponent<PlanetWalker>();
        _touchController = GetComponent<TouchController>();
        _characterController = GetComponent<CharacterController>();
        _combat = GetComponent<PlayerRangeCombat>();
        _vitals = GetComponent<PlayerVitals>();
        _vitalsBars = GetComponent<VitalsBars>();
        _floatingWeapon = GetComponent<FloatingWeapon>();
        _input = GetComponent<StarterAssetsInputs>();
    }

    /// <summary>True when the player is pushing the stick / WASD. Used to skip land recover
    /// once <see cref="PlayerDiveAnimation.CanSkipLand"/> opens.</summary>
    public bool WantsMove => _input != null && _input.move.sqrMagnitude > 0.0025f;

    /// <summary>Locks locomotion and hides gameplay chrome before the first fall frame.</summary>
    public void BeginCinematic()
    {
        _cinematic = true;
        SetLocomotionLocked(true);
        ZeroMoveInput();

        if (_animator != null)
        {
            if (_controller == null)
                _controller = _animator.runtimeAnimatorController;
            _animator.enabled = false;
        }

        if (_characterController != null)
            _characterController.enabled = false;
        if (_combat != null)
            _combat.HideRange();
        if (_vitals != null)
            _vitals.SetInvulnerable(true);
        if (_vitalsBars != null)
            _vitalsBars.SetHidden(true);
        if (_floatingWeapon != null)
            _floatingWeapon.SetVisible(false);
    }

    /// <summary>Restores the gameplay AnimatorController and unlocks walking.</summary>
    public void EndCinematic()
    {
        if (!_cinematic)
            return;
        _cinematic = false;

        if (_animator != null)
        {
            _animator.runtimeAnimatorController = _controller;
            _animator.enabled = true;
            if (WantsMove)
                _animator.SetBool("Moving", true);
        }

        if (_planetWalker != null)
            _planetWalker.EnsureWalkingOnPlanet();

        if (_combat != null)
            _combat.enabled = true;
        if (_vitals != null)
            _vitals.SetInvulnerable(false);
        if (_vitalsBars != null)
            _vitalsBars.SetHidden(false);
        if (_floatingWeapon != null)
            _floatingWeapon.SetVisible(true);

        SetLocomotionLocked(false);
    }

    void SetLocomotionLocked(bool locked)
    {
        if (_planetWalker != null)
            _planetWalker.LockLocomotion = locked;
        if (_touchController != null)
            _touchController.LockLocomotion = locked;
    }

    void ZeroMoveInput()
    {
        if (_input != null)
            _input.move = Vector2.zero;
    }
}
