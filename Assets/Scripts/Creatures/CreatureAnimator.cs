using UnityEngine;

/// <summary>
/// Drives creature Animator bools shared by locomotion and combat.
/// Expects controller params: IsMoving, IsAttacking, AttackKind, AttackAnimSpeed.
/// States: Idle / Run, plus LightAttack and HeavyAttack when the creature has both.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CreatureAnimator : MonoBehaviour
{
    static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");
    static readonly int AttackAnimSpeedHash = Animator.StringToHash("AttackAnimSpeed");
    static readonly int AttackKindHash = Animator.StringToHash("AttackKind");
    static readonly int Idle1Hash = Animator.StringToHash("Idle1");
    static readonly int Idle2Hash = Animator.StringToHash("Idle2");
    static readonly int RunHash = Animator.StringToHash("Run");
    static readonly int AttackHash = Animator.StringToHash("Attack");
    static readonly int LightAttackHash = Animator.StringToHash("LightAttack");
    static readonly int HeavyAttackHash = Animator.StringToHash("HeavyAttack");
    static readonly int DeathHash = Animator.StringToHash("Death");
    static readonly int DieHash = Animator.StringToHash("Die");

    [SerializeField] string attackClipName = "Attack";

    Animator _animator;
    CreatureAbility _ability;
    bool _moving;
    bool _attacking;
    CreatureAttackKind _attackKind;
    int _hasMovingParam = -1;
    float _attackClipLength = -1f;
    float _lightClipLength = -1f;
    float _heavyClipLength = -1f;
    float _deathClipLength = -1f;
    bool _dying;
    bool _groundDeath;
    float _deathStartTime = -1f;
    bool _heavyLatched;
    int _latchedHeavySerial = -1;
    float _heavyStartTime = -1f;
    Transform _bodyRoot;
    Vector3 _bodyBindLocalPosition;
    Transform[] _bones;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        CacheBody();
        CacheAttackClipLength();
        if (_animator != null && HasParameter(AttackAnimSpeedHash))
            _animator.SetFloat(AttackAnimSpeedHash, 1f);

        // Controllers with both idle takes (Snarlfang) start on one of them at random,
        // then the controller alternates every loop so each pose gets half the time.
        if (_animator != null && _animator.HasState(0, Idle1Hash) && _animator.HasState(0, Idle2Hash))
        {
            bool idle2 = Random.value < 0.5f;
            _animator.Play(idle2 ? Idle2Hash : Idle1Hash, 0, Random.value);
        }
    }

    public void SetMoving(bool moving)
    {
        if (_dying)
            return;
        if (_moving == moving)
            return;

        _moving = moving;
        PushMoving();
        if (_moving)
            TryEnterRun();
    }

    void LateUpdate()
    {
        if (_dying)
        {
            TryEnterDeath();
            if (_groundDeath)
                StickDeathToGround();
            return;
        }

        // The idle states crossfade into each other on exit time. Push the bool again after
        // that transition is chosen, and enter Run or the attack immediately instead of waiting it out.
        PushMoving();
        ResolveAbility();
        if (TickHeavyOpener())
            return;

        if (_attacking)
        {
            TryEnterAttack();
            return;
        }

        if (_moving && !IsAttackPose())
            TryEnterRun();
    }

    void PushMoving()
    {
        if (!CanDriveMoving())
            return;

        _animator.SetBool(IsMovingHash, _moving);
    }

    void TryEnterRun()
    {
        if (!CanDriveMoving() || !_animator.isInitialized || !_animator.HasState(0, RunHash))
            return;
        if (IsInOrEntering(RunHash))
            return;

        _animator.CrossFadeInFixedTime(RunHash, 0.12f, 0, 0f);
    }

    bool IsAttackPose()
    {
        if (!_attacking || _animator == null)
            return false;

        return IsInOrEntering(AttackHash)
            || IsInOrEntering(LightAttackHash)
            || IsInOrEntering(HeavyAttackHash);
    }

    bool IsInOrEntering(int stateHash)
    {
        if (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash == stateHash)
            return true;
        if (!_animator.IsInTransition(0))
            return false;

        return _animator.GetNextAnimatorStateInfo(0).shortNameHash == stateHash;
    }

    bool CanDriveMoving()
    {
        if (_animator == null || _animator.runtimeAnimatorController == null)
            return false;
        if (_hasMovingParam >= 0)
            return _hasMovingParam == 1;
        if (_animator.parameterCount == 0)
            return false;

        _hasMovingParam = HasParameter(IsMovingHash) ? 1 : 0;
        return _hasMovingParam == 1;
    }

    public void SetAttacking(bool attacking)
    {
        SetAttacking(attacking, CreatureAttackKind.Light);
    }

    public void SetAttacking(bool attacking, CreatureAttackKind kind)
    {
        if (_dying)
            return;

        // One heavy clip per stealth exit. Later SetAttacking calls must not restart it.
        if (_heavyLatched)
        {
            _attacking = attacking;
            if (_animator != null && HasParameter(IsAttackingHash))
                _animator.SetBool(IsAttackingHash, attacking);
            return;
        }

        bool kindChanged = _attackKind != kind;
        _attackKind = kind;

        if (_animator != null && HasParameter(AttackKindHash))
            _animator.SetInteger(AttackKindHash, (int)kind);

        if (_attacking == attacking)
        {
            if (attacking && kindChanged)
                TryEnterAttack();
            return;
        }

        _attacking = attacking;
        if (_animator != null && HasParameter(IsAttackingHash))
            _animator.SetBool(IsAttackingHash, attacking);

        if (_attacking)
            TryEnterAttack();
    }

    void TryEnterAttack()
    {
        if (_animator == null || !_animator.isInitialized)
            return;

        int hash = _attackKind == CreatureAttackKind.Heavy ? HeavyAttackHash : LightAttackHash;
        if (!_animator.HasState(0, hash))
        {
            // Grimling only has the shared Attack state, and its controller transitions on IsAttacking.
            if (hash != AttackHash && _animator.HasState(0, AttackHash) && _attackKind == CreatureAttackKind.Light)
                return;
            if (!_animator.HasState(0, AttackHash))
                return;
            hash = AttackHash;
        }

        if (IsInOrEntering(hash))
            return;

        _animator.CrossFadeInFixedTime(hash, 0.08f, 0, 0f);
    }

    void ResolveAbility()
    {
        if (_ability == null)
            _ability = GetComponent<CreatureAbility>() ?? GetComponentInParent<CreatureAbility>();
    }

    /// <summary>
    /// Plays the stealth-exit heavy clip once, then hands the swing to light attacks.
    /// Returns true when this frame was consumed by that opener.
    /// </summary>
    bool TickHeavyOpener()
    {
        bool heavy = _ability != null && _ability.IsHeavyAttack;
        if (!heavy)
        {
            if (!_heavyLatched)
                return false;

            _heavyLatched = false;
            _latchedHeavySerial = -1;
            _heavyStartTime = -1f;
            BeginLightAttacks();
            return true;
        }

        int serial = _ability.HeavySwingSerial;
        if (!_heavyLatched || _latchedHeavySerial != serial)
        {
            _heavyLatched = true;
            _latchedHeavySerial = serial;
            _heavyStartTime = -1f;
            _attackKind = CreatureAttackKind.Heavy;
            if (_animator != null && HasParameter(AttackKindHash))
                _animator.SetInteger(AttackKindHash, (int)CreatureAttackKind.Heavy);
            if (_animator != null && HasParameter(AttackAnimSpeedHash))
                _animator.SetFloat(AttackAnimSpeedHash, 1f);
            if (_attacking)
                BeginHeavyClip();
            return true;
        }

        if (_heavyStartTime < 0f)
        {
            if (_attacking)
                BeginHeavyClip();
            return true;
        }

        if (!HeavyOpenerDone())
            return true;

        _heavyLatched = false;
        _latchedHeavySerial = -1;
        _heavyStartTime = -1f;
        _ability.CompleteHeavySwing();
        BeginLightAttacks();
        return true;
    }

    void BeginHeavyClip()
    {
        _heavyStartTime = Time.time;
        if (_animator != null && HasParameter(AttackAnimSpeedHash))
            _animator.SetFloat(AttackAnimSpeedHash, 1f);
        CrossFadeAttack(HeavyAttackHash, restart: true);
    }

    void BeginLightAttacks()
    {
        _attackKind = CreatureAttackKind.Light;
        if (_animator != null && HasParameter(AttackKindHash))
            _animator.SetInteger(AttackKindHash, (int)CreatureAttackKind.Light);
        if (_animator != null && HasParameter(AttackAnimSpeedHash))
            _animator.SetFloat(AttackAnimSpeedHash, 1f);
        if (_attacking)
            CrossFadeAttack(LightAttackHash);
    }

    bool HeavyOpenerDone()
    {
        float length = CacheClip(ref _heavyClipLength, "HeavyAttack");
        if (_heavyStartTime >= 0f && length > 0.05f && Time.time - _heavyStartTime >= length)
            return true;

        if (_animator == null || !_animator.isInitialized)
            return false;

        AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash == HeavyAttackHash && state.normalizedTime >= 0.98f && !_animator.IsInTransition(0))
            return true;

        // Leaving the clip (a flinch, a step back) spends the exit strike. It must not start again.
        return Time.time - _heavyStartTime > 0.2f && !IsInOrEntering(HeavyAttackHash);
    }

    void CrossFadeAttack(int hash, bool restart = false)
    {
        if (_animator == null || !_animator.isInitialized || !_animator.HasState(0, hash))
            return;
        if (!restart && IsInOrEntering(hash))
            return;

        _animator.CrossFadeInFixedTime(hash, 0.08f, 0, 0f);
    }

    /// <summary>
    /// Scales the active attack clip so one cycle roughly matches 1 / attacksPerSecond.
    /// </summary>
    public void SetAttackRate(float attacksPerSecond, CreatureAttackKind kind = CreatureAttackKind.Light)
    {
        if (_animator == null || !HasParameter(AttackAnimSpeedHash))
            return;

        // Snarlfang's light and heavy clips stay at authored speed so each swing is readable.
        // Hit rate still comes from CreatureStats. Grimling keeps the scaled Attack clip.
        if (_animator.HasState(0, LightAttackHash) || _animator.HasState(0, HeavyAttackHash))
        {
            _animator.SetFloat(AttackAnimSpeedHash, 1f);
            return;
        }

        float clipLength = ClipLength(kind);
        if (clipLength <= 0.01f || attacksPerSecond <= 0.01f)
        {
            _animator.SetFloat(AttackAnimSpeedHash, 1f);
            return;
        }

        float interval = 1f / attacksPerSecond;
        _animator.SetFloat(AttackAnimSpeedHash, clipLength / interval);
    }

    public void ResetToIdle()
    {
        if (_dying)
            return;

        SetMoving(false);
        SetAttacking(false);
    }

    /// <summary>
    /// Plays the Death clip once. Returns its length in seconds, or 0 when this controller has no Death state.
    /// </summary>
    public float PlayDeath()
    {
        _dying = true;
        _moving = false;
        _attacking = false;

        if (_animator == null || !_animator.HasState(0, DeathHash))
            return 0f;

        _groundDeath = true;
        _deathStartTime = -1f;
        if (HasParameter(DieHash))
            _animator.ResetTrigger(DieHash);
        TryEnterDeath();

        float length = CacheClip(ref _deathClipLength, "Death");
        return length > 0.05f ? length : 0f;
    }

    void TryEnterDeath()
    {
        if (_animator == null || !_animator.isInitialized || !_animator.HasState(0, DeathHash))
            return;
        if (IsInOrEntering(DeathHash))
            return;
        // A second crossfade would restart the clip from the first frame.
        if (_deathStartTime >= 0f)
            return;

        _deathStartTime = Time.time;
        _animator.CrossFadeInFixedTime(DeathHash, 0.05f, 0, 0f);
    }

    void CacheBody()
    {
        Transform[] transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name != "target_character")
                continue;

            _bodyRoot = transforms[i];
            _bodyBindLocalPosition = _bodyRoot.localPosition;
            break;
        }

        if (_bodyRoot == null)
            return;

        Transform[] bones = _bodyRoot.GetComponentsInChildren<Transform>(true);
        int count = 0;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != _bodyRoot)
                count++;
        }

        _bones = new Transform[count];
        int write = 0;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == _bodyRoot)
                continue;

            _bones[write] = bones[i];
            write++;
        }
    }

    /// <summary>
    /// The death clip keeps the hips near standing height, so the fallen body floats.
    /// Drop the skeleton until its lowest bone sits on the ground.
    /// </summary>
    void StickDeathToGround()
    {
        if (_bodyRoot == null || _bones == null || _bones.Length == 0)
            return;

        _bodyRoot.localPosition = _bodyBindLocalPosition;

        Vector3 up = transform.up;
        Vector3 ground = transform.position;
        float lowest = float.PositiveInfinity;
        for (int i = 0; i < _bones.Length; i++)
        {
            Transform bone = _bones[i];
            if (bone == null)
                continue;

            float height = Vector3.Dot(bone.position - ground, up);
            if (height < lowest)
                lowest = height;
        }

        // A wild sample would throw the body off the planet. Standing hip height is about 1.5m.
        if (float.IsPositiveInfinity(lowest) || lowest < -2f || lowest > 4f)
            return;

        _bodyRoot.position -= up * lowest;
    }

    bool HasParameter(int hash)
    {
        if (_animator == null)
            return false;

        AnimatorControllerParameter[] parameters = _animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == hash)
                return true;
        }

        return false;
    }

    void CacheAttackClipLength()
    {
        ClipLength(CreatureAttackKind.Light);
    }

    float ClipLength(CreatureAttackKind kind)
    {
        if (kind == CreatureAttackKind.Heavy)
            return CacheClip(ref _heavyClipLength, "HeavyAttack");

        float light = CacheClip(ref _lightClipLength, "LightAttack");
        if (light > 0.01f)
            return light;

        return CacheClip(ref _attackClipLength, attackClipName);
    }

    float CacheClip(ref float cached, string clipName)
    {
        if (cached > 0f || _animator == null || _animator.runtimeAnimatorController == null)
            return Mathf.Max(0f, cached);

        AnimationClip[] clips = _animator.runtimeAnimatorController.animationClips;
        if (clips == null)
            return 0f;

        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null)
                continue;

            if (string.Equals(clip.name, clipName, System.StringComparison.OrdinalIgnoreCase))
            {
                cached = clip.length;
                return cached;
            }
        }

        cached = 0f;
        return 0f;
    }
}
