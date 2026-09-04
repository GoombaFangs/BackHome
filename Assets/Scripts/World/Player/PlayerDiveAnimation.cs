using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays <c>Starbot_Animation_Dive_Down</c> on the nested Starbot inside PlayerDiveDownCapsule
/// for the whole crash fall, and holds the last dive pose until the capsule is hidden.
/// The land clip plays on the real Player (see PlayerLandIntro), not here.
/// </summary>
public class PlayerDiveAnimation : MonoBehaviour
{
    [Tooltip("Nested Starbot model. Leave empty to find \"" +
             PlayerDiveDownCapsulePaths.DiveModelChildName + "\" under this object.")]
    [SerializeField] Transform modelRoot;
    [Tooltip("In-air dive. Leave empty to load Starbot_Animation_Dive_Down from Resources.")]
    [SerializeField] AnimationClip diveClip;

    Animator _animator;
    PlayableGraph _graph;
    AnimationClipPlayable _playable;
    float _clipTime;
    bool _playing;

    public bool IsPlaying => _playing;

    void Awake() => EnsureWired();
    void OnDestroy() => Stop();

    public void Play()
    {
        EnsureWired();
        if (_animator == null || diveClip == null)
        {
            Debug.LogWarning(
                "PlayerDiveAnimation: no model Animator or Dive_Down clip on " + name + ".",
                this);
            return;
        }

        Stop();
        PrepareAnimator();

        _clipTime = 0f;
        _graph = PlayableGraph.Create("PlayerDiveAnimation");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Dive", _animator);
        _playable = AnimationClipPlayable.Create(_graph, diveClip);
        _playable.SetApplyFootIK(false);
        _playable.SetDuration(diveClip.length);
        _playable.SetTime(0);
        _playable.SetSpeed(1);
        output.SetSourcePlayable(_playable);
        _graph.Play();
        _graph.Evaluate();
        _playing = true;
    }

    public void Tick(float deltaTime)
    {
        if (!_playing || !_graph.IsValid() || !_playable.IsValid() || diveClip == null)
            return;

        _clipTime += deltaTime;
        if (_clipTime > diveClip.length)
            _clipTime = diveClip.length;

        _playable.SetTime(_clipTime);
        _graph.Evaluate();
    }

    public void Stop()
    {
        _playing = false;
        if (_graph.IsValid())
            _graph.Destroy();
    }

    void PrepareAnimator()
    {
        if (modelRoot != null)
            modelRoot.gameObject.SetActive(true);

        _animator.gameObject.SetActive(true);
        _animator.runtimeAnimatorController = null;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.updateMode = AnimatorUpdateMode.Normal;
        _animator.enabled = true;
        _animator.fireEvents = false;
        _animator.speed = 1f;

        if (_animator.avatar == null || !_animator.avatar.isValid)
        {
            Avatar avatar = LoadAvatar();
            if (avatar != null)
                _animator.avatar = avatar;
        }

        SkinnedMeshRenderer[] meshes = _animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < meshes.Length; i++)
        {
            meshes[i].enabled = true;
            meshes[i].updateWhenOffscreen = true;
        }
    }

    void EnsureWired()
    {
        if (modelRoot == null)
            modelRoot = transform.Find(PlayerDiveDownCapsulePaths.DiveModelChildName);

        if (_animator == null && modelRoot != null)
        {
            _animator = modelRoot.GetComponent<Animator>();
            if (_animator == null)
                _animator = modelRoot.GetComponentInChildren<Animator>(true);
            if (_animator == null)
                _animator = modelRoot.gameObject.AddComponent<Animator>();
        }

        if (_animator == null)
        {
            SkinnedMeshRenderer skin = GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (skin != null)
            {
                _animator = skin.GetComponentInParent<Animator>();
                if (_animator == null)
                    _animator = skin.gameObject.AddComponent<Animator>();
                if (modelRoot == null)
                    modelRoot = _animator.transform;
            }
        }

        if (diveClip == null)
            diveClip = LoadClip(
                PlayerDiveDownCapsulePaths.ResourcesDiveClip,
                PlayerDiveDownCapsulePaths.ResourcesDiveDownModel);
    }

    static AnimationClip LoadClip(string standaloneResource, string fbxResource)
    {
        AnimationClip standalone = Resources.Load<AnimationClip>(standaloneResource);
        if (standalone != null)
            return standalone;

        UnityEngine.Object[] assets = Resources.LoadAll(fbxResource);
        if (assets == null)
            return null;

        AnimationClip named = null;
        AnimationClip first = null;
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip == null)
                continue;
            if (clip.name.IndexOf("__preview__", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;
            if (first == null)
                first = clip;
            if (clip.name.IndexOf("Dive", StringComparison.OrdinalIgnoreCase) >= 0)
                named = clip;
        }

        return named != null ? named : first;
    }

    static Avatar LoadAvatar()
    {
        UnityEngine.Object[] assets = Resources.LoadAll(PlayerDiveDownCapsulePaths.ResourcesDiveModel);
        if (assets == null)
            return null;

        for (int i = 0; i < assets.Length; i++)
        {
            Avatar avatar = assets[i] as Avatar;
            if (avatar != null && avatar.isValid)
                return avatar;
        }

        return null;
    }
}
