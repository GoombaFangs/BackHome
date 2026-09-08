using UnityEngine;

/// <summary>
/// Ground-layer helpers. Study walking uses <see cref="NyxaraRouteBounds"/> (kinematic band).
/// Ridge/cliff stay visual-only. Border boxes stay in the prefab for layout compare.
/// </summary>
public static class NyxaraTerrainCollision
{
    public const string GroundLayerName = "Ground";

    public static int GroundLayerIndex
    {
        get
        {
            int layer = LayerMask.NameToLayer(GroundLayerName);
            return layer >= 0 ? layer : 3;
        }
    }

    public static void ClearBlockingMesh(GameObject go)
    {
        if (go == null)
            return;
        var collider = go.GetComponent<MeshCollider>();
        if (collider != null)
        {
            collider.sharedMesh = null;
            collider.enabled = false;
        }
    }

    /// <summary>Authored Border cubes. Not tile mesh, ridge, or cliff.</summary>
    public static bool IsBlockingWall(Collider col)
    {
        if (col == null)
            return false;

        Transform t = col.transform;
        while (t != null)
        {
            if (t.name == "Borders")
                return true;
            if (t.GetComponent<SphericalPlanet>() != null)
                break;
            t = t.parent;
        }

        return false;
    }
}
