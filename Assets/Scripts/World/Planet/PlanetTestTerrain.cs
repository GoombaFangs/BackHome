using UnityEngine;

/// <summary>
/// Places the authored Nyxara Test meshes (ridge + cliff) on the planet.
/// Those OBJs are already in PlanetNyxara parent-local space at radius 75.
/// </summary>
public static class PlanetTestTerrain
{
    public const string RootName = "TestTerrainMeshes";

    const string NorthRidgeResource = "Galaxy/Nyxara/Test/North_Ridge";
    const string SouthCliffResource = "Galaxy/Nyxara/Test/South_Cliff";

    public static Transform FindRoot(SphericalPlanet planet)
    {
        return planet != null ? planet.transform.Find(RootName) : null;
    }

    public static int Attach(SphericalPlanet planet)
    {
        if (planet == null)
            return 0;

        Remove(planet);

        var root = new GameObject(RootName);
        root.transform.SetParent(planet.transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        int added = 0;
        added += AttachOne(root.transform, NorthRidgeResource, "North_Ridge") ? 1 : 0;
        added += AttachOne(root.transform, SouthCliffResource, "South_Cliff") ? 1 : 0;
        return added;
    }

    public static void Remove(SphericalPlanet planet)
    {
        Transform root = FindRoot(planet);
        if (root == null)
            return;

        if (Application.isPlaying)
            Object.Destroy(root.gameObject);
        else
            Object.DestroyImmediate(root.gameObject);
    }

    static bool AttachOne(Transform parent, string resourcePath, string name)
    {
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab != null)
        {
            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            EnsureColliders(instance);
            return true;
        }

        Mesh mesh = LoadMesh(resourcePath);
        if (mesh == null)
        {
            Debug.LogWarning("[BackHome] Missing Test mesh: Resources/" + resourcePath + ".obj");
            return false;
        }

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = FallbackMaterial();
        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;
        col.convex = false;
        return true;
    }

    static Mesh LoadMesh(string resourcePath)
    {
        Mesh mesh = Resources.Load<Mesh>(resourcePath);
        if (mesh != null)
            return mesh;

        Mesh[] all = Resources.LoadAll<Mesh>(resourcePath);
        return all != null && all.Length > 0 ? all[0] : null;
    }

    static void EnsureColliders(GameObject instance)
    {
        MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter.sharedMesh == null)
                continue;

            MeshCollider col = filter.GetComponent<MeshCollider>();
            if (col == null)
                col = filter.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = filter.sharedMesh;
            col.convex = false;
        }
    }

    static Material FallbackMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader) { name = "TestTerrain_Fallback" };
        Color rock = new Color(0.45f, 0.42f, 0.55f, 1f);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", rock);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", rock);
        return mat;
    }
}
