using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads a target scene when the player enters this trigger volume.
/// The player eases into idle, freezes, then dematerializes through a body hologram shader
/// while the teleport VFX plays. After that the camera lifts into space and the scene swaps.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GalaxyGate : MonoBehaviour
{
    [SerializeField] string targetSceneName;
    [Tooltip("World-space distance used when CharacterController triggers are unavailable.")]
    [SerializeField] float proximityFallbackRadius = 2.4f;

    /// <summary>Scene this gate loads on teleport. Exposed so other gates spawned at runtime
    /// can inherit whatever destination was authored on the gate they're replacing.</summary>
    public string TargetSceneName
    {
        get => targetSceneName;
        set => targetSceneName = value;
    }

    /// <summary>Trigger radius used when CharacterController trigger events are unavailable.</summary>
    public float ProximityFallbackRadius => proximityFallbackRadius;

    /// <summary>Minimum scatter radius that stays outside <paramref name="gate"/>'s re-teleport
    /// zone (plus a small margin), clamped so it never exceeds <paramref name="maxRadius"/>.
    /// Returns 0 if <paramref name="gate"/> is null.</summary>
    public static float GetSafeMinSpawnRadius(GalaxyGate gate, float maxRadius)
    {
        if (gate == null)
            return 0f;

        const float safetyMargin = 0.75f;
        return Mathf.Min(gate.proximityFallbackRadius + safetyMargin, maxRadius * 0.9f);
    }

    [Header("Teleport VFX")]
    [Tooltip("Hovl Studio Teleport prefab (or any particle effect). Played at the player's feet. " +
        "Leave empty to cut instantly.")]
    [SerializeField] GameObject teleportEffectPrefab;
    [SerializeField, Min(0.05f)] float effectDuration = 1.6f;
    [SerializeField, Min(0.01f)] float effectScale = 1.25f;
    [Tooltip("Body dissolve/hologram material. Leave empty to load Resources/Player/Teleport/TeleportBody.")]
    [SerializeField] Material teleportBodyMaterial;
    [Tooltip("How long the run cycle is allowed to ease into idle before the player is frozen " +
        "and the teleport VFX starts.")]
    [SerializeField, Min(0.05f)] float idleBlendDuration = 0.45f;

    [Header("Camera Dolly")]
    [Tooltip("During the teleport VFX the camera eases inward toward the player. 0 = no push-in.")]
    [SerializeField, Min(0f)] float cameraDollyInDistance = 3.6f;
    [Tooltip("World-space height above the player's feet that the dolly looks at.")]
    [SerializeField, Min(0f)] float cameraDollyLookHeight = 1.15f;
    [Tooltip("FOV change during the push-in (negative = tighter, more intimate).")]
    [SerializeField] float cameraDollyFovDelta = -3.5f;

    [Header("Camera Lift")]
    [Tooltip("After the teleport VFX ends and the player vanishes, the camera rises into space " +
        "for this many seconds before the scene loads.")]
    [SerializeField, Min(0.05f)] float cameraLiftDuration = 1f;
    [SerializeField, Min(1f)] float cameraLiftDistance = 90f;

    bool _loading;
    Transform _player;
    Transform _ignoreUntilExit;
    float _armAtTime;

    void Reset()
    {
        EnsureTrigger();
    }

    void Awake()
    {
        EnsureTrigger();
    }

    void OnEnable()
    {
        // Refresh when a gate is armed later (e.g. the incoming crash-site portal).
        _armAtTime = Time.time + 0.75f;
    }

    void EnsureTrigger()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    /// <summary>Don't teleport <paramref name="occupant"/> while they are still standing in this
    /// volume. Arms automatically once they walk out.</summary>
    public void IgnoreUntilOccupantLeaves(Transform occupant)
    {
        _ignoreUntilExit = occupant;
    }

    void Update()
    {
        if (_loading || string.IsNullOrEmpty(targetSceneName) || Time.time < _armAtTime)
            return;

        if (IsIgnoringOccupant())
            return;

        if (_player == null)
        {
            GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo == null)
                return;
            _player = playerGo.transform;
        }

        if ((_player.position - transform.position).sqrMagnitude <= proximityFallbackRadius * proximityFallbackRadius)
            BeginTeleport(_player);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_loading || string.IsNullOrEmpty(targetSceneName) || Time.time < _armAtTime)
            return;

        if (IsIgnoringOccupant())
            return;

        if (!IsPlayer(other))
            return;

        BeginTeleport(ResolvePlayerRoot(other.transform));
    }

    bool IsIgnoringOccupant()
    {
        if (_ignoreUntilExit == null)
            return false;

        float radius = proximityFallbackRadius * 1.2f;
        if ((_ignoreUntilExit.position - transform.position).sqrMagnitude > radius * radius)
        {
            _ignoreUntilExit = null;
            return false;
        }

        return true;
    }

    void BeginTeleport(Transform player)
    {
        if (_loading || string.IsNullOrEmpty(targetSceneName))
            return;

        _loading = true;
        player = ResolvePlayerRoot(player);

        if (teleportEffectPrefab == null || effectDuration <= 0f)
        {
            SceneManager.LoadScene(targetSceneName);
            return;
        }

        StartCoroutine(PlayTeleportThenLoad(player));
    }

    IEnumerator PlayTeleportThenLoad(Transform player)
    {
        Vector3 frozenPosition = player.position;
        Quaternion frozenRotation = player.rotation;
        SetPlayerInvulnerable(player, true);
        HidePlayerHud(player);
        StopLocomotion(player);
        yield return BlendToIdle(player, frozenPosition, frozenRotation);
        FreezePose(player);
        HideFloatingWeapons(player);

        Vector3 up = GetUpAt(frozenPosition);
        TeleportBodyFx bodyFx = TeleportBodyFx.Begin(player, teleportBodyMaterial, frozenPosition, up);
        CameraShot shot = BeginTeleportCamera(frozenPosition, up);
        GameObject fx = SpawnEffect(frozenPosition);

        // Finish dissolve + dolly a beat before the camera punches out, so the VFX
        // never starts a second cycle under the lift.
        float bodyDuration = Mathf.Max(0.2f, effectDuration * 0.82f);

        float elapsed = 0f;
        while (elapsed < effectDuration)
        {
            HoldPlayer(player, frozenPosition, frozenRotation);
            elapsed += Time.deltaTime;
            float bodyT = Mathf.Clamp01(elapsed / bodyDuration);
            if (bodyFx != null)
                bodyFx.SetProgress(TeleportBodyEase(bodyT));
            ApplyCameraDolly(shot, bodyT);
            yield return null;
        }

        HoldPlayer(player, frozenPosition, frozenRotation);
        SetPlayerVisible(player, false);
        if (bodyFx != null)
        {
            bodyFx.SetProgress(1f);
            bodyFx.Release();
        }
        StopEffect(fx);
        yield return LiftCameraIntoSpace(shot, frozenPosition, up);
        SceneManager.LoadScene(targetSceneName);
    }

    GameObject SpawnEffect(Vector3 position)
    {
        Vector3 up = GetUpAt(position);
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up);

        GameObject fx = Instantiate(teleportEffectPrefab, position, rotation);
        fx.name = teleportEffectPrefab.name;
        fx.transform.localScale = Vector3.one * effectScale;

        ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            main.loop = false;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            systems[i].Play(false);
        }

        return fx;
    }

    static void StopEffect(GameObject fx)
    {
        if (fx == null)
            return;

        ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            if (systems[i] != null)
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    struct CameraShot
    {
        public Camera cam;
        public Vector3 startPos;
        public Quaternion startRot;
        public float startFov;
        public Vector3 dollyPos;
        public Quaternion dollyRot;
        public float dollyFov;
        public bool valid;
    }

    CameraShot BeginTeleportCamera(Vector3 lookAt, Vector3 up)
    {
        CameraFollow follow = FindAnyObjectByType<CameraFollow>();
        if (follow != null)
            follow.enabled = false;

        Camera cam = Camera.main;
        var shot = new CameraShot { cam = cam, valid = cam != null };
        if (!shot.valid)
            return shot;

        shot.startPos = cam.transform.position;
        shot.startRot = cam.transform.rotation;
        shot.startFov = cam.fieldOfView;
        shot.dollyPos = shot.startPos;
        shot.dollyRot = shot.startRot;
        shot.dollyFov = shot.startFov + cameraDollyFovDelta;

        Vector3 focus = lookAt + up * cameraDollyLookHeight;
        Vector3 toFocus = focus - shot.startPos;
        float remaining = toFocus.magnitude;
        if (remaining > 0.05f && cameraDollyInDistance > 0f)
        {
            Vector3 dir = toFocus / remaining;
            float travel = Mathf.Min(cameraDollyInDistance, remaining * 0.38f);
            shot.dollyPos = shot.startPos + dir * travel;
            shot.dollyRot = Quaternion.LookRotation(dir, up);
        }

        return shot;
    }

    static void ApplyCameraDolly(CameraShot shot, float t)
    {
        if (!shot.valid)
            return;

        float eased = t * t * (3f - 2f * t);
        shot.cam.transform.position = Vector3.Lerp(shot.startPos, shot.dollyPos, eased);
        shot.cam.transform.rotation = Quaternion.Slerp(shot.startRot, shot.dollyRot, eased);
        shot.cam.fieldOfView = Mathf.Lerp(shot.startFov, shot.dollyFov, eased);
    }

    IEnumerator LiftCameraIntoSpace(CameraShot shot, Vector3 lookAt, Vector3 up)
    {
        Camera cam = shot.valid ? shot.cam : Camera.main;
        if (cam == null)
            yield break;

        CameraFollow follow = FindAnyObjectByType<CameraFollow>();
        if (follow != null)
            follow.enabled = false;

        Vector3 start = cam.transform.position;
        Quaternion startRot = cam.transform.rotation;
        float startFov = cam.fieldOfView;
        Vector3 end = start + up * cameraLiftDistance;
        float endFov = shot.valid ? shot.startFov : startFov;

        float elapsed = 0f;
        while (elapsed < cameraLiftDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / cameraLiftDuration);
            // Accelerate away from the planet so the lift reads as a launch, not a drift.
            float eased = t * t;
            cam.transform.position = Vector3.LerpUnclamped(start, end, eased);
            cam.fieldOfView = Mathf.Lerp(startFov, endFov, t);

            Vector3 toSite = lookAt - cam.transform.position;
            if (toSite.sqrMagnitude > 0.001f)
                cam.transform.rotation = Quaternion.Slerp(startRot, Quaternion.LookRotation(toSite.normalized, up), t);

            yield return null;
        }

        cam.transform.position = end;
        cam.fieldOfView = endFov;
    }

    IEnumerator BlendToIdle(Transform player, Vector3 position, Quaternion rotation)
    {
        Animator animator = player != null
            ? player.GetComponent<Animator>() ?? player.GetComponentInChildren<Animator>()
            : null;
        if (animator == null)
            yield break;

        animator.applyRootMotion = false;
        animator.speed = 1f;

        int speedId = Animator.StringToHash("Speed");
        int motionId = Animator.StringToHash("MotionSpeed");
        int groundedId = Animator.StringToHash("Grounded");
        int jumpId = Animator.StringToHash("Jump");
        int freeFallId = Animator.StringToHash("FreeFall");

        float damp = Mathf.Max(0.08f, idleBlendDuration * 0.35f);
        float elapsed = 0f;
        while (elapsed < idleBlendDuration)
        {
            HoldPlayer(player, position, rotation);
            animator.SetBool(groundedId, true);
            animator.SetBool(jumpId, false);
            animator.SetBool(freeFallId, false);
            animator.SetFloat(speedId, 0f, damp, Time.deltaTime);
            // MotionSpeed drives this blend-tree state's playback rate — keep it at 1 so
            // the run cycle actually plays down into idle instead of freezing mid-stride.
            animator.SetFloat(motionId, 1f);

            elapsed += Time.deltaTime;
            if (elapsed > 0.12f && animator.GetFloat(speedId) <= 0.12f)
                break;

            yield return null;
        }

        animator.SetFloat(speedId, 0f);
        animator.SetFloat(motionId, 1f);

        // One extra beat so the idle pose actually lands before we freeze the clip.
        float settle = 0.12f;
        elapsed = 0f;
        while (elapsed < settle)
        {
            HoldPlayer(player, position, rotation);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    static void HidePlayerHud(Transform player)
    {
        if (player == null)
            return;

        PlayerRangeCombat combat = player.GetComponent<PlayerRangeCombat>()
                                   ?? player.GetComponentInParent<PlayerRangeCombat>()
                                   ?? player.GetComponentInChildren<PlayerRangeCombat>();
        if (combat != null)
            combat.HideRange();

        VitalsBars vitals = player.GetComponent<VitalsBars>()
                            ?? player.GetComponentInParent<VitalsBars>()
                            ?? player.GetComponentInChildren<VitalsBars>();
        if (vitals != null)
            vitals.SetHidden(true);

        PlayerLowHpWarning warning = Object.FindAnyObjectByType<PlayerLowHpWarning>(FindObjectsInactive.Include);
        if (warning != null)
            warning.HideForCinematic();

        PlayerLowOxygenWarning oxygen = Object.FindAnyObjectByType<PlayerLowOxygenWarning>(FindObjectsInactive.Include);
        if (oxygen != null)
            oxygen.HideForCinematic();
    }

    static void HideFloatingWeapons(Transform player)
    {
        if (player == null)
            return;

        FloatingWeapon[] weapons = player.GetComponentsInChildren<FloatingWeapon>(true);
        for (int i = 0; i < weapons.Length; i++)
        {
            if (weapons[i] != null)
                weapons[i].SetVisible(false);
        }
    }

    static void StopLocomotion(Transform player)
    {
        if (player == null)
            return;

        StarterAssetsInputs input = player.GetComponent<StarterAssetsInputs>();
        if (input != null)
            input.move = Vector2.zero;

        PlanetWalker walker = player.GetComponent<PlanetWalker>();
        if (walker != null)
            walker.enabled = false;

        // PlanetWalker.OnDisable re-enables TouchController — turn it back off after that.
        TouchController motor = player.GetComponent<TouchController>();
        if (motor != null)
            motor.enabled = false;

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.Move(Vector3.zero);
            controller.enabled = false;
        }

        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        PlayerRangeCombat combat = player.GetComponent<PlayerRangeCombat>();
        if (combat != null)
            combat.enabled = false;

        Animator animator = player.GetComponent<Animator>() ?? player.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.speed = 1f;
        }
    }

    static float TeleportBodyEase(float t)
    {
        // Charge the hologram first, then commit the feet-to-head dissolve.
        if (t < 0.2f)
            return (t / 0.2f) * 0.22f;

        float u = (t - 0.2f) / 0.8f;
        u = u * u * (3f - 2f * u);
        return 0.22f + u * 0.78f;
    }

    static void SetPlayerInvulnerable(Transform player, bool invulnerable)
    {
        if (player == null)
            return;

        PlayerVitals vitals = player.GetComponent<PlayerVitals>()
                             ?? player.GetComponentInParent<PlayerVitals>()
                             ?? player.GetComponentInChildren<PlayerVitals>();
        if (vitals != null)
            vitals.SetInvulnerable(invulnerable);
    }

    static void FreezePose(Transform player)
    {
        if (player == null)
            return;

        Animator animator = player.GetComponent<Animator>() ?? player.GetComponentInChildren<Animator>();
        if (animator != null)
            animator.speed = 0f;
    }

    static void HoldPlayer(Transform player, Vector3 position, Quaternion rotation)
    {
        if (player == null)
            return;

        player.SetPositionAndRotation(position, rotation);
    }

    static void SetPlayerVisible(Transform player, bool visible)
    {
        if (player == null)
            return;

        Renderer[] renderers = player.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        }
    }

    static Transform ResolvePlayerRoot(Transform from)
    {
        if (from == null)
            return null;

        if (from.root.CompareTag("Player"))
            return from.root;

        PlanetWalker walker = from.GetComponentInParent<PlanetWalker>();
        if (walker != null)
            return walker.transform;

        TouchController motor = from.GetComponentInParent<TouchController>();
        if (motor != null)
            return motor.transform;

        return from;
    }

    static Vector3 GetUpAt(Vector3 worldPosition)
    {
        return SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(worldPosition)
            : Vector3.up;
    }

    static bool IsPlayer(Collider other)
    {
        if (other == null)
            return false;

        if (other.CompareTag("Player"))
            return true;

        Transform root = other.transform.root;
        if (root.CompareTag("Player"))
            return true;

        return other.GetComponentInParent<PlanetWalker>() != null
               || other.GetComponentInParent<TouchController>() != null
               || other.GetComponentInParent<CharacterController>() != null;
    }
}
