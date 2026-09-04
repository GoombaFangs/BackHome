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
/// collider + its own GalaxyGate/PlayerCapsuleBeacon all disabled) and the ground portal is placed
/// at that exact crash point (reusing <see cref="portalAnchor"/> when wired, otherwise
/// instantiating <see cref="portalPrefab"/>). The portal inherits the capsule's teleport
/// destination and "home" beacon, so it fully takes over the "stuck in the ground" role while the
/// falling object visually stays the capsule the whole way down.
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
        "Leave empty to load ImpactVfx from Resources (see PlayerDiveDownCapsulePaths).")]
    [SerializeField] GameObject impactEffectPrefab;

    [Header("Ground Portal")]
    [Tooltip("Swapped in the instant the capsule lands: the capsule itself (mesh + collider + " +
        "its own GalaxyGate/PlayerCapsuleBeacon) is hidden and this prefab becomes the thing left " +
        "embedded in the ground at the crash point (only when no portalAnchor is wired). Leave " +
        "empty to load Portal/Portal from Resources.")]
    [SerializeField] GameObject portalPrefab;
    [Tooltip("How far to sink the portal into the ground, in world units.")]
    [SerializeField] float portalEmbed = 0.4f;
    [Tooltip("Optional: an authored Transform (e.g. the 'Portal' marker PlayerCrashLandingPreview " +
        "creates in the Scene view) marking where the crash should land. Drag it around to move " +
        "the whole landing site - the capsule's fall trajectory follows it automatically. Hidden " +
        "during the fall and repositioned to the exact crash point when the capsule lands. Leave " +
        "empty to instantiate a fresh portal prefab at the crash site instead.")]
    [SerializeField] Transform portalAnchor;

    [Header("Player Spawn")]
    [Tooltip("The player spawns at a random point within this distance of the ground portal, " +
        "instead of exactly on top of it. Also drives the environment exclusion disk around the " +
        "landing site. This is the only place in the project to tune that radius.")]
    [SerializeField, Min(0f)] float spawnRadius = 4f;

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

    /// <summary>The ground portal left behind at the crash site once the cinematic finishes.
    /// SceneBootstrap reads <see cref="TryComputePlayerSpawnPose"/> to scatter the player spawn.</summary>
    public Transform GroundPortal => _groundPortal;

    /// <summary>World-space scatter radius for the initial player spawn around <see cref="GroundPortal"/>.</summary>
    public float SpawnRadius => spawnRadius;

    const string DefaultPortalResource = "Portal/Portal";

    Transform _groundPortal;
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
        ApplyExclusionSpawnRadius(portalAnchor);
        StartCoroutine(RunSequence());
    }

    /// <summary>The authored landing marker - falls back to the capsule's own transform when
    /// <see cref="portalAnchor"/> isn't wired up, so existing setups keep working unchanged.</summary>
    Transform LandingAnchor => portalAnchor != null ? portalAnchor : playerCapsule;

    /// <summary>Hides <see cref="portalAnchor"/> during the fall - it's only meant to be visible
    /// and draggable in the Editor before Play. Re-enabled at the crash site by
    /// <see cref="SpawnGroundPortal"/>.</summary>
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

        SphericalPlanet planet = ResolvePlanet();
        Vector3 up = planet != null ? planet.GetUpAt(_restPosition) : Vector3.up;

        if (snapLandingToGround && planet != null)
        {
            float clearance = GetGroundClearance(up) + extraGroundClearance;
            _restPosition = planet.GetSurfacePoint(up, clearance);
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

        PlayerDiveAnimation diveAnimation = ResolveDiveAnimation();
        diveAnimation?.Play();

        // Straight line, fixed angle, fast - a hard crash, no tumbling or easing into place. A
        // tremble rides on top the whole way down so it reads as a rough, out-of-control fall.
        float fallTimer = 0f;
        while (fallTimer < fallDuration)
        {
            fallTimer += Time.deltaTime;
            float t = fallEase.Evaluate(Mathf.Clamp01(fallTimer / fallDuration));
            Vector3 basePosition = Vector3.LerpUnclamped(spacePosition, _restPosition, t);
            playerCapsule.position = basePosition + GetFallTrembleOffset(fallTimer);
            diveAnimation?.Tick(Time.deltaTime);
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

        // Solid bounds only (no particle/line/trail renderers) - a capsule that's purely a
        // fire-trail VFX rig (no body mesh at all, like PlayerDiveDownCapsule) has no solid
        // renderers, so this correctly falls through to 0 instead of using the fire trail/sparks/
        // smoke effects' simulated bounds, which can be many units across and would otherwise
        // shove the landing site far above the actual ground.
        if (PlayerVfxUtility.TryGetSolidRendererBounds(playerCapsule, out Bounds bounds))
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

    /// <summary>Single place both <see cref="RunSequence"/> and <see cref="TryComputeLandingSite"/>
    /// resolve the planet from - avoids the two drifting to different lookup strategies.</summary>
    static SphericalPlanet ResolvePlanet() =>
        SphericalPlanet.Instance != null ? SphericalPlanet.Instance : FindAnyObjectByType<SphericalPlanet>();

    static CameraFollow ResolveCameraFollow()
    {
        CameraFollow follow = FindAnyObjectByType<CameraFollow>();
        if (follow == null && Camera.main != null)
            follow = Camera.main.GetComponent<CameraFollow>();
        return follow;
    }

    // Uses the authored trail already nested on the Starbot (PlayerDiveDownCapsule →
    // Starbot_Animation_Dive_Down_and_Land → Trail). Never AddComponent a second PlayerFireTrail
    // onto the capsule root - that spawned a duplicate streak (FlameBody/Line/etc.) at the
    // capsule origin, offset from the character.
    PlayerFireTrail ResolveFireTrail(PlayerReentryGlow reentryGlow)
    {
        PlayerFireTrail trail = playerCapsule.GetComponentInChildren<PlayerFireTrail>(true);
        if (trail != null)
            return trail;

        Transform effectRoot = reentryGlow != null ? reentryGlow.EffectRoot : null;
        if (effectRoot != null)
            trail = effectRoot.GetComponentInChildren<PlayerFireTrail>(true);
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
        return impact;
    }

    /// <summary>Places the ground portal at the capsule's actual crash point. Reuses
    /// <see cref="portalAnchor"/> when wired (the editor preview marker), otherwise instantiates
    /// <see cref="portalPrefab"/>. Inherits the capsule's GalaxyGate destination and carries
    /// PlayerCapsuleBeacon so the home marker keeps pointing here.</summary>
    void SpawnGroundPortal(Vector3 up)
    {
        Vector3 crashPosition = playerCapsule.position;
        Quaternion crashRotation = playerCapsule.rotation;

        GameObject portal;
        if (portalAnchor != null)
        {
            portal = portalAnchor.gameObject;
            EnablePortalAnchor(portal);
        }
        else
        {
            GameObject prefab = ResolvePortalPrefab();
            if (prefab == null)
            {
                Debug.LogWarning("PlayerCrashIntro: no portal prefab found at Resources/" + DefaultPortalResource + ".", this);
                return;
            }

            portal = Instantiate(prefab);
            portal.name = prefab.name;
        }

        if (!TryApplyGroundPortalPose(portal.transform, crashPosition, crashRotation))
            portal.transform.SetPositionAndRotation(crashPosition, crashRotation);

        GalaxyGate sourceGate = playerCapsule.GetComponent<GalaxyGate>();
        GalaxyGate portalGate = portal.GetComponent<GalaxyGate>();
        if (sourceGate != null && portalGate != null)
            portalGate.TargetSceneName = sourceGate.TargetSceneName;

        if (portal.GetComponent<PlayerCapsuleBeacon>() == null)
            portal.AddComponent<PlayerCapsuleBeacon>();

        if (portal.GetComponent<PlanetEnvironmentExclusionZone>() == null)
            portal.AddComponent<PlanetEnvironmentExclusionZone>();

        ApplyExclusionSpawnRadius(portal.transform);

        Transform parent = PlanetSurfacePose.GetOrCreateObjectsRoot(ResolvePlanet());
        if (parent != null)
            portal.transform.SetParent(parent, true);

        _groundPortal = portal.transform;
    }

    /// <summary>Sticks a portal transform flush to the ground near <paramref name="nearWorldPoint"/>,
    /// aligned to the local surface normal and slightly embedded along <see cref="portalEmbed"/>.</summary>
    public bool TryApplyGroundPortalPose(Transform portal, Vector3 nearWorldPoint, Quaternion yawSource)
    {
        if (portal == null)
            return false;

        if (!PlanetSurfacePose.TryResolvePlanet(portal, out SphericalPlanet planet, out PlanetTileMap tiles))
            planet = ResolvePlanet();

        if (planet == null)
            return false;

        Vector3 radial = nearWorldPoint - planet.Center;
        if (radial.sqrMagnitude < 0.0001f)
            radial = planet.transform.up;
        else
            radial.Normalize();

        float yaw = PlanetSurfacePose.ExtractYaw(yawSource, radial);
        Vector3 groundPosition;
        Vector3 groundUp;
        Quaternion rotation;

        if (!PlanetSurfacePose.TryGetPose(planet, tiles, radial, yaw, 0f, out groundPosition, out rotation, out groundUp))
        {
            groundPosition = tiles != null && tiles.ProvidesWalkSurface
                ? tiles.GetWalkSurfacePoint(radial, 0f)
                : planet.GetSurfacePoint(radial, 0f);
            groundUp = tiles != null && tiles.ProvidesWalkSurface
                ? tiles.GetWalkSurfaceNormal(radial)
                : planet.GetUpAt(groundPosition);
            rotation = PlanetSurfacePose.RotationFromUp(groundUp, yaw);
        }

        if (Application.isPlaying
            && PlanetSurfacePose.TrySampleGroundBelow(
                groundPosition + groundUp * 2f, groundUp, 12f, 0f, out Vector3 rayPosition, out Vector3 rayNormal))
        {
            groundPosition = rayPosition;
            groundUp = rayNormal;
            rotation = PlanetSurfacePose.RotationFromUp(groundUp, yaw);
        }

        portal.SetPositionAndRotation(groundPosition, rotation);
        PortalGroundSnap.Snap(portal, groundPosition, groundUp, portalEmbed);
        return true;
    }

    /// <summary>Computes where the player should spawn: scattered within <see cref="spawnRadius"/>
    /// of the ground portal (never closer than the portal's own re-teleport radius). Used by
    /// <see cref="SceneBootstrap"/> after the crash cinematic finishes.</summary>
    public bool TryComputePlayerSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        Transform portal = _groundPortal != null ? _groundPortal : portalAnchor;
        if (portal == null)
            return false;

        Vector3 anchorPosition = portal.position;
        Quaternion anchorRotation = portal.rotation;

        if (spawnRadius <= 0.0001f)
        {
            position = anchorPosition;
            rotation = anchorRotation;
            return true;
        }

        if (!PlanetSurfacePose.TryResolvePlanet(portal, out SphericalPlanet planet, out PlanetTileMap tiles))
        {
            position = anchorPosition;
            rotation = anchorRotation;
            return true;
        }

        float minRadius = GalaxyGate.GetSafeMinSpawnRadius(portal.GetComponent<GalaxyGate>(), spawnRadius);
        if (PlanetRadialSampling.TryGetRandomPointNear(planet, anchorPosition, minRadius, spawnRadius, out Vector3 direction)
            && PlanetSurfacePose.TryGetPose(
                planet, tiles, direction, UnityEngine.Random.Range(0f, 360f), PlanetSurfacePose.DefaultHover,
                out position, out rotation, out _))
        {
            return true;
        }

        position = anchorPosition;
        rotation = anchorRotation;
        return true;
    }

    void ApplyExclusionSpawnRadius(Transform portal)
    {
        if (portal == null)
            return;

        PlanetEnvironmentExclusionZone zone = portal.GetComponent<PlanetEnvironmentExclusionZone>();
        if (zone != null)
            zone.SetPlayerSpawnRadius(spawnRadius);
    }

    /// <summary>Landing site for the portal itself — on the walkable surface with no capsule
    /// clearance. Used by editor preview tools instead of <see cref="TryComputeLandingSite"/>.</summary>
    public bool TryComputePortalLandingSite(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        Transform landingAnchor = LandingAnchor;
        if (landingAnchor == null)
            return false;

        if (!PlanetSurfacePose.TryResolvePlanet(landingAnchor, out SphericalPlanet planet, out PlanetTileMap tiles))
            return false;

        Vector3 radial = landingAnchor.position - planet.Center;
        if (radial.sqrMagnitude < 0.0001f)
            radial = planet.transform.up;
        else
            radial.Normalize();

        float yaw = PlanetSurfacePose.ExtractYaw(landingAnchor.rotation, radial);
        if (!PlanetSurfacePose.TryGetPose(planet, tiles, radial, yaw, 0f, out position, out rotation, out _))
            return false;

        return true;
    }

    void EnablePortalAnchor(GameObject portal)
    {
        Renderer[] renderers = portal.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null)
                renderers[i].enabled = true;

        Collider[] colliders = portal.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null)
                colliders[i].enabled = true;

        GalaxyGate gate = portal.GetComponent<GalaxyGate>();
        if (gate != null)
            gate.enabled = true;
    }

    GameObject ResolvePortalPrefab() =>
        portalPrefab != null ? portalPrefab : Resources.Load<GameObject>(DefaultPortalResource);

    /// <summary>The fixed landing/crash site itself - where the capsule comes to rest and the
    /// ground portal ends up (see <see cref="SpawnGroundPortal"/>). This is what the editor
    /// preview tool (see PlayerCrashLandingPreview) seeds its auto-created portalAnchor marker from.</summary>
    public bool TryComputeLandingSite(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        Transform landingAnchor = LandingAnchor;
        if (landingAnchor == null)
            return false;

        Vector3 restPosition = landingAnchor.position;
        Quaternion restRotation = landingAnchor.rotation;
        bool hasPlanet = PlanetSurfacePose.TryResolvePlanet(landingAnchor, out SphericalPlanet planet, out _);
        Vector3 up = hasPlanet ? planet.GetUpAt(restPosition) : Vector3.up;

        if (snapLandingToGround && hasPlanet)
        {
            float clearance = GetGroundClearance(up) + extraGroundClearance;
            restPosition = planet.GetSurfacePoint(up, clearance);
        }

        position = restPosition;
        rotation = PlanetSurfacePose.RotationFromUp(up, PlanetSurfacePose.ExtractYaw(restRotation, up));
        return true;
    }

#if UNITY_EDITOR
    void OnValidate() => ApplyExclusionSpawnRadius(portalAnchor);

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

    /// <summary>Editor-only: snaps an authored portal marker flush to the ground near a landing site.</summary>
    public bool EditorApplyGroundPortalPose(Transform portal, Vector3 nearWorldPoint, Quaternion yawSource) =>
        TryApplyGroundPortalPose(portal, nearWorldPoint, yawSource);
#endif

    PlayerDiveAnimation ResolveDiveAnimation()
    {
        PlayerDiveAnimation dive = playerCapsule.GetComponent<PlayerDiveAnimation>();
        if (dive == null)
            dive = playerCapsule.GetComponentInChildren<PlayerDiveAnimation>(true);
        if (dive == null)
            dive = playerCapsule.gameObject.AddComponent<PlayerDiveAnimation>();
        return dive;
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

        DisableCapsuleGateAndBeacon();
    }

    void DisableCapsuleGateAndBeacon()
    {
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
