using UnityEngine;

/// <summary>
/// Pins this transform to one humanoid foot bone so its DustTrailVfx/ParticleSystem trails from
/// that specific foot instead of the character's center - used to give running two separate dust
/// kicks, one per leg (see DustTrailFeetSetup, the editor tool that sets this up on
/// "DustTrail_LeftFoot"/"DustTrail_RightFoot" under Player.prefab).
///
/// Deliberately doesn't parent under the bone itself (humanoid rigs can have odd bone scale/roll
/// baked in from retargeting, which would distort the particle system) and pins height to the
/// up-reference transform's ground level rather than the bone's raw Y, so a foot swinging up
/// mid-stride doesn't leave the dust trailing through the air - only its left/right and
/// forward/back sway is followed, same as the reference video's "parent to the feet" approach but
/// without dust floating up during the leg's swing phase.
/// </summary>
public class DustTrailFootFollower : MonoBehaviour
{
    [SerializeField] HumanBodyBones foot = HumanBodyBones.LeftFoot;
    [Tooltip("Extra local offset (relative to the up-reference transform, i.e. the character root) " +
        "added after pinning to the foot's ground-level position.")]
    [SerializeField] Vector3 localOffset = Vector3.zero;

    Transform _footBone;
    Transform _upReference;

    public void SetFoot(HumanBodyBones value) => foot = value;

    void Awake()
    {
        Animator animator = GetComponentInParent<Animator>();
        _upReference = animator != null ? animator.transform : transform.parent;
        if (animator != null && animator.isHuman)
            _footBone = animator.GetBoneTransform(foot);
    }

    void LateUpdate()
    {
        if (_footBone == null || _upReference == null)
            return;

        Vector3 local = _upReference.InverseTransformPoint(_footBone.position);
        local.y = 0f; // stay at ground level instead of following the foot's swing height
        transform.SetPositionAndRotation(_upReference.TransformPoint(local + localOffset), _upReference.rotation);
    }
}
