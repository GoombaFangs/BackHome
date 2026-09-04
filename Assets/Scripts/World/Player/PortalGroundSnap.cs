using UnityEngine;

/// <summary>Ground alignment helpers for the portal prefab's crater bowl.</summary>
static class PortalGroundSnap
{
    const string CraterRootName = "Crater";
    const string CraterMeshChildName = "Crater";

    public static void Snap(Transform portal, Vector3 surfacePoint, Vector3 surfaceUp, float embed)
    {
        if (portal == null)
            return;

        Physics.SyncTransforms();

        Transform craterRoot = portal.Find(CraterRootName);
        if (craterRoot != null && TrySnapMeshBottom(portal, craterRoot, surfacePoint, surfaceUp, embed))
            return;

        if (craterRoot != null)
        {
            PlayerVfxUtility.SnapBaseToSurface(portal, surfacePoint, surfaceUp, embed, craterRoot);
            return;
        }

        PlayerVfxUtility.SnapBaseToSurface(portal, surfacePoint, surfaceUp, embed);
    }

    static bool TrySnapMeshBottom(
        Transform portal,
        Transform craterRoot,
        Vector3 surfacePoint,
        Vector3 surfaceUp,
        float embed)
    {
        Transform meshTransform = craterRoot.Find(CraterMeshChildName);
        if (meshTransform == null)
            meshTransform = craterRoot;

        MeshFilter filter = meshTransform.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            return false;

        Bounds localBounds = filter.sharedMesh.bounds;
        Vector3 center = localBounds.center;
        Vector3 extents = localBounds.extents;
        float lowestAlongUp = float.MaxValue;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 localCorner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    float alongUp = Vector3.Dot(meshTransform.TransformPoint(localCorner) - surfacePoint, surfaceUp);
                    if (alongUp < lowestAlongUp)
                        lowestAlongUp = alongUp;
                }
            }
        }

        portal.position -= surfaceUp * (lowestAlongUp + embed);
        return true;
    }
}
