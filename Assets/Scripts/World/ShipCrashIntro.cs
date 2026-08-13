using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Crash-landing cinematic for a planet's ShipCapsule (the escape-pod / return-portal object):
/// the capsule appears alone in space and immediately drops, trembling, in a fast, straight line
/// at a fixed angle (never rotates) into whatever position it was placed at in the scene - a hard
/// crash, not a graceful landing. Camera follows the capsule for the whole sequence; other systems
/// (see SceneBootstrap) listen for OnLanded to know when it's safe to spawn the player and hand
/// the camera back.
///
/// One reusable prefab: every planet scene drops this in and wires only <see cref="shipCapsule"/>
/// to its own ShipCapsule instance. Start pose and fall distance are derived from the planet
/// (via SphericalPlanet.GetUpAt), so no other per-scene setup is required.
/// </summary>
public class ShipCrashIntro : MonoBehaviour
{
    [Header("Actor")]
    [SerializeField] Transform shipCapsule;
    [Tooltip("Authored re-entry fire effect (flame shell + embers + flicker light). Passed to " +
        "ShipReentryGlow so the look is edited as a normal prefab instead of generated in code.")]
    [SerializeField] GameObject reentryEffectPrefab;

    [Header("Timing")]
    [Tooltip("Seconds for the crash fall itself. Keep this short - it's a hard crash, not a slow glide.")]
    [SerializeField, Min(0.05f)] float fallDuration = 1.1f;
    [Tooltip("Straight-line speed profile. Linear (or ease-in) reads as a hard crash; ease-in-out reads as a soft landing.")]
    [SerializeField] AnimationCurve fallEase = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Space Start Pose")]
    [Tooltip("How far out (along the planet's radial up at the landing site) the capsule starts, alone in space.")]
    [SerializeField, Min(1f)] float startDistance = 70f;

    [Header("Crash Tremble")]
    [Tooltip("Tremble amplitude applied throughout the fall itself, in world units - sells a rough, out-of-control crash rather than a smooth glide.")]
    [SerializeField, Min(0f)] float fallTrembleAmplitude = 0.12f;
    [Tooltip("How fast the tremble oscillates.")]
    [SerializeField, Min(0.01f)] float fallTrembleFrequency = 18f;

    [Header("Cinematic Camera")]
    [SerializeField] float cinematicOffsetHeight = 16f;
    [SerializeField] float cinematicOffsetBack = 26f;
    [Tooltip("Camera shake on impact. Set duration to 0 to disable.")]
    [SerializeField, Min(0f)] float impactShakeDuration = 0.25f;
    [SerializeField, Min(0f)] float impactShakeMagnitude = 0.6f;

    /// <summary>Raised once the capsule has settled into its authored resting pose.</summary>
    public event Action OnLanded;

    Vector3 _restPosition;
    Quaternion _restRotation;
    bool _hasRun;

    void Start()
    {
        if (shipCapsule == null)
        {
            Debug.LogWarning("ShipCrashIntro: no shipCapsule assigned, skipping cinematic.", this);
            OnLanded?.Invoke();
            return;
        }

        StartCoroutine(RunSequence());
    }

    IEnumerator RunSequence()
    {
        if (_hasRun)
            yield break;
        _hasRun = true;

        _restPosition = shipCapsule.position;
        _restRotation = shipCapsule.rotation;

        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(_restPosition)
            : Vector3.up;

        Vector3 spacePosition = _restPosition + up * startDistance;
        // Fixed angle for the entire sequence - never rotates, this is a crash, not a landing.
        shipCapsule.SetPositionAndRotation(spacePosition, _restRotation);

        // Already burning from the moment it appears - sells "falling through atmosphere" rather
        // than "frozen still in space", even before the fast fall itself begins.
        ShipReentryGlow reentryGlow = ResolveReentryGlow();
        reentryGlow?.Play();

        CameraFollow cameraFollow = ResolveCameraFollow();
        bool hasOriginalOffsets = cameraFollow != null;
        float originalHeight = hasOriginalOffsets ? cameraFollow.OffsetHeight : 0f;
        float originalBack = hasOriginalOffsets ? cameraFollow.OffsetBack : 0f;

        if (cameraFollow != null)
        {
            cameraFollow.SetOffsets(cinematicOffsetHeight, cinematicOffsetBack);
            cameraFollow.SetTarget(shipCapsule);
            // The crash falls too fast for the casual-follow damping to track - snap dead-center instead.
            cameraFollow.SetSnapToTarget(true);
        }

        ShipFireTrail fireTrail = ResolveFireTrail();
        fireTrail?.Play(-up); // falling inward (toward the planet), i.e. opposite of radial "up"

        // Straight line, fixed angle, fast - a hard crash, no tumbling or easing into place. A
        // tremble rides on top the whole way down so it reads as a rough, out-of-control fall.
        float fallTimer = 0f;
        while (fallTimer < fallDuration)
        {
            fallTimer += Time.deltaTime;
            float t = fallEase.Evaluate(Mathf.Clamp01(fallTimer / fallDuration));
            Vector3 basePosition = Vector3.LerpUnclamped(spacePosition, _restPosition, t);
            shipCapsule.position = basePosition + GetFallTrembleOffset(fallTimer);
            yield return null;
        }

        shipCapsule.SetPositionAndRotation(_restPosition, _restRotation);
        fireTrail?.Stop();
        reentryGlow?.Stop();
        ResolveImpactEffect()?.Trigger();
        cameraFollow?.Shake(impactShakeDuration, impactShakeMagnitude);

        if (cameraFollow != null)
        {
            cameraFollow.SetSnapToTarget(false);
            if (hasOriginalOffsets)
                cameraFollow.SetOffsets(originalHeight, originalBack);
        }

        OnLanded?.Invoke();
    }

    Vector3 GetFallTrembleOffset(float time)
    {
        if (fallTrembleAmplitude <= 0f)
            return Vector3.zero;

        float t = time * fallTrembleFrequency;
        float x = Mathf.PerlinNoise(t, 0.17f) - 0.5f;
        float y = Mathf.PerlinNoise(0.42f, t) - 0.5f;
        float z = Mathf.PerlinNoise(t, 0.83f) - 0.5f;
        return new Vector3(x, y, z) * (2f * fallTrembleAmplitude);
    }

    static CameraFollow ResolveCameraFollow()
    {
        CameraFollow follow = FindAnyObjectByType<CameraFollow>();
        if (follow == null && Camera.main != null)
            follow = Camera.main.GetComponent<CameraFollow>();
        return follow;
    }

    ShipFireTrail ResolveFireTrail()
    {
        ShipFireTrail trail = shipCapsule.GetComponent<ShipFireTrail>();
        if (trail == null)
            trail = shipCapsule.gameObject.AddComponent<ShipFireTrail>();
        return trail;
    }

    ShipCrashImpact ResolveImpactEffect()
    {
        ShipCrashImpact impact = shipCapsule.GetComponent<ShipCrashImpact>();
        if (impact == null)
            impact = shipCapsule.gameObject.AddComponent<ShipCrashImpact>();
        return impact;
    }

    ShipReentryGlow ResolveReentryGlow()
    {
        ShipReentryGlow glow = shipCapsule.GetComponent<ShipReentryGlow>();
        if (glow == null)
            glow = shipCapsule.gameObject.AddComponent<ShipReentryGlow>();
        // Only override if wired here - lets ShipReentryGlow keep its own prefab if it was added
        // and configured by hand directly on the ShipCapsule instead.
        if (reentryEffectPrefab != null)
            glow.SetEffectPrefab(reentryEffectPrefab);
        return glow;
    }
}
