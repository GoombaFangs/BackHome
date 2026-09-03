using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Crash-landing cinematic for a planet's player capsule (PlayerDiveDownCapsule): the capsule
/// appears alone in space and immediately drops, trembling, in a fast, straight line at a fixed
/// angle (never rotates) into whatever position it was placed at in the scene - a hard crash, not
/// a graceful landing. Camera follows the capsule for the whole sequence; other systems (see
/// SceneBootstrap) listen for OnLanded to know when it's safe to spawn the player and hand the
/// camera back.
///
/// The capsule is only the thing that falls: the instant it hits the ground, it's hidden (mesh +
/// collider + its own GalaxyGate/PlayerCapsuleBeacon all disabled) and <see cref="portalPrefab"/>
/// is instantiated in its place, embedded into the ground like the crater. The portal inherits the
/// capsule's teleport destination and "home" beacon, so it fully takes over the "stuck in the
/// ground" role while the falling object visually stays the capsule the whole way down.
///
/// One reusable prefab: every planet scene drops this in and wires only <see cref="playerCapsule"/>
/// to its own PlayerDiveDownCapsule instance. Start pose and fall distance are derived from the
/// planet (via SphericalPlanet.GetUpAt/GetSurfacePoint), so no other per-scene setup is required -
/// even if the capsule's authored position isn't already sitting exactly on the ground.
/// </summary>
public class PlayerCrashIntro : MonoBehaviour
{
    [Header("Actor")]
    [FormerlySerializedAs("shipCapsule")]
    [SerializeField] Transform playerCapsule;
    [Tooltip("Authored re-entry fire trail (Hovl streak + sparks/smoke inside the capsule's own " +
        "nested \"TrailVfx\"). Leave empty to use whatever's already nested on the capsule.")]
    [SerializeField] GameObject reentryEffectPrefab;
    [Tooltip("Authored impact burst (Hovl Studio explosion/dust inside ImpactVfx). Passed " +
        "to PlayerCrashImpact so the look is edited as a normal prefab instead of generated in code. " +
        "Leave empty to load Player/Capsule/ImpactVfx from Resources.")]
    [SerializeField] GameObject impactEffectPrefab;
    [Tooltip("Impact crater mesh stamped onto the planet at the landing site. Leave empty to " +
        "load the Crater model from Resources/Player/Capsule.")]
    [SerializeField] GameObject craterPrefab;
    [SerializeField, Min(0.01f)] float craterScale = 6f;
    [Tooltip("How far to sink the crater into the ground, in world units.")]
    [SerializeField] float craterEmbed = 0.2f;

    [Header("Ground Portal")]
    [Tooltip("Swapped in the instant the capsule lands: the capsule itself (mesh + collider + " +
        "its own GalaxyGate/PlayerCapsuleBeacon) is hidden and this prefab becomes the thing left " +
        "embedded in the ground - it inherits the capsule's teleport destination and 'home' " +
        "beacon, so nothing else needs to change. Leave empty to load Portal/Portal from Resources.")]
    [SerializeField] GameObject portalPrefab;
    [Tooltip("How far to sink the portal into the ground, in world units.")]
    [SerializeField] float portalEmbed = 0.4f;
    [Tooltip("Optional: an authored Transform (e.g. the 'Portal' marker PlayerCrashLandingPreview " +
        "creates in the Scene view) marking exactly where the crash should land and where the " +
        "player will spawn (see SceneBootstrap). Drag it around to move the whole landing site - " +
        "the capsule's fall trajectory and rest pose follow it automatically. It's hidden the " +
        "instant the cinematic starts (see HidePortalAnchor) since the real portal is spawned " +
        "fresh once the capsule lands. Leave empty to keep using the capsule's own authored " +
        "position/rotation as the landing site, same as before.")]
    [SerializeField] Transform portalAnchor;

    [Header("Timing")]
    [Tooltip("Seconds for the crash fall itself. Keep this short - it's a hard crash, not a slow glide.")]
    [SerializeField, Min(0.05f)] float fallDuration = 1.1f;
    [Tooltip("Straight-line speed profile. Linear (or ease-in) reads as a hard crash; ease-in-out reads as a soft landing.")]
    [SerializeField] AnimationCurve fallEase = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Space Start Pose")]
    [Tooltip("How far out (along the planet's radial up at the landing site) the capsule starts, alone in space.")]
    [SerializeField, Min(1f)] float startDistance = 70f;
    [Tooltip("Snap the landing spot onto the planet's actual terrain surface (offset by the " +
        "capsule's own half-height so it rests on top of the ground) instead of trusting wherever " +
        "the object happened to be authored in the scene - needed because the capsule is often a " +
        "repurposed/floating placeholder, not already sitting on the ground.")]
    [SerializeField] bool snapLandingToGround = true;
    [Tooltip("Extra clearance added on top of the auto-measured half-height, in world units.")]
    [SerializeField] float extraGroundClearance = 0f;

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

    const string DefaultPortalResource = "Portal/Portal";

    Vector3 _restPosition;
    Quaternion _restRotation;
    bool _hasRun;
    Renderer[] _capsuleRenderers;
    Collider[] _capsuleColliders;

    void Start()
    {
        if (playerCapsule == null)
        {
            Debug.LogWarning("PlayerCrashIntro: no playerCapsule assigned, skipping cinematic.", this);
            OnLanded?.Invoke();
            return;
        }

        HidePortalAnchor();
        StartCoroutine(RunSequence());
    }

    /// <summary>The authored landing marker - falls back to the capsule's own transform when
    /// <see cref="portalAnchor"/> isn't wired up, so existing setups keep working unchanged.</summary>
    Transform LandingAnchor => portalAnchor != null ? portalAnchor : playerCapsule;

    /// <summary>Hides <see cref="portalAnchor"/>'s own visuals/collider/gate the instant the
    /// cinematic starts - it's only meant to be visible/draggable in the Editor before Play, as a
    /// stand-in for wherever the real portal will be spawned once the capsule lands.</summary>
    void HidePortalAnchor()
    {
        if (portalAnchor == null)
            return;

        Renderer[] renderers = portalAnchor.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].enabled = false;

        Collider[] colliders = portalAnchor.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = false;

        GalaxyGate gate = portalAnchor.GetComponent<GalaxyGate>();
        if (gate != null)
            gate.enabled = false;
    }

    IEnumerator RunSequence()
    {
        if (_hasRun)
            yield break;
        _hasRun = true;

        // Snapshot the capsule's own (authored) renderers/colliders before any cinematic
        // components below get a chance to parent runtime VFX under it - so hiding the capsule
        // after landing never accidentally hides the impact/glow/trail effects too.
        _capsuleRenderers = playerCapsule.GetComponentsInChildren<Renderer>(true);
        _capsuleColliders = playerCapsule.GetComponentsInChildren<Collider>(true);

        Transform landingAnchor = LandingAnchor;
        _restPosition = landingAnchor.position;
        _restRotation = landingAnchor.rotation;

        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(_restPosition)
            : Vector3.up;

        if (snapLandingToGround && SphericalPlanet.Instance != null)
        {
            float clearance = GetGroundClearance(up) + extraGroundClearance;
            _restPosition = SphericalPlanet.Instance.GetSurfacePoint(up, clearance);
        }

        Vector3 spacePosition = _restPosition + up * startDistance;
        // Fixed angle for the entire sequence - never rotates, this is a crash, not a landing.
        playerCapsule.SetPositionAndRotation(spacePosition, _restRotation);

        // Already burning from the moment it appears - sells "falling through atmosphere" rather
        // than "frozen still in space", even before the fast fall itself begins.
        PlayerReentryGlow reentryGlow = ResolveReentryGlow();
        reentryGlow?.Play();

        CameraFollow cameraFollow = ResolveCameraFollow();
        bool hasOriginalOffsets = cameraFollow != null;
        float originalHeight = hasOriginalOffsets ? cameraFollow.OffsetHeight : 0f;
        float originalBack = hasOriginalOffsets ? cameraFollow.OffsetBack : 0f;

        if (cameraFollow != null)
        {
            cameraFollow.SetOffsets(cinematicOffsetHeight, cinematicOffsetBack);
            cameraFollow.SetTarget(playerCapsule);
            // The crash falls too fast for the casual-follow damping to track - snap dead-center instead.
            cameraFollow.SetSnapToTarget(true);
        }

        PlayerFireTrail fireTrail = ResolveFireTrail(reentryGlow);
        fireTrail?.Play(-up); // falling inward (toward the planet), i.e. opposite of radial "up"

        // Straight line, fixed angle, fast - a hard crash, no tumbling or easing into place. A
        // tremble rides on top the whole way down so it reads as a rough, out-of-control fall.
        float fallTimer = 0f;
        while (fallTimer < fallDuration)
        {
            fallTimer += Time.deltaTime;
            float t = fallEase.Evaluate(Mathf.Clamp01(fallTimer / fallDuration));
            Vector3 basePosition = Vector3.LerpUnclamped(spacePosition, _restPosition, t);
            playerCapsule.position = basePosition + GetFallTrembleOffset(fallTimer);
            yield return null;
        }

        playerCapsule.SetPositionAndRotation(_restPosition, _restRotation);
        fireTrail?.Stop();
        reentryGlow?.Stop();
        ResolveImpactEffect()?.Trigger();
        cameraFollow?.Shake(impactShakeDuration, impactShakeMagnitude);
        SpawnGroundPortal(up);
        HideCapsuleAfterLanding();

        if (cameraFollow != null)
        {
            cameraFollow.SetSnapToTarget(false);
            if (hasOriginalOffsets)
                cameraFollow.SetOffsets(originalHeight, originalBack);
        }

        OnLanded?.Invoke();
    }

    /// <summary>Half the capsule's extent along the surface normal, so GetSurfacePoint() lands it
    /// resting on top of the ground instead of embedded in it or hovering above it.</summary>
    float GetGroundClearance(Vector3 up)
    {
        MeshFilter filter = playerCapsule.GetComponentInChildren<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            // Project through the mesh's own (unrotated) local bounds rather than the world-space
            // AABB - the world AABB is only a tight fit along axes it happens to be aligned with,
            // so for anything tilted (like this capsule) it overstates the true extent along an
            // arbitrary direction, making it hover higher than it actually needs to.
            Vector3 localUp = filter.transform.InverseTransformDirection(up);
            Vector3 localExtents = filter.sharedMesh.bounds.extents;
            Vector3 scale = filter.transform.lossyScale;
            Vector3 scaledExtents = new Vector3(
                localExtents.x * Mathf.Abs(scale.x),
                localExtents.y * Mathf.Abs(scale.y),
                localExtents.z * Mathf.Abs(scale.z));
            Vector3 absLocalUp = new Vector3(Mathf.Abs(localUp.x), Mathf.Abs(localUp.y), Mathf.Abs(localUp.z));
            return Vector3.Dot(scaledExtents, absLocalUp);
        }

        if (PlayerVfxUtility.TryGetRendererBounds(playerCapsule, out Bounds bounds))
        {
            Vector3 absUp = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
            return Vector3.Dot(bounds.extents, absUp);
        }

        return 0f;
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

    // Prefers whatever's already authored inside the reentry glow's effect prefab (e.g.
    // "FireTrail" baked into TrailVfx) so the trail's sub-emitters live in the same editable
    // place. Only falls back to bolting a fresh component straight onto the capsule if that
    // prefab doesn't have one - keeps this working with no setup at all on a capsule that
    // doesn't have a nested trail effect.
    PlayerFireTrail ResolveFireTrail(PlayerReentryGlow reentryGlow)
    {
        Transform effectRoot = reentryGlow != null ? reentryGlow.EffectRoot : null;
        PlayerFireTrail trail = effectRoot != null ? effectRoot.GetComponentInChildren<PlayerFireTrail>(true) : null;
        if (trail == null)
            trail = playerCapsule.GetComponent<PlayerFireTrail>();
        if (trail == null)
        {
            // Authored particle FX (e.g. Hovl Studio nested inside TrailVfx) already cover the
            // re-entry look - don't bolt the old procedural trail on top of them.
            if (effectRoot != null && effectRoot.GetComponentInChildren<ParticleSystem>(true) != null)
                return null;

            trail = playerCapsule.gameObject.AddComponent<PlayerFireTrail>();
            // No authored "FireTrail" child found anywhere - the sub-emitters above were just
            // built procedurally straight onto the capsule, so tidy them under the same visible
            // root as the reentry glow instead of leaving them loose. Purely cosmetic: everything
            // simulates in world space regardless of where it sits in the hierarchy.
            trail.SetEffectParent(effectRoot);
        }
        return trail;
    }

    PlayerCrashImpact ResolveImpactEffect()
    {
        PlayerCrashImpact impact = playerCapsule.GetComponent<PlayerCrashImpact>();
        if (impact == null)
            impact = playerCapsule.gameObject.AddComponent<PlayerCrashImpact>();
        // Only override if wired here - lets PlayerCrashImpact keep its own prefab if it was added
        // and configured by hand directly on the capsule instead.
        if (impactEffectPrefab != null)
            impact.SetEffectPrefab(impactEffectPrefab);
        impact.SetCrater(craterPrefab, craterScale, craterEmbed);
        return impact;
    }

    /// <summary>Instantiates the portal prefab at the landing site, embedded into the ground like
    /// the crater. Inherits the capsule's own GalaxyGate destination (if any) so the teleport
    /// keeps working, and carries the PlayerCapsuleBeacon so the home marker keeps pointing here -
    /// the portal fully takes over the capsule's "stuck in the ground" role.</summary>
    void SpawnGroundPortal(Vector3 up)
    {
        GameObject prefab = ResolvePortalPrefab();
        if (prefab == null)
        {
            Debug.LogWarning("PlayerCrashIntro: no portal prefab found at Resources/" + DefaultPortalResource + ".", this);
            return;
        }

        Quaternion rotation = PlanetSurfacePose.RotationFromUp(up, PlanetSurfacePose.ExtractYaw(_restRotation, up));
        GameObject portal = Instantiate(prefab, _restPosition, rotation);
        portal.name = prefab.name;
        EmbedInGround(portal.transform, up);

        GalaxyGate sourceGate = playerCapsule.GetComponent<GalaxyGate>();
        GalaxyGate portalGate = portal.GetComponent<GalaxyGate>();
        if (sourceGate != null && portalGate != null)
            portalGate.TargetSceneName = sourceGate.TargetSceneName;

        if (portal.GetComponent<PlayerCapsuleBeacon>() == null)
            portal.AddComponent<PlayerCapsuleBeacon>();

        SphericalPlanet planet = SphericalPlanet.Instance;
        Transform parent = PlanetSurfacePose.GetOrCreateObjectsRoot(planet);
        if (parent != null)
            portal.transform.SetParent(parent, true);
    }

    GameObject ResolvePortalPrefab() =>
        portalPrefab != null ? portalPrefab : Resources.Load<GameObject>(DefaultPortalResource);

    /// <summary>Computes the same landing pose <see cref="SpawnGroundPortal"/> uses at runtime,
    /// without running the cinematic. Used by <see cref="SceneBootstrap"/> so the player always
    /// spawns exactly where the portal ends up (instead of drifting apart from a separately
    /// authored spawn point), and by editor tooling (see PlayerCrashLandingPreview) to preview
    /// the landing site while still in Edit Mode.</summary>
    public bool TryComputeLandingPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        Transform landingAnchor = LandingAnchor;
        if (landingAnchor == null)
            return false;

        Vector3 restPosition = landingAnchor.position;
        Quaternion restRotation = landingAnchor.rotation;
        SphericalPlanet planet = FindAnyObjectByType<SphericalPlanet>();
        Vector3 up = planet != null ? planet.GetUpAt(restPosition) : Vector3.up;

        if (snapLandingToGround && planet != null)
        {
            float clearance = GetGroundClearance(up) + extraGroundClearance;
            restPosition = planet.GetSurfacePoint(up, clearance);
        }

        position = restPosition;
        rotation = PlanetSurfacePose.RotationFromUp(up, PlanetSurfacePose.ExtractYaw(restRotation, up));
        return true;
    }

#if UNITY_EDITOR
    /// <summary>Editor-only accessor so tools (see PlayerCrashLandingPreview) can find the capsule
    /// this cinematic will animate - e.g. to hide it in the Scene view and preview the portal
    /// it leaves behind instead.</summary>
    public Transform PlayerCapsule => playerCapsule;

    /// <summary>Editor-only accessor for <see cref="portalAnchor"/> - see PlayerCrashLandingPreview,
    /// which creates/wires this up the first time and never touches it again once it's assigned.</summary>
    public Transform PortalAnchor => portalAnchor;

    /// <summary>Editor-only: resolves the same portal prefab <see cref="SpawnGroundPortal"/> would use
    /// at runtime (falling back to the default Resources portal), for Scene view previews.</summary>
    public GameObject EditorResolvePortalPrefab() => ResolvePortalPrefab();
#endif

    void EmbedInGround(Transform portal, Vector3 up)
    {
        if (!PlayerVfxUtility.TryGetRendererBounds(portal, out Bounds bounds))
        {
            portal.position -= up * portalEmbed;
            return;
        }

        // Sit the mesh so its center is slightly below the surface, matching how the crater is
        // embedded - reads as "stuck in the ground" rather than resting on top of it.
        float alongUp = Vector3.Dot(bounds.center - _restPosition, up);
        portal.position -= up * (alongUp + portalEmbed);
    }

    /// <summary>Hides exactly the capsule's own (authored) renderers/colliders and disables its
    /// GalaxyGate/PlayerCapsuleBeacon now that the ground portal has taken over - never touches the
    /// runtime impact/glow/trail VFX parented under it, which are left to play out and fade on
    /// their own.</summary>
    void HideCapsuleAfterLanding()
    {
        if (_capsuleRenderers != null)
        {
            for (int i = 0; i < _capsuleRenderers.Length; i++)
                if (_capsuleRenderers[i] != null)
                    _capsuleRenderers[i].enabled = false;
        }

        if (_capsuleColliders != null)
        {
            for (int i = 0; i < _capsuleColliders.Length; i++)
                if (_capsuleColliders[i] != null)
                    _capsuleColliders[i].enabled = false;
        }

        GalaxyGate gate = playerCapsule.GetComponent<GalaxyGate>();
        if (gate != null)
            gate.enabled = false;

        PlayerCapsuleBeacon beacon = playerCapsule.GetComponent<PlayerCapsuleBeacon>();
        if (beacon != null)
            beacon.enabled = false;
    }

    PlayerReentryGlow ResolveReentryGlow()
    {
        PlayerReentryGlow glow = playerCapsule.GetComponent<PlayerReentryGlow>();
        if (glow == null)
            glow = playerCapsule.gameObject.AddComponent<PlayerReentryGlow>();
        // Only override if wired here - lets PlayerReentryGlow keep its own prefab if it was added
        // and configured by hand directly on the capsule instead.
        if (reentryEffectPrefab != null)
            glow.SetEffectPrefab(reentryEffectPrefab);
        return glow;
    }
}
