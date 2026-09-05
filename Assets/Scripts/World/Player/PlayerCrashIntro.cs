using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Crash-landing cinematic: the live Player transform falls from space onto the planet. The
/// visible mesh during the fall is the nested Starbot FBX from PlayerDiveDownCapsule (the rig the
/// dive/land clips were authored on). The gameplay Player mesh stays hidden until the land clip
/// finishes (or is skipped by move input in its last 1.5s), then locomotion unlocks. The authored
/// fire trail rides on that cinematic mesh.
///
/// A portal then falls from space toward the crash site and only plants on the ground after the
/// player has walked clear of that disk.
///
/// One reusable prefab: every planet scene drops this in and wires only <see cref="playerCapsule"/>
/// to its own PlayerDiveDownCapsule instance. Start pose and fall distance are derived from the
/// planet (via SphericalPlanet.GetUpAt/GetSurfacePoint).
/// </summary>
[DefaultExecutionOrder(-500)]
public class PlayerCrashIntro : MonoBehaviour
{
    [Header("Actor")]
    [FormerlySerializedAs("shipCapsule")]
    [SerializeField] Transform playerCapsule;
    [Tooltip("Optional override. Leave empty to use SceneBootstrap's player prefab, then Resources/Player/Player.")]
    [SerializeField] GameObject playerPrefab;
    [Tooltip("Authored impact burst (Hovl Studio explosion/dust inside ImpactVfx). Passed " +
        "to PlayerCrashImpact so the look is edited as a normal prefab instead of generated in code. " +
        "Leave empty to load ImpactVfx from Resources (see PlayerDiveDownCapsulePaths).")]
    [SerializeField] GameObject impactEffectPrefab;

    [Header("Landing Site")]
    [Tooltip("Clears streamed planet environment (grass/trees/rocks) in a disk around the crash " +
        "site. Also used if SceneBootstrap has to place the player without running this cinematic.")]
    [SerializeField, Min(0f)] float spawnRadius = 4f;

    [Header("Incoming Portal")]
    [Tooltip("Portal that falls in after the player. Leave empty to load Portal/Portal from Resources.")]
    [SerializeField] GameObject portalPrefab;
    [Tooltip("How far to sink the planted portal into the ground, in world units.")]
    [SerializeField] float portalEmbed = 0.4f;
    [Tooltip("Seconds for the portal's drop onto the ground, after the player has walked clear.")]
    [SerializeField, Min(0.05f)] float portalFallDuration = 0.5f;
    [Tooltip("How far above the plant point the portal starts. Keep this much shorter than the player crash so it plops in from nearby, not from space.")]
    [SerializeField, Min(1f)] float portalStartDistance = 14f;
    [Tooltip("How far the player must walk from the crash site before the portal falls in. Keep this just outside the gate trigger.")]
    [SerializeField, Min(0.5f)] float portalClearRadius = 4f;
    [Tooltip("Impact burst played the instant the portal plants. Leave empty to load Portal/PortalImpactVfx from Resources.")]
    [SerializeField] GameObject portalImpactVfxPrefab;
    [Tooltip("Scene the planted portal teleports to. The Resources portal prefab is shared with the ship.")]
    [SerializeField] string portalTargetScene = "SpaceShip";

    [Header("Timing")]
    [Tooltip("Seconds for the crash fall itself. Keep this short - it's a hard crash, not a slow glide.")]
    [SerializeField, Min(0.05f)] float fallDuration = 1.1f;
    [Tooltip("Straight-line speed profile. Linear (or ease-in) reads as a hard crash; ease-in-out reads as a soft landing.")]
    [SerializeField] AnimationCurve fallEase = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Space Start Pose")]
    [Tooltip("How far out (along the planet's radial up at the landing site) the capsule starts, alone in space.")]
    [SerializeField, Min(1f)] float startDistance = 70f;
    [Tooltip("Snap the landing spot onto the planet surface instead of trusting wherever the " +
        "capsule was authored in the scene.")]
    [SerializeField] bool snapLandingToGround = true;
    [Tooltip("Extra world-space clearance above the planet surface at rest.")]
    [SerializeField] float extraGroundClearance = 0f;

    [Header("Crash Tremble")]
    [Tooltip("Tremble amplitude applied throughout the fall itself, in world units - sells a rough, out-of-control crash rather than a smooth glide.")]
    [SerializeField, Min(0f)] float fallTrembleAmplitude = 0.12f;
    [Tooltip("How fast the tremble oscillates.")]
    [SerializeField, Min(0.01f)] float fallTrembleFrequency = 18f;

    [Header("Cinematic Camera")]
    [Tooltip("Tight opening shot, so the falling player fills the frame.")]
    [SerializeField] float cinematicStartOffsetHeight = 3.2f;
    [SerializeField] float cinematicStartOffsetBack = 5.5f;
    [Tooltip("Wide crash shot as the player nears the ground - the previous cinematic framing.")]
    [SerializeField] float cinematicOffsetHeight = 16f;
    [SerializeField] float cinematicOffsetBack = 26f;
    [Tooltip("Pulls the camera from the opening close-up to the wide crash shot. 0 = tight, 1 = wide.")]
    [SerializeField] AnimationCurve cinematicZoomOut = DefaultCinematicZoomOut();
    [Tooltip("Camera jolt on landing. Set duration to 0 to disable.")]
    [SerializeField, Min(0f)] float impactShakeDuration = 0.5f;
    [SerializeField, Min(0f)] float impactShakeMagnitude = 5.5f;

    /// <summary>Raised as soon as the falling Player exists, before the fall starts.
    /// SceneBootstrap binds camera / HUD here — the same transform later walks.</summary>
    public event Action<Transform> OnPlayerReady;

    /// <summary>Raised once locomotion is unlocked (land clip finished or skipped by move).</summary>
    public event Action OnLanded;

    /// <summary>World-space radius of the environment exclusion disk around the crash site.</summary>
    public float SpawnRadius => spawnRadius;

    const string DefaultPortalResource = "Portal/Portal";
    const string DefaultPortalImpactResource = "Portal/PortalImpactVfx";

    Transform _landingSite;
    Transform _incomingPortal;
    Transform _player;
    PlayerLandIntro _actor;
    PlayerDiveAnimation _diveAnimation;
    PlayerFireTrail _attachedTrail;
    Transform _diveModel;
    Renderer[] _hiddenPlayerRenderers;
    Vector3 _restPosition;
    Quaternion _restRotation;
    bool _hasRun;
    Renderer[] _capsuleRenderers;
    Collider[] _capsuleColliders;

    void Awake()
    {
        // The dive capsule used to carry a GalaxyGate. Strip only that leftover so the incoming
        // ground portal can keep its own gate when it plants later.
        StripCapsuleGates();
        DisableCapsuleTriggers();
    }

    void Start()
    {
        if (playerCapsule == null)
        {
            Debug.LogWarning("PlayerCrashIntro: no playerCapsule assigned, skipping cinematic.", this);
            if (TrySpawnPlayer(transform.position, transform.rotation))
                OnPlayerReady?.Invoke(_player);
            OnLanded?.Invoke();
            return;
        }

        StartCoroutine(RunSequence());
    }

    void StripCapsuleGates()
    {
        if (playerCapsule == null)
            return;

        GalaxyGate[] gates = playerCapsule.GetComponentsInChildren<GalaxyGate>(true);
        for (int i = 0; i < gates.Length; i++)
        {
            GalaxyGate gate = gates[i];
            if (gate == null)
                continue;
            gate.enabled = false;
            Destroy(gate);
        }
    }

    void DisableCapsuleTriggers()
    {
        if (playerCapsule == null)
            return;

        Collider[] colliders = playerCapsule.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    Transform LandingAnchor => playerCapsule;

    IEnumerator RunSequence()
    {
        if (_hasRun)
            yield break;
        _hasRun = true;

        Transform landingAnchor = LandingAnchor;
        _restPosition = landingAnchor.position;
        _restRotation = landingAnchor.rotation;

        SphericalPlanet planet = ResolvePlanet();
        Vector3 up = planet != null ? planet.GetUpAt(_restPosition) : Vector3.up;

        if (snapLandingToGround && planet != null)
        {
            float clearance = extraGroundClearance;
            _restPosition = planet.GetSurfacePoint(up, clearance);
        }

        Vector3 spacePosition = _restPosition + up * startDistance;
        EnsureLandingSite(_restPosition, _restRotation);

        if (!TrySpawnPlayer(spacePosition, _restRotation))
        {
            Debug.LogWarning("PlayerCrashIntro: could not spawn Player.prefab, skipping cinematic.", this);
            OnLanded?.Invoke();
            yield break;
        }

        playerCapsule.SetPositionAndRotation(spacePosition, _restRotation);
        _player.SetPositionAndRotation(spacePosition, _restRotation);
        ConvertCapsuleToVfx();

        _actor.BeginCinematic();
        HidePlayerGameplayVisuals();
        if (_diveAnimation != null && _diveAnimation.HasDiveClip)
            _diveAnimation.PlayDive();

        OnPlayerReady?.Invoke(_player);

        CameraFollow cameraFollow = ResolveCameraFollow();
        bool hasOriginalOffsets = cameraFollow != null;
        float originalHeight = hasOriginalOffsets ? cameraFollow.OffsetHeight : 0f;
        float originalBack = hasOriginalOffsets ? cameraFollow.OffsetBack : 0f;

        if (cameraFollow != null)
        {
            ApplyCinematicCamera(cameraFollow, 0f);
            cameraFollow.SetTarget(_player);
            cameraFollow.SetSnapToTarget(true);
        }

        _attachedTrail?.Play(-up);

        float contact = _diveAnimation != null && _diveAnimation.HasLandClip
            ? _diveAnimation.LandGroundContactTime
            : 0f;
        // Switch onto the land clip just before impact, already at the grounded pose — starting
        // the land take at t=0 would replay the aerial hips-drop and pop the character back up.
        float lead = Mathf.Min(contact, 0.25f);
        float landClipStart = Mathf.Max(0f, contact - lead);
        bool landStarted = false;

        float fallTimer = 0f;
        while (fallTimer < fallDuration)
        {
            fallTimer += Time.deltaTime;
            float t = fallEase.Evaluate(Mathf.Clamp01(fallTimer / fallDuration));
            Vector3 basePosition = Vector3.LerpUnclamped(spacePosition, _restPosition, t);
            Vector3 pose = basePosition + GetFallTrembleOffset(fallTimer);
            _player.SetPositionAndRotation(pose, _restRotation);
            playerCapsule.SetPositionAndRotation(pose, _restRotation);
            ApplyCinematicCamera(cameraFollow, t);

            if (!landStarted && fallTimer >= fallDuration - lead)
            {
                _diveAnimation?.PlayLand(landClipStart);
                landStarted = true;
            }

            _diveAnimation?.Tick(Time.deltaTime);
            yield return null;
        }

        _player.SetPositionAndRotation(_restPosition, _restRotation);
        playerCapsule.SetPositionAndRotation(_restPosition, _restRotation);
        ApplyCinematicCamera(cameraFollow, 1f);

        if (!landStarted)
            _diveAnimation?.PlayLand(landClipStart);

        HideCrashVfx();
        ResolveImpactEffect()?.Trigger();
        cameraFollow?.ImpactShake(impactShakeDuration, impactShakeMagnitude);
        HideCapsuleAfterLanding();
        StartCoroutine(DropIncomingPortal(up));

        // Hold the crash framing through the landing jolt so it doesn't get lost in the
        // handoff back to the gameplay camera.
        if (cameraFollow != null && impactShakeDuration > 0f)
            yield return new WaitForSeconds(Mathf.Min(0.22f, impactShakeDuration * 0.45f));

        if (cameraFollow != null)
        {
            cameraFollow.SetSnapToTarget(false);
            if (hasOriginalOffsets)
                cameraFollow.SetOffsets(originalHeight, originalBack);
        }

        while (_diveAnimation != null && !_diveAnimation.IsFinished)
        {
            _player.SetPositionAndRotation(_restPosition, _restRotation);
            _diveAnimation.Tick(Time.deltaTime);
            if (_diveAnimation.CanSkipLand && _actor != null && _actor.WantsMove)
                break;
            yield return null;
        }

        RevealPlayerGameplayVisuals();
        _actor.EndCinematic();
        OnLanded?.Invoke();
    }

    bool TrySpawnPlayer(Vector3 position, Quaternion rotation)
    {
        Transform existing = FindExistingPlayer();
        if (existing != null)
        {
            _player = existing;
        }
        else
        {
            GameObject prefab = ResolvePlayerPrefab();
            if (prefab == null)
                return false;

            GameObject playerObject = Instantiate(prefab, position, rotation);
            playerObject.name = prefab.name;
            _player = playerObject.transform;
        }

        _actor = _player.GetComponent<PlayerLandIntro>();
        if (_actor == null)
            _actor = _player.gameObject.AddComponent<PlayerLandIntro>();
        return true;
    }

    GameObject ResolvePlayerPrefab()
    {
        if (playerPrefab != null)
            return playerPrefab;

        SceneBootstrap bootstrap = FindAnyObjectByType<SceneBootstrap>();
        if (bootstrap != null && bootstrap.PlayerPrefab != null)
            return bootstrap.PlayerPrefab;

        return Resources.Load<GameObject>("Player/Player");
    }

    static Transform FindExistingPlayer()
    {
        TouchController motor = FindAnyObjectByType<TouchController>();
        if (motor != null)
            return motor.transform;

        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged != null ? tagged.transform : null;
    }

    /// <summary>Wear the authored dive FBX on the live Player transform. That mesh matches the
    /// dive/land clips; the gameplay Player mesh does not (different bind pose). Trail stays on
    /// the dive model so it follows Armature/Hips.</summary>
    void ConvertCapsuleToVfx()
    {
        DisableCapsuleGateAndBeacon();

        _diveAnimation = playerCapsule.GetComponent<PlayerDiveAnimation>();
        if (_diveAnimation == null)
            _diveAnimation = playerCapsule.GetComponentInChildren<PlayerDiveAnimation>(true);
        if (_diveAnimation == null)
            _diveAnimation = playerCapsule.gameObject.AddComponent<PlayerDiveAnimation>();
        _diveAnimation.enabled = true;

        _diveModel = _diveAnimation.ModelRoot;
        if (_diveModel == null)
            _diveModel = playerCapsule.Find(PlayerDiveDownCapsulePaths.DiveModelChildName);

        if (_diveModel != null)
        {
            _diveModel.SetParent(_player, false);
            _diveModel.localPosition = Vector3.zero;
            _diveModel.localRotation = Quaternion.identity;
            _diveModel.localScale = Vector3.one;
            _diveModel.gameObject.SetActive(true);
        }

        _attachedTrail = _diveModel != null
            ? _diveModel.GetComponentInChildren<PlayerFireTrail>(true)
            : playerCapsule.GetComponentInChildren<PlayerFireTrail>(true);
        if (_attachedTrail != null)
            _attachedTrail.RetargetFollow();

        _capsuleRenderers = playerCapsule.GetComponentsInChildren<Renderer>(true);
        _capsuleColliders = playerCapsule.GetComponentsInChildren<Collider>(true);
        if (_capsuleColliders != null)
        {
            for (int i = 0; i < _capsuleColliders.Length; i++)
            {
                if (_capsuleColliders[i] != null)
                    _capsuleColliders[i].enabled = false;
            }
        }
    }

    void HidePlayerGameplayVisuals()
    {
        if (_player == null)
            return;

        Renderer[] renderers = _player.GetComponentsInChildren<Renderer>(true);
        var hidden = new System.Collections.Generic.List<Renderer>(renderers.Length);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;
            if (IsCinematicVisual(renderer.transform))
                continue;
            renderer.enabled = false;
            hidden.Add(renderer);
        }

        _hiddenPlayerRenderers = hidden.ToArray();
    }

    void RevealPlayerGameplayVisuals()
    {
        _diveAnimation?.Stop();
        if (_diveModel != null)
        {
            _diveModel.gameObject.SetActive(false);
            if (playerCapsule != null)
                _diveModel.SetParent(playerCapsule, false);
        }

        if (_hiddenPlayerRenderers == null)
            return;

        for (int i = 0; i < _hiddenPlayerRenderers.Length; i++)
        {
            if (_hiddenPlayerRenderers[i] != null)
                _hiddenPlayerRenderers[i].enabled = true;
        }

        _hiddenPlayerRenderers = null;
    }

    bool IsCinematicVisual(Transform t)
    {
        if (t == null)
            return false;
        if (_diveModel != null && (t == _diveModel || t.IsChildOf(_diveModel)))
            return true;
        if (_attachedTrail != null && (t == _attachedTrail.transform || t.IsChildOf(_attachedTrail.transform)))
            return true;
        return false;
    }

    void HideCrashVfx()
    {
        _attachedTrail?.Stop();
        if (_attachedTrail != null)
            _attachedTrail.gameObject.SetActive(false);
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

    /// <summary>0 = opening close-up, 1 = the wide crash shot we used to hold for the whole fall.</summary>
    static AnimationCurve DefaultCinematicZoomOut()
    {
        return new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.22f, 0.08f, 0.45f, 0.45f),
            new Keyframe(1f, 1f, 1.45f, 0f));
    }

    void ApplyCinematicCamera(CameraFollow cameraFollow, float fallProgress)
    {
        if (cameraFollow == null)
            return;

        float zoom = cinematicZoomOut != null && cinematicZoomOut.length > 0
            ? Mathf.Clamp01(cinematicZoomOut.Evaluate(Mathf.Clamp01(fallProgress)))
            : Mathf.Clamp01(fallProgress);
        cameraFollow.SetOffsets(
            Mathf.Lerp(cinematicStartOffsetHeight, cinematicOffsetHeight, zoom),
            Mathf.Lerp(cinematicStartOffsetBack, cinematicOffsetBack, zoom));
    }

    static SphericalPlanet ResolvePlanet() =>
        SphericalPlanet.Instance != null ? SphericalPlanet.Instance : FindAnyObjectByType<SphericalPlanet>();

    static CameraFollow ResolveCameraFollow()
    {
        CameraFollow follow = FindAnyObjectByType<CameraFollow>();
        if (follow == null && Camera.main != null)
            follow = Camera.main.GetComponent<CameraFollow>();
        return follow;
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

    IEnumerator DropIncomingPortal(Vector3 up)
    {
        GameObject prefab = portalPrefab != null
            ? portalPrefab
            : Resources.Load<GameObject>(DefaultPortalResource);
        if (prefab == null)
        {
            Debug.LogWarning("PlayerCrashIntro: no portal prefab found at Resources/" + DefaultPortalResource + ".", this);
            yield break;
        }

        Vector3 groundPosition = _restPosition;
        Quaternion groundRotation = _restRotation;
        Vector3 groundUp = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
        if (TryComputeGroundPortalPose(_restPosition, _restRotation, out Vector3 snappedPosition, out Quaternion snappedRotation, out Vector3 snappedUp))
        {
            groundPosition = snappedPosition;
            groundRotation = snappedRotation;
            groundUp = snappedUp;
        }

        Vector3 spacePosition = groundPosition + groundUp * portalStartDistance;

        GameObject portal = Instantiate(prefab);
        portal.name = prefab.name;
        portal.SetActive(false);
        ConfigureFallingPortal(portal);
        SetCraterActive(portal, false);
        SetPortalVisible(portal, false);
        portal.transform.SetPositionAndRotation(spacePosition, groundRotation);
        portal.SetActive(true);
        _incomingPortal = portal.transform;

        Transform parent = PlanetSurfacePose.GetOrCreateObjectsRoot(ResolvePlanet());
        if (parent != null)
            portal.transform.SetParent(parent, true);

        SphericalPlanet planet = ResolvePlanet();
        float clearRadius = portalClearRadius;
        while (_player != null && !HasWalkedClear(planet, groundPosition, _player.position, clearRadius))
            yield return null;

        SetPortalVisible(portal, true);

        float fallTimer = 0f;
        while (fallTimer < portalFallDuration)
        {
            fallTimer += Time.deltaTime;
            float t = fallEase.Evaluate(Mathf.Clamp01(fallTimer / portalFallDuration));
            Vector3 pose = Vector3.LerpUnclamped(spacePosition, groundPosition, t) + GetFallTrembleOffset(fallTimer);
            portal.transform.SetPositionAndRotation(pose, groundRotation);
            yield return null;
        }

        PlantIncomingPortal(portal, groundPosition, groundRotation, groundUp);
    }

    static bool HasWalkedClear(SphericalPlanet planet, Vector3 landing, Vector3 playerPosition, float radius)
    {
        if (radius <= 0.0001f)
            return true;

        Vector3 center = planet != null ? planet.Center : Vector3.zero;
        float distance = planet != null
            ? PlanetSurfacePose.GetSurfaceDistance(center, landing, playerPosition)
            : Vector3.Distance(landing, playerPosition);
        return distance >= radius;
    }

    void ConfigureFallingPortal(GameObject portal)
    {
        GalaxyGate gate = portal.GetComponent<GalaxyGate>();
        if (gate != null)
        {
            gate.enabled = false;
            if (!string.IsNullOrEmpty(portalTargetScene))
                gate.TargetSceneName = portalTargetScene;
        }

        Collider[] colliders = portal.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    static void SetCraterActive(GameObject portal, bool active)
    {
        if (portal == null)
            return;

        Transform crater = FindDirectChild(portal.transform, "Crater");
        if (crater != null)
            crater.gameObject.SetActive(active);
    }

    static Transform FindDirectChild(Transform root, string name)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != null && child.name == name)
                return child;
        }

        return null;
    }

    static void SetPortalVisible(GameObject portal, bool visible)
    {
        if (portal == null)
            return;

        Renderer[] renderers = portal.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        }
    }

    void PlantIncomingPortal(GameObject portal, Vector3 groundPosition, Quaternion groundRotation, Vector3 groundUp)
    {
        SetPortalVisible(portal, true);
        PortalGroundSnap.Snap(portal.transform, groundPosition, groundUp, portalEmbed);
        SetCraterActive(portal, true);
        PlayPortalImpactVfx(portal.transform, groundUp);
        PortalSignBillboard sign = portal.GetComponentInChildren<PortalSignBillboard>(true);
        if (sign != null)
            sign.PlayPlantImpact();

        GalaxyGate gate = portal.GetComponent<GalaxyGate>();
        if (gate != null)
        {
            if (!string.IsNullOrEmpty(portalTargetScene))
                gate.TargetSceneName = portalTargetScene;
            if (_player != null)
                gate.IgnoreUntilOccupantLeaves(_player);
            gate.enabled = true;
        }

        Collider[] colliders = portal.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = true;
        }

        if (portal.GetComponent<PlayerCapsuleBeacon>() == null)
            portal.AddComponent<PlayerCapsuleBeacon>();

        PlanetEnvironmentExclusionZone zone = portal.GetComponent<PlanetEnvironmentExclusionZone>();
        if (zone == null)
            zone = portal.AddComponent<PlanetEnvironmentExclusionZone>();
        zone.SetPlayerSpawnRadius(spawnRadius);

        if (_landingSite != null)
        {
            PlayerCapsuleBeacon siteBeacon = _landingSite.GetComponent<PlayerCapsuleBeacon>();
            if (siteBeacon != null)
                siteBeacon.enabled = false;
        }
    }

    void PlayPortalImpactVfx(Transform portal, Vector3 groundUp)
    {
        if (portal == null)
            return;

        GameObject prefab = portalImpactVfxPrefab != null
            ? portalImpactVfxPrefab
            : Resources.Load<GameObject>(DefaultPortalImpactResource);
        if (prefab == null)
        {
            Debug.LogWarning("PlayerCrashIntro: no portal impact VFX found at Resources/" + DefaultPortalImpactResource + ".", this);
            return;
        }

        Quaternion rotation = groundUp.sqrMagnitude > 0.0001f
            ? Quaternion.FromToRotation(Vector3.up, groundUp.normalized)
            : portal.rotation;
        GameObject fx = Instantiate(prefab, portal.position, rotation, portal);
        fx.name = prefab.name;

        ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem ps = systems[i];
            if (ps == null)
                continue;

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(false);
        }
    }

    bool TryComputeGroundPortalPose(
        Vector3 nearWorldPoint,
        Quaternion yawSource,
        out Vector3 groundPosition,
        out Quaternion rotation,
        out Vector3 groundUp)
    {
        groundPosition = nearWorldPoint;
        rotation = yawSource;
        groundUp = Vector3.up;

        SphericalPlanet planet = ResolvePlanet();
        if (planet == null)
            return false;

        Vector3 radial = nearWorldPoint - planet.Center;
        if (radial.sqrMagnitude < 0.0001f)
            radial = planet.transform.up;
        else
            radial.Normalize();

        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        float yaw = PlanetSurfacePose.ExtractYaw(yawSource, radial);
        if (!PlanetSurfacePose.TryGetPose(planet, tiles, radial, yaw, 0f, out groundPosition, out rotation, out groundUp))
        {
            groundPosition = planet.GetSurfacePoint(radial, 0f);
            groundUp = planet.GetUpAt(groundPosition);
            rotation = PlanetSurfacePose.RotationFromUp(groundUp, yaw);
        }

        if (PlanetSurfacePose.TrySampleGroundBelow(
                groundPosition + groundUp * 2f, groundUp, 12f, 0f, out Vector3 rayPosition, out Vector3 rayNormal))
        {
            groundPosition = rayPosition;
            groundUp = rayNormal;
            rotation = PlanetSurfacePose.RotationFromUp(groundUp, yaw);
        }

        return true;
    }

    /// <summary>Invisible marker at the crash site: keeps grass/trees off the landing disk and
    /// hosts <see cref="PlayerCapsuleBeacon"/> so the home arrow still points here after the
    /// cinematic capsule is hidden.</summary>
    void EnsureLandingSite(Vector3 position, Quaternion rotation)
    {
        if (_landingSite != null)
            return;

        GameObject site = new GameObject("CrashLandingSite");
        site.transform.SetPositionAndRotation(position, rotation);

        PlanetEnvironmentExclusionZone zone = site.AddComponent<PlanetEnvironmentExclusionZone>();
        zone.SetPlayerSpawnRadius(spawnRadius);

        if (site.GetComponent<PlayerCapsuleBeacon>() == null)
            site.AddComponent<PlayerCapsuleBeacon>();

        Transform parent = PlanetSurfacePose.GetOrCreateObjectsRoot(ResolvePlanet());
        if (parent != null)
            site.transform.SetParent(parent, true);

        _landingSite = site.transform;
    }

    /// <summary>Computes where the player should spawn if this cinematic is not the one placing
    /// them: the crash site itself. Used by <see cref="SceneBootstrap"/> as a fallback.</summary>
    public bool TryComputePlayerSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        Transform anchor = _landingSite != null ? _landingSite : LandingAnchor;
        if (anchor == null)
            return false;

        position = anchor.position;
        rotation = anchor.rotation;
        return true;
    }

    /// <summary>Hides the dive capsule's own renderers/colliders and disables its
    /// GalaxyGate now that the landing site marker has taken over.</summary>
    void HideCapsuleAfterLanding()
    {
        if (_capsuleRenderers != null)
        {
            for (int i = 0; i < _capsuleRenderers.Length; i++)
            {
                Renderer renderer = _capsuleRenderers[i];
                if (renderer == null)
                    continue;
                if (!renderer.transform.IsChildOf(playerCapsule))
                    continue;
                renderer.enabled = false;
            }
        }

        if (_capsuleColliders != null)
        {
            for (int i = 0; i < _capsuleColliders.Length; i++)
            {
                Collider collider = _capsuleColliders[i];
                if (collider == null)
                    continue;
                if (!collider.transform.IsChildOf(playerCapsule))
                    continue;
                collider.enabled = false;
            }
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
}
