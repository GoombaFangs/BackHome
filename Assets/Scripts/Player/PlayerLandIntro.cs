using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays <c>Starbot_Animation_Dive_Down_and_Land</c> on the real Player after the crash cinematic
/// hides the capsule. The capsule only plays Dive_Down; this is the land/recover so the handoff
/// doesn't swap clips on two different rigs mid-fall.
///
/// Uses a PlayableGraph (same reason as PlayerDiveAnimation) and a remapped in-place clip
/// (Armature → target_character, hips translation stripped) so Generic bone paths actually bind
/// to the live Player skeleton.
/// </summary>
public class PlayerLandIntro : MonoBehaviour
{
    [Tooltip("Land clip remapped onto the Player rig. Leave empty to load from Resources.")]
    [SerializeField] AnimationClip landClip;
    [Tooltip("Skip the aerial drop at the start of Dive_Down_and_Land (hips fall from ~10 to ~0.7 " +
             "in the first 0.5s). The capsule already did that dive.")]
    [SerializeField, Min(0f)] float startTime = 0.5f;

    Animator _animator;
    RuntimeAnimatorController _controller;
    PlayableGraph _graph;
    AnimationClipPlayable _playable;
    PlanetWalker _planetWalker;
    TouchController _touchController;
    bool _playing;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();
        _planetWalker = GetComponent<PlanetWalker>();
        _touchController = GetComponent<TouchController>();
        if (landClip == null)
            landClip = Resources.Load<AnimationClip>(PlayerDiveDownCapsulePaths.ResourcesLandInPlaceClip);
    }

    void OnDestroy() => StopGraph();

    /// <summary>Starts the land clip and locks locomotion until it finishes.</summary>
    public void Play()
    {
        if (_playing)
            return;
        if (landClip == null)
            landClip = Resources.Load<AnimationClip>(PlayerDiveDownCapsulePaths.ResourcesLandInPlaceClip);
        if (_animator == null || landClip == null)
        {
            Debug.LogWarning("PlayerLandIntro: no Animator or land clip - skipping land pose.", this);
            return;
        }

        StartCoroutine(PlayRoutine());
    }

    IEnumerator PlayRoutine()
    {
        _playing = true;
        LockLocomotion(true);

        StarterAssetsInputs input = GetComponent<StarterAssetsInputs>();
        if (input != null)
            input.move = Vector2.zero;

        _controller = _animator.runtimeAnimatorController;
        _animator.runtimeAnimatorController = null;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.enabled = true;

        float time = Mathf.Clamp(startTime, 0f, Mathf.Max(0f, landClip.length - 0.05f));
        _graph = PlayableGraph.Create("PlayerLandIntro");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Land", _animator);
        _playable = AnimationClipPlayable.Create(_graph, landClip);
        _playable.SetApplyFootIK(false);
        _playable.SetDuration(landClip.length);
        _playable.SetTime(time);
        _playable.SetSpeed(1);
        output.SetSourcePlayable(_playable);
        _graph.Play();
        _graph.Evaluate();

        while (time < landClip.length - 0.01f)
        {
            time += Time.deltaTime;
            if (time > landClip.length)
                time = landClip.length;
            if (input != null)
                input.move = Vector2.zero;
            _playable.SetTime(time);
            _graph.Evaluate();
            yield return null;
        }

        StopGraph();
        if (_animator != null)
            _animator.runtimeAnimatorController = _controller;
        LockLocomotion(false);
        _playing = false;
    }

    void LockLocomotion(bool locked)
    {
        if (_planetWalker != null)
            _planetWalker.LockLocomotion = locked;
        if (_touchController != null)
            _touchController.LockLocomotion = locked;
    }

    void StopGraph()
    {
        if (_graph.IsValid())
            _graph.Destroy();
    }
}
