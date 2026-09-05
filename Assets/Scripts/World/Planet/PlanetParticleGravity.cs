using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unity Particle Systems apply <see cref="Physics.gravity"/>, which always points world-down
/// (typically -Y). On a spherical planet that reads as "falling sideways" whenever the emitter
/// is anywhere except the pole. This remaps each system's authored gravityModifier so particles
/// accelerate toward the planet center instead - the same "down" the walker and the surface
/// already use.
///
/// Added automatically by <see cref="SphericalPlanet"/> at play time; no per-effect wiring.
/// Authored gravityModifier values are left intact on the prefabs (so Scene view preview still
/// works) and only swapped at runtime.
/// </summary>
[DefaultExecutionOrder(-50)]
public class PlanetParticleGravity : MonoBehaviour
{
    class Binding
    {
        public ParticleSystem system;
        public ParticleSystem.MinMaxCurve gravity;
        public float multiplier;
    }

    readonly List<Binding> _bindings = new List<Binding>(16);
    readonly HashSet<ParticleSystem> _seen = new HashSet<ParticleSystem>();
    ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[128];

    void OnDisable()
    {
        RestoreWorldGravity();
        _bindings.Clear();
        _seen.Clear();
    }

    void LateUpdate()
    {
        // Discover here (not Update) so bursts spawned later in the frame — e.g. CapsuleImpact
        // Trigger()'d from a coroutine — are remapped the same frame they first appear.
        Discover();
        ApplyPlanetGravity();
    }

    void Discover()
    {
        ParticleSystem[] systems = FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude);

        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem ps = systems[i];
            if (!_seen.Add(ps))
                continue;

            ParticleSystem.MainModule main = ps.main;
            ParticleSystem.MinMaxCurve gravity = main.gravityModifier;
            float multiplier = main.gravityModifierMultiplier;
            if (!HasGravity(gravity, multiplier))
                continue;

            _bindings.Add(new Binding
            {
                system = ps,
                gravity = gravity,
                multiplier = multiplier
            });

            // Disable Physics.gravity so we don't double-apply at the pole (where planet-down
            // and world-down are the same vector).
            main.gravityModifier = 0f;
        }
    }

    void ApplyPlanetGravity()
    {
        SphericalPlanet planet = SphericalPlanet.Instance;
        if (planet == null)
            return;

        Vector3 center = planet.Center;
        float gravityMag = Physics.gravity.sqrMagnitude > 0.0001f
            ? Physics.gravity.magnitude
            : 9.81f;

        for (int i = _bindings.Count - 1; i >= 0; i--)
        {
            Binding binding = _bindings[i];
            ParticleSystem ps = binding.system;
            if (ps == null)
            {
                _bindings.RemoveAt(i);
                continue;
            }

            if (ps.isPaused || ps.particleCount == 0)
                continue;

            ParticleSystem.MainModule main = ps.main;
            float dt = (main.useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime)
                * main.simulationSpeed;
            if (dt <= 0f)
                continue;

            int count = ps.particleCount;
            if (_buffer.Length < count)
                _buffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];

            count = ps.GetParticles(_buffer);
            Transform simSpace = GetSimulationSpaceTransform(ps, main);

            for (int p = 0; p < count; p++)
            {
                float modifier = EvaluateModifier(binding.gravity, _buffer[p]) * binding.multiplier;
                if (modifier == 0f)
                    continue;

                Vector3 worldPos = simSpace != null
                    ? simSpace.TransformPoint(_buffer[p].position)
                    : _buffer[p].position;

                Vector3 toCenter = center - worldPos;
                float sqr = toCenter.sqrMagnitude;
                if (sqr < 0.0001f)
                    continue;

                Vector3 worldDeltaV = toCenter * (gravityMag * modifier * dt / Mathf.Sqrt(sqr));
                if (simSpace != null)
                    worldDeltaV = simSpace.InverseTransformDirection(worldDeltaV);

                _buffer[p].velocity += worldDeltaV;
            }

            ps.SetParticles(_buffer, count);
        }
    }

    void RestoreWorldGravity()
    {
        for (int i = 0; i < _bindings.Count; i++)
        {
            ParticleSystem ps = _bindings[i].system;
            if (ps == null)
                continue;

            ParticleSystem.MainModule main = ps.main;
            main.gravityModifier = _bindings[i].gravity;
        }
    }

    static bool HasGravity(ParticleSystem.MinMaxCurve gravity, float multiplier)
    {
        if (Mathf.Abs(multiplier) < 0.0001f)
            return false;

        switch (gravity.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return Mathf.Abs(gravity.constant) > 0.0001f;
            case ParticleSystemCurveMode.TwoConstants:
                return Mathf.Abs(gravity.constantMin) > 0.0001f
                    || Mathf.Abs(gravity.constantMax) > 0.0001f;
            default:
                return true;
        }
    }

    static float EvaluateModifier(ParticleSystem.MinMaxCurve gravity, ParticleSystem.Particle particle)
    {
        if (gravity.mode == ParticleSystemCurveMode.TwoConstants)
            return (gravity.constantMin + gravity.constantMax) * 0.5f;

        float lifetime = particle.startLifetime;
        float t = lifetime > 0.0001f ? 1f - particle.remainingLifetime / lifetime : 0f;
        return gravity.Evaluate(t);
    }

    static Transform GetSimulationSpaceTransform(ParticleSystem ps, ParticleSystem.MainModule main)
    {
        switch (main.simulationSpace)
        {
            case ParticleSystemSimulationSpace.Local:
                return ps.transform;
            case ParticleSystemSimulationSpace.Custom:
                return main.customSimulationSpace;
            default:
                return null;
        }
    }
}
