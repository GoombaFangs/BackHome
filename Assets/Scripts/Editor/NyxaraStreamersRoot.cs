using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared lookup for the scene's "Streamers" GameObject — the parent all runtime streaming
/// components (<see cref="PlanetGrassStreamer"/>, <see cref="PlanetTreeStreamer"/>,
/// <see cref="PlanetRockStreamer"/>) live on, kept separate from the planet itself so the planet's
/// hierarchy only holds actual planet content (Tiles, Environment, VisualShell).
/// </summary>
public static class NyxaraStreamersRoot
{
    const string RootName = "Streamers";

    public static Transform FindOrCreate()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
            return existing.transform;

        var go = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(go, "Create Streamers Root");
        return go.transform;
    }
}
