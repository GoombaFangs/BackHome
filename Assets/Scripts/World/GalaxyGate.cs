using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Loads a target scene when the player enters this trigger volume.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GalaxyGate : MonoBehaviour
{
    [SerializeField] string targetSceneName;
    [Tooltip("World-space distance used when CharacterController triggers are unavailable.")]
    [SerializeField] float proximityFallbackRadius = 2.4f;

    bool _loading;
    Transform _player;
    float _armAtTime;

    void Reset()
    {
        EnsureTrigger();
    }

    void Awake()
    {
        EnsureTrigger();
        // Avoid instantly loading if the player spawns next to the portal.
        _armAtTime = Time.time + 0.75f;
    }

    void EnsureTrigger()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    void Update()
    {
        if (_loading || string.IsNullOrEmpty(targetSceneName) || Time.time < _armAtTime)
            return;

        if (_player == null)
        {
            GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo == null)
                return;
            _player = playerGo.transform;
        }

        if ((_player.position - transform.position).sqrMagnitude <= proximityFallbackRadius * proximityFallbackRadius)
            LoadTargetScene();
    }

    void OnTriggerEnter(Collider other)
    {
        if (_loading || string.IsNullOrEmpty(targetSceneName) || Time.time < _armAtTime)
            return;

        if (!IsPlayer(other))
            return;

        LoadTargetScene();
    }

    void LoadTargetScene()
    {
        if (_loading || string.IsNullOrEmpty(targetSceneName))
            return;

        _loading = true;
        SceneManager.LoadScene(targetSceneName);
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
               || other.GetComponentInParent<TochController>() != null
               || other.GetComponentInParent<CharacterController>() != null;
    }
}
