using UnityEngine;

/// <summary>
/// Drives creature Animator bools shared by locomotion and combat.
/// Expects controller params: IsMoving, IsAttacking, AttackAnimSpeed (idle / Run / Attack).
/// </summary>
[RequireComponent(typeof(Animator))]
public class CreatureAnimator : MonoBehaviour
{
    static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    static readonly int IsAttackingHash = Animator.StringToHash("IsAttacking");
    static readonly int AttackAnimSpeedHash = Animator.StringToHash("AttackAnimSpeed");
    static readonly int Idle1Hash = Animator.StringToHash("Idle1");
    static readonly int Idle2Hash = Animator.StringToHash("Idle2");

    [SerializeField] string attackClipName = "Attack";

    Animator _animator;
    bool _moving;
    bool _attacking;
    float _attackClipLength = -1f;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

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
        if (_moving == moving)
            return;

        _moving = moving;
        if (_animator != null && HasParameter(IsMovingHash))
            _animator.SetBool(IsMovingHash, moving);
    }

    public void SetAttacking(bool attacking)
    {
        if (_attacking == attacking)
            return;

        _attacking = attacking;
        if (_animator != null && HasParameter(IsAttackingHash))
            _animator.SetBool(IsAttackingHash, attacking);
    }

    /// <summary>
    /// Scales Attack so one clip cycle roughly matches 1 / attacksPerSecond.
    /// </summary>
    public void SetAttackRate(float attacksPerSecond)
    {
        if (_animator == null || !HasParameter(AttackAnimSpeedHash))
            return;

        CacheAttackClipLength();
        if (_attackClipLength <= 0.01f || attacksPerSecond <= 0.01f)
        {
            _animator.SetFloat(AttackAnimSpeedHash, 1f);
            return;
        }

        float interval = 1f / attacksPerSecond;
        _animator.SetFloat(AttackAnimSpeedHash, _attackClipLength / interval);
    }

    public void ResetToIdle()
    {
        SetMoving(false);
        SetAttacking(false);
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
        if (_attackClipLength > 0f || _animator == null || _animator.runtimeAnimatorController == null)
            return;

        AnimationClip[] clips = _animator.runtimeAnimatorController.animationClips;
        if (clips == null)
            return;

        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip == null)
                continue;

            if (string.Equals(clip.name, attackClipName, System.StringComparison.OrdinalIgnoreCase))
            {
                _attackClipLength = clip.length;
                return;
            }
        }
    }
}
