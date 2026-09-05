using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Plays the authored dive/land takes on the nested Starbot FBX (Armature skeleton). Those clips
/// must not be retargeted onto the live Player rig — different bind-pose translations crumple the
/// mesh. The Player transform still carries the fall; this only drives the matching cinematic mesh.
/// </summary>
public class PlayerDiveAnimation : MonoBehaviour
{
    [Tooltip("Nested Starbot model. Leave empty to find \"" +
             PlayerDiveDownCapsulePaths.DiveModelChildName + "\" under this object.")]
    [SerializeField] Transform modelRoot;
    [Tooltip("In-air dive. Leave empty to load Starbot_Animation_Dive_Down from Resources.")]
    [SerializeField] AnimationClip diveClip;
    [Tooltip("Land/recover. Leave empty to load Starbot_Animation_Dive_Down_and_Land from Resources.")]
    [SerializeField] AnimationClip landClip;
    [Tooltip("Seconds into the land clip that the character contacts the ground.")]
    [SerializeField, Min(0.05f)] float landGroundContactTime = 0.55f;
    [Tooltip("Move input during this many seconds at the end of the land clip skips recover and starts gameplay.")]
    [SerializeField, Min(0f)] float landSkipWindow = 1.5f;

    Animator _animator;
    PlayableGraph _graph;
    AnimationClipPlayable _playable;
    AnimationClip _activeClip;
    float _clipTime;
    bool _playing;
    bool _landClipActive;

    public Transform ModelRoot
    {
        get
        {
            EnsureWired();
            return modelRoot;
        }
    }

    public bool HasDiveClip
    {
        get
        {
            EnsureWired();
            return diveClip != null;
        }
    }

    public bool HasLandClip
    {
        get
        {
            EnsureWired();
            return landClip != null;
        }
    }

    public float LandLength => HasLandClip ? landClip.length : 0f;
    public float LandGroundContactTime => Mathf.Min(landGroundContactTime, Mathf.Max(0.05f, LandLength));
    public bool IsPlaying => _playing;
    public bool IsFinished => !_playing;

    /// <summary>True after ground contact, while the land clip is in its last skip-window
    /// seconds, so move input can cancel recover and start gameplay.</summary>
    public bool CanSkipLand
    {
        get
        {
            if (!_landClipActive || !_playing || _activeClip == null || landSkipWindow <= 0f)
                return false;
            if (_clipTime < LandGroundContactTime)
                return false;
            return _clipTime >= _activeClip.length - landSkipWindow;
        }
    }

    void Awake() => EnsureWired();
    void OnDestroy() => Stop();

    public void PlayDive() => PlayClip(diveClip, 0f, false);

    public void PlayLand(float startTime = 0f) => PlayClip(landClip, startTime, true);

    public void Tick(float deltaTime)
    {
        if (!_playing || !_graph.IsValid() || !_playable.IsValid() || _activeClip == null)
            return;

        _clipTime += deltaTime;
        if (_clipTime > _activeClip.length)
            _clipTime = _activeClip.length;

        _playable.SetTime(_clipTime);
        _graph.Evaluate();

        if (_landClipActive && _clipTime >= _activeClip.length - 0.01f)
            _playing = false;
    }

    public void Stop()
    {
        _playing = false;
        _landClipActive = false;
        _activeClip = null;
        if (_graph.IsValid())
            _graph.Destroy();
    }

    void PlayClip(AnimationClip clip, float startTime, bool land)
    {
        EnsureWired();
        if (_animator == null || clip == null)
        {
            _playing = false;
            return;
        }

        Stop();
        PrepareAnimator();

        _activeClip = clip;
        _landClipActive = land;
        _clipTime = Mathf.Clamp(startTime, 0f, Mathf.Max(0f, clip.length - 0.05f));
        _graph = PlayableGraph.Create("PlayerDiveAnimation");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Dive", _animator);
        _playable = AnimationClipPlayable.Create(_graph, clip);
        _playable.SetApplyFootIK(false);
        _playable.SetDuration(clip.length);
        _playable.SetTime(_clipTime);
        _playable.SetSpeed(1);
        output.SetSourcePlayable(_playable);
        _graph.Play();
        _graph.Evaluate();
        _playing = true;
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
        if (landClip == null)
            landClip = LoadClip(
                PlayerDiveDownCapsulePaths.ResourcesLandClip,
                PlayerDiveDownCapsulePaths.ResourcesDiveModel);
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
