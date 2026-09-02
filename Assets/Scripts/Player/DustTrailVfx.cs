using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Small puffs of dust that trail behind whatever this is attached to while it's running -
/// mesh-render-mode particles using the "Dust" cloud mesh, spawned via "rate over distance" so
/// they naturally only appear while actually covering ground (see the reference technique this
/// was built from: https://www.youtube.com/shorts/Qu6ANC3FHk4).
///
/// Fully self-contained: drop the "DustTrail" prefab under the feet of any moving object (see
/// <see cref="DustTrailVfxBaker"/> for the editor tool that bakes/wires it) and it just works -
/// no other setup required. If the object has an Animator driven by PlanetWalker/TouchController
/// (both write a normalized 0..1 "MotionSpeed" with a ~0.7 run threshold), emission is gated to
/// only kick in once actually running, not just walking; without a matching Animator it simply
/// emits any time it's moving, so it's just as usable on non-player objects.
///
/// Built procedurally in <see cref="Configure"/> (called from Awake, same "authored prefab, code
/// only fills in what's missing" idiom as <see cref="ShipFireTrail"/>/ShipCrashImpact use for the
/// ship VFX) so every value below is a normal tunable Inspector field, not buried in a giant
/// hand-authored ParticleSystem YAML block.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class DustTrailVfx : MonoBehaviour
{
    [Header("Puffs")]
    [SerializeField, Min(0.05f)] float minLifetime = 0.5f;
    [SerializeField, Min(0.05f)] float maxLifetime = 1f;
    [SerializeField, Min(0.01f)] float minSize = 0.28f;
    [SerializeField, Min(0.01f)] float maxSize = 0.55f;
    [Tooltip("Negative gravity modifier so puffs drift upward instead of falling.")]
    [SerializeField, Min(0f)] float riseStrength = 0.3f;
    [SerializeField, Min(0f)] float minRatePerDistance = 2f;
    [SerializeField, Min(0f)] float maxRatePerDistance = 4f;
    [SerializeField, Min(0.01f)] float shapeRadius = 0.15f;
    [SerializeField, Min(1)] int maxParticles = 150;

    [Header("Run Gate")]
    [Tooltip("If on, only emits once the parent Animator's \"MotionSpeed\" reaches this (both " +
        "movement scripts default their run threshold to 0.7, so 0.65 gives a hair of slack). " +
        "If there's no such Animator, this is ignored and the trail just emits while moving.")]
    [SerializeField] bool onlyEmitWhileRunning = true;
    [SerializeField, Range(0f, 1f)] float runThreshold = 0.65f;

    static Mesh _dustMesh;
    static Material _dustMaterial;

    ParticleSystem _ps;
    ParticleSystem.EmissionModule _emission;
    Animator _animator;
    int _animIDMotionSpeed;
    bool _running;

    void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        Configure();

        _emission = _ps.emission;
        _animator = GetComponentInParent<Animator>();
        _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        SetRunning(!onlyEmitWhileRunning || _animator == null);
    }

    void Update()
    {
        bool running = !onlyEmitWhileRunning
            || _animator == null
            || _animator.GetFloat(_animIDMotionSpeed) >= runThreshold;

        if (running != _running)
            SetRunning(running);
    }

    void SetRunning(bool running)
    {
        _running = running;
        _emission.enabled = running;
    }

    /// <summary>Rebuilds every module from the fields above. Public/idempotent so the editor
    /// baker can call it again to refresh an already-authored prefab after tuning values.</summary>
    public void Configure()
    {
        ParticleSystem ps = _ps != null ? _ps : GetComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startDelay = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startColor = Color.white;
        main.gravityModifier = -riseStrength;
        main.maxParticles = maxParticles;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = new ParticleSystem.MinMaxCurve(minRatePerDistance, maxRatePerDistance);
        emission.SetBursts(new ParticleSystem.Burst[0]);

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = shapeRadius;
        shape.arc = 360f;
        shape.randomDirectionAmount = 0f;

        // Grows in for an instant then shrinks back to nothing - avoids puffs "popping" in at
        // full size and reads as a soft, dissipating cloud instead.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve growThenShrink = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 3.5f),
            new Keyframe(0.25f, 1f, 0f, 0f),
            new Keyframe(1f, 0f, -1.2f, 0f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, growThenShrink);

        ParticleSystem.RotationOverLifetimeModule rotationOverLifetime = ps.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-25f * Mathf.Deg2Rad, 25f * Mathf.Deg2Rad);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.2f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        ParticleSystemRenderer renderer = GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        Mesh mesh = GetDustMesh();
        if (mesh != null)
            renderer.mesh = mesh;
        renderer.sharedMaterial = GetDustMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.alignment = ParticleSystemRenderSpace.World;
    }

    static Mesh GetDustMesh()
    {
        if (_dustMesh == null)
        {
            GameObject prefab = Resources.Load<GameObject>("Player/DustTrail/Dust");
            if (prefab != null)
                _dustMesh = prefab.GetComponentInChildren<MeshFilter>()?.sharedMesh;
        }
        return _dustMesh;
    }

    static Material GetDustMaterial()
    {
        if (_dustMaterial == null)
            _dustMaterial = Resources.Load<Material>("Player/DustTrail/DustTrail");
        return _dustMaterial;
    }
}
