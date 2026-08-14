using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One-shot impact burst for the ShipCapsule crash cinematic (see <see cref="ShipCrashIntro"/>):
/// an explosive flash + dust cloud + fiery sparks + tumbling rock debris, all firing the instant
/// the capsule hits the ground.
///
/// Like <see cref="ShipReentryGlow"/>, the actual look is authored as a normal prefab (<see
/// cref="effectPrefab"/>, e.g. "CapsuleImpact") - drag one in and it's fully editable with the
/// regular Particle System inspectors instead of being generated in code. Wire it via
/// ShipCrashIntro.impactEffectPrefab (or assign it directly here if you add this component to the
/// ShipCapsule by hand). If left empty, falls back to building everything procedurally in code so
/// it still works as a single drop-in component with nothing to wire up.
///
/// Usage: call <see cref="Trigger"/> exactly once, at the moment of impact. If the GameObject
/// doesn't already have this component, <see cref="ShipCrashIntro"/> adds it automatically.
/// </summary>
public class ShipCrashImpact : MonoBehaviour
{
    [Header("Effect Prefab")]
    [Tooltip("Authored impact burst prefab (flash + dust + sparks + debris, e.g. CapsuleImpact). " +
        "Edit this prefab directly in the Editor to tune the look - it's instantiated as-is at " +
        "runtime. Leave empty to fall back to the procedural builder below.")]
    [SerializeField] GameObject effectPrefab;

    [Header("Impact Flash (explosive pop, procedural fallback)")]
    [Tooltip("Instant, blinding, additive pop right at the impact point - the split-second 'boom' " +
        "flash that reads as an explosion rather than just a scatter of debris.")]
    [SerializeField] bool enableFlash = true;
    [SerializeField, Min(0)] int flashCount = 2;
    [SerializeField] Vector2 flashSize = new Vector2(3.5f, 6f);
    [SerializeField] Vector2 flashLifetime = new Vector2(0.1f, 0.16f);
    [SerializeField] Color flashColor = new Color(1f, 0.96f, 0.85f, 1f);

    [Header("Dust Cloud (procedural fallback)")]
    [SerializeField, Min(0)] int dustCount = 40;
    [SerializeField] Vector2 dustSpeed = new Vector2(2.5f, 6f);
    [SerializeField] Vector2 dustSize = new Vector2(0.9f, 2.2f);
    [SerializeField] Vector2 dustLifetime = new Vector2(0.8f, 1.4f);
    [SerializeField] Color dustColor = new Color(0.5f, 0.42f, 0.34f, 0.55f);

    [Header("Spark Burst (procedural fallback)")]
    [SerializeField, Min(0)] int sparkCount = 70;
    [SerializeField] Vector2 sparkSpeed = new Vector2(5f, 12f);
    [SerializeField] Vector2 sparkSize = new Vector2(0.1f, 0.26f);
    [SerializeField] Vector2 sparkLifetime = new Vector2(0.25f, 0.6f);
    [SerializeField] Color sparkHotColor = new Color(1f, 0.9f, 0.5f, 1f);
    [SerializeField] Color sparkCoolColor = new Color(1f, 0.45f, 0.08f, 1f);
    [SerializeField, Min(0f)] float sparkGravity = 2.4f;

    [Header("Rock Debris (procedural fallback)")]
    [SerializeField, Min(0)] int debrisCount = 24;
    [SerializeField] Vector2 debrisSpeed = new Vector2(3f, 8f);
    [SerializeField] Vector2 debrisSize = new Vector2(0.12f, 0.34f);
    [SerializeField] Vector2 debrisLifetime = new Vector2(0.8f, 1.6f);
    [SerializeField] Color debrisColor = new Color(0.28f, 0.24f, 0.2f, 1f);
    [SerializeField, Min(0f)] float debrisGravity = 3.4f;

    GameObject _instance;
    ParticleSystem _flash;
    ParticleSystem _dust;
    ParticleSystem _sparks;
    ParticleSystem _debris;
    bool _built;

    static Material _flashMaterial;
    static Material _softAdditiveMaterial;
    static Material _softAlphaMaterial;
    static Material _debrisMaterial;

    /// <summary>Assign the authored prefab (e.g. from ShipCrashIntro right after adding this
    /// component). Safe to call any time before Trigger() - deliberately NOT built in Awake():
    /// when ShipCrashIntro adds this component via AddComponent, Awake() runs synchronously
    /// before SetEffectPrefab() gets a chance to run, which would otherwise lock in the
    /// procedural fallback before the prefab is ever assigned. Trigger() builds lazily on first
    /// use instead, by which point the prefab (if any) is already wired up.</summary>
    public void SetEffectPrefab(GameObject prefab)
    {
        effectPrefab = prefab;
    }

    void Build()
    {
        if (_built)
            return;
        _built = true;

        // Prefer the authored look: instantiate it and just find its named children, same
        // pattern as ShipReentryGlow. Only build procedurally if no prefab is assigned.
        Transform root = transform;
        if (effectPrefab != null)
        {
            _instance = Instantiate(effectPrefab, transform);
            _instance.name = effectPrefab.name;
            _instance.transform.localPosition = Vector3.zero;
            _instance.transform.localRotation = Quaternion.identity;
            root = _instance.transform;
        }

        if (enableFlash)
            _flash = BuildFlashSystem(root, "ImpactFlash");

        // randomDirectionAmount blends in fully-random directions on top of the Hemisphere's
        // outward spread - sells a chaotic "blast" scatter (including to the sides) instead of a
        // neat, uniform sprinkler pattern.
        _dust = BuildBurstSystem(root, "ImpactDust", ParticleSystemRenderMode.Billboard,
            dustCount, dustSpeed, dustSize, dustLifetime,
            new ParticleSystem.MinMaxGradient(dustColor), gravity: 0.4f,
            GetSoftAlphaMaterial(), fadeToBlack: false, drag: 1.4f, randomDirectionAmount: 0.3f);

        _sparks = BuildBurstSystem(root, "ImpactSparks", ParticleSystemRenderMode.Billboard,
            sparkCount, sparkSpeed, sparkSize, sparkLifetime,
            new ParticleSystem.MinMaxGradient(sparkHotColor, sparkCoolColor), gravity: sparkGravity,
            GetSoftAdditiveMaterial(), fadeToBlack: true, drag: 0f, randomDirectionAmount: 0.5f);

        _debris = BuildBurstSystem(root, "ImpactDebris", ParticleSystemRenderMode.Mesh,
            debrisCount, debrisSpeed, debrisSize, debrisLifetime,
            new ParticleSystem.MinMaxGradient(Color.white), gravity: debrisGravity,
            GetDebrisMaterial(), fadeToBlack: false, drag: 0f,
            mesh: ShipVfxUtility.GetCubeMesh(), tumble: true, fade: false, randomDirectionAmount: 0.5f);
    }

    /// <summary>Fires the flash/dust/spark/debris burst once. Safe to call multiple times.</summary>
    public void Trigger()
    {
        Build();
        _flash?.Play(true);
        _dust?.Play(true);
        _sparks?.Play(true);
        _debris?.Play(true);
    }

    ParticleSystem BuildBurstSystem(Transform parent, string childName, ParticleSystemRenderMode renderMode,
        int count, Vector2 speed, Vector2 size, Vector2 lifetime, ParticleSystem.MinMaxGradient color,
        float gravity, Material material, bool fadeToBlack, float drag, Mesh mesh = null, bool tumble = false,
        bool fade = true, float randomDirectionAmount = 0f)
    {
        // Already authored in the prefab - reuse as-is instead of overwriting hand-tuned values
        // with the procedural defaults below.
        Transform existing = parent.Find(childName);
        if (existing != null)
        {
            ParticleSystem existingPs = existing.GetComponent<ParticleSystem>();
            if (existingPs != null)
                return existingPs;
        }

        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = color;
        main.gravityModifier = gravity;
        main.maxParticles = Mathf.Max(1, count * 2);

        if (tumble)
        {
            ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-180f, 180f);
            rotation.y = new ParticleSystem.MinMaxCurve(-180f, 180f);
            rotation.z = new ParticleSystem.MinMaxCurve(-180f, 180f);
        }

        if (drag > 0f)
        {
            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = Mathf.Clamp01(drag * 0.3f);
            limit.limit = new ParticleSystem.MinMaxCurve(speed.x * 0.4f);
        }

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.25f;
        shape.randomDirectionAmount = randomDirectionAmount;

        if (fade)
        {
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient fadeGradient = new Gradient();
            Color endColor = fadeToBlack ? Color.black : Color.white;
            fadeGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(endColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 0.85f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = fadeGradient;
        }

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, tumble
            ? AnimationCurve.Linear(0f, 1f, 1f, 0.6f)
            : AnimationCurve.Linear(0f, 1f, 1f, 1.6f));

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = renderMode;
        // Billboards face the camera (View); tumbling mesh debris needs its own simulated
        // rotation respected instead of being forced to face the camera every frame.
        renderer.alignment = renderMode == ParticleSystemRenderMode.Mesh
            ? ParticleSystemRenderSpace.World
            : ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = material;
        if (mesh != null)
            renderer.mesh = mesh;

        return ps;
    }

    /// <summary>A couple of huge, instantly-fading additive puffs that pop right at the impact
    /// point - unlike the other bursts, this one doesn't fly anywhere (no shape/velocity), it just
    /// blooms and vanishes in a fraction of a second, reading as the "boom" of an explosion.</summary>
    ParticleSystem BuildFlashSystem(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing != null)
        {
            ParticleSystem existingPs = existing.GetComponent<ParticleSystem>();
            if (existingPs != null)
                return existingPs;
        }

        GameObject go = new GameObject(childName);
        go.transform.SetParent(parent, false);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(flashLifetime.x, flashLifetime.y);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(flashSize.x, flashSize.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startColor = flashColor;
        main.maxParticles = Mathf.Max(1, flashCount * 2);

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)flashCount) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false; // stays put at the impact point, doesn't travel

        // Pops open fast then collapses even faster - a lingering flash reads as a glow, not a blast.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.25f, 0f, 8f),
            new Keyframe(0.2f, 1f, 0f, 0f),
            new Keyframe(1f, 0f, -4f, 0f)));

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = ShipVfxUtility.BuildFadeGradient(false);

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingFudge = -10f; // draw in front of dust/sparks/debris - it's the initial pop
        renderer.material = GetFlashMaterial();

        return ps;
    }

    static Material GetFlashMaterial()
    {
        // Heavily over-brightened - this single puff needs to blow straight through the Bloom
        // threshold on its own to read as a blinding "boom" rather than a soft glow.
        if (_flashMaterial == null)
            _flashMaterial = ShipVfxUtility.BuildParticleMaterial(ShipVfxUtility.GetSoftDotTexture(), true, "ShipCrashImpact_Flash (Generated)", 4.5f);
        return _flashMaterial;
    }

    static Material GetSoftAdditiveMaterial()
    {
        // Over-brightened so the spark burst blooms into a punchy flash right on impact instead
        // of a dim scatter of dots.
        if (_softAdditiveMaterial == null)
            _softAdditiveMaterial = ShipVfxUtility.BuildParticleMaterial(ShipVfxUtility.GetSoftDotTexture(), true, "ShipCrashImpact_Additive (Generated)", 2.8f);
        return _softAdditiveMaterial;
    }

    static Material GetSoftAlphaMaterial()
    {
        if (_softAlphaMaterial == null)
            _softAlphaMaterial = ShipVfxUtility.BuildParticleMaterial(ShipVfxUtility.GetSoftDotTexture(), false, "ShipCrashImpact_Dust (Generated)");
        return _softAlphaMaterial;
    }

    Material GetDebrisMaterial()
    {
        if (_debrisMaterial == null)
            _debrisMaterial = ShipVfxUtility.BuildOpaqueTintedMaterial(debrisColor, "ShipCrashImpact_Debris (Generated)");
        return _debrisMaterial;
    }
}
