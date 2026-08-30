using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Death screen. Listens for <see cref="PlayerVitals.Died"/> and, the instant it fires:
/// shows the DeathPanel, hard-stops player locomotion so nothing can be moved, and hands the
/// camera to <see cref="PlayerDeathCameraLock"/> for a small scripted settle instead of letting
/// the normal follow camera keep reacting to the death (which reads as it "going crazy").
/// After a short beat time freezes and the camera fully locks with a dazed overlay - this is
/// the single timer for that final step, so the freeze and the camera lock can never drift
/// apart.
/// </summary>
public class PlayerDeathUI : MonoBehaviour
{
    [SerializeField] string shipSceneName = "SpaceShip";
    [Tooltip("Seconds after death before the camera fully locks, time freezes and stays frozen.")]
    [SerializeField, Min(0f)] float freezeDelay = 2f;
    [Tooltip("DeathPanel prefab (You are dead + Continue). Instantiated under the UI canvas.")]
    [SerializeField] GameObject deathPanelPrefab;

    PlayerVitals _vitals;
    PlayerDeathCameraLock _cameraLock;
    GameObject _deathPanelInstance;
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

        // Instant: panel + locomotion lock. Nothing here waits on the timer below.
        EnsurePanelInstance(active: true);
        StopPlayerLocomotion();

        if (_cameraLock == null)
            _cameraLock = GetComponent<PlayerDeathCameraLock>();
        if (_cameraLock != null)
            _cameraLock.BeginDeathFall();

        StartCoroutine(FreezeAfterDelay());
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

    IEnumerator FreezeAfterDelay()
    {
        if (freezeDelay > 0f)
            yield return new WaitForSecondsRealtime(freezeDelay);

        if (_cameraLock != null)
            _cameraLock.Lock();

        Time.timeScale = 0f;
    }

    public void OnContinueClicked()
    {
        if (_loading)
            return;

        _loading = true;
        Time.timeScale = 1f;

        string scene = string.IsNullOrWhiteSpace(shipSceneName) ? "SpaceShip" : shipSceneName;
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
