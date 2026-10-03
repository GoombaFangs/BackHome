using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Runs the special ability on <see cref="CreatureStats"/>.
/// Stealth lasts up to Ability Duration, or ends sooner on reaching heavy-attack range.
/// Either exit plays one heavy swing, then light attacks.
/// </summary>
[DefaultExecutionOrder(20)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Creature))]
public class CreatureAbility : MonoBehaviour
{
    const float FadeDuration = 0.12f;

    static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

    struct BodySlot
    {
        public Renderer Renderer;
        public Material[] Materials;
        public ShadowCastingMode Shadows;
    }

    Creature _creature;
    CreatureChase _chase;
    BodySlot[] _slots;
    bool _stealthed;
    float _stealthElapsed;
    bool _fading;
    float _stealthOpacity = 0.22f;
    float _fade;
    float _fadeTarget;
    bool _heavyOpener;
    int _heavySerial;

    /// <summary>True while the creature is inside the stealth window.
    /// Attack damage and the attack-range stop stay off until this clears.</summary>
    public bool BlocksAttack => _stealthed;

    /// <summary>False during stealth, so player weapons will not acquire or hit this creature.</summary>
    public bool IsTargetable => !_stealthed;

    /// <summary>True while the stealth-exit heavy swing is still playing.</summary>
    public bool IsHeavyAttack => _heavyOpener;

    /// <summary>Increments each time stealth ends into a new heavy swing.</summary>
    public int HeavySwingSerial => _heavySerial;

    void Awake()
    {
        _creature = GetComponent<Creature>();
        _chase = GetComponent<CreatureChase>();
        PrepareBody();
    }

    void OnEnable()
    {
        if (_chase != null)
            _chase.PlayerSpotted += OnPlayerSpotted;
    }

    void OnDisable()
    {
        if (_chase != null)
            _chase.PlayerSpotted -= OnPlayerSpotted;

        _stealthed = false;
        _stealthElapsed = 0f;
        _fading = false;
        _fade = 0f;
        _fadeTarget = 0f;
        _heavyOpener = false;
        ApplyOpacity(0f);
    }

    void OnDestroy()
    {
        if (_slots == null)
            return;

        for (int i = 0; i < _slots.Length; i++)
        {
            Material[] materials = _slots[i].Materials;
            if (materials == null)
                continue;

            for (int m = 0; m < materials.Length; m++)
            {
                if (materials[m] != null)
                    Destroy(materials[m]);
            }
        }
    }

    void Update()
    {
        bool frozen = _creature != null && _creature.IsFrozen;

        if (_stealthed)
        {
            bool dead = _creature == null || !_creature.IsAlive;
            bool lostPlayer = _chase == null || !_chase.IsAggroed;
            if (!frozen)
                _stealthElapsed += Time.deltaTime;

            if (dead || (!frozen && lostPlayer))
                EndStealth(startHeavy: false);
            else if (!frozen && (HasReachedPlayer() || StealthTimedOut()))
                EndStealth(startHeavy: true);
        }

        TickFade();
    }

    void OnPlayerSpotted()
    {
        if (_stealthed || _creature == null || !_creature.IsAlive || !_creature.HasStats)
            return;

        CreatureStats stats = _creature.Stats;
        if (stats.SpecialAbility != CreatureSpecialAbility.Stealth)
            return;

        _heavyOpener = false;
        _stealthElapsed = 0f;
        _stealthOpacity = stats.StealthOpacity;
        _stealthed = true;
        _fadeTarget = 1f;
        _fading = true;
    }

    bool HasReachedPlayer()
    {
        if (_chase == null || _creature == null || !_creature.HasStats)
            return false;

        CreatureStats stats = _creature.Stats;
        float reach = stats.SpecialAbility == CreatureSpecialAbility.Stealth
            ? stats.HeavyAttackRange
            : stats.AttackRange;
        return reach > 0f && _chase.IsPlayerWithin(reach);
    }

    bool StealthTimedOut()
    {
        if (_creature == null || !_creature.HasStats)
            return false;

        float duration = _creature.Stats.AbilityDuration;
        return duration > 0f && _stealthElapsed >= duration;
    }

    void EndStealth(bool startHeavy)
    {
        _stealthed = false;
        _fadeTarget = 0f;
        _fading = true;

        if (!startHeavy || _creature == null || !_creature.HasStats
            || _creature.Stats.SpecialAbility != CreatureSpecialAbility.Stealth)
        {
            _heavyOpener = false;
            return;
        }

        _heavyOpener = true;
        _heavySerial++;
    }

    /// <summary>Called when the heavy-attack clip has finished its single play.</summary>
    public void CompleteHeavySwing()
    {
        _heavyOpener = false;
    }

    void TickFade()
    {
        if (!_fading)
            return;

        float step = FadeDuration <= 0f ? 1f : Time.deltaTime / FadeDuration;
        _fade = Mathf.MoveTowards(_fade, _fadeTarget, step);
        ApplyOpacity(_fade);

        if (!_stealthed && _fade <= 0.0001f)
        {
            _fade = 0f;
            _fading = false;
            ApplyOpacity(0f);
        }
    }

    void PrepareBody()
    {
        Renderer[] found = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        for (int i = 0; i < found.Length; i++)
        {
            if (IsBodyRenderer(found[i]))
                count++;
        }

        _slots = new BodySlot[count];
        int write = 0;
        for (int i = 0; i < found.Length; i++)
        {
            Renderer renderer = found[i];
            if (!IsBodyRenderer(renderer))
                continue;

            Material[] shared = renderer.sharedMaterials;
            var instances = new Material[shared.Length];
            for (int m = 0; m < shared.Length; m++)
            {
                if (shared[m] == null)
                    continue;

                instances[m] = new Material(shared[m])
                {
                    name = shared[m].name + " (Creature)"
                };
            }

            renderer.sharedMaterials = instances;
            _slots[write] = new BodySlot
            {
                Renderer = renderer,
                Materials = instances,
                Shadows = renderer.shadowCastingMode
            };
            write++;
        }
    }

    void ApplyOpacity(float fade)
    {
        if (_slots == null)
            return;

        fade = Mathf.Clamp01(fade);
        bool ghost = fade > 0.001f;
        float opacity = ghost ? Mathf.Lerp(1f, _stealthOpacity, fade) : 1f;

        for (int i = 0; i < _slots.Length; i++)
        {
            Renderer renderer = _slots[i].Renderer;
            if (renderer != null)
                renderer.shadowCastingMode = ghost ? ShadowCastingMode.Off : _slots[i].Shadows;

            Material[] materials = _slots[i].Materials;
            if (materials == null)
                continue;

            for (int m = 0; m < materials.Length; m++)
                ApplyMaterial(materials[m], ghost, opacity);
        }
    }

    static void ApplyMaterial(Material material, bool ghost, float opacity)
    {
        if (material == null || !material.HasProperty(OpacityId))
            return;

        material.SetFloat(OpacityId, opacity);

        if (!material.HasProperty(SrcBlendId))
            return;

        if (ghost)
        {
            material.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
            material.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat(ZWriteId, 1f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("DepthNormalsOnly", false);
            return;
        }

        material.SetFloat(SrcBlendId, (float)BlendMode.One);
        material.SetFloat(DstBlendId, (float)BlendMode.Zero);
        material.SetFloat(ZWriteId, 1f);
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
        material.SetShaderPassEnabled("DepthOnly", true);
        material.SetShaderPassEnabled("DepthNormalsOnly", true);
    }

    static bool IsBodyRenderer(Renderer renderer)
    {
        if (renderer == null || renderer is ParticleSystemRenderer)
            return false;
        if (renderer.GetComponentInParent<Canvas>(true) != null)
            return false;
        if (renderer.GetComponentInParent<VitalsBarsView>(true) != null)
            return false;
        return renderer is SkinnedMeshRenderer || renderer is MeshRenderer;
    }
}
