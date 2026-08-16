using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Swaps the player's mesh materials onto <c>BackHome/VFX/TeleportBody</c> for the duration of a
/// teleport, then drives the dissolve from feet to head along planet-up.
/// </summary>
public sealed class TeleportBodyFx
{
    const string ResourcesPath = "Player/Teleport/TeleportBody";

    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int OriginId = Shader.PropertyToID("_OriginWS");
    static readonly int AxisId = Shader.PropertyToID("_AxisWS");
    static readonly int HeightId = Shader.PropertyToID("_Height");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    readonly List<Renderer> _renderers = new List<Renderer>();
    readonly List<Material[]> _original = new List<Material[]>();
    readonly List<Material> _runtime = new List<Material>();

    public static TeleportBodyFx Begin(Transform player, Material template, Vector3 origin, Vector3 axis)
    {
        if (player == null)
            return null;

        if (template == null)
            template = Resources.Load<Material>(ResourcesPath);
        if (template == null)
            return null;

        SkinnedMeshRenderer[] skins = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skins == null || skins.Length == 0)
            return null;

        var fx = new TeleportBodyFx();
        fx.Capture(skins, template, origin, axis.normalized);
        return fx;
    }

    public void SetProgress(float progress)
    {
        float p = Mathf.Clamp01(progress);
        for (int i = 0; i < _runtime.Count; i++)
        {
            if (_runtime[i] != null)
                _runtime[i].SetFloat(ProgressId, p);
        }
    }

    public void Release()
    {
        for (int i = 0; i < _renderers.Count; i++)
        {
            if (_renderers[i] != null && _original[i] != null)
                _renderers[i].sharedMaterials = _original[i];
        }

        for (int i = 0; i < _runtime.Count; i++)
        {
            if (_runtime[i] != null)
                Object.Destroy(_runtime[i]);
        }

        _renderers.Clear();
        _original.Clear();
        _runtime.Clear();
    }

    void Capture(SkinnedMeshRenderer[] skins, Material template, Vector3 origin, Vector3 axis)
    {
        float minH = float.PositiveInfinity;
        float maxH = float.NegativeInfinity;
        var unique = new Dictionary<Material, Material>();

        for (int i = 0; i < skins.Length; i++)
        {
            SkinnedMeshRenderer skin = skins[i];
            if (skin == null || !skin.enabled)
                continue;

            EncapsulateBounds(skin.bounds, origin, axis, ref minH, ref maxH);

            Material[] source = skin.sharedMaterials;
            _renderers.Add(skin);
            _original.Add(source);

            var replaced = new Material[source.Length];
            for (int m = 0; m < source.Length; m++)
            {
                Material key = source[m] != null ? source[m] : template;
                if (!unique.TryGetValue(key, out Material instance))
                {
                    instance = Object.Instantiate(template);
                    instance.name = template.name + " (Runtime)";
                    CopyAppearance(key, instance);
                    unique.Add(key, instance);
                    _runtime.Add(instance);
                }
                replaced[m] = instance;
            }

            skin.sharedMaterials = replaced;
        }

        if (float.IsInfinity(minH) || maxH - minH < 0.05f)
        {
            minH = 0f;
            maxH = 2f;
        }

        Vector3 foot = origin + axis * minH;
        float height = Mathf.Max(0.05f, maxH - minH);

        for (int i = 0; i < _runtime.Count; i++)
        {
            Material mat = _runtime[i];
            mat.SetFloat(ProgressId, 0f);
            mat.SetVector(OriginId, foot);
            mat.SetVector(AxisId, axis);
            mat.SetFloat(HeightId, height);
        }
    }

    static void EncapsulateBounds(Bounds bounds, Vector3 origin, Vector3 axis, ref float minH, ref float maxH)
    {
        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 p = c + new Vector3(e.x * x, e.y * y, e.z * z);
                    float h = Vector3.Dot(p - origin, axis);
                    if (h < minH)
                        minH = h;
                    if (h > maxH)
                        maxH = h;
                }
            }
        }
    }

    static void CopyAppearance(Material source, Material dest)
    {
        if (source == null || dest == null)
            return;

        Texture albedo = null;
        if (source.HasProperty(BaseMapId))
            albedo = source.GetTexture(BaseMapId);
        if (albedo == null)
            albedo = source.mainTexture;
        if (albedo != null && dest.HasProperty(BaseMapId))
            dest.SetTexture(BaseMapId, albedo);

        if (source.HasProperty(BaseColorId) && dest.HasProperty(BaseColorId))
            dest.SetColor(BaseColorId, source.GetColor(BaseColorId));
    }
}
