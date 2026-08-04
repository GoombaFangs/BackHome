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

    [SerializeField] string attackClipName = "Attack";

    Animator _animator;
    bool _moving;
    bool _attacking;
    float _attackClipLength = -1f;

    public bool IsMoving => _moving;
    public bool IsAttacking => _attacking;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        CacheAttackClipLength();
        if (_animator != null)
            _animator.SetFloat(AttackAnimSpeedHash, 1f);
    }

    public void SetMoving(bool moving)
    {
        if (_moving == moving)
            return;

        _moving = moving;
        if (_animator != null)
            _animator.SetBool(IsMovingHash, moving);
    }

    public void SetAttacking(bool attacking)
    {
        if (_attacking == attacking)
            return;

        _attacking = attacking;
        if (_animator != null)
            _animator.SetBool(IsAttackingHash, attacking);
    }

    /// <summary>
    /// Scales Attack so one clip cycle roughly matches 1 / attacksPerSecond.
    /// </summary>
    public void SetAttackRate(float attacksPerSecond)
    {
        if (_animator == null)
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
