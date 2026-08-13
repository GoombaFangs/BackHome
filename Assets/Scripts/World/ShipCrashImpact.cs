using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One-shot impact burst for the ShipCapsule crash cinematic (see <see cref="ShipCrashIntro"/>):
/// dust cloud + fiery sparks + tumbling rock debris, all firing the instant the capsule hits the
/// ground. Fully procedural (materials, debris mesh, particle sprite all built in code) so, like
/// <see cref="ShipFireTrail"/>, it's a single drop-in component with nothing to wire up per scene.
///
/// Usage: call <see cref="Trigger"/> exactly once, at the moment of impact. If the GameObject
/// doesn't already have this component, <see cref="ShipCrashIntro"/> adds it automatically.
/// </summary>
public class ShipCrashImpact : MonoBehaviour
{
    [Header("Dust Cloud")]
    [SerializeField, Min(0)] int dustCount = 22;
    [SerializeField] Vector2 dustSpeed = new Vector2(1.5f, 3.5f);
    [SerializeField] Vector2 dustSize = new Vector2(0.7f, 1.6f);
    [SerializeField] Vector2 dustLifetime = new Vector2(0.7f, 1.2f);
    [SerializeField] Color dustColor = new Color(0.5f, 0.42f, 0.34f, 0.55f);

    [Header("Spark Burst")]
    [SerializeField, Min(0)] int sparkCount = 32;
    [SerializeField] Vector2 sparkSpeed = new Vector2(3.5f, 7.5f);
    [SerializeField] Vector2 sparkSize = new Vector2(0.08f, 0.22f);
    [SerializeField] Vector2 sparkLifetime = new Vector2(0.25f, 0.55f);
    [SerializeField] Color sparkHotColor = new Color(1f, 0.9f, 0.5f, 1f);
    [SerializeField] Color sparkCoolColor = new Color(1f, 0.45f, 0.08f, 1f);
    [SerializeField, Min(0f)] float sparkGravity = 2.2f;

    [Header("Rock Debris")]
    [SerializeField, Min(0)] int debrisCount = 12;
    [SerializeField] Vector2 debrisSpeed = new Vector2(2f, 5f);
    [SerializeField] Vector2 debrisSize = new Vector2(0.1f, 0.28f);
    [SerializeField] Vector2 debrisLifetime = new Vector2(0.8f, 1.5f);
    [SerializeField] Color debrisColor = new Color(0.28f, 0.24f, 0.2f, 1f);
    [SerializeField, Min(0f)] float debrisGravity = 3.2f;

    ParticleSystem _dust;
    ParticleSystem _sparks;
    ParticleSystem _debris;
    bool _built;

    static Material _softAdditiveMaterial;
    static Material _softAlphaMaterial;
    static Material _debrisMaterial;

    void Awake()
    {
        Build();
    }

    void Build()
    {
        if (_built)
            return;
        _built = true;

        _dust = BuildBurstSystem("ImpactDust", ParticleSystemRenderMode.Billboard,
            dustCount, dustSpeed, dustSize, dustLifetime,
            new ParticleSystem.MinMaxGradient(dustColor), gravity: 0.4f,
            GetSoftAlphaMaterial(), fadeToBlack: false, drag: 1.4f);

        _sparks = BuildBurstSystem("ImpactSparks", ParticleSystemRenderMode.Billboard,
            sparkCount, sparkSpeed, sparkSize, sparkLifetime,
            new ParticleSystem.MinMaxGradient(sparkHotColor, sparkCoolColor), gravity: sparkGravity,
            GetSoftAdditiveMaterial(), fadeToBlack: true, drag: 0f);

        _debris = BuildBurstSystem("ImpactDebris", ParticleSystemRenderMode.Mesh,
            debrisCount, debrisSpeed, debrisSize, debrisLifetime,
            new ParticleSystem.MinMaxGradient(Color.white), gravity: debrisGravity,
            GetDebrisMaterial(), fadeToBlack: false, drag: 0f,
            mesh: ShipVfxUtility.GetCubeMesh(), tumble: true, fade: false);
    }

    /// <summary>Fires the dust/spark/debris burst once. Safe to call multiple times.</summary>
    public void Trigger()
    {
        Build();
        _dust?.Play(true);
        _sparks?.Play(true);
        _debris?.Play(true);
    }

    ParticleSystem BuildBurstSystem(string childName, ParticleSystemRenderMode renderMode,
        int count, Vector2 speed, Vector2 size, Vector2 lifetime, ParticleSystem.MinMaxGradient color,
        float gravity, Material material, bool fadeToBlack, float drag, Mesh mesh = null, bool tumble = false,
        bool fade = true)
    {
        GameObject go = new GameObject(childName);
        go.transform.SetParent(transform, false);

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

    static Material GetSoftAdditiveMaterial()
    {
        if (_softAdditiveMaterial == null)
            _softAdditiveMaterial = ShipVfxUtility.BuildParticleMaterial(ShipVfxUtility.GetSoftDotTexture(), true, "ShipCrashImpact_Additive (Generated)");
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
