using System.Collections.Generic;
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

    [Header("Crater")]
    [Tooltip("Impact crater mesh spawned on the planet surface at the moment of impact. " +
        "Leave empty to load Ship/Capsule/Crater from Resources.")]
    [SerializeField] GameObject craterPrefab;
    [SerializeField, Min(0.01f)] float craterScale = 6f;
    [Tooltip("How far to sink the crater into the ground, in world units. Keeps the rim on the " +
        "surface and the bowl clipping into the planet instead of sitting on top like a hat.")]
    [SerializeField] float craterEmbed = 0.2f;

    const string DefaultCraterResource = "Ship/Capsule/Crater";
    const string DefaultCraterMaterialResource = "Ship/Capsule/Materials/Crater";

    GameObject _instance;
    ParticleSystem[] _systems;
    bool _built;
    bool _craterSpawned;

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

    public void SetCrater(GameObject prefab, float scale, float embed)
    {
        craterPrefab = prefab;
        craterScale = Mathf.Max(0.01f, scale);
        craterEmbed = embed;
    }

    void Build()
    {
        if (_built)
            return;
        _built = true;

        // Prefer the authored look: instantiate the prefab as-is and play every particle system
        // in it. Do not also spawn the old procedural ImpactDebris/Flash/etc. — the prefab is
        // allowed to rename/replace those (ImpactDirt, ImpactGrass, ...) and those children
        // would otherwise never be Play()'d.
        if (effectPrefab != null)
        {
            _instance = Instantiate(effectPrefab, transform);
            _instance.name = effectPrefab.name;
            _instance.transform.localPosition = Vector3.zero;
            AlignEmitterToPlanetUp(_instance.transform);
            _systems = _instance.GetComponentsInChildren<ParticleSystem>(true);
            PrepareSystems(_systems);
            return;
        }

        var built = new List<ParticleSystem>(4);
        if (enableFlash)
            built.Add(BuildFlashSystem(transform, "ImpactFlash"));

        // randomDirectionAmount blends in fully-random directions on top of the Hemisphere's
        // outward spread - sells a chaotic "blast" scatter (including to the sides) instead of a
        // neat, uniform sprinkler pattern.
        built.Add(BuildBurstSystem(transform, "ImpactDust", ParticleSystemRenderMode.Billboard,
            dustCount, dustSpeed, dustSize, dustLifetime,
            new ParticleSystem.MinMaxGradient(dustColor), gravity: 0.4f,
            GetSoftAlphaMaterial(), fadeToBlack: false, drag: 1.4f, randomDirectionAmount: 0.3f));

        built.Add(BuildBurstSystem(transform, "ImpactSparks", ParticleSystemRenderMode.Billboard,
            sparkCount, sparkSpeed, sparkSize, sparkLifetime,
            new ParticleSystem.MinMaxGradient(sparkHotColor, sparkCoolColor), gravity: sparkGravity,
            GetSoftAdditiveMaterial(), fadeToBlack: true, drag: 0f, randomDirectionAmount: 0.5f));

        built.Add(BuildBurstSystem(transform, "ImpactDebris", ParticleSystemRenderMode.Mesh,
            debrisCount, debrisSpeed, debrisSize, debrisLifetime,
            new ParticleSystem.MinMaxGradient(Color.white), gravity: debrisGravity,
            GetDebrisMaterial(), fadeToBlack: false, drag: 0f,
            mesh: ShipVfxUtility.GetCubeMesh(), tumble: true, fade: false, randomDirectionAmount: 0.5f));

        _systems = built.ToArray();
        PrepareSystems(_systems);
    }

    /// <summary>Fires every authored (or procedurally built) burst once, and stamps a crater
    /// into the planet surface. Safe to call multiple times (the crater only spawns once).</summary>
    public void Trigger()
    {
        Build();
        if (_systems != null)
        {
            for (int i = 0; i < _systems.Length; i++)
            {
                ParticleSystem ps = _systems[i];
                if (ps == null)
                    continue;

                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(false);
            }
        }

        if (Application.isPlaying)
            SpawnCrater();
    }

    void SpawnCrater()
    {
        if (_craterSpawned)
            return;
        _craterSpawned = true;

        GameObject prefab = craterPrefab != null
            ? craterPrefab
            : Resources.Load<GameObject>(DefaultCraterResource);
        if (prefab == null)
        {
            Debug.LogWarning("ShipCrashImpact: no crater model found at Resources/" +
                DefaultCraterResource + ".", this);
            return;
        }

        SphericalPlanet planet = SphericalPlanet.Instance;
        Vector3 up = planet != null ? planet.GetUpAt(transform.position) : Vector3.up;
        Vector3 surface = planet != null
            ? planet.GetSurfacePoint(up)
            : transform.position;

        Quaternion rotation = PlanetSurfacePose.RotationFromUp(up, Random.Range(0f, 360f));
        GameObject crater = Instantiate(prefab, surface, rotation);
        crater.name = "ImpactCrater";
        crater.transform.localScale = Vector3.one * craterScale;

        StripRuntimeJunk(crater);
        ApplyCraterMaterial(crater);
        EmbedInSurface(crater.transform, surface, up);

        Transform parent = PlanetSurfacePose.GetOrCreateObjectsRoot(planet);
        if (parent != null)
            crater.transform.SetParent(parent, true);
    }

    static void StripRuntimeJunk(GameObject crater)
    {
        Animator[] animators = crater.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
            Destroy(animators[i]);

        Collider[] colliders = crater.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            Destroy(colliders[i]);
    }

    static void ApplyCraterMaterial(GameObject crater)
    {
        Material material = Resources.Load<Material>(DefaultCraterMaterialResource);
        if (material == null)
            return;

        MeshRenderer[] renderers = crater.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterial = material;
    }

    void EmbedInSurface(Transform crater, Vector3 surface, Vector3 up)
    {
        if (!ShipVfxUtility.TryGetRendererBounds(crater, out Bounds bounds))
        {
            crater.position = surface - up * craterEmbed;
            return;
        }

        // Sit the mesh so its center is slightly below the surface: rim stays visible,
        // bowl clips into the planet instead of hovering as a separate prop.
        float alongUp = Vector3.Dot(bounds.center - surface, up);
        crater.position -= up * (alongUp + craterEmbed);
    }

    static void AlignEmitterToPlanetUp(Transform emitter)
    {
        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(emitter.position)
            : Vector3.up;
        emitter.rotation = Quaternion.FromToRotation(Vector3.up, up);
    }

    static void PrepareSystems(ParticleSystem[] systems)
    {
        if (systems == null)
            return;

        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem ps = systems[i];
            if (ps == null)
                continue;

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        }
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
