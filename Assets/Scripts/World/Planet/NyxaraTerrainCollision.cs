using UnityEngine;

/// <summary>
/// Border cubes are kinematic walls, not physics colliders. The tile mesh is the walk floor.
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
