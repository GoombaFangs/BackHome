using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Floating damage / heal popup: rises, fades, optional outline, then destroys itself.
/// </summary>
[RequireComponent(typeof(TextMesh))]
public class DamagePopup : MonoBehaviour
{
    [SerializeField] TextMesh textMesh;
    [SerializeField] float lifetime = 0.85f;
    [SerializeField] float floatSpeed = 1.6f;
    [SerializeField] float fadeDelay = 0.25f;
    [SerializeField] float startScale = 0.55f;
    [SerializeField] float peakScale = 0.85f;
    [SerializeField] float endScale = 0.45f;
    [SerializeField] Color damageColor = new Color(1f, 0.35f, 0.25f, 1f);
    [SerializeField] Color healColor = new Color(0.35f, 0.95f, 0.45f, 1f);
    [Tooltip("Extra spawn offset on this prefab (local to surface: X right, Y up, Z forward). Applies to every clone.")]
    [SerializeField] Vector3 spawnOffset = new Vector3(0f, 0.35f, 0f);

    [Header("Outline")]
    [SerializeField] bool useOutline = true;
    [SerializeField] Color outlineColor = new Color(0f, 0f, 0f, 0.92f);
    [SerializeField, Min(0f)] float outlineOffset = 0.04f;
    [SerializeField] TextMesh[] outlineMeshes;

    Vector3 _moveDir = Vector3.up;
    float _age;
    Color _baseColor;
    bool _setup;
    readonly List<TextMesh> _outlines = new();

    public static DamagePopup Create(
        DamagePopup prefab,
        Vector3 worldPosition,
        float amount,
        Vector3 up,
        bool isHeal = false)
    {
        if (prefab == null)
            return null;

        DamagePopup popup = Instantiate(prefab, worldPosition, Quaternion.identity);
        popup.Setup(amount, up, isHeal);
        return popup;
    }

    void Awake()
    {
        if (textMesh == null)
            textMesh = GetComponent<TextMesh>();
        CacheOutlines();
    }

    public void Setup(float amount, Vector3 up, bool isHeal = false)
    {
        if (textMesh == null)
            textMesh = GetComponent<TextMesh>();

        CacheOutlines();

        _moveDir = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;

        Vector3 right = Vector3.Cross(_moveDir, Vector3.forward);
        if (right.sqrMagnitude < 0.001f)
            right = Vector3.Cross(_moveDir, Vector3.right);
        right.Normalize();
        Vector3 forward = Vector3.Cross(right, _moveDir).normalized;

        transform.position +=
            right * spawnOffset.x
            + _moveDir * spawnOffset.y
            + forward * spawnOffset.z
            // Slight random sideways drift so stacked hits don't fully overlap.
            + right * Random.Range(-0.35f, 0.35f);

        int display = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(amount)));
        string text = display.ToString();
        textMesh.text = text;
        _baseColor = isHeal ? healColor : damageColor;
        textMesh.color = _baseColor;
        transform.localScale = Vector3.one * startScale;

        ApplyOutlineText(text);
        ApplyOutlineColor(_baseColor.a);

        _age = 0f;
        _setup = true;
    }

    void LateUpdate()
    {
        if (!_setup)
            return;

        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;

        _age += Time.deltaTime;
        float t = lifetime > 0f ? Mathf.Clamp01(_age / lifetime) : 1f;

        transform.position += _moveDir * (floatSpeed * Time.deltaTime);

        float scale;
        if (t < 0.2f)
            scale = Mathf.Lerp(startScale, peakScale, t / 0.2f);
        else
            scale = Mathf.Lerp(peakScale, endScale, (t - 0.2f) / 0.8f);
        transform.localScale = Vector3.one * scale;

        Color c = _baseColor;
        if (_age > fadeDelay)
        {
            float fadeT = lifetime > fadeDelay
                ? Mathf.Clamp01((_age - fadeDelay) / (lifetime - fadeDelay))
                : 1f;
            c.a = Mathf.Lerp(_baseColor.a, 0f, fadeT);
        }

        textMesh.color = c;
        ApplyOutlineColor(c.a);

        if (_age >= lifetime)
            Destroy(gameObject);
    }

    void CacheOutlines()
    {
        if (_outlines.Count > 0)
            return;

        if (outlineMeshes != null)
        {
            for (int i = 0; i < outlineMeshes.Length; i++)
            {
                if (outlineMeshes[i] != null)
                    _outlines.Add(outlineMeshes[i]);
            }
        }

        if (_outlines.Count == 0 && useOutline)
            BuildRuntimeOutlines();

        for (int i = 0; i < _outlines.Count; i++)
            _outlines[i].gameObject.SetActive(useOutline);
    }

    void BuildRuntimeOutlines()
    {
        Vector2[] dirs =
        {
            new Vector2(-1f, -1f), new Vector2(-1f, 0f), new Vector2(-1f, 1f),
            new Vector2(0f, -1f), new Vector2(0f, 1f),
            new Vector2(1f, -1f), new Vector2(1f, 0f), new Vector2(1f, 1f),
        };

        MeshRenderer mainRenderer = textMesh.GetComponent<MeshRenderer>();
        int baseOrder = mainRenderer != null ? mainRenderer.sortingOrder : 50;

        for (int i = 0; i < dirs.Length; i++)
        {
            var go = new GameObject($"Outline_{i}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(
                dirs[i].x * outlineOffset,
                dirs[i].y * outlineOffset,
                0.02f);

            TextMesh outline = go.AddComponent<TextMesh>();
            CopyTextMeshSettings(textMesh, outline);
            outline.color = outlineColor;

            MeshRenderer outlineRenderer = go.GetComponent<MeshRenderer>();
            if (outlineRenderer != null)
            {
                outlineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                outlineRenderer.receiveShadows = false;
                outlineRenderer.sortingOrder = baseOrder - 1;
                if (mainRenderer != null && mainRenderer.sharedMaterial != null)
                    outlineRenderer.sharedMaterial = mainRenderer.sharedMaterial;
            }

            _outlines.Add(outline);
        }
    }

    static void CopyTextMeshSettings(TextMesh from, TextMesh to)
    {
        to.font = from.font;
        to.fontSize = from.fontSize;
        to.fontStyle = from.fontStyle;
        to.characterSize = from.characterSize;
        to.lineSpacing = from.lineSpacing;
        to.anchor = from.anchor;
        to.alignment = from.alignment;
        to.tabSize = from.tabSize;
        to.richText = from.richText;
        to.offsetZ = from.offsetZ;
    }

    void ApplyOutlineText(string text)
    {
        for (int i = 0; i < _outlines.Count; i++)
        {
            TextMesh outline = _outlines[i];
            if (outline == null)
                continue;
            CopyTextMeshSettings(textMesh, outline);
            outline.text = text;
        }
    }

    void ApplyOutlineColor(float fillAlpha)
    {
        Color oc = outlineColor;
        oc.a = outlineColor.a * fillAlpha;
        for (int i = 0; i < _outlines.Count; i++)
        {
            if (_outlines[i] != null)
                _outlines[i].color = oc;
        }
    }
}
