using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Placeholder death screen. Listens for <see cref="PlayerVitals.Died"/>,
/// shows the DeathPanel prefab + Continue, then reloads the spaceship scene (full vitals).
/// </summary>
public class PlayerDeathUI : MonoBehaviour
{
    [SerializeField] string shipSceneName = "SpaceShip";
    [Tooltip("DeathPanel prefab (You are dead + Continue). Instantiated under the UI canvas.")]
    [SerializeField] GameObject deathPanelPrefab;

    PlayerVitals _vitals;
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
        EnsurePanelInstance(active: true);
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
