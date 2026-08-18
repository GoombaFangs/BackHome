using UnityEngine;

/// <summary>
/// Short white overlay flash when this creature takes damage.
/// Adds <c>BackHome/VFX/HitFlash</c> as an extra pass on body renderers.
/// </summary>
[RequireComponent(typeof(Creature))]
public class CreatureHitFlash : MonoBehaviour
{
    const string ResourcesPath = "Galaxy/Planets/Nyxara/Creatures/Shaders/HitFlash";

    static readonly int HitColorId = Shader.PropertyToID("_HitColor");
    static readonly int HitAmountId = Shader.PropertyToID("_HitAmount");

    [SerializeField] Material overlayMaterial;
    [SerializeField] Color hitColor = Color.white;
    [SerializeField, Min(0.01f)] float flashDuration = 0.1f;

    Creature _creature;
    Renderer[] _renderers;
    Material[][] _originalMaterials;
    Material _runtime;
    float _elapsed;
    bool _flashing;
    bool _overlayAttached;

    void Awake()
    {
        _creature = GetComponent<Creature>();
        CollectRenderers();
        EnsureMaterial();
    }

    void OnEnable()
    {
        if (_creature != null)
            _creature.Damaged += OnDamaged;
    }

    void OnDisable()
    {
        if (_creature != null)
            _creature.Damaged -= OnDamaged;

        StopFlash();
    }

    void OnDestroy()
    {
        DetachOverlay();
        if (_runtime != null)
            Destroy(_runtime);
    }

    void LateUpdate()
    {
        if (!_flashing)
            return;

        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / flashDuration);
        // Snap to full white, then drop off fast.
        float amount = (1f - t) * (1f - t);
        if (t >= 1f)
        {
            StopFlash();
            return;
        }

        ApplyAmount(amount);
    }

    void OnDamaged(float _)
    {
        if (_runtime == null && !EnsureMaterial())
            return;

        AttachOverlay();
        _elapsed = 0f;
        _flashing = true;
        ApplyAmount(1f);
    }

    void StopFlash()
    {
        _flashing = false;
        ApplyAmount(0f);
        DetachOverlay();
    }

    void ApplyAmount(float amount)
    {
        if (_runtime == null)
            return;

        _runtime.SetColor(HitColorId, hitColor);
        _runtime.SetFloat(HitAmountId, Mathf.Clamp01(amount));
    }

    bool EnsureMaterial()
    {
        if (_runtime != null)
            return true;

        Material template = overlayMaterial;
        if (template == null)
            template = Resources.Load<Material>(ResourcesPath);
        if (template == null)
            return false;

        _runtime = new Material(template);
        _runtime.name = template.name + " (HitFlash)";
        ApplyAmount(0f);
        return true;
    }

    void AttachOverlay()
    {
        if (_overlayAttached || _runtime == null || _renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            Material[] original = _originalMaterials[i];
            if (renderer == null || original == null || original.Length == 0)
                continue;

            var next = new Material[original.Length + 1];
            for (int m = 0; m < original.Length; m++)
                next[m] = original[m];
            next[original.Length] = _runtime;
            renderer.sharedMaterials = next;
        }

        _overlayAttached = true;
    }

    void DetachOverlay()
    {
        if (!_overlayAttached || _renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            Material[] original = _originalMaterials[i];
            if (renderer != null && original != null)
                renderer.sharedMaterials = original;
        }

        _overlayAttached = false;
    }

    void CollectRenderers()
    {
        Renderer[] found = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        for (int i = 0; i < found.Length; i++)
        {
            if (IsBodyRenderer(found[i]))
                count++;
        }

        _renderers = new Renderer[count];
        _originalMaterials = new Material[count][];
        int write = 0;
        for (int i = 0; i < found.Length; i++)
        {
            if (!IsBodyRenderer(found[i]))
                continue;

            Renderer renderer = found[i];
            _renderers[write] = renderer;
            _originalMaterials[write] = renderer.sharedMaterials;
            write++;
        }
    }

    static bool IsBodyRenderer(Renderer renderer)
    {
        if (renderer == null || renderer is ParticleSystemRenderer)
            return false;
        if (renderer.GetComponent<AttackRangeIndicator>() != null)
            return false;
        return renderer is SkinnedMeshRenderer || renderer is MeshRenderer;
    }
}
