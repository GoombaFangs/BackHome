using UnityEngine;

/// <summary>
/// Leftover Border cubes are ignored as walls. Walk blocking uses the land tile mesh.
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

    public static bool IsBlockingWall(Collider col)
    {
        return false;
    }
}
