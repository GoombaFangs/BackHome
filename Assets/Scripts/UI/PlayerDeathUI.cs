using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Death screen. Listens for <see cref="PlayerVitals.Died"/> and, the instant it fires: hard-stops
/// player locomotion, starts the Player's "Dying" animation (Starbot_Animation_dying), and freezes
/// every creature in the scene (see <see cref="FreezeAllCreatures"/>) so nothing can interrupt it -
/// but does NOT show the DeathPanel yet. Only once that animation has fully played through (see
/// <see cref="WaitForDyingAnimation"/>) does <see cref="DeathSequence"/> hand the camera to
/// <see cref="PlayerDeathCameraLock"/> for a small scripted settle instead of letting the normal
/// follow camera keep reacting to the death (which reads as it "going crazy"), and, a short beat
/// later, freeze time and reveal the DeathPanel - <see cref="DeathSequence"/> is the single
/// coroutine driving that whole tail end, so the animation, the camera lock, the time freeze and
/// the panel can never drift apart.
/// </summary>
public class PlayerDeathUI : MonoBehaviour
{
    const string DeathOverlayName = "DeathOverlay";
    const string DeathOverlayMaterialResourcesPath = "HUD/DeathOverlay";

    static readonly int DeathOverlayFrostAmountId = Shader.PropertyToID("_FrostAmount");
    static readonly int DeathOverlayVignetteAmountId = Shader.PropertyToID("_VignetteAmount");

    [FormerlySerializedAs("shipSceneName")]
    [SerializeField] string spaceshipSceneName = "SpaceShip";
    [Tooltip("Seconds after death before the camera fully locks, time freezes and stays frozen.")]
    [SerializeField, Min(0f)] float freezeDelay = 2f;
    [Tooltip("DeathPanel prefab (You are dead + Continue). Instantiated under the UI canvas.")]
    [SerializeField] GameObject deathPanelPrefab;

    [Header("Death Overlay")]
    [Tooltip("Full-screen frost/vignette Image (BackHome/Hud/LowOxygenOverlay shader, see DeathOverlay.mat) that fades in the instant the Dying animation starts. Auto-found by name under the Canvas if left empty.")]
    [SerializeField] Image deathOverlay;
    [SerializeField, Range(0f, 1f)] float deathOverlayFrostAmount = 0.5f;
    [SerializeField, Range(0f, 1f)] float deathOverlayVignetteAmount = 0.85f;
    [SerializeField, Min(0.05f)] float deathOverlayFadeIn = 1.8f;

    PlayerVitals _vitals;
    PlayerDeathCameraLock _cameraLock;
    Animator _animator;
    GameObject _deathPanelInstance;
    Material _deathOverlayRuntime;
    Coroutine _deathOverlayRoutine;
    bool _shown;
    bool _loading;

    void Start()
    {
        EnsurePanelInstance(active: false);
        TryBindPlayer();
    }

    void OnDestroy()
    {
        UnbindPlayer();
        if (_shown)
            Time.timeScale = 1f;

        if (_deathOverlayRuntime != null)
            Destroy(_deathOverlayRuntime);
    }

    void Update()
    {
        if (_vitals == null)
            TryBindPlayer();
    }

    void TryBindPlayer()
    {
        if (_vitals != null)
            return;

        GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null)
            return;

        _vitals = playerGo.GetComponent<PlayerVitals>();
        if (_vitals == null)
            _vitals = playerGo.GetComponentInChildren<PlayerVitals>();

        if (_vitals == null)
            return;

        _vitals.Died += OnPlayerDied;

        if (!_vitals.IsAlive)
            OnPlayerDied();
    }

    void UnbindPlayer()
    {
        if (_vitals == null)
            return;

        _vitals.Died -= OnPlayerDied;
        _vitals = null;
    }

    void OnPlayerDied()
    {
        if (_shown)
            return;

        _shown = true;

        // Instant: lock the player's own input/physics, kick off the Dying animation, and freeze
        // every creature in place so nothing can interrupt it. The DeathPanel and the camera
        // settle are deliberately delayed (see DeathSequence) until Starbot_Animation_dying has
        // fully played out, so the player actually gets to watch the death animation instead of
        // it happening off-screen behind the panel while creatures keep attacking.
        StopPlayerLocomotion();
        PlayDeathAnimation();
        FreezeAllCreatures();
        FadeInDeathOverlay();

        StartCoroutine(DeathSequence());
    }

    void StopPlayerLocomotion()
    {
        if (_vitals == null)
            return;

        Transform player = _vitals.transform;

        StarterAssetsInputs input = player.GetComponent<StarterAssetsInputs>();
        if (input != null)
            input.move = Vector2.zero;

        PlanetWalker walker = player.GetComponent<PlanetWalker>();
        if (walker != null)
            walker.enabled = false;

        // PlanetWalker.OnDisable re-enables TouchController - turn it back off after that.
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
    }

    // Switches the Player's Animator into the "Dying" state (Starbot_Animation_dying), which
    // holds its final frame forever once played - see PlayerModelSwapTool.WireDyingStateIntoController.
    void PlayDeathAnimation()
    {
        if (_vitals == null)
            return;

        Transform player = _vitals.transform;
        _animator = player.GetComponent<Animator>() ?? player.GetComponentInChildren<Animator>();
        if (_animator == null)
            return;

        _animator.SetBool("Dead", true);
    }

    // Stops every creature dead in its tracks - no more chasing, no more attacking - so nothing
    // can interrupt or damage the player during the short beat where the Dying animation plays.
    void FreezeAllCreatures()
    {
        Creature[] creatures = FindObjectsByType<Creature>(FindObjectsInactive.Exclude);
        foreach (Creature creature in creatures)
            creature.SetFrozen(true);
    }

    // Fades the DeathOverlay (frost/vignette darkening, see the Death Overlay fields above) in
    // from wherever it currently is, the instant the Dying animation starts playing. Idempotent -
    // restarts the fade from its current value if called again mid-fade, but does not reset it.
    void FadeInDeathOverlay()
    {
        if (!EnsureDeathOverlay())
            return;

        deathOverlay.gameObject.SetActive(true);
        if (_deathOverlayRoutine != null)
            StopCoroutine(_deathOverlayRoutine);
        _deathOverlayRoutine = StartCoroutine(FadeInDeathOverlayRoutine());
    }

    IEnumerator FadeInDeathOverlayRoutine()
    {
        float t = 0f;
        while (t < deathOverlayFadeIn)
        {
            t += Time.unscaledDeltaTime;
            ApplyDeathOverlay(Mathf.Clamp01(t / deathOverlayFadeIn));
            yield return null;
        }

        ApplyDeathOverlay(1f);
        _deathOverlayRoutine = null;
    }

    void ApplyDeathOverlay(float amount)
    {
        if (_deathOverlayRuntime == null)
            return;

        _deathOverlayRuntime.SetFloat(DeathOverlayFrostAmountId, deathOverlayFrostAmount * amount);
        _deathOverlayRuntime.SetFloat(DeathOverlayVignetteAmountId, deathOverlayVignetteAmount * amount);
    }

    bool EnsureDeathOverlay()
    {
        if (deathOverlay == null)
        {
            Transform found = FindExistingDeathOverlay();
            if (found != null)
                deathOverlay = found.GetComponent<Image>();
        }

        if (deathOverlay == null)
            return false;

        if (_deathOverlayRuntime == null)
        {
            Material source = Resources.Load<Material>(DeathOverlayMaterialResourcesPath);
            if (source == null)
            {
                Debug.LogWarning($"{nameof(PlayerDeathUI)}: DeathOverlay material missing at Resources/{DeathOverlayMaterialResourcesPath}.", this);
                return false;
            }

            _deathOverlayRuntime = new Material(source) { name = source.name + " (Runtime)" };
        }

        if (deathOverlay.material != _deathOverlayRuntime)
            deathOverlay.material = _deathOverlayRuntime;

        return true;
    }

    Transform FindExistingDeathOverlay()
    {
        Transform direct = transform.Find(DeathOverlayName);
        if (direct != null)
            return direct;

        Canvas canvas = GetComponentInChildren<Canvas>(true);
        return canvas != null ? canvas.transform.Find(DeathOverlayName) : null;
    }

    // The short window between death and the DeathPanel: first lets Starbot_Animation_dying play
    // all the way through undisturbed (creatures are already frozen - see FreezeAllCreatures),
    // then - only once it's finished - runs the existing camera settle (BeginDeathFall -> a beat
    // later -> Lock, which is also where time actually freezes), and finally reveals the panel.
    IEnumerator DeathSequence()
    {
        yield return WaitForDyingAnimation();

        if (_cameraLock == null)
            _cameraLock = GetComponent<PlayerDeathCameraLock>();
        if (_cameraLock != null)
            _cameraLock.BeginDeathFall();

        if (freezeDelay > 0f)
            yield return new WaitForSecondsRealtime(freezeDelay);

        if (_cameraLock != null)
            _cameraLock.Lock();

        Time.timeScale = 0f;

        EnsurePanelInstance(active: true);
    }

    // Blocks until the Animator has fully played through the "Dying" state once (normalizedTime
    // reaches 1 - the clip doesn't loop, so it then just holds the death pose). Returns
    // immediately if there's no Animator, and is capped by a timeout so a missing/mis-wired
    // "Dying" state can never leave the DeathPanel stuck forever.
    IEnumerator WaitForDyingAnimation()
    {
        if (_animator == null)
            yield break;

        const float timeout = 6f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
            if (state.IsName("Dying") && state.normalizedTime >= 1f)
                yield break;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    public void OnContinueClicked()
    {
        if (_loading)
            return;

        _loading = true;
        Time.timeScale = 1f;

        string scene = string.IsNullOrWhiteSpace(spaceshipSceneName) ? "SpaceShip" : spaceshipSceneName;
        SceneManager.LoadScene(scene);
    }

    void EnsurePanelInstance(bool active)
    {
        if (_deathPanelInstance != null)
        {
            _deathPanelInstance.SetActive(active);
            if (active)
                _deathPanelInstance.transform.SetAsLastSibling();
            return;
        }

        // Prefer an already-placed child (e.g. prefab instance under the canvas).
        Transform existing = FindExistingDeathPanel();
        if (existing != null)
        {
            _deathPanelInstance = existing.gameObject;
            WireContinueButton(_deathPanelInstance.transform);
            _deathPanelInstance.SetActive(active);
            if (active)
                _deathPanelInstance.transform.SetAsLastSibling();
            return;
        }

        if (deathPanelPrefab == null)
        {
            Debug.LogWarning($"{nameof(PlayerDeathUI)}: assign a DeathPanel prefab.", this);
            return;
        }

        Canvas canvas = GetComponentInChildren<Canvas>(true);
        Transform parent = canvas != null ? canvas.transform : transform;

        _deathPanelInstance = Instantiate(deathPanelPrefab, parent, false);
        _deathPanelInstance.name = "DeathPanel";
        WireContinueButton(_deathPanelInstance.transform);
        _deathPanelInstance.SetActive(active);
        if (active)
            _deathPanelInstance.transform.SetAsLastSibling();
    }

    Transform FindExistingDeathPanel()
    {
        Transform direct = transform.Find("DeathPanel");
        if (direct != null)
            return direct;

        Canvas canvas = GetComponentInChildren<Canvas>(true);
        if (canvas != null)
        {
            Transform underCanvas = canvas.transform.Find("DeathPanel");
            if (underCanvas != null)
                return underCanvas;
        }

        return null;
    }

    void WireContinueButton(Transform root)
    {
        Button button = root.GetComponentInChildren<Button>(true);
        if (button == null)
            return;

        button.onClick.RemoveListener(OnContinueClicked);
        button.onClick.AddListener(OnContinueClicked);
    }
}
