using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fiery re-entry trail + trailing embers for the ShipCapsule crash cinematic (see
/// <see cref="ShipCrashIntro"/>). Fully procedural: builds its own additive material and a soft
/// round particle sprite in code, so it's a single drop-in component with no external
/// texture/material/shader assets to wire up per scene.
///
/// Usage: <see cref="Play"/> when the fall starts, <see cref="Stop"/> on impact. If the
/// GameObject doesn't already have this component, <see cref="ShipCrashIntro"/> adds it
/// automatically with sensible defaults.
/// </summary>
[RequireComponent(typeof(TrailRenderer))]
public class ShipFireTrail : MonoBehaviour
{
    [Header("Trail Shape")]
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

    [Header("Embers")]
    [SerializeField] bool enableEmbers = true;
    [SerializeField, Min(0f)] float emberRate = 22f;
    [SerializeField, Min(0.01f)] float emberSize = 0.28f;
    [SerializeField] Color emberHotColor = new Color(1f, 0.85f, 0.4f, 1f);
    [SerializeField] Color emberCoolColor = new Color(0.9f, 0.25f, 0.05f, 1f);

    TrailRenderer _trail;
    ParticleSystem _embers;

    static Material _trailMaterial;
    static Material _emberMaterial;

    void Awake()
    {
        _trail = GetComponent<TrailRenderer>();
        ConfigureTrail();
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

    void ConfigureEmbers()
    {
        GameObject emberObject = new GameObject("FireEmbers");
        emberObject.transform.SetParent(transform, false);

        _embers = emberObject.AddComponent<ParticleSystem>();
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
        // Fade RGB to black as well as alpha to zero (see comment on tailColor above -
        // additive blend is forced directly, so relying on alpha alone won't fade it out).
        Gradient fadeGradient = new Gradient();
        fadeGradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fadeGradient;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = _embers.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

        ParticleSystemRenderer renderer = emberObject.GetComponent<ParticleSystemRenderer>();
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = GetEmberMaterial();
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
        if (_embers != null)
        {
            _embers.Clear(true);
            _embers.Play(true);
        }
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

    /// <summary>Call on impact - stops new emission; already-emitted trail/embers fade out naturally.</summary>
    public void Stop()
    {
        if (_trail != null)
            _trail.emitting = false;
        if (_embers != null)
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    static Material GetTrailMaterial()
    {
        if (_trailMaterial == null)
            _trailMaterial = ShipVfxUtility.BuildParticleMaterial(Texture2D.whiteTexture, true, "ShipFireTrail_Streak (Generated)");
        return _trailMaterial;
    }

    static Material GetEmberMaterial()
    {
        if (_emberMaterial == null)
            _emberMaterial = ShipVfxUtility.BuildParticleMaterial(ShipVfxUtility.GetSoftDotTexture(), true, "ShipFireTrail_Ember (Generated)");
        return _emberMaterial;
    }
}
