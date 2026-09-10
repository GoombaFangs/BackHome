using UnityEngine;

/// <summary>
/// Replaces the built-in low-poly sphere so cartoon water waves have enough verts to read.
/// Radius stays 0.5 to match Unity's sphere, so the Sea scale is unchanged.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter))]
public sealed class CasualWaterSphereMesh : MonoBehaviour
{
    const int DefaultLat = 80;
    const int DefaultLon = 144;

    [SerializeField] int latitudeSegments = DefaultLat;
    [SerializeField] int longitudeSegments = DefaultLon;

    MeshFilter _filter;
    Mesh _mesh;
    float _builtScale = -1f;

    public void Refresh()
    {
        ApplyMesh();
    }

    void OnEnable()
    {
        ApplyMesh();
    }

    void OnDisable()
    {
        ReleaseMesh();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        latitudeSegments = Mathf.Max(12, latitudeSegments);
        longitudeSegments = Mathf.Max(16, longitudeSegments);
        if (!isActiveAndEnabled)
            return;

        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled)
                ApplyMesh();
        };
    }
#endif

    void ApplyMesh()
    {
        _filter = GetComponent<MeshFilter>();
        if (_filter == null)
            return;

        int lat = Mathf.Max(12, latitudeSegments);
        int lon = Mathf.Max(16, longitudeSegments);
        float worldScale = transform.lossyScale.x;
        if (_mesh != null
            && _mesh.vertexCount == (lat + 1) * (lon + 1)
            && Mathf.Abs(_builtScale - worldScale) < 0.01f)
        {
            _filter.sharedMesh = _mesh;
            return;
        }

        ReleaseMesh();
        _mesh = BuildSphere(0.5f, lat, lon, worldScale);
        _mesh.hideFlags = HideFlags.DontSave;
        _builtScale = worldScale;
        _filter.sharedMesh = _mesh;
    }

    void ReleaseMesh()
    {
        if (_mesh == null)
            return;

        if (Application.isPlaying)
            Destroy(_mesh);
        else
            DestroyImmediate(_mesh);

        _mesh = null;
        _builtScale = -1f;
    }

    static Mesh BuildSphere(float radius, int latSegments, int lonSegments, float worldScale)
    {
        int ringVerts = lonSegments + 1;
        int vertCount = (latSegments + 1) * ringVerts;
        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var colors = new Color[vertCount];

        for (int lat = 0; lat <= latSegments; lat++)
        {
            float v = lat / (float)latSegments;
            float pitch = Mathf.PI * (-0.5f + v);
            float y = Mathf.Sin(pitch);
            float ringRadius = Mathf.Cos(pitch);

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float u = lon / (float)lonSegments;
                float yaw = u * Mathf.PI * 2f;
                var normal = new Vector3(
                    Mathf.Cos(yaw) * ringRadius,
                    y,
                    Mathf.Sin(yaw) * ringRadius);

                int index = lat * ringVerts + lon;
                vertices[index] = normal * radius;
                normals[index] = normal;
                uvs[index] = new Vector2(vertices[index].x, vertices[index].z) * worldScale;
                // Bitgem foam uses vertex red. Black keeps foam to scene-depth shores only.
                colors[index] = Color.black;
            }
        }

        var triangles = new int[latSegments * lonSegments * 6];
        int t = 0;
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int current = lat * ringVerts + lon;
                int next = current + ringVerts;

                triangles[t++] = current;
                triangles[t++] = next;
                triangles[t++] = current + 1;

                triangles[t++] = current + 1;
                triangles[t++] = next;
                triangles[t++] = next + 1;
            }
        }

        var mesh = new Mesh();
        mesh.name = "CasualWaterSphere";
        mesh.indexFormat = vertCount > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}
