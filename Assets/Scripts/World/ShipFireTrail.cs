using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fiery re-entry trail for the ShipCapsule crash cinematic (see <see cref="ShipCrashIntro"/>):
/// a bright core streak (TrailRenderer) plus a thick, overlapping column of flame puffs, a sooty
/// smoke fringe, and outward-flying sparks - together reading as a proper meteor fireball rather
/// than a thin line.
///
/// Authored as a normal child ("FireTrail", with FlameBody/Smoke/Sparks/FireEmbers underneath it)
/// inside the CapsuleParticalSystem prefab, so every particle system here is directly editable in
/// the Inspector/Scene view like any hand-authored effect. If any of those children are missing
/// (e.g. this component ends up on a capsule that doesn't use that prefab), it falls back to
/// building them procedurally in code so it still works as a drop-in component with nothing to
/// wire up - see <see cref="ConfigureFlameBody"/> etc.
///
/// Usage: <see cref="Play"/> when the fall starts, <see cref="Stop"/> on impact. If the
/// GameObject doesn't already have this component, <see cref="ShipCrashIntro"/> adds it
/// automatically with sensible defaults.
/// </summary>
[RequireComponent(typeof(TrailRenderer))]
public class ShipFireTrail : MonoBehaviour
{
    [Header("Trail Shape (bright core streak)")]
    [SerializeField, Min(0.05f)] float trailTime = 0.4f;
    [SerializeField, Min(0.01f)] float headWidth = 1.4f;
    [SerializeField, Min(0f)] float tailWidthRatio = 0.05f;
    [SerializeField, Min(0f)] float minVertexDistance = 0.1f;
    [Tooltip("World units of trail pre-seeded behind the capsule the instant Play() is called, so " +
        "the streak appears at full length immediately instead of visibly growing in over the first " +
        "fraction of a second (a TrailRenderer normally starts as a single point).")]
    [SerializeField, Min(0f)] float instantSeedLength = 5f;

    [Header("Trail Color (head -> tail)")]
    [SerializeField] Color coreColor = new Color(1f, 0.96f, 0.78f, 0.95f);
    [SerializeField] Color midColor = new Color(1f, 0.5f, 0.08f, 0.85f);
    // RGB (not just alpha) fades toward black - the additive blend is forced directly via
    // Src/DstBlend, bypassing the shader's own alpha-premultiply path, so alpha alone can't be
    // relied on to fade the tail to nothing.
    [SerializeField] Color tailColor = new Color(0.05f, 0.01f, 0f, 0f);

    [Header("Flame Body (thick overlapping fire puffs)")]
    [Tooltip("Continuously-spawned, additively-blended glow puffs along the fall path - this is " +
        "what gives the trail volume/thickness instead of reading as a flat line.")]
    [SerializeField] bool enableFlameBody = true;
    [SerializeField, Min(0f)] float flameRate = 85f;
    [SerializeField, Min(0.01f)] float flameSize = 1.9f;
    [SerializeField, Min(0.01f)] float flameLifetime = 0.35f;
    [SerializeField] Color flameHotColor = new Color(1f, 0.85f, 0.5f, 1f);
    [SerializeField] Color flameCoolColor = new Color(1f, 0.28f, 0.05f, 1f);

    [Header("Smoke Fringe (sooty depth around the flame)")]
    [SerializeField] bool enableSmoke = true;
    [SerializeField, Min(0f)] float smokeRate = 20f;
    [SerializeField, Min(0.01f)] float smokeSize = 2.6f;
    [SerializeField, Min(0.01f)] float smokeLifetime = 0.7f;
    [SerializeField] Color smokeColor = new Color(0.3f, 0.14f, 0.08f, 0.5f);

    [Header("Sparks (fast outward-flying embers)")]
    [SerializeField] bool enableSparks = true;
    [SerializeField, Min(0f)] float sparkRate = 40f;
    [SerializeField, Min(0.01f)] float sparkSize = 0.16f;
    [SerializeField] Color sparkHotColor = new Color(1f, 0.9f, 0.55f, 1f);
    [SerializeField] Color sparkCoolColor = new Color(1f, 0.35f, 0.05f, 1f);

    [Header("Embers (soft drifting glow motes)")]
    [SerializeField] bool enableEmbers = true;
    [SerializeField, Min(0f)] float emberRate = 22f;
    [SerializeField, Min(0.01f)] float emberSize = 0.28f;
    [SerializeField] Color emberHotColor = new Color(1f, 0.85f, 0.4f, 1f);
    [SerializeField] Color emberCoolColor = new Color(0.9f, 0.25f, 0.05f, 1f);

    TrailRenderer _trail;
    ParticleSystem _flameBody;
    ParticleSystem _smoke;
    ParticleSystem _sparks;
    ParticleSystem _embers;

    static Material _trailMaterial;
    static Material _flameMaterial;
    static Material _smokeMaterial;
    static Material _sparkMaterial;
    static Material _emberMaterial;

    void Awake()
    {
        _trail = GetComponent<TrailRenderer>();
        ConfigureTrail();
        if (enableFlameBody)
            ConfigureFlameBody();
        if (enableSmoke)
            ConfigureSmoke();
        if (enableSparks)
            ConfigureSparks();
        if (enableEmbers)
            ConfigureEmbers();
        Stop();
    }

    void ConfigureTrail()
    {
        _trail.time = trailTime;
        _trail.minVertexDistance = minVertexDistance;
        _trail.widthMultiplier = headWidth;
        _trail.widthCurve = new AnimationCurve(
            new Keyframe(0f, 1f, 0f, 0f),
            new Keyframe(1f, tailWidthRatio, 0f, 0f));

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(coreColor, 0f),
                new GradientColorKey(midColor, 0.4f),
                new GradientColorKey(tailColor, 1f),
            },
            new[]
            {
                new GradientAlphaKey(coreColor.a, 0f),
                new GradientAlphaKey(midColor.a, 0.4f),
                new GradientAlphaKey(tailColor.a, 1f),
            });
        _trail.colorGradient = gradient;

        _trail.alignment = LineAlignment.View;
        _trail.numCapVertices = 4;
        _trail.numCornerVertices = 2;
        _trail.textureMode = LineTextureMode.Stretch;
        _trail.shadowCastingMode = ShadowCastingMode.Off;
        _trail.receiveShadows = false;
        _trail.material = GetTrailMaterial();
        _trail.emitting = false;
    }

    /// <summary>Thick column of overlapping additive glow puffs - the main "this is a fireball, not
    /// a line" volume. Particles spawn big and bright right at the capsule and immediately start
    /// shrinking/darkening, so density stays highest at the head and trails off toward the tail.</summary>
    void ConfigureFlameBody()
    {
        if (TryGetExistingChild("FlameBody", out _flameBody))
            return; // already authored in the prefab - respect it, don't overwrite with code defaults

        _flameBody = CreateChildParticleSystem("FlameBody");

        ParticleSystem.MainModule main = _flameBody.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(flameLifetime * 0.7f, flameLifetime * 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(flameSize * 0.75f, flameSize * 1.15f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startColor = new ParticleSystem.MinMaxGradient(flameHotColor, flameCoolColor);
        main.maxParticles = 200;

        ParticleSystem.EmissionModule emission = _flameBody.emission;
        emission.rateOverTime = flameRate;

        ParticleSystem.ShapeModule shape = _flameBody.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = flameSize * 0.22f;

        // Grows for an instant (like a blooming flame ball) then collapses - avoids a "pop" of
        // uniformly full-size puffs and reads as turbulent combustion instead.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = _flameBody.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve growThenShrink = new AnimationCurve(
            new Keyframe(0f, 0.6f, 0f, 4f),
            new Keyframe(0.18f, 1f, 0f, 0f),
            new Keyframe(1f, 0f, -2f, 0f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, growThenShrink);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = _flameBody.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = ShipVfxUtility.BuildFadeGradient(true);

        ParticleSystem.RotationOverLifetimeModule rotationOverLifetime = _flameBody.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-90f * Mathf.Deg2Rad, 90f * Mathf.Deg2Rad);

        ParticleSystem.NoiseModule noise = _flameBody.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.5f;

        ParticleSystemRenderer renderer = _flameBody.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = GetFlameMaterial();
    }

    /// <summary>Sooty, alpha-blended puffs mixed into the flame's fringe - adds depth/weight so it
    /// doesn't read as a "clean" fire, closer to real re-entry plasma/burn residue.</summary>
    void ConfigureSmoke()
    {
        if (TryGetExistingChild("Smoke", out _smoke))
            return;

        _smoke = CreateChildParticleSystem("Smoke");

        ParticleSystem.MainModule main = _smoke.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(smokeLifetime * 0.8f, smokeLifetime * 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(smokeSize * 0.7f, smokeSize * 1.2f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startColor = smokeColor;
        main.maxParticles = 100;

        ParticleSystem.EmissionModule emission = _smoke.emission;
        emission.rateOverTime = smokeRate;

        ParticleSystem.ShapeModule shape = _smoke.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = smokeSize * 0.3f;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = _smoke.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.5f, 0f, 1.5f),
            new Keyframe(0.4f, 1f, 0f, 0f),
            new Keyframe(1f, 1.3f, 0f, 0f)));

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = _smoke.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient smokeFade = new Gradient();
        smokeFade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = smokeFade;

        ParticleSystem.RotationOverLifetimeModule rotationOverLifetime = _smoke.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-40f * Mathf.Deg2Rad, 40f * Mathf.Deg2Rad);

        ParticleSystemRenderer renderer = _smoke.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingFudge = 10f; // draw behind the flame body/streak
        renderer.material = GetSmokeMaterial();
    }

    /// <summary>Bright streaks flung outward from the fall, stretched along their own velocity -
    /// the punchy "impactful sparks" read the flat embers alone can't give.</summary>
    void ConfigureSparks()
    {
        if (TryGetExistingChild("Sparks", out _sparks))
            return;

        _sparks = CreateChildParticleSystem("Sparks");

        ParticleSystem.MainModule main = _sparks.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        main.startSize = new ParticleSystem.MinMaxCurve(sparkSize * 0.7f, sparkSize * 1.3f);
        main.startColor = new ParticleSystem.MinMaxGradient(sparkHotColor, sparkCoolColor);
        main.gravityModifier = 0.6f;
        main.maxParticles = 150;

        ParticleSystem.EmissionModule emission = _sparks.emission;
        emission.rateOverTime = sparkRate;

        ParticleSystem.ShapeModule shape = _sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        shape.randomDirectionAmount = 1f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = _sparks.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = ShipVfxUtility.BuildFadeGradient(true);

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = _sparks.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));

        ParticleSystemRenderer renderer = _sparks.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.12f;
        renderer.lengthScale = 3f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = GetSparkMaterial();
    }

    void ConfigureEmbers()
    {
        if (TryGetExistingChild("FireEmbers", out _embers))
            return;

        _embers = CreateChildParticleSystem("FireEmbers");

        ParticleSystem.MainModule main = _embers.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(emberSize * 0.6f, emberSize);
        main.startColor = new ParticleSystem.MinMaxGradient(emberHotColor, emberCoolColor);
        main.maxParticles = 80;

        ParticleSystem.EmissionModule emission = _embers.emission;
        emission.rateOverTime = emberRate;

        ParticleSystem.ShapeModule shape = _embers.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.18f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = _embers.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = ShipVfxUtility.BuildFadeGradient(true);

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = _embers.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        ParticleSystemRenderer renderer = _embers.GetComponent<ParticleSystemRenderer>();
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = GetEmberMaterial();
    }

    /// <summary>Looks for an already-authored child (e.g. baked into the CapsuleParticalSystem
    /// prefab) so Awake() can reuse it as-is instead of stomping hand-tuned values with the
    /// procedural defaults below.</summary>
    bool TryGetExistingChild(string name, out ParticleSystem ps)
    {
        Transform existing = transform.Find(name);
        ps = existing != null ? existing.GetComponent<ParticleSystem>() : null;
        return ps != null;
    }

    ParticleSystem CreateChildParticleSystem(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go.AddComponent<ParticleSystem>();
    }

    /// <summary>Re-parents FlameBody/Smoke/Sparks/FireEmbers under <paramref name="parent"/> (e.g.
    /// ShipReentryGlow.EffectRoot, "CapsuleParticalSystem") purely for a tidy hierarchy - all of
    /// them simulate in world space, so moving them in the hierarchy has no effect on where the
    /// particles actually appear. Only meaningful for the procedural fallback path (no authored
    /// "FireTrail" child was found, see ShipCrashIntro.ResolveFireTrail) - when the children are
    /// reused from the prefab they're already correctly nested. Safe no-op if <paramref
    /// name="parent"/> is null.</summary>
    public void SetEffectParent(Transform parent)
    {
        if (parent == null)
            return;

        ReparentKeepingWorldPose(_flameBody, parent);
        ReparentKeepingWorldPose(_smoke, parent);
        ReparentKeepingWorldPose(_sparks, parent);
        ReparentKeepingWorldPose(_embers, parent);
    }

    static void ReparentKeepingWorldPose(ParticleSystem ps, Transform parent)
    {
        if (ps != null)
            ps.transform.SetParent(parent, true);
    }

    /// <summary>
    /// Call when the fall begins - clears any stale trail and starts emitting fresh.
    /// <paramref name="travelDirection"/> (the direction the capsule is about to move) lets the
    /// trail pre-seed a short tail behind it immediately, so it reads as a full-length streak from
    /// the very first frame instead of visibly growing in.
    /// </summary>
    public void Play(Vector3 travelDirection = default)
    {
        if (_trail != null)
        {
            _trail.Clear();
            _trail.emitting = true;
            SeedInstantTail(travelDirection);
        }
        PlaySystem(_flameBody);
        PlaySystem(_smoke);
        PlaySystem(_sparks);
        PlaySystem(_embers);
    }

    static void PlaySystem(ParticleSystem ps)
    {
        if (ps == null)
            return;
        ps.Clear(true);
        ps.Play(true);
    }

    void SeedInstantTail(Vector3 travelDirection)
    {
        if (instantSeedLength <= 0f || travelDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 behind = -travelDirection.normalized;
        Vector3 origin = transform.position;

        const int seedSteps = 6;
        for (int i = seedSteps; i >= 1; i--)
        {
            float t = (float)i / seedSteps;
            _trail.AddPosition(origin + behind * (instantSeedLength * t));
        }
        _trail.AddPosition(origin);
    }

    /// <summary>Call on impact - stops new emission; already-emitted trail/particles fade out naturally.</summary>
    public void Stop()
    {
        if (_trail != null)
            _trail.emitting = false;
        StopSystem(_flameBody);
        StopSystem(_smoke);
        StopSystem(_sparks);
        StopSystem(_embers);
    }

    static void StopSystem(ParticleSystem ps)
    {
        if (ps != null)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    // HDR over-brighten multipliers for the "hot" additive materials - pushes rendered pixels
    // past the Bloom threshold so the fireball glows strongly on its own, not just where puffs
    // happen to overlap. Smoke is intentionally excluded (soot shouldn't bloom).
    const float TrailHdrBoost = 2.2f;
    const float FlameHdrBoost = 2.6f;
    const float SparkHdrBoost = 3.2f;
    const float EmberHdrBoost = 2.4f;

    static Material GetTrailMaterial()
    {
        if (_trailMaterial == null)
            _trailMaterial = ShipVfxUtility.BuildParticleMaterial(Texture2D.whiteTexture, true, "ShipFireTrail_Streak (Generated)", TrailHdrBoost);
        return _trailMaterial;
    }

    static Material GetFlameMaterial()
    {
        if (_flameMaterial == null)
            _flameMaterial = ShipVfxUtility.BuildParticleMaterial(ShipVfxUtility.GetFireGlowTexture(), true, "ShipFireTrail_Flame (Generated)", FlameHdrBoost);
        return _flameMaterial;
    }

    static Material GetSmokeMaterial()
    {
        if (_smokeMaterial == null)
            _smokeMaterial = ShipVfxUtility.BuildParticleMaterial(ShipVfxUtility.GetFireSmokeTexture(), false, "ShipFireTrail_Smoke (Generated)");
        return _smokeMaterial;
    }

    static Material GetSparkMaterial()
    {
        if (_sparkMaterial == null)
            _sparkMaterial = ShipVfxUtility.BuildParticleMaterial(ShipVfxUtility.GetSoftDotTexture(), true, "ShipFireTrail_Spark (Generated)", SparkHdrBoost);
        return _sparkMaterial;
    }

    static Material GetEmberMaterial()
    {
        if (_emberMaterial == null)
            _emberMaterial = ShipVfxUtility.BuildParticleMaterial(ShipVfxUtility.GetSoftDotTexture(), true, "ShipFireTrail_Ember (Generated)", EmberHdrBoost);
        return _emberMaterial;
    }
}
