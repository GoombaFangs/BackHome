using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(NyxaraTerrainStudySession))]
public class NyxaraTerrainStudySessionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var session = (NyxaraTerrainStudySession)target;
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Study extras only. Do not Apply Prefab onto PlanetNyxara. " +
            "Walls, triggers, and enemies are not moved. Tick coverFullRing (or Bake Full Ring) to wrap the route.",
            MessageType.Info);

        if (GUILayout.Button("Rebuild Ridge And Cliff From Borders", GUILayout.Height(36)))
            NyxaraTerrainStudyWorkspace.RebuildRidgeAndCliffFromBorders();

        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();

        NyxaraA2BoundaryOverlay overlay = session.BoundaryOverlay;
        if (overlay != null && overlay.summary != null && overlay.HasSamples)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("A2 Boundary Measurements", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"{overlay.summary.recommendation}\n\n" +
                $"{overlay.summary.wallVerdict}\n\n" +
                $"Ground: {overlay.summary.groundSource}\n" +
                $"Walk radius: {overlay.summary.minWalkRadius:0.000} – {overlay.summary.maxWalkRadius:0.000} " +
                $"(heightmap {(overlay.summary.heightmapAffectsRadius ? "on" : "off")})\n" +
                $"South overlap (trigger past authored wall): {overlay.summary.maxSouthOverlapArcUnits:0.00} u " +
                $"({overlay.summary.maxSouthOverlapDegrees:0.02}°, {overlay.summary.triggerSouthOfWallCount} meridians)\n" +
                $"Fit-pack proposed shift: {overlay.summary.fitPackProposedShiftUnits:0.00} u (not applied)\n" +
                $"West opening: {overlay.westOpening.openingArcUnits:0.0} u  |  East opening: {overlay.eastOpening.openingArcUnits:0.0} u\n" +
                $"West corridor → R1: {(overlay.westCorridor.reachedAdjacentTrigger ? "reaches R1" : overlay.westCorridor.notes)}\n" +
                $"East corridor → A1: {overlay.eastCorridor.notes}",
                overlay.activeSouthBoundary == NyxaraA2BoundaryOverlay.SouthBoundaryChoice.AuthoredWalls
                    ? MessageType.Info
                    : MessageType.Warning);
        }

        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Rebuild A2 Boundaries", GUILayout.Height(28)))
            {
                Undo.RecordObject(session, "Rebuild A2 Boundaries");
                session.RebuildBoundaryOverlay();
            }

            if (GUILayout.Button("Save Overlay JSON", GUILayout.Height(28)))
                NyxaraTerrainStudyWorkspace.SaveBoundaryOverlayJson();
        }

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Bake Full Ring Ridge And Cliff", GUILayout.Height(32)))
            NyxaraTerrainStudyWorkspace.BakeFullRing();

        if (GUILayout.Button("Bake A2 North Ridge", GUILayout.Height(28)))
            NyxaraTerrainStudyWorkspace.BakeNorthRidge();

        NyxaraA2NorthRidge ridge = session.GetComponentInChildren<NyxaraA2NorthRidge>(true);
        if (ridge != null && !string.IsNullOrEmpty(ridge.BakeReport))
            EditorGUILayout.HelpBox(ridge.BakeReport, MessageType.None);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Mountain", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Rebuild From Borders resamples the live Borders cubes, then rebuilds tiles, mountain, and sea. " +
            "Bake Full Ring wraps every north wall (mountain). Gaps without a wall stay open. " +
            "Sea transform is authored — Play does not move it. " +
            "Do not Apply Prefab onto PlanetNyxara.",
            MessageType.Info);
        if (GUILayout.Button("Place Player In A2 (Game Camera)", GUILayout.Height(28)))
            NyxaraTerrainStudyWorkspace.PlacePlayerInA2();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Stage 9 — Package", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "NyxaraA2Terrain.prefab is visuals only (identity under the planet). " +
            "Bake both meshes, then Save A2 Terrain Prefab. Do not Apply onto PlanetNyxara. " +
            "Compare Layout To Snapshot checks Areas/Borders against the stage-1 JSON.",
            MessageType.Info);
        if (GUILayout.Button("Save A2 Terrain Prefab", GUILayout.Height(28)))
            NyxaraTerrainStudyWorkspace.SaveA2TerrainPrefab();
        if (GUILayout.Button("Compare Layout To Snapshot", GUILayout.Height(28)))
            NyxaraTerrainStudyWorkspace.CompareLayoutToSnapshot();
        if (GUILayout.Button("Remove Duplicate Area And Wall Copies", GUILayout.Height(28)))
            NyxaraTerrainStudyWorkspace.RemoveDuplicateLayoutCopiesMenu();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Stage 6 — Placeholder walls", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "After a full-ring bake, hide every Borders cube renderer. In Play, solid Border colliders are off (triggers stay). " +
            "A2-only hide is Cube (2)/(3)/(4)/(7); Cube (5) stays until R1 is in the ring. " +
            "Do not Apply Prefab onto PlanetNyxara.",
            MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Hide A2 Wall Renderers", GUILayout.Height(28)))
            {
                Undo.RecordObject(session, "Hide A2 Placeholder Wall Renderers");
                session.SetHideCoveredPlaceholderWallRenderers(true);
            }

            if (GUILayout.Button("Show A2 Wall Renderers", GUILayout.Height(28)))
            {
                Undo.RecordObject(session, "Show A2 Placeholder Wall Renderers");
                session.SetHideCoveredPlaceholderWallRenderers(false);
            }
        }
    }
}
