using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// When the player is on floor -1, fades floor 1 visually (colliders stay).
/// Floors 0 and 1 leave floor 1 fully opaque.
/// </summary>
public class FloorVisibilityController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Transform player;
    [SerializeField] Transform floor1Root;

    [Header("Floor -1 detection")]
    [Tooltip("Player is on floor -1 while Y is below this value.")]
    [SerializeField] float floorMinus1MaxY = -2f;
    [SerializeField] float hysteresis = 0.4f;

    [Header("Transparency")]
    [Range(0f, 1f)]
    [SerializeField] float fadedAlpha = 0.28f;

    Renderer[] _renderers;
    Material[][] _originalShared;
    Material[][] _fadedShared;
    readonly List<Material> _fadedMaterials = new List<Material>();

    Shader _urpLit;
    bool _onFloorMinus1;
    bool _isFaded;
    bool _initialized;

    void Awake()
    {
        _urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (_urpLit == null)
            Debug.LogWarning("FloorVisibilityController: URP Lit shader not found.");

        CacheFloor1();
    }

    void Start()
    {
        EnsurePlayer();
        if (player == null || floor1Root == null)
            return;

        _onFloorMinus1 = player.position.y < floorMinus1MaxY;
        _initialized = true;
        ApplyVisibility();
    }

    void OnDestroy()
    {
        for (int i = 0; i < _fadedMaterials.Count; i++)
        {
            if (_fadedMaterials[i] != null)
                Destroy(_fadedMaterials[i]);
        }
    }

    void LateUpdate()
    {
        EnsurePlayer();
        if (player == null || floor1Root == null)
            return;

        bool onMinus1 = DetectFloorMinus1(player.position.y);
        if (!_initialized || onMinus1 != _onFloorMinus1)
        {
            _onFloorMinus1 = onMinus1;
            _initialized = true;
            ApplyVisibility();
        }
    }

    void EnsurePlayer()
    {
        if (player != null)
            return;

        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null)
        {
            player = tagged.transform;
            return;
        }

        TouchController motor = FindFirstObjectByType<TouchController>();
        if (motor != null)
            player = motor.transform;
    }

    void CacheFloor1()
    {
        if (floor1Root == null)
            return;

        _renderers = floor1Root.GetComponentsInChildren<Renderer>(true);
        _originalShared = new Material[_renderers.Length][];
        _fadedShared = new Material[_renderers.Length][];

        for (int i = 0; i < _renderers.Length; i++)
            _originalShared[i] = _renderers[i].sharedMaterials;
    }

    bool DetectFloorMinus1(float y)
    {
        if (!_initialized)
            return y < floorMinus1MaxY;

        if (_onFloorMinus1)
            return y < floorMinus1MaxY + hysteresis;

        return y < floorMinus1MaxY - hysteresis;
    }

    void ApplyVisibility()
    {
        bool shouldFade = _onFloorMinus1;
        if (shouldFade == _isFaded)
            return;

        if (shouldFade)
            FadeFloor1();
        else
            RestoreFloor1();

        _isFaded = shouldFade;
    }

    void FadeFloor1()
    {
        if (_urpLit == null || _renderers == null)
            return;

        for (int r = 0; r < _renderers.Length; r++)
        {
            Renderer renderer = _renderers[r];
            if (renderer == null)
                continue;

            if (_fadedShared[r] == null)
            {
                Material[] originals = _originalShared[r];
                var faded = new Material[originals.Length];
                for (int m = 0; m < originals.Length; m++)
                {
                    faded[m] = CreateFadedMaterial(originals[m], fadedAlpha);
                    if (faded[m] != null)
                        _fadedMaterials.Add(faded[m]);
                }

                _fadedShared[r] = faded;
            }
            else
            {
                SetMaterialsAlpha(_fadedShared[r], fadedAlpha);
            }

            renderer.sharedMaterials = _fadedShared[r];
        }
    }

    void RestoreFloor1()
    {
        if (_renderers == null)
            return;

        for (int r = 0; r < _renderers.Length; r++)
        {
            if (_renderers[r] != null)
                _renderers[r].sharedMaterials = _originalShared[r];
        }
    }

    static void SetMaterialsAlpha(Material[] materials, float alpha)
    {
        if (materials == null)
            return;

        for (int i = 0; i < materials.Length; i++)
        {
            Material mat = materials[i];
            if (mat == null || !mat.HasProperty("_BaseColor"))
                continue;

            Color c = mat.GetColor("_BaseColor");
            c.a = alpha;
            mat.SetColor("_BaseColor", c);
        }
    }

    Material CreateFadedMaterial(Material source, float alpha)
    {
        var mat = new Material(_urpLit);

        Texture albedo = GetTexture(source, "_BaseMap") ?? GetTexture(source, "_MainTex");
        if (albedo != null)
            mat.SetTexture("_BaseMap", albedo);

        Color color = Color.white;
        if (source != null)
        {
            if (source.HasProperty("_BaseColor"))
                color = source.GetColor("_BaseColor");
            else if (source.HasProperty("_Color"))
                color = source.GetColor("_Color");
        }

        color.a = alpha;
        mat.SetColor("_BaseColor", color);

        Texture bump = GetTexture(source, "_BumpMap") ?? GetTexture(source, "_NormalMap");
        if (bump != null)
        {
            mat.SetTexture("_BumpMap", bump);
            mat.EnableKeyword("_NORMALMAP");
        }

        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        return mat;
    }

    static Texture GetTexture(Material source, string property)
    {
        if (source == null || !source.HasProperty(property))
            return null;
        return source.GetTexture(property);
    }
}
