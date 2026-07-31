using UnityEngine;

/// <summary>
/// One surface tile on a spherical planet. Visual + lightweight gameplay data.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class PlanetTile : MonoBehaviour
{
    [SerializeField] string tileId = "grass";
    [SerializeField] bool walkable = true;
    [SerializeField] string zoneId = "default";
    [SerializeField] Texture2D albedo;
    [SerializeField] Color tint = Color.white;

    MeshFilter _filter;
    MeshRenderer _renderer;
    Material _runtimeMaterial;

    public string TileId => tileId;
    public bool Walkable => walkable;
    public string ZoneId => zoneId;
    public Texture2D Albedo => albedo;
    public Color Tint => tint;

    void OnEnable()
    {
        EnsureVisual();
        ApplyMaterial();
    }

    void OnValidate()
    {
        ApplyMaterial();
    }

    void OnDestroy()
    {
        if (_runtimeMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(_runtimeMaterial);
        else
            DestroyImmediate(_runtimeMaterial);

        _runtimeMaterial = null;
    }

    public void Configure(string id, bool isWalkable, string zone, Texture2D texture, Color color)
    {
        tileId = id;
        walkable = isWalkable;
        zoneId = zone;
        if (texture != null)
            albedo = texture;
        tint = color;
        EnsureVisual();
        ApplyMaterial();
    }

    public void ApplyGameplayData(string id, bool isWalkable, string zone)
    {
        tileId = id;
        walkable = isWalkable;
        zoneId = zone;
    }

    void EnsureVisual()
    {
        _filter = GetComponent<MeshFilter>();
        if (_filter == null)
            _filter = gameObject.AddComponent<MeshFilter>();

        // Built-in Plane: 10x10 in XZ, normal +Y.
        if (_filter.sharedMesh == null)
            _filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Plane.fbx");

        if (_filter.sharedMesh == null)
        {
            // Fallback if builtin path differs between Unity versions.
            var temp = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _filter.sharedMesh = temp.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying)
                Destroy(temp);
            else
                DestroyImmediate(temp);
        }

        _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null)
            _renderer = gameObject.AddComponent<MeshRenderer>();
    }

    void ApplyMaterial()
    {
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null)
            return;

        if (_runtimeMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return;

            _runtimeMaterial = new Material(shader) { name = $"Tile_{tileId}_Unlit" };
            _runtimeMaterial.hideFlags = HideFlags.DontSave;
        }

        if (_runtimeMaterial.HasProperty("_BaseColor"))
            _runtimeMaterial.SetColor("_BaseColor", tint);
        else
            _runtimeMaterial.color = tint;

        if (albedo != null)
        {
            _runtimeMaterial.mainTexture = albedo;
            if (_runtimeMaterial.HasProperty("_BaseMap"))
                _runtimeMaterial.SetTexture("_BaseMap", albedo);
            if (_runtimeMaterial.HasProperty("_MainTex"))
                _runtimeMaterial.SetTexture("_MainTex", albedo);
        }

        _renderer.sharedMaterial = _runtimeMaterial;
    }
}
