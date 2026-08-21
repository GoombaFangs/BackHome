using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared lookup for the scene's environment streaming root — the parent all runtime streaming
/// components (<see cref="PlanetGrassStreamer"/>, <see cref="PlanetTreeStreamer"/>,
/// <see cref="PlanetRockStreamer"/>) and <see cref="PlanetEnvironmentManager"/> live on.
/// </summary>
public static class NyxaraStreamersRoot
{
    const string RootName = "EnvironmentManager";
    const string LegacyRootName = "Streamers";

    public static Transform FindOrCreate()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing == null)
            existing = GameObject.Find(LegacyRootName);

        if (existing != null)
        {
            if (!string.Equals(existing.name, RootName, System.StringComparison.Ordinal))
                existing.name = RootName;

            return existing.transform;
        }

        var go = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(go, "Create EnvironmentManager");
        return go.transform;
    }
}
