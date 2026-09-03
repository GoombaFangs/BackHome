using System.Collections;
using UnityEngine;

/// <summary>
/// Continuous "already on fire" ambient effect for the player capsule crash cinematic (see
/// <see cref="PlayerCrashIntro"/>): a flame shell hugging the hull, embers peeling off, and a warm
/// flickering light. Runs the whole time the capsule is airborne - from the moment it appears
/// alone in space through the crash fall - so the space-hold doesn't read as "frozen still", but
/// as "already burning, already falling". Stops on impact.
///
/// Unlike <see cref="PlayerFireTrail"/> / <see cref="PlayerCrashImpact"/>, the actual look is
/// authored as a normal prefab (<see cref="effectPrefab"/>) - drag one in and it's fully editable
/// with the regular Particle System / Light inspectors and Scene view preview, instead of being
/// generated in code. Wire it via PlayerCrashIntro.reentryEffectPrefab (or assign it directly here
/// if you add this component to the capsule by hand).
///
/// In the common case (PlayerDiveDownCapsule) the effect is already nested directly on the
/// capsule as a child named "TrailVfx" - <see cref="Build"/> reuses that child as-is instead of
/// instantiating a second copy, and <see cref="effectPrefab"/>/Resources fallback only matter for
/// a capsule that doesn't already have one nested.
/// </summary>
public class PlayerReentryGlow : MonoBehaviour
{
    [Tooltip("Prefab with the re-entry fire look (flame shell + embers + flicker light). Edit this " +
        "prefab directly in the Editor to tune the effect - it's instantiated as-is at runtime. " +
        "Leave empty if the effect is already nested directly on this capsule (e.g. \"TrailVfx\").")]
    [SerializeField] GameObject effectPrefab;

    [Header("Light Flicker (animated on top of the prefab's own light settings)")]
    [SerializeField] bool flickerLight = true;
    [SerializeField, Min(0f)] float lightFlickerSpeed = 16f;
    [SerializeField, Range(0f, 1f)] float lightFlickerAmount = 0.35f;

    GameObject _instance;
    ParticleSystem[] _particleSystems;
    Light _light;
    float _baseLightIntensity;
    Coroutine _flickerRoutine;
    bool _built;

    /// <summary>Name of the trail VFX child baked directly into PlayerDiveDownCapsule (see
    /// PlayerCapsuleVfxBaker) - checked first, regardless of <see cref="effectPrefab"/>, since the
    /// canonical capsule always already has this nested and there's nothing left to instantiate.</summary>
    const string NestedEffectChildName = "TrailVfx";

    /// <summary>The instantiated effect prefab's transform (e.g. "TrailVfx"), once built - lets
    /// other VFX components (see PlayerFireTrail.SetEffectParent) nest under the same visible root
    /// instead of scattering loose particle objects directly under the capsule. Null until
    /// <see cref="Play"/> (or <see cref="Awake"/>, if the prefab was already assigned) has
    /// actually built it.</summary>
    public Transform EffectRoot => _instance != null ? _instance.transform : null;

    void Awake()
    {
        Build();
    }

    /// <summary>Assign the authored prefab (e.g. from PlayerCrashIntro right after adding this
    /// component). Safe to call before Play() even if Awake() already ran.</summary>
    public void SetEffectPrefab(GameObject prefab)
    {
        effectPrefab = prefab;
    }

    void Build()
    {
        if (_built)
            return;

        // Reuse an already-authored child instead of instantiating a second, duplicate copy on
        // top of it - PlayerDiveDownCapsule has "TrailVfx" nested directly in the prefab (see
        // PlayerCapsuleVfxBaker), so there's nothing left to instantiate at runtime for it.
        Transform existing = transform.Find(NestedEffectChildName);
        if (existing == null && effectPrefab != null)
            existing = transform.Find(effectPrefab.name);

        if (existing != null)
        {
            _instance = existing.gameObject;
        }
        else if (effectPrefab != null)
        {
            _instance = Instantiate(effectPrefab, transform);
            _instance.name = effectPrefab.name;
            _instance.transform.localPosition = Vector3.zero;
            _instance.transform.localRotation = Quaternion.identity;
        }
        else
        {
            return; // Nothing nested and nothing assigned - Play() will warn and no-op.
        }

        _built = true;
        _particleSystems = _instance.GetComponentsInChildren<ParticleSystem>(true);
        _light = _instance.GetComponentInChildren<Light>(true);
        _baseLightIntensity = _light != null ? _light.intensity : 0f;

        SetInstanceActive(false);
    }

    /// <summary>Call the moment the capsule appears in space - burns continuously until Stop().</summary>
    public void Play()
    {
        Build();
        if (_instance == null)
        {
            Debug.LogWarning("PlayerReentryGlow: no nested \"" + NestedEffectChildName +
                "\" child and no effect prefab assigned - skipping re-entry fire.", this);
            return;
        }

        SetInstanceActive(true);
        if (_particleSystems != null)
        {
            foreach (ParticleSystem ps in _particleSystems)
            {
                ps.Clear(false);
                ps.Play(false);
            }
        }

        if (_light != null)
        {
            _light.enabled = true;
            if (flickerLight)
            {
                if (_flickerRoutine != null)
                    StopCoroutine(_flickerRoutine);
                _flickerRoutine = StartCoroutine(FlickerRoutine());
            }
        }
    }

    /// <summary>Call on impact - the fire is extinguished by the crash.</summary>
    public void Stop()
    {
        if (_particleSystems != null)
        {
            foreach (ParticleSystem ps in _particleSystems)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (_flickerRoutine != null)
        {
            StopCoroutine(_flickerRoutine);
            _flickerRoutine = null;
        }
        if (_light != null)
        {
            _light.enabled = false;
            _light.intensity = _baseLightIntensity;
        }
    }

    void SetInstanceActive(bool active)
    {
        if (_instance != null)
            _instance.SetActive(active);
    }

    IEnumerator FlickerRoutine()
    {
        float seed = Random.value * 100f;
        while (true)
        {
            float n = Mathf.PerlinNoise(seed, Time.time * lightFlickerSpeed);
            _light.intensity = _baseLightIntensity * Mathf.Lerp(1f - lightFlickerAmount, 1f + lightFlickerAmount, n);
            yield return null;
        }
    }
}
