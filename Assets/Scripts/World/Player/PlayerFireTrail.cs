using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// SuperCasual comet trail for the player capsule crash cinematic (see <see cref="PlayerCrashIntro"/>):
/// a soft teardrop (LineRenderer core + glow) that joins the blue orb at its trailing rim, plus an
/// optional thin motion streak (TrailRenderer). Tuned to sit in the same visual family as the orb -
/// round falloff, modest color, no HDR blowout - rather than a photoreal fireball.
///
/// Authored as a normal child ("FireTrail", with FlameBody/Smoke/Sparks/FireEmbers underneath it)
/// inside the capsule's nested "TrailVfx", so every particle system here is directly editable in
/// the Inspector/Scene view like any hand-authored effect. If any of those children are missing
/// (e.g. this component ends up on a capsule that doesn't have that nested), it falls back to
/// building them procedurally in code so it still works as a drop-in component with nothing to
/// wire up - see <see cref="ConfigureFlameBody"/> etc.
///
/// Usage: <see cref="Play"/> when the fall starts, <see cref="Stop"/> on impact. If the
/// GameObject doesn't already have this component, <see cref="PlayerCrashIntro"/> adds it
/// automatically with sensible defaults.
/// </summary>
[RequireComponent(typeof(TrailRenderer))]
[DefaultExecutionOrder(-80)]
public class PlayerFireTrail : MonoBehaviour
{
    [Header("Trail Shape (thin motion streak)")]
    [SerializeField, Min(0.05f)] float trailTime = 0.45f;
    [SerializeField, Min(0.01f)] float headWidth = 0.48f;
    [SerializeField, Min(0f)] float tailWidthRatio = 0f;
    [SerializeField, Min(0f)] float minVertexDistance = 0.1f;
    [Tooltip("World units of trail pre-seeded behind the capsule the instant Play() is called, so " +
        "the streak appears at full length immediately instead of visibly growing in over the first " +
        "fraction of a second (a TrailRenderer normally starts as a single point).")]
    [SerializeField, Min(0f)] float instantSeedLength = 8f;

    [Header("Trail Color (head -> tail)")]
    [SerializeField] Color coreColor = new Color(1f, 0.90f, 0.68f, 0.78f);
    [SerializeField] Color midColor = new Color(1f, 0.58f, 0.28f, 0.50f);
    // RGB (not just alpha) fades toward black - the additive blend is forced directly via
    // Src/DstBlend, bypassing the shader's own alpha-premultiply path, so alpha alone can't be
    // relied on to fade the tail to nothing.
    [SerializeField] Color tailColor = new Color(0.95f, 0.32f, 0.10f, 0f);

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

    [Header("Fire Line (soft comet teardrop)")]
    [Tooltip("Comet tail behind the orb: a cream core plus a peach envelope, joining at the rim " +
        "and tapering to a point. Light Perlin wobble keeps it alive without reading as fire noise.")]
    [SerializeField] bool enableFireLine = true;
    [SerializeField, Min(0.5f)] float lineLength = 8.5f;
    [SerializeField, Min(0.05f)] float lineCoreWidth = 0.42f;
    [SerializeField, Min(0.05f)] float lineGlowWidth = 1.85f;
    [SerializeField, Range(0f, 0.6f)] float lineFlicker = 0.10f;
    [SerializeField] Color lineCoreColor = new Color(1f, 0.92f, 0.72f, 0.82f);
    [SerializeField] Color lineMidColor = new Color(1f, 0.55f, 0.24f, 0.58f);
    [SerializeField] Color lineTailColor = new Color(0.95f, 0.30f, 0.08f, 0f);
    [SerializeField] bool enableLineTongues = false;

    TrailRenderer _trail;
    ParticleSystem _flameBody;
    ParticleSystem _smoke;
    ParticleSystem _sparks;
    ParticleSystem _embers;
    LineRenderer _lineCore;
    LineRenderer _lineGlow;
    ParticleSystem _lineTongues;
    Material _lineMaterial;
    Vector3 _travelDirection = Vector3.down;
    Vector3 _lastLinePosition;
    bool _linePlaying;
    bool _hasLastLinePosition;
    SkinnedMeshRenderer _followSkin;
    Transform _followBone;

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
        if (enableFireLine)
            ConfigureFireLine();
        Stop();
    }

    void ConfigureTrail()
    {
        _trail.time = trailTime;
        _trail.minVertexDistance = minVertexDistance;
        _trail.widthMultiplier = headWidth;
        _trail.widthCurve = BuildCometWidthCurve(Mathf.Max(0.2f, tailWidthRatio));

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
        _trail.numCapVertices = 8;
        _trail.numCornerVertices = 4;
        _trail.textureMode = LineTextureMode.Stretch;
        _trail.textureScale = Vector2.one;
        _trail.shadowCastingMode = ShadowCastingMode.Off;
        _trail.receiveShadows = false;
        _trail.sortingOrder = TrailSortingOrder;
        _trail.sharedMaterial = GetTrailMaterial();
        SetAllTrailsEmitting(false);
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
        colorOverLifetime.color = PlayerVfxUtility.BuildFadeGradient(true);

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
        colorOverLifetime.color = PlayerVfxUtility.BuildFadeGradient(true);

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
        colorOverLifetime.color = PlayerVfxUtility.BuildFadeGradient(true);

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = _embers.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        ParticleSystemRenderer renderer = _embers.GetComponent<ParticleSystemRenderer>();
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = GetEmberMaterial();
    }

    /// <summary>Two layered LineRenderers that always point opposite the fall: a thin white-hot
    /// core and a wider orange envelope. Vertices are rebuilt every frame with Perlin offset that
    /// grows toward the tail, so the streak licks like flame instead of sitting as a straight
    /// laser. Looks for an authored "Line" child first (same pattern as the particle systems).</summary>
    void ConfigureFireLine()
    {
        Transform lineRoot = transform.Find("Line");
        if (lineRoot == null)
        {
            GameObject go = new GameObject("Line");
            go.transform.SetParent(transform, false);
            lineRoot = go.transform;
        }

        _lineCore = lineRoot.GetComponent<LineRenderer>();
        if (_lineCore == null)
            _lineCore = lineRoot.gameObject.AddComponent<LineRenderer>();

        Transform glowRoot = lineRoot.Find("LineGlow");
        if (glowRoot == null)
        {
            GameObject glowGo = new GameObject("LineGlow");
            glowGo.transform.SetParent(lineRoot, false);
            glowRoot = glowGo.transform;
        }

        _lineGlow = glowRoot.GetComponent<LineRenderer>();
        if (_lineGlow == null)
            _lineGlow = glowRoot.gameObject.AddComponent<LineRenderer>();

        if (_lineMaterial == null)
            _lineMaterial = new Material(GetTrailMaterial()) { name = "PlayerFireTrail_Line" };

        ApplyFireLineStyle(_lineCore, lineCoreWidth, CoreSortingOrder, BuildFireLineGradient(true));
        ApplyFireLineStyle(_lineGlow, lineGlowWidth, GlowSortingOrder, BuildFireLineGradient(false));
        ConfigureFireLineTongues(lineRoot);
        // Prefab-authored LineRenderers stay visible in Prefab Mode (Awake doesn't run there).
        // In Play Mode Stop() hides them until Play().
        if (Application.isPlaying)
            SetFireLineVisible(false);
    }

    /// <summary>Short stretched fire wisps around the main line - breaks the ribbon into
    /// overlapping flame tongues so it doesn't read as a single smooth stroke.</summary>
    void ConfigureFireLineTongues(Transform lineRoot)
    {
        Transform existing = lineRoot.Find("Tongues");
        _lineTongues = existing != null ? existing.GetComponent<ParticleSystem>() : null;
        if (_lineTongues == null)
        {
            GameObject go = new GameObject("Tongues");
            go.transform.SetParent(lineRoot, false);
            _lineTongues = go.AddComponent<ParticleSystem>();
            ApplyTonguesSimulation();
        }

        ApplyTonguesRenderer();
    }

    void ApplyTonguesSimulation()
    {
        ParticleSystem.MainModule main = _lineTongues.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
        main.startColor = new ParticleSystem.MinMaxGradient(lineCoreColor, lineMidColor);
        main.maxParticles = 80;

        ParticleSystem.EmissionModule emission = _lineTongues.emission;
        emission.rateOverTime = 28f;

        ParticleSystem.ShapeModule shape = _lineTongues.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.22f;

        ParticleSystem.InheritVelocityModule inherit = _lineTongues.inheritVelocity;
        inherit.enabled = true;
        inherit.mode = ParticleSystemInheritVelocityMode.Current;
        inherit.curve = new ParticleSystem.MinMaxCurve(0.35f);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = _lineTongues.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = PlayerVfxUtility.BuildFadeGradient(true);

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = _lineTongues.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
    }

    void ApplyTonguesRenderer()
    {
        // Trails without a URP material render as solid magenta bars - keep tongues as
        // stretched fire wisps only, same recipe as the working Sparks red child.
        ParticleSystem.TrailModule trails = _lineTongues.trails;
        trails.enabled = false;

        ParticleSystemRenderer renderer = _lineTongues.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = GetFlameMaterial();
        renderer.trailMaterial = null;
        renderer.sortingOrder = TrailSortingOrder;
    }

    void ApplyFireLineStyle(LineRenderer line, float width, int sortingOrder, Gradient gradient)
    {
        line.positionCount = FireLinePointCount;
        line.useWorldSpace = true;
        line.loop = false;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 8;
        line.numCornerVertices = 4;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.allowOcclusionWhenDynamic = false;
        line.sortingOrder = sortingOrder;
        line.widthMultiplier = width;
        line.widthCurve = BuildCometWidthCurve(sortingOrder == GlowSortingOrder ? 0.18f : 0.28f);
        line.colorGradient = gradient;
        line.sharedMaterial = _lineMaterial;
    }

    Gradient BuildFireLineGradient(bool core)
    {
        Gradient gradient = new Gradient();
        if (core)
        {
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(lineCoreColor, 0f),
                    new GradientColorKey(new Color(1f, 0.78f, 0.42f, 0.72f), 0.28f),
                    new GradientColorKey(lineMidColor, 0.62f),
                    new GradientColorKey(lineTailColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(lineCoreColor.a, 0f),
                    new GradientAlphaKey(0.7f, 0.35f),
                    new GradientAlphaKey(0.28f, 0.75f),
                    new GradientAlphaKey(0f, 1f),
                });
        }
        else
        {
            Color glowHead = new Color(1f, 0.64f, 0.30f, 0.38f);
            Color glowMid = new Color(1f, 0.40f, 0.14f, 0.18f);
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(glowHead, 0f),
                    new GradientColorKey(glowMid, 0.4f),
                    new GradientColorKey(lineTailColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.38f, 0f),
                    new GradientAlphaKey(0.22f, 0.35f),
                    new GradientAlphaKey(0.08f, 0.72f),
                    new GradientAlphaKey(0f, 1f),
                });
        }
        return gradient;
    }

    const int FireLinePointCount = 14;

    void LateUpdate()
    {
        SnapToCharacter();

        if (!_linePlaying || _lineCore == null)
            return;

        Vector3 origin = transform.position;
        Vector3 travel = _travelDirection;
        if (_hasLastLinePosition)
        {
            Vector3 delta = origin - _lastLinePosition;
            if (delta.sqrMagnitude > 0.0001f)
                travel = delta;
        }
        _lastLinePosition = origin;
        _hasLastLinePosition = true;

        if (travel.sqrMagnitude < 0.0001f)
            travel = _travelDirection;
        Vector3 behind = -travel.normalized;

        RebuildFireLine(_lineCore, origin, behind, lineFlicker * 0.45f, 0f);
        RebuildFireLine(_lineGlow, origin, behind, lineFlicker, 17.3f);
    }

    void RebuildFireLine(LineRenderer line, Vector3 origin, Vector3 behind, float flicker, float noiseSeed)
    {
        if (line == null)
            return;

        Vector3 right = Vector3.Cross(behind, Vector3.up);
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.Cross(behind, Vector3.forward);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, behind);

        float time = Time.time;
        int count = line.positionCount;
        for (int i = 0; i < count; i++)
        {
            float t = count > 1 ? i / (float)(count - 1) : 0f;
            // Displacement grows toward the tail so the head stays a tight hot core and the end
            // breaks into licking flame tongues.
            float amp = flicker * lineGlowWidth * t * t;
            float n1 = (Mathf.PerlinNoise(time * 16f + noiseSeed, t * 3.4f) - 0.5f) * 2f;
            float n2 = (Mathf.PerlinNoise(t * 3.4f, time * 21f + noiseSeed + 8f) - 0.5f) * 2f;
            float tongue = Mathf.Sin(time * 13f + t * 10f + noiseSeed) * 0.35f;
            Vector3 offset = right * ((n1 + tongue) * amp) + up * (n2 * amp * 0.75f);
            line.SetPosition(i, origin + behind * (lineLength * t) + offset);
        }
    }

    void SetFireLineVisible(bool visible)
    {
        if (_lineCore != null)
            _lineCore.enabled = visible;
        if (_lineGlow != null)
            _lineGlow.enabled = visible;
    }

    void OnDestroy()
    {
        if (_lineMaterial != null)
            Destroy(_lineMaterial);
    }

    /// <summary>Looks for an already-authored child (e.g. baked into the capsule's nested
    /// "TrailVfx") so Awake() can reuse it as-is instead of stomping hand-tuned values with the
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
    /// PlayerReentryGlow.EffectRoot, "TrailVfx") purely for a tidy hierarchy - all of
    /// them simulate in world space, so moving them in the hierarchy has no effect on where the
    /// particles actually appear. Only meaningful for the procedural fallback path (no authored
    /// "FireTrail" child was found, see PlayerCrashIntro.ResolveFireTrail) - when the children are
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
        SnapToCharacter();
        TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
        {
            trails[i].Clear();
            trails[i].emitting = true;
        }
        SeedInstantTail(travelDirection);
        PlaySystem(_flameBody);
        PlaySystem(_smoke);
        PlaySystem(_sparks);
        PlaySystem(_embers);

        if (travelDirection.sqrMagnitude > 0.0001f)
            _travelDirection = travelDirection;
        _hasLastLinePosition = false;
        _linePlaying = enableFireLine && _lineCore != null;
        SetFireLineVisible(_linePlaying);
        if (_linePlaying && enableLineTongues)
            PlaySystem(_lineTongues);
    }

    /// <summary>Pins this trail to the animated Starbot mesh/hips. The clip moves Armature/Hips,
    /// not the FBX root this object is parented to - without this the streak sits at a fixed local
    /// offset and reads as "below the character" as soon as the dive pose lifts the body.</summary>
    void SnapToCharacter()
    {
        if (_followSkin == null && _followBone == null)
            ResolveFollowTarget();

        if (_followBone != null)
        {
            transform.position = _followBone.position;
            return;
        }

        if (_followSkin != null)
            transform.position = _followSkin.bounds.center;
    }

    void ResolveFollowTarget()
    {
        Transform model = transform.parent;
        if (model == null)
            return;

        _followBone = model.Find("Armature/Hips");
        if (_followBone == null)
        {
            Transform[] children = model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == "Hips")
                {
                    _followBone = children[i];
                    break;
                }
            }
        }

        _followSkin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
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
        TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>(true);

        const int seedSteps = 8;
        for (int i = 0; i < trails.Length; i++)
        {
            TrailRenderer trail = trails[i];
            for (int step = seedSteps; step >= 1; step--)
            {
                float t = (float)step / seedSteps;
                trail.AddPosition(origin + behind * (instantSeedLength * t));
            }
            trail.AddPosition(origin);
        }
    }

    /// <summary>Call on impact - stops new emission; already-emitted trail/particles fade out naturally.</summary>
    public void Stop()
    {
        SetAllTrailsEmitting(false);
        StopSystem(_flameBody);
        StopSystem(_smoke);
        StopSystem(_sparks);
        StopSystem(_embers);
        StopSystem(_lineTongues);
        _linePlaying = false;
        SetFireLineVisible(false);
    }

    static void StopSystem(ParticleSystem ps)
    {
        if (ps != null)
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void SetAllTrailsEmitting(bool emitting)
    {
        TrailRenderer[] trails = GetComponentsInChildren<TrailRenderer>(true);
        for (int i = 0; i < trails.Length; i++)
            trails[i].emitting = emitting;
    }

    const int GlowSortingOrder = 0;
    const int TrailSortingOrder = 1;
    const int CoreSortingOrder = 2;

    /// <summary>SuperCasual comet silhouette: hairline join at the orb rim, juicy body just
    /// behind, then a smooth taper to a point. <paramref name="head"/> is the width at t=0
    /// (the rim) as a fraction of the max width.</summary>
    static AnimationCurve BuildCometWidthCurve(float head)
    {
        return new AnimationCurve(
            new Keyframe(0f, head, 0f, 6f),
            new Keyframe(0.11f, 1f, 0f, 0f),
            new Keyframe(0.45f, 0.48f, -1.15f, -1.15f),
            new Keyframe(1f, 0f, -0.35f, 0f));
    }

    // Modest over-brighten so the comet reads as a glow, not a blown-out bloom slab. The orb
    // itself sits around HDR ~2.4; staying well below that keeps it the hero silhouette.
    const float TrailHdrBoost = 1.15f;
    const float FlameHdrBoost = 1.2f;
    const float SparkHdrBoost = 1.6f;
    const float EmberHdrBoost = 1.15f;

    static Material GetTrailMaterial()
    {
        if (_trailMaterial == null)
        {
            _trailMaterial = Resources.Load<Material>(PlayerDiveDownCapsulePaths.ResourcesReentryTrailMaterial);
            if (_trailMaterial == null)
                _trailMaterial = Resources.Load<Material>(PlayerDiveDownCapsulePaths.ResourcesReentryFlameMaterial);
            if (_trailMaterial == null)
                _trailMaterial = PlayerVfxUtility.BuildParticleMaterial(PlayerVfxUtility.GetSoftDotTexture(), false, "PlayerFireTrail_Streak (Generated)", TrailHdrBoost);
        }
        return _trailMaterial;
    }

    static Material GetFlameMaterial()
    {
        if (_flameMaterial == null)
            _flameMaterial = PlayerVfxUtility.BuildParticleMaterial(PlayerVfxUtility.GetFireGlowTexture(), true, "PlayerFireTrail_Flame (Generated)", FlameHdrBoost);
        return _flameMaterial;
    }

    static Material GetSmokeMaterial()
    {
        if (_smokeMaterial == null)
            _smokeMaterial = PlayerVfxUtility.BuildParticleMaterial(PlayerVfxUtility.GetFireSmokeTexture(), false, "PlayerFireTrail_Smoke (Generated)");
        return _smokeMaterial;
    }

    static Material GetSparkMaterial()
    {
        if (_sparkMaterial == null)
            _sparkMaterial = PlayerVfxUtility.BuildParticleMaterial(PlayerVfxUtility.GetSoftDotTexture(), true, "PlayerFireTrail_Spark (Generated)", SparkHdrBoost);
        return _sparkMaterial;
    }

    static Material GetEmberMaterial()
    {
        if (_emberMaterial == null)
            _emberMaterial = PlayerVfxUtility.BuildParticleMaterial(PlayerVfxUtility.GetSoftDotTexture(), true, "PlayerFireTrail_Ember (Generated)", EmberHdrBoost);
        return _emberMaterial;
    }
}
