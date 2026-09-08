# Nyxara Terrain Plan — Stage 1 Project Audit

**Date:** 2026-09-08  
**Scope:** Read-only audit of Planet Nyxara. No game code, prefab, or scene was changed.  
**Goal:** Replace placeholder wall *look* on the northern ridge and southern cliff while keeping the existing layout (H, A1–A5, R1/R2, B; no X/Y).

This document is the handoff for later stages. Identifiers below match the Unity project.

---

## 1. Verdict against the packed study

The Test pack (`Assets/Resources/Galaxy/Nyxara/Test`) was built from an exported `PlanetNyxara.prefab` that could not resolve its base prefab. **In this repository the base exists**, and the current authored numbers match the pack.

| Claim in Test / prompts | Current project | Result |
|---|---|---|
| `radius = 75` and `m_Radius = 75` | Variant overrides `SphericalPlanet.radius` **and** `SphereCollider.m_Radius` to **75** | **Match** (local units) |
| Nine areas, no X/Y | `Areas` children: **H, A1, A2, A3, A4, A5, R1, R2, B**. No objects named X or Y | **Match** |
| 24 walls | `Borders` children: **Cube** plus **Cube (1)** … **Cube (23)** | **Match** |
| Old plan radius 60 | Not used on Nyxara | Do not reuse |
| Inherited root Scale unresolved | Full chain is **(1, 1, 1)** | **Resolved** (see Scale) |

Local poses of H / A2 / Cube (2–5, 7) in `PlanetNyxara.prefab` match `Test/Measured-Layout.json` and `Test/Source/parsed.json`.

---

## 2. Prefab chain

```
PlanetBase.prefab
  guid aa06111e000000000000000000000006
  default radius 42.6
        ↓ Prefab Variant
PlanetNyxara.prefab
  guid aa07111e000000000000000000000007
  path Assets/Resources/Galaxy/Nyxara/PlanetNyxara.prefab
  overrides radius → 75
  adds Areas + Borders
        ↓ scene instance (no Scale override)
Assets/Scenes/PlanetNyxara.unity
```

- **Playable prefab:** `PlanetNyxara` (variant).
- **Base prefab:** `Assets/Resources/Galaxy/PlanetBase.prefab`.
- The Test README’s `missing_base_prefab: true` was true for the isolated export. It is **false** in this repo.
- Variant `PrefabInstance` source: `{fileID: 100100000, guid: aa06111e000000000000000000000006}`.
- Scene instance source: `{guid: aa07111e000000000000000000000007}`. Root local position/rotation identity. **No `m_LocalScale` override** on the planet in the scene.

`PlanetBase` children used at runtime: `Tiles`, `Environment`.  
`PlanetNyxara` extra children: `Areas`, `Borders` (both local TRS identity).

---

## 3. Relevant files

### Planet, ground, tiles
| File | Role |
|---|---|
| `Assets/Resources/Galaxy/PlanetBase.prefab` | Base: `SphericalPlanet`, `PlanetTileMap`, `SphereCollider`, `Tiles` |
| `Assets/Resources/Galaxy/Nyxara/PlanetNyxara.prefab` | Variant + Areas + Borders |
| `Assets/Scenes/PlanetNyxara.unity` | Play scene; instance of the variant |
| `Assets/Scripts/World/Planet/SphericalPlanet.cs` | Sphere mesh, radius API, optional heightmap / custom visual |
| `Assets/Scripts/World/Planet/PlanetTileMap.cs` | Painted tile mesh + walk `MeshCollider` |
| `Assets/Scripts/World/Planet/PlanetTileset.cs` | Atlas entries (`walkable`, `zoneId`) |
| `Assets/Resources/Galaxy/Nyxara/Tiles/NyxaraTileset.asset` | Nyxara tileset |

### Movement, camera, gravity, enemies
| File | Role |
|---|---|
| `Assets/Scripts/Player/PlanetWalker.cs` | Planet stick, wall capsule-cast, radial gravity fallback |
| `Assets/Scripts/Camera/CameraFollow.cs` | High-angle follow, radial up, no yaw-with-facing |
| `Assets/Scripts/World/Planet/PlanetParticleGravity.cs` | Particle gravity toward planet center |
| `Assets/Scripts/Creatures/CreatureChase.cs` | Surface chase; **no NavMesh**, **no wall capsule-cast** |
| `Assets/Scripts/Creatures/CreatureSpawner.cs` | Spawn around scene anchors |
| `Assets/Scripts/Player/PlanetTileSensor.cs` | Reads tile `zoneId` under the player |

### A2 fit study (reference only)
| File | Role |
|---|---|
| `Assets/Resources/Galaxy/Nyxara/Test/README.md` | Pack notes |
| `Assets/Resources/Galaxy/Nyxara/Test/Measured-Layout.json` | Extracted poses + proposed A2 bounds |
| `Assets/Resources/Galaxy/Nyxara/Test/Nyxara-A2-MeshData.json` | `radius: 75`, planet-local vertices |
| `Assets/Resources/Galaxy/Nyxara/Test/Editor/NyxaraA2StudyImporter.cs` | Editor import under planet, identity TRS |
| `Assets/Resources/Galaxy/Nyxara/Test/*.obj` | Fit meshes; **no colliders** |
| `Assets/Resources/Galaxy/Nyxara/Test/Validation.json` | Mesh radius checks; `unity_engine_tested: false` |
| `Assets/Resources/Galaxy/Nyxara/Test/Source/build_study.py` | How ridge/cliff were generated |

---

## 4. Geometry source (what the player actually walks on)

Nyxara is **not** an authored planet mesh. Ground is generated:

1. **`SphericalPlanet`** builds a UV sphere at `radius` (75). It also has a visual shell (heightmap on `PlanetBase`: `useVisualShell = 1`, `shellHeightAmplitude = 1.6`). Nyxara does not override those shell fields.
2. **`PlanetTileMap`** rebuilds a combined tile mesh in Edit and Play (`ExecuteAlways`). Nyxara overrides:
   - `enableBlocks = 0` (flat shell tiles, not extruded cubes)
   - `blockHeight = 0.05`, `blockGap = 0.001` (unused while blocks are off)
   - `latitudeBands = 36`, `longitudeBands = 72` → **2592** cells
   - `cellSubdivisions = 2`, `seamOverlap = 0.012`, `surfaceLift = 0.08` (inherited)
   - `hidePlanetBaseMesh = 1`, `showTileVisuals = 1`, `hideShellWhileShowingTiles = 1`
   - `useTileMeshCollider = 1`, `disableBaseSphereCollider = 1`
3. Walk collider is the **tile `MeshCollider`** (non-convex). The base `SphereCollider` is disabled while that collider is on.
4. Tile vertices use `GetTerrainRadius(direction) + lift`. Lift is `max(surfaceLift, radius * 0.003)` → **0.225** at r=75. With blocks off, analytic walk radius is about **75.225** local units, plus any readable heightmap sample.
5. `SphericalPlanet.customVisualModel` is empty. Do **not** hang the ridge/cliff on that slot: it replaces the whole visual shell, disables child colliders, and is hidden while tiles are shown.

**Attachment rule:** tiles are a child `Tiles` with local identity. Vertices are planet-local, then scaled by the planet transform. `PlanetTileMap.LocalSurfacePoint` divides by `lossyScale.x` so **world** walk radius stays `terrainRadius + lift` even if the root is scaled. Areas, Borders, and study meshes do **not** get that compensation.

---

## 5. Coordinate systems

Work in **PlanetNyxara local XYZ**. Origin = planet center. Identity rotation. Up for gameplay = radial (`position - center`).

### 5.1 Study / Measured-Layout (do not feed these longitudes into `PlanetTileMap`)

From `inspect_prefab.py` / `build_study.py`:

- `lon = atan2(x, z)` — **0° along +Z**, + toward +X  
- `lat = asin(y / r)`  
- Sphere point: `(cos(lat)*sin(lon), sin(lat), cos(lat)*cos(lon))`

A2 in this frame: position `(43.2, -7, 62.8)`, r≈76.54, **lon 34.52°, lat −5.25°**. Study sector **lon 20–50°**.

### 5.2 `SphericalPlanet` / `PlanetTileMap`

- UV: `u = atan2(z, x)`, `v = asin(y)/π + 0.5`  
- Tile point: `(cos(lat)*cos(lon), sin(lat), cos(lat)*sin(lon))` — **0° along +X**, + toward +Z  

Same A2 position is about **lon 55.5°** in the tilemap frame (90° − 34.5° in this quadrant).

**Rule:** parent `Nyxara-A2-MeshData.json` meshes with local position/rotation zero and scale one. Convert longitudes only with an explicit frame tag.

Unity left-handed; study vertices were written as Unity local positions. Prefer JSON import over OBJ if a DCC remaps axes.

---

## 6. Parent Scale

| Transform | Local Scale |
|---|---|
| `PlanetBase` / `PlanetNyxara` root | `(1, 1, 1)` |
| Scene instance of PlanetNyxara | not overridden → `(1, 1, 1)` |
| `Areas`, `Borders`, `Tiles` | `(1, 1, 1)` |
| Area cubes | uniform 20 / 25 / 30 / 60 (volume size, not planet scale) |
| Wall cubes | typically `(56.7, 43.9, 2)` or similar |

**World radius today = 75.** If the planet root is scaled later:

- Tile mesh keeps world radius ≈ 75 + lift (divides by `lossyScale`).
- Areas, Borders, and any child ridge/cliff **scale with the root**.
- `SphereCollider.radius` is local; world radius would become `75 * lossyScale`.

Do not scale `PlanetNyxara` to “fit” art. Change `SphericalPlanet.radius` and re-author local positions instead.

---

## 7. Trigger volume vs walkable area

These are different systems. Do not treat Area cube size as floor size.

### Triggers (`Areas`)

| Name | Local position | Uniform scale | BoxCollider | Role |
|---|---|---|---|---|
| H | (74.8, 0, −13.2) | ~30 | trigger | Hub marker |
| A1 | (64.6, 23.6, 36.0) | 25 | trigger | Encounter volume |
| A2 | (43.2, −7.0, 62.8) | 25 | trigger | Encounter volume |
| A3 | (−17.3, −22.6, 72.2) | 25 | trigger | Encounter volume |
| A4 | (−63.6, 13.7, 44.0) | 25 | trigger | Encounter volume |
| A5 | (−67.7, 12.5, −38.4) | 25 | trigger | Encounter volume |
| R1 | (8.4, 12.9, 76.5) | 20 | trigger | Oxygen-station marker |
| R2 | (−72.4, 5.3, 7.8) | 20 | trigger | Oxygen-station marker |
| B | (18.7, 8.0, −53.5) | 60 | trigger | Boss volume |

- `m_Size = (1,1,1)` on the collider; **world volume = scale**. A1–A5 are 25³ cubes, not 25×25 floor patches.
- Layer **Ground** (index 3). `MeshRenderer` is **enabled** (placeholder cubes are visible).
- **No gameplay MonoBehaviour** on these objects. Names are layout markers.
- `PlayerVitals.RefillOxygen()` is used on the spaceship scene, not by R1/R2. R1/R2 are **not** wired as oxygen refill volumes in code found in this audit.
- A2’s cube extends through the sphere. Intersection with r=75 is the study “footprint”. That footprint is **not** the walk collider.

### Walkable surface

- Tile `MeshCollider` on Ground.
- `PlanetWalker` radial snap accepts **only** that tile collider (or the planet sphere if tiles are off). Wall boxes are **not** walk surfaces.
- Analytic floor clamp: `GetWalkSurfaceRadius` ≈ `GetTerrainRadius + 0.225`. A lowered cliff mesh will be **ignored** unless tiles are lowered too: the walker will be pulled back to ~75.225.
- Tile `walkable` / `zoneId` affect paint and spawn filters (`shadow_grass`), not Area triggers.

`CreatureSpawner` in the scene uses five `Spawn Point` anchors under a **scene root** `Creatures` (not parented to the planet), each radius 12, 12 Grimlings. That is independent of the A2 trigger cube.

---

## 8. Borders, connections, blockages

`Borders` is 24 scaled Unity cubes, layer Ground, **`BoxCollider` not trigger**, renderer enabled. These **are** the current gameplay walls.

`PlanetWalker.ResolveObstacleMove` capsule-casts `groundLayer` against **children of the planet** (except grass-named props and the planet collider itself). Walls therefore block the player and allow wall-slide.

`CreatureChase` only radial-snaps to the ground. Enemies can likely **walk through** walls; they are not NavMesh agents. Scene `NavMeshData` is empty.

### A2 sector (study lon 20–50° from +Z)

| Wall | Approx. study lon / lat | Typical job at A2 |
|---|---|---|
| Cube (4) | 25.5°, 25.2° | North-west ridge wall (shorter: 46 × 28.6 × 2) |
| Cube (5) | 2.05°, 21.3° | North, west of A2 (toward R1) |
| Cube (3) | 42.8°, 35.8° | North-east ridge wall |
| Cube (7) | 15.2°, −17.0° | South-west cliff wall |
| Cube (2) | 44.8°, −9.5° | South-east cliff wall |

Study `wall_sources` along lon 20–50°: north = Cube (4) then Cube (3), with a Cube (5) sliver near 21–22°; south = Cube (7) then Cube (2) from ~26.25°.

**Keep open (do not close with rock):**
- West toward **R1** (study lon ~6°).
- East toward **A1** (study lon ~61°).

**Keep blocked:** north of A2 (ridge) and south of A2 (cliff), using the **authored wall faces**, not the A2 trigger’s extra southern bite.

### Proposed 7.49-unit south shift

The pack moves the cliff south because the A2 **trigger** crosses the south wall on a perfect r=75 sphere (`max_south_adjustment_units ≈ 7.49`). **Do not apply this in later stages unless we decide the wall is wrong.** Default: keep Cube (2)/Cube (7) as the gameplay south edge; treat the dashed vs white lines in `Nyxara-A2-Plan.png` as existing vs optional.

---

## 9. Camera, player, gravity

**Camera** (`Main Camera` + `CameraFollow` in `PlanetNyxara.unity`):
- `offsetHeight = 22`, `offsetBack = 9.5`, FOV 60, perspective  
- `preferPlanetRadialUp = 1`, `followTargetFacing = 0`  
- Ridge height ~24 local units will sit in this framing; **not verified in Game View**.

**Player (`PlanetWalker`):**
- Active on scenes named Planet/Galaxy.
- Disables `CharacterController` / flat `TouchController`; uses a trigger capsule for sensors only.
- Move: tangent to the sphere, camera-relative.
- Stick: raycast inward to tile collider, then clamp to analytic floor.
- Gravity: `gravityStrength = 18` toward planet center **only when the ground ray misses**. There is **no fall-off-the-cliff mechanic**. The floor clamp fights any hole unless tiles are lowered.
- Walls: capsule-cast, steep hits block, outward slopes are walkable.

**Gravity (other):**
- Character gravity is walker-owned, not `Physics.gravity`.
- `PlanetParticleGravity` remaps particle gravity toward the planet center at play time.

---

## 10. Missing dependencies / broken references

| Item | Status |
|---|---|
| Base prefab `PlanetBase` | **Present** (was missing in the isolated export) |
| Nyxara albedo override `guid: c0b7d1e0a070e1000000000000000013` | **Missing asset** on the variant `albedoTexture` override. Base still has a real albedo GUID. Cosmetic for tiles (tileset atlas is separate). |
| Study meshes colliders / UVs / LOD | Intentionally absent |
| Per-cell radial cut in `PlanetTileMap` | **Does not exist**. Full sphere is always meshed. Cliff interior will stay hidden until tiles in that sector are lowered or omitted **and** that data is serialized on the variant. |
| R1/R2 oxygen refill behaviour | **No script** found on those volumes |
| Area trigger gameplay (enter A2 → spawn, etc.) | **Not on the cubes**. Spawns are separate `Spawn Point` objects |
| NavMesh | Unused |
| `SphericalPlanet.customVisualModel` | Empty; wrong hook while tiles are the visible ground |

---

## 11. Recommended implementation (single path)

**Planet-local authored sector meshes as children of `PlanetNyxara`, plus a serialized `PlanetTileMap` radial mask for the cliff only.**

Why this matches the stack:

- Ground and walk collision already come from `PlanetTileMap`, which regenerates in Edit Mode and Play Mode. A one-off scene mesh would vanish or z-fight on rebuild.
- `SphericalPlanet.customVisualModel` cannot replace a sector and is hidden by tiles.
- Study vertices are already in planet-local space. `NyxaraA2StudyImporter` is the correct placement pattern (child, identity TRS, do not Center Origin).
- Player blocking already lives on `Borders` BoxColliders. Keep them until the rock silhouette matches, then hide renderers (stage 6), don’t delete colliders until a replacement collider exists.
- Player snap uses the tile collider **and** an analytic floor at ~75.225. A cliff that only exists as an OBJ will look right from outside and still be unwalkable-wrong: the original tiles will cover the drop, and the walker will refuse to go below the analytic radius.

Concrete sequence (later stages, not this audit):

1. Duplicate a **test scene** or prefab variant. Do not retarget `EditorBuildSettings` or other scenes.
2. Import `Nyxara-A2-MeshData.json` under the planet (or equivalent editor bake). Default **authored** south wall. Store the 7.49 m shift as a separate optional dataset.
3. Add serialized planning fields on existing planet components (sector lon/lat in **tilemap** frame, ridge height, cliff depth, playable margin). Do not start a second planet system.
4. **North ridge:** extra mesh outside the north wall, starting on the blocked side. Leave openings. No new walk collider on the ridge.
5. **South cliff:** same mesh **and** a `PlanetTileMap` change so cells in the cliff sector rebuild at lower radius (or are omitted) so the drop is visible and survives `RebuildVisuals`. Keep a blocking collider on the lip. Do not enable fall gameplay. **Stage 5 is recorded in section 17.**
6. Same mask + meshes around H, A1–A5, R1/R2, B (`coverFullRing` on the study instance). **Stage 7 is in the study scene (section 19 / 23); Play/Game View still unsigned.**

Do not parent art under `Areas` (triggers) or replace wall cubes in stage 2. Do not scale the planet root.

---

## 12. What could not be checked

This audit did not run Unity, Play Mode, or a device.

- Actual tile mesh vs perfect r=75 (heightmap readability / displacement).
- Game View framing of a 24-unit ridge with `CameraFollow` 22/9.5.
- Whether scene YAML `m_AddedGameObjects` (Areas/Borders) duplicate in the Hierarchy or are nested-prefab serialization of the same objects. Poses match the variant.
- Player capsule vs each A2 opening width.
- Creature vs wall overlap (code says they do not capsule-cast walls).
- Oxygen at R1/R2 in play.
- Broken albedo GUID at runtime.
- Z-fight amount between study playable patch (r=75) and tiles (~75.225).
- Mobile cost of 12,720 study triangles + 2592 tile cells.
- Whether `Test/Editor/NyxaraA2StudyImporter.cs` compiles in this Editor version (code matches current Unity JSON/Mesh APIs).

---

## 13. Stage 1 complete

Stage 2 (workspace only) is recorded in section 14. Ridge/cliff meshes and tile radial masks are later stages.

---

## 14. Stage 2 — A2 workspace (2026-09-08)

No play-scene references were changed. `EditorBuildSettings` still lists `PlanetNyxara.unity`. `PlanetNyxara.prefab` was not duplicated; planning data lives on `PlanetTileMap` (existing terrain component) and is enabled only by the study session object.

### How to open the study

1. In Unity, **File → Open Scene** → `Assets/Scenes/PlanetNyxaraTerrainStudy.unity`  
   or menu **BackHome → Nyxara Terrain Study → Open A2 Study Scene**.
2. Select `PlanetNyxara / NyxaraA2TerrainStudy` (or the planet) to see the A2 sector gizmos: cyan sector, purple ridge height, orange cliff depth, green north (+Y).
3. Edit **Terrain Work Plan** on `PlanetTileMap` or on `NyxaraA2TerrainStudy`. Ridge 24 and cliff 13 (range 12–14) are test proposals.
4. **Do not Apply** Prefab overrides from this instance back onto `PlanetNyxara`.

### How to revert

- **BackHome → Nyxara Terrain Study → Remove A2 Study Extras (Revert)** deletes `NyxaraA2TerrainStudy` and sets `workPlan.enabled = false` on the instance.
- **BackHome → Nyxara Terrain Study → Open Original PlanetNyxara Scene** returns to the play scene.
- Closing the study scene without saving also leaves the original scene/prefab untouched.

### Coordinate contract (stored on the plan + snapshot)

| Axis | Meaning |
|---|---|
| North | Planet-local **+Y** (positive latitude) |
| Height | Radial distance from planet center minus `SphericalPlanet.radius` |
| Sector lon | **PlanetTileMap** frame: `atan2(z, x)`, 0° at local +X, A2 = **40°–70°** |
| Study lon | Documented only: `atan2(x, z)`, 0° at local +Z, A2 = **20°–50°** |
| Local ↔ world | `SphericalPlanet.PlanetLocalToWorld` / `WorldToPlanetLocal`. Identity today (root TRS = 1). |

### Snapshot (authored walls, not the optional 7.49 south shift)

`Assets/Resources/Galaxy/Nyxara/Test/Nyxara-A2-LayoutSnapshot.json`

- 9 areas, 24 walls, local poses, both longitude frames.
- A2 west passage (study lon 20): N–S opening **~49.6** local units between Cube (4/5/3) and Cube (7/2).
- A2 east passage (study lon 50): N–S opening **~48.6** local units between Cube (3) and Cube (2).
- East exit toward A1 is that corridor, not a gap at the A2 trigger center (Cube (2) sits on that latitude).

### Files created / updated

| File | Role |
|---|---|
| `Assets/Scenes/PlanetNyxaraTerrainStudy.unity` | Study copy of the play scene + `NyxaraA2TerrainStudy` child |
| `Assets/Scripts/World/Planet/PlanetTileMap.cs` | `TerrainWorkPlan` + lon/lat helpers + gizmos |
| `Assets/Scripts/World/Planet/SphericalPlanet.cs` | North, radial height, local↔world matrices |
| `Assets/Scripts/World/Planet/NyxaraTerrainStudySession.cs` | Study root; applies plan onto `PlanetTileMap` |
| `Assets/Scripts/Editor/Nyxara/NyxaraTerrainStudyWorkspace.cs` | Open / apply / revert menus |
| `Assets/Scripts/Editor/PlanetTileMapEditor.cs` | Work-plan inspector block |
| `Assets/Resources/Galaxy/Nyxara/Test/Nyxara-A2-LayoutSnapshot.json` | Layout snapshot |
| `Docs/NyxaraTerrainPlan.md` | This stage |

Not created: a second planet generator, ridge/cliff meshes, or tile radial mask (those start in later stages). Gizmos and the study scene were authored in YAML and were not play-tested in the Unity Editor in this pass.

---

## 15. Stage 3 — A2 boundary overlay (2026-09-08)

No walls, triggers, or enemies were moved. The play scene and prefab were not retargeted.

### What was sampled

- **Ground:** analytic walk sphere at **r = 75.225** (`PlanetTileMap` lift). Parent TRS of Areas/Borders is identity, so collider local space is planet-local. In the study scene the overlay raycasts the tile `MeshCollider` when it exists and uses `GetWalkSurfacePoint` (heightmap included) otherwise.
- **A2 trigger ∩ ground:** `BoxCollider` containment via `InverseTransformPoint` (rotation + scale of the cube and parents).
- **Wall lips:** same test on every `Borders` non-trigger `BoxCollider`. North inner lip = lowest hit latitude of Cube (3)/(4)/(5). South inner lip = highest hit latitude of Cube (2)/(7).

### Scene View (study scene)

Select `NyxaraA2TerrainStudy`. Colors:

| Overlay | Meaning |
|---|---|
| Green fill | Existing walkable band inside walls, inset by combat margin |
| Cyan fill | A2 trigger ∩ ground (not the floor) |
| Purple line | Authored **north** wall |
| Solid orange | Authored **south** wall (gameplay default) |
| White dotted | Optional south lip if the trigger is kept clear |
| Red dotted | Where the trigger sits south of Cube (2) |
| Lime meridians | West/east openings |
| Gold dotted | Combat margin (1 local unit) |

`activeSouthBoundary` defaults to **AuthoredWalls**. OptionalTriggerClearance only changes later-stage planning (`workPlan.useOptionalSouthCliffShift`); colliders stay put.

### Measurements (collider / walk-sphere sample)

| Item | Value |
|---|---|
| West opening (study lon 20, Cube 4–7) | **49.9** u raw, **48.0** u inside 1 u margin |
| East opening (study lon 50, Cube 3–2) | **48.9** u raw, **47.0** u inside margin |
| West corridor toward R1 | Open for ≥ **41** u at mid-latitude; no wall hit in the sample |
| East corridor toward A1 | Reaches the **A1 trigger** after **~1.4** u at the N–S mid-latitude (~12.6°) |
| Trigger south of Cube (2) | 11 meridians; peak at study lon **45.25°** |
| Overlap on walk r=75.225 | **6.30** u (4.8°) |
| Overlap on reference r=75 | **6.49** u (4.96°) |
| Fit-pack 7.49 | r=75 overlap **+ ~1 u margin** (6.49 + 1.00 = 7.49). Not a missing wall. |

### Recommended choice

**Keep the authored south wall (Cube 2 / Cube 7).**

Cube (2)/(7) are Ground-layer **non-trigger** boxes that `PlanetWalker` capsule-casts. A2 is a named trigger volume with no gameplay script. The SE bite is the volume going through the cliff wall, not proof the wall is in the wrong place. Draw the ~7.3 u walk-surface clearance (white dotted) as an alternate for later mesh tests only.

### Files

| File | Role |
|---|---|
| `Assets/Scripts/World/Planet/NyxaraA2BoundaryOverlay.cs` | Sample + Scene gizmos |
| `Assets/Scripts/World/Planet/NyxaraTerrainStudySession.cs` | Holds overlay; rebuild on enable |
| `Assets/Scripts/Editor/Nyxara/NyxaraTerrainStudySessionEditor.cs` | Measurements + rebuild/save |
| `Assets/Scripts/Editor/Nyxara/NyxaraTerrainStudyWorkspace.cs` | Rebuild / save JSON menus |
| `Assets/Resources/Galaxy/Nyxara/Test/Nyxara-A2-BoundaryOverlay.json` | Saved samples + recommendation |
| `Docs/NyxaraTerrainPlan.md` | This stage |

Rebuild after opening the study scene: **BackHome → Nyxara Terrain Study → Rebuild A2 Boundary Overlay**. Heightmap (±1.6 on PlanetBase) can nudge the Unity numbers slightly off the analytic table above.

---

## 16. Stage 4 — A2 north ridge (2026-09-08)

Editor bake only (not per-frame). No wall, trigger, or enemy was moved. No ridge collider. Play scene / prefab not retargeted.

### Geometry

- Planet-local identity child `NyxaraA2NorthRidge` under `NyxaraA2TerrainStudy`.
- Follows the sphere: `dir * (walkRadius + height)`.
- Starts **0.35° north** of the measured north-wall inner lip (blocked side of Cube 3/4/5). Ring 0 is buried 0.08 into the walk surface. Offline OBJ check: **0 vertices south of the wall**, max height **24**, min height **−0.08**.
- West/east sector edges fade to the surface so the study cut meets the ground. Openings stay in the N–S corridors **south** of the wall.
- Steep south face, wider slope behind, three offset peaks (west ~24, east ~21, mid saddle ~17). Not a constant-height slab.
- Detail 2: **533 verts / 960 tris**. UV0 is arc-length (lon × slope) for `BackHome/CasualToon` `_BaseMap` (same shader as existing Nyxara rocks, BlueRidge albedo, tiling 2.2×1.6). Cull matches other Nyxara rocks (Off). Normals + tangents recalculated on bake.
- `ridgeHeight` on the work plan is the editable max (default 24). Re-bake after changing it.

### Camera (not engine-tested)

`CameraFollow` on the play scene is about height 22 / back 9.5 (script default 24 / 8). A 24-unit peak sits near that camera height. Change `ridgeHeight` and bake again if it fills the Game View.

### How to view / test

1. Open **BackHome → Nyxara Terrain Study → Open A2 Study Scene**.
2. **Bake A2 North Ridge** (menu or the button on `NyxaraA2TerrainStudy`). Saves `NyxaraA2NorthRidge.asset`.
3. Scene View: ridge should sit behind the north cubes, A2 floor and lime opening gizmos still empty.
4. Play the **study** scene (not the build list): walk A2 west toward R1 and east toward A1; walls still block; ridge has no extra collider.
5. Do **not** Apply Prefab onto `PlanetNyxara`. Revert still deletes `NyxaraA2TerrainStudy` (and this child). Baked assets stay on disk.

**Unity was not run in this pass.** The OBJ was generated with the same heightfield and checked for radius/playable clearance offline. Winding, CasualToon lighting, and Game View framing need an Editor look.

### Files

| File | Role |
|---|---|
| `Assets/Scripts/World/Planet/NyxaraA2NorthRidgeMeshBuilder.cs` | Heightfield bake |
| `Assets/Scripts/World/Planet/NyxaraA2NorthRidge.cs` | Scene visual, no collider |
| `Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2NorthRidge.obj` | Saved model (planet-local) |
| `Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2NorthRidge.mat` | CasualToon + BlueRidge albedo |
| `Assets/Scenes/PlanetNyxaraTerrainStudy.unity` | Ridge child under the study root |

---

## 17. Stage 5 — A2 south cliff + tile pit (2026-09-08)

Authored south boundary (Cube 2 / Cube 7). No 7.49 shift. No wall, trigger, enemy, gravity, or fall-mechanic change. Play scene / production prefab not retargeted. `EditorBuildSettings` unchanged.

### Generator

- Shared profile `NyxaraA2CliffProfile`: lip polyline (study lon + lat) + radial drop toward the planet center.
- `PlanetTileMap.LocalSurfacePoint` adds that offset to **tile vertices only**. `GetWalkSurfaceRadius` stays at the original walk sphere (~75.225) so `PlanetWalker` does not treat the pit as a floor it can enter.
- Plan fields now used at generate time: `enabled`, `cliffDepth` / min / max, `cliffSpanDegrees` (18°), `cliffLipStudyLongitudes`, `cliffLipLatitudes`. The study session captures the lip from the overlay (authored unless optional shift is on) and `SetWorkPlan` calls `RebuildVisuals`.
- Drop is zero north of the lip, outside study lon 20–50 (8% edge fade so A1 / R1 neighbors stay full radius), and it returns to full radius south of `sectorLatitudeMin` (−28) over 2.4°. No hole in the rest of the planet.
- Persistence: lip + depth live on `TerrainWorkPlan` (copied onto the study planet instance). Survives Regenerate, scene load, and Play Mode while the study session is present. Revert still sets `workPlan.enabled = false`. Do **not** Apply Prefab onto `PlanetNyxara`.

### Geometry

- Planet-local identity child `NyxaraA2SouthCliff` under `NyxaraA2TerrainStudy`.
- Same radius function as the tiles: `walkRadius + RadialOffset` (negative). Ring 0 starts 0.18° south of the inner lip and is buried 0.08 into the still-high tiles. Face bulge 0.22 so the rock sits proud of the lowered tiles; bulge is 0 at lip and pit floor so the shared boundary does not open a seam.
- Steep face then a floor at **12–14** local units toward center (`cliffDepth` 13, max 14). Offline OBJ: **533 verts / 960 tris**, ring 0 radius **75.145** (walk 75.225 minus 0.08 bury), deepest vertex **−13.999** (radius 61.226). UV0 arc-length. CasualToon + BlueRidge albedo, slightly darker than the north ridge. Cull Off. No MeshCollider on the cliff.
- Authored south **BoxColliders** stay the lip block. The character still sticks to tile colliders on the playable floor; the pit is on the blocked side of those walls.

### How to view / test

1. Open **BackHome → Nyxara Terrain Study → Open A2 Study Scene**.
2. **Bake A2 South Cliff** (menu or the button on `NyxaraA2TerrainStudy`). Saves `NyxaraA2SouthCliff.asset`.
3. Scene View: rock lip on the south cubes, A2 floor and west/east openings unchanged, pit not covered by the original shell.
4. Play the **study** scene: walk A2; south walls still block; cliff has no extra collider; no fall mechanic.
5. Regenerating tiles in the study scene should keep the pit (plan is enabled on the instance).

**Unity was not run in this pass.** The OBJ was generated with the same profile and checked offline: 0 verts north of the lip; playable floor / R1 / A1 offsets 0; west and east lon columns only bury 0.08; interior drop 12–14; both sector ends fade to full radius. Lighting and Game View need an Editor look.

### Files

| File | Role |
|---|---|
| `Assets/Scripts/World/Planet/NyxaraA2CliffProfile.cs` | Shared lip + radial drop |
| `Assets/Scripts/World/Planet/NyxaraA2SouthCliffMeshBuilder.cs` | Cliff heightfield bake |
| `Assets/Scripts/World/Planet/NyxaraA2SouthCliff.cs` | Scene visual, no collider |
| `Assets/Scripts/World/Planet/PlanetTileMap.cs` | Tile verts follow the pit; walk radius does not |
| `Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2SouthCliff.obj` | Saved model (planet-local) |
| `Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2SouthCliff.mat` | CasualToon + BlueRidge albedo |
| `Assets/Scenes/PlanetNyxaraTerrainStudy.unity` | Cliff child under the study root |

---

## 18. Stage 6 — A2 playtest (2026-09-08)

**Unity / Game View / Play Mode were not run in this pass.** Nothing below is a passed playtest. Do not treat the ridge, cliff, camera, combat, or wall-hide as verified in game.

### What was changed (A2 only, study scene)

- **Walk stick:** `PlanetTileMap.TryPickWalkSurfaceHit` is now shared by `PlanetWalker`, `CreatureChase`, and `CreatureSpawner`. Hits below the analytic walk radius (~75.225) are ignored, and wall boxes are not floors. The lowered south tiles stay a visual pit, not a new walk path. Player lip block is still Cube (2)/(7) BoxColliders. No fall mechanic, no gravity change.
- **Placeholder wall look:** study session can hide **MeshRenderer** on Cube **(2)/(3)/(4)/(7)**. Colliders stay. Default **off**. Cube **(5)** is outside A2 (study lon ~2°) and is never hidden. Turn hide on only after Game View shows the rock covering those cubes. Revert / disable session restores renderers.
- **Colliders:** existing Border boxes (thickness 2, Ground, non-trigger) stay the lip. Inner face is the playable edge; ridge starts ~0.35° north of it, cliff ~0.18° south. No new MeshCollider on ridge/cliff. Production prefab and `PlanetNyxara.unity` were not edited. Do **not** Apply Prefab.

### What could not be checked here

- Game View at the game aspect (Android **portrait**, orientation 1). Use **9:16** (e.g. 1080×1920), not the desktop 1024×768 default.
- Entering A2, walking both lips, combat near cover, exiting west→R1 / east→A1, stick to ground.
- Whether ridge height 24 sits in the camera (`offsetHeight` 22, `offsetBack` 9.5). Near the north wall the camera can sit inside the ridge volume — if it hides enemies, telegraphs, or exits, **lower `ridgeHeight` and re-bake** before adding fade/occlusion tricks.
- Invisible-wall feel after hiding cube renderers.
- Save: there is **no planet save file**. Inventory uses `DontDestroyOnLoad`. Death Continue loads **SpaceShip**. Terrain persistence to check is: study scene save → reload → Play Mode on/off, and that `workPlan.enabled` still lowers A2 tiles.

`RangedBullet` homes to the creature and does not raycast world colliders. Scene spawn anchors are not inside A2 (radius 12 from those points does not fill the arena); pull or walk an enemy in if you need combat.

### Manual Game View checklist (you run this)

Setup: **BackHome → Nyxara Terrain Study → Open A2 Study Scene**. Bake north ridge and south cliff if meshes are empty. Game View **9:16**. Play. Do **not** use the build-list play scene for this pass.

1. **Enter A2** from the west opening (toward R1, study lon ~20) and from the east opening (toward A1, study lon ~50). Floor stays at walk height; openings stay clear.
2. **North boundary:** walk the full inner lip of Cube (3)/(4). You must not climb the ridge or walk north of the wall. Character stays snapped to tiles.
3. **South boundary:** walk the inner lip of Cube (2)/(7). You must not cross onto the pit or walk down the cliff. No fall. Pit is visible, not a path.
4. **Combat:** if no Grimling is in A2, pull one in. Fight near the north rock and south lip. Projectiles should still hit the creature. Enemies must not path down the pit. Telegraphs and the two exits must stay readable; if the ridge hides them, lower `ridgeHeight` (try 16–18), bake again, retest before any hide VFX.
5. **Placeholder cubes:** with renderers still ON, confirm rock covers Cube (2)/(3)/(4)/(7) from 9:16. Only then **Hide A2 Wall Renderers**. Blocking must still match the rock lip — not a gap inside the arena. Cube (5) stays visible. If a cube still sticks out, leave its renderer on and stop; do not hide “for later.”
6. **Persistence:** stop Play, save the study scene, re-open it, Play again. Pit and ridge still there. Exit Play; `workPlan` still enabled on the instance. Revert still deletes the study root and shows the cubes again.

If any step fails, keep the fix inside A2 (plan, bake, wall renderer flag, ridge height). Do not roll the same mesh to other sectors until this list is actually walked in Game View.

---

## 19. Stage 7 — rest of PlanetNyxara (study scene, 2026-09-08)

**Built in `PlanetNyxaraTerrainStudy` only.** Same method as A2: overlay samples every Borders box, ridge on the blocked north side of each north wall, cliff + tile pit on the blocked south side of each south wall. Meridians with **no** north wall get no mountain; meridians with **no** south wall get no pit. N–S corridors stay the playable floor.

`coverFullRing` lives on `TerrainWorkPlan`. Production `PlanetNyxara.prefab` stays `workPlan.enabled = false`. No Apply Prefab. Optional ~7.49 south shift stays **off**. No X/Y, oxygen, or shortcuts.

**Game View, Play Mode, a lap of the ring, combat, Profiler, and device were not run in this pass.** Section 18’s A2 checklist is still unsigned. Do not treat the ring as camera-signed-off.

Bake: **BackHome → Nyxara Terrain Study → Bake Full Ring Ridge And Cliff** (or the same button on `NyxaraA2TerrainStudy`). Re-bakes overwrite `NyxaraA2NorthRidge.asset` / `NyxaraA2SouthCliff.asset`.

### Intended layout (authored walls)

Nine areas, 24 walls. Ring order in the **study** frame (`atan2(x,z)`, 0° at local +Z, north = +Y). Positions from `Nyxara-A2-LayoutSnapshot.json`.

| Stop | Study lon | Lat | Nearest north-side walls (blocked) | Nearest south-side walls (blocked) |
|---|---|---|---|---|
| **R1** | 6.3° | 9.5° | Cube (5) 2°/21°, Cube (6) −19°/18° | Cube (7) 15°/−17°, Cube (8) −5°/−29° |
| **A2** *(in ring, untested)* | 20–50° | −28–36 | Cube (4), (5 sliver), (3) | Cube (7), (2) |
| **A1** | 60.9° | 17.7° | Cube (3) 43°/36° | Cube (2) 45°/−10°, Cube (1) 84°/−8° |
| **H** | 100.0° | 0° | Cube 96°/23° | Cube (1) 84°/−8° |
| **B** | 160.7° | 8.0° | Cube (23) 127°/21°, (22) 133°/29°, (21) 163°/38°, (17) −174°/39° | Cube (18) −168°/−29° |
| **A5** | −119.6° | 9.1° | Cube (19) −143°/32°, (15) −111°/21° | Cube (20) −135°/−12°, (16) −107°/−6° |
| **R2** | −83.9° | 4.2° | Cube (13) −86°/17°, (12) −65°/22° | Cube (14) −86°/−8°, (16) −107°/−6° |
| **A4** | −55.3° | 10.1° | Cube (11) −44°/22°, (12) −65°/22° | Cube (10) −59°/−22°, (9) −37°/−35° |
| **A3** | −13.5° | −16.9° | Cube (6) −19°/18° | Cube (8) −5°/−29°, (9) −37°/−35° |

Wall longitudes are the cube **centers**, not inner lips. Real lips must be sampled per segment the same way A2 used `NyxaraA2BoundaryOverlay` (authored BoxColliders, not a constant latitude, not a symmetric ring).

**Keep open (do not seal with rock):** every current N–S corridor between a north wall and a south wall, including A2 west → R1 (~50 u at lon 20) and A2 east → A1 (~49 u at lon 50). Where two areas already meet, use a rock spur that **stops at the existing gap**. Do not add X/Y, oxygen volumes, or new cuts.

**Keep blocked:** north of each encounter’s authored north cubes (ridge, blocked side), south of the authored south cubes (cliff toward planet center). Do not apply A2’s optional 7.49 south shift globally.

### What was built (files; engine unsigned)

One wrapped overlay (study lon −180°…180°, ~181 meridians). North vs south is the wall **center** latitude (planet-local Y ≥ 0 = north). Lip arrays store a missing sentinel on meridians with no wall so interpolation does not invent rock across gaps.

- **North:** same mountain mass as A2, repeating ~32° lobes, height × wall presence. No A2 lon-edge fade (that existed only because A2 was a sector cut).
- **South:** same drop profile; tile `RadialOffset` and cliff mesh follow presence. No pit where there is no south wall.
- **Hide:** after the full-ring bake, every Borders cube **renderer** can hide; BoxColliders stay. Cube (5) is included once R1 is in the ring.
- A2-only bake remains if `coverFullRing` is off.

### Route tour (paper only — not walked in Game View)

R1 → A2 → A1 → H → B → A5 → R2 → A4 → A3 → R1. Same nine names as the prefab. No new stops.

### Changes this stage

| Change | Status |
|---|---|
| Planet-wide ridge/cliff meshes (study bake) | **Code ready** — run Bake Full Ring in the Editor |
| Tile pit on every south wall (study `workPlan`) | **Code ready** — `coverFullRing` on the study instance |
| New areas, walls, oxygen, shortcuts, X/Y | **Not made** |
| Production prefab / play scene | **Unchanged** |

### Tests still not done

Everything in section 18, plus: no lap around the globe in Game View 9:16, no combat at H/B/R1/R2, no production prefab apply.

---

## 20. Stage 8 — A2 art pass (2026-09-08)

A2 art pass first; ring wrap is section 19 / 23. Triggers, openings, and wall **colliders** were not moved. No Asset Store packages added.

**Game camera / Game View were not run.** Lighting, seams, and 9:16 readability are **not** signed off. Re-bake in the study scene after pulling these files: **Bake A2 North Ridge** and **Bake A2 South Cliff**.

### Style (existing assets)

| Surface | Look | Source |
|---|---|---|
| Clearing floor | Light green tileset | `NyxaraTileset` `Fill_Grass` / `shadow_grass` (unchanged atlas) |
| Lip strip (~2.2°) | Dark green **soil** vertex tint, not a leaf carpet | `PlanetTileMap.GetA2ClearingTint` — skipped on lon fade (openings stay light) |
| Ridge + cliff | Purple-gray rock | `BackHome/CasualToon` + BlueRidge albedo `guid: 4147a8ed…`, same tiling **2×2** |

Missing (clear placeholders, not purchased): no dedicated purple-gray albedo, no rock normal map (same gap as `BlueRidgeFormation.mat`). Tint + existing albedo stand in.

### Silhouette

- Ridge: extra offset peaks, a lower shelf, two hollows, light terracing, rounded lip, irregular **outer** skirt (blocked/north side only) so the cube reads less square from outside.
- Cliff: rock steps and asymmetric clusters on the **face mesh only** (tile pit profile unchanged so it does not become a walk ramp). Outer skirt south of the lip only.
- Vertex colors on both meshes (CasualToon multiplies `COLOR`).
- No props in the west/east corridors or on the combat floor.

### Files

Ridge/cliff mesh builders, `NyxaraA2NorthRidge.mat`, `NyxaraA2SouthCliff.mat`, `PlanetTileMap` soil tint.

### Still unchecked in the game camera

Cel lighting vs key light, UV seams at 2×2, whether the ridge still eats the 22/9.5 framing, whether soil tint is too strong, cube hide after coverage. Run section 18 first, then look at A2 in **9:16 Game View**, not only Scene View distance.

---

## 21. Stage 9 — closeout and how to use (2026-09-08)

No player Build was published. `EditorBuildSettings` still has only `SpaceShip.unity` and `PlanetNyxara.unity`. `Portal.prefab` still loads `PlanetNyxara`. `SpaceShip.unity` was not edited. **Do not Apply Prefab onto `PlanetNyxara`.**

**Unity Play Mode, Game View, Profiler, and a device were not run in this closeout.** Section 18’s lap, combat, camera, save, and Grimling checks are still empty. Below, static YAML/code results are separated from those missing playtests.

### Compared to the stage-1 snapshot

Source of truth: `Assets/Resources/Galaxy/Nyxara/Test/Nyxara-A2-LayoutSnapshot.json` (same poses as `Measured-Layout.json` / section 7).

`PlanetNyxara.prefab` (guid `aa07111e…`) is **unchanged** vs git. Nine Areas and 24 Borders still match the snapshot (position, scale, trigger vs wall):

| Name | Local position | Scale | Collider |
|---|---|---|---|
| H | (74.8, 0, −13.2) | ~30 | trigger |
| A1 | (64.6, 23.6, 36) | 25 | trigger |
| A2 | (43.2, −7, 62.8) | 25 | trigger |
| A3 | (−17.3, −22.6, 72.2) | 25 | trigger |
| A4 | (−63.6, 13.7, 44) | 25 | trigger |
| A5 | (−67.7, 12.5, −38.4) | 25 | trigger |
| R1 | (8.4, 12.9, 76.5) | 20 | trigger |
| R2 | (−72.4, 5.3, 7.8) | 20 | trigger |
| B | (18.7, 8, −53.5) | 60 | trigger |

A2 walls **Cube (2)/(3)/(4)/(7)/(5)** are still non-trigger boxes at the snapshot poses. West → R1 and east → A1 were not sealed in mesh builders (lon-edge fade; no rock in the corridors). Optional 7.49 south shift is still **off**.

Play scene `PlanetNyxara.unity` still instances that prefab (`m_SourcePrefab` guid `aa07111e…`, root identity). It already had a large local YAML diff before this stage (nested override rewrite). This stage **did not** edit it or retarget it.

Editor menu **BackHome → Nyxara Terrain Study → Compare Layout To Snapshot** re-checks Areas/Borders in the open scene against the JSON. Run it in Unity; it was not executed here.

### Play / systems (not run)

| Check | Result |
|---|---|
| Full character lap of all nine areas | **Not run** |
| Combat at A2 lips, enemy pathing, bullets, camera 22 / 9.5 | **Not run** |
| Save / Continue / tile regenerate in Play | **Not run** |
| R1 / R2 / B “work as before” | Volumes **unchanged** in the prefab. R1/R2 still have **no** oxygen-refill script (section 7). B is still the scale-60 trigger. Behaviour in Play was not verified. |
| Travel time / oxygen remeasure | **Not remeasured.** Authored wall and trigger sizes did not change, so opening width/length did not change. If Play later shows a narrower corridor, measure then. |

### Profiler (no device, Unity Profiler not opened)

Project target from `ProjectSettings`: **Android**, Quality **Mobile**, min SDK **26**, ARM64 (`AndroidTargetArchitectures: 2`). No numeric triangle/ms budget is stored in the repo.

**Editor / asset counts (not Profiler ms, not a phone):**

| Item | Count | Note |
|---|---|---|
| A2 ridge OBJ | 533 verts, 960 tris | `detailLevel` 2; no MeshCollider |
| A2 cliff OBJ | 533 verts, 960 tris | Same grid; tiles still own the pit collider |
| Extra draw | 2 materials, 1 shared BlueRidge albedo | Intentional tint split, not a second texture |
| Tile sphere | 36×72 cells, `cellSubdivisions` 2 | Unchanged planet cost |
| Original fit-pack study mesh | 12,720 tris | A2 rock is lighter than that pack |

Rebuild: `PlanetTileMap.RebuildVisuals` on enable / `SetWorkPlan`, not `Update`. Ridge/cliff have **no** `Update`; they assign a baked mesh. In-memory preview exists only in the Editor when the `.asset` bake is missing. Production `workPlan.enabled` defaults **false**, so the play prefab does not lower A2 tiles.

Keep `detailLevel` at **2**. Level 4 is 2,432 tris per mesh and will not read at camera height 22 on a phone.

**Mobile Profiler:** not run. Do not treat the OBJ counts as a device pass.

### Prefab to use (new, not a production variant)

`Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2Terrain.prefab`

- Children: `NyxaraA2NorthRidge`, `NyxaraA2SouthCliff`. Layer 0. **No colliders.**
- Parent under `PlanetNyxara` (or `NyxaraA2TerrainStudy`) at **identity TRS**.
- After **Bake A2 North Ridge** and **Bake A2 South Cliff**, run **Save A2 Terrain Prefab** so the baked `.asset` meshes are stored on the prefab.
- Study extras stay in `PlanetNyxaraTerrainStudy.unity` only. Instantiating this prefab in `PlanetNyxara.unity` or `SpaceShip.unity` is out of this stage.

This is **not** a new `PlanetNyxara` variant. The playable variant is still `PlanetNyxara.prefab`.

### How to use (study)

1. **BackHome → Nyxara Terrain Study → Open A2 Study Scene**
2. Bake both meshes. Game View **9:16**. Walk the section 18 list.
3. **Compare Layout To Snapshot**
4. **Save A2 Terrain Prefab**
5. Hide Cube (2)/(3)/(4)/(7) renderers only after the rock covers them. Do not Apply Prefab.

### Parameters that are safe to change (A2 study session / `TerrainWorkPlan`)

| Field | Default | What it does | Do not |
|---|---|---|---|
| `ridgeHeight` | 18 (study; field default 24) | Visual mountain height | Raise until the camera 22 / 9.5 framing dies; retest combat first |
| `cliffDepth` | 13 (12–14) | Tile pit + cliff face depth | Enable fall gameplay |
| `detailLevel` | 2 | Ridge/cliff tessellation 1–4 | Leave at 4 “for quality” |
| `playableMargin` | 1 | Keep walk inside walls | Shrink openings |
| `cliffSpanDegrees` | 18 | How far south the pit goes | Push into A1/R1 (lon fade exists, but don’t widen the sector) |
| `useOptionalSouthCliffShift` | false | 7.49 extra south bite | Turn on unless design changes the authored wall |
| `studyLongitudeMin/Max` | 20–50 (A2) or −180–180 (ring) | Sector in the study frame | Expand A2 without overlay; ring uses `coverFullRing` |
| Materials `_BaseColor` / tiling 2×2 | ridge slightly lighter than cliff | Same albedo as BlueRidge | Add a second atlas; keep the two tints in the same family |
| `coverFullRing` | false until Bake Full Ring | Wrap every north/south wall | Enable on `PlanetNyxara.prefab` |
| `hideCoveredPlaceholderWallRenderers` | true (study) | Hide covered Border **renderers** | Hide colliders |

Do **not** move Area triggers or Border boxes to “fit” the art.

### Shared code that already affects every planet

`PlanetWalker` / `CreatureChase` / `CreatureSpawner` walk-stick (`TryPickWalkSurfaceHit`) and `PlanetTileMap` work-plan fields ship in gameplay scripts. The pit and soil tint run only when `workPlan.enabled` is true (study instance). The stick change is live on the play scene too.

### Files this terrain work added or edited (A2)

Study scene, session/editor/workspace, overlay, cliff profile, ridge/cliff builders + components, `NyxaraA2Terrain.prefab`, A2 `.obj` / `.mat`, Test JSONs, `PlanetTileMap` (plan + pit + soil tint + walk pick), this document. Production prefab **not** duplicated.

### Remaining limits

- Ring meshes exist in code; they are on disk only after **Bake Full Ring** in the Editor. Play/Game View unsigned.
- No dedicated rock albedo/normal (placeholder: BlueRidge + tint).
- No R1/R2 oxygen wiring (pre-existing).
- Grimlings still do not capsule-cast walls (pre-existing).
- `.asset` baked meshes are created in the Editor bake, not stored as YAML here until you bake.
- Game camera, lighting, seams, hide-cubes, Profiler, device: **unchecked**.

### How to go back to the original look

1. **BackHome → Nyxara Terrain Study → Remove A2 Study Extras (Revert)** (study scene only).
2. **Open Original PlanetNyxara Scene**. Play still uses `PlanetNyxara.prefab` with cube walls.
3. To drop the A2 art pack: delete `Assets/Resources/Galaxy/Nyxara/Terrain/A2/` and the study scene. Leave `PlanetNyxara.prefab` as the source of areas/walls.
4. Do not revert `PlanetWalker` walk-stick unless you intend to undo the pit-safe floor on every planet.

---

## 22. A2 visual fix — mountain mass + cliff drop (2026-09-08)

A2 study scene only. No expansion to other sectors. Authored walls and triggers were not moved. Optional 7.49 south shift stays **off**. No fall / gravity change.

**Game View, Play Mode, and before/after screenshots were not captured in this pass.** Do not treat the new shapes as camera-signed-off.

### What was actually on screen (code/assets)

| Object | Role |
|---|---|
| `NyxaraA2NorthRidge` / `NyxaraA2SouthCliff` under `NyxaraA2TerrainStudy` | **Active** visuals (planet-local, no collider) |
| `PlanetTileMap` Tiles | **Active** walk mesh; A2 south cells drop when `workPlan.enabled` |
| Border Cube (2)/(3)/(4)/(7) | **Active** blockers; renderers now hidden in the study session (colliders stay) |
| Cube (5) | Shared with R1 — renderer **stays on** |
| `Assets/Resources/Galaxy/Nyxara/Test/*.obj` | Fit-study **reference** only; not parented in the study scene |
| Overlay gizmos on `NyxaraA2TerrainStudy` | Editor-only; turn Gizmos off for result shots |

**Game camera:** `Main Camera` + `CameraFollow` (`offsetHeight` 22, `offsetBack` 9.5) follows the **player**, not the Scene View. Rotating Scene View does not change Game View. Menu **Place Player In A2 (Game Camera)** snaps the walker onto the A2 walk surface so Game View 9:16 actually looks at A2.

The previous ridge heightfield was a thin sawtooth (many small Gauss peaks + terraces + high-frequency noise, rings bunched on the lip). That matches a narrow jagged strip, not a mountain mass. Placeholder wall renderers were still on (`hideCoveredPlaceholderWallRenderers: 0`). The cliff mesh added clusters on top of the tile pit, so it read as a rock inside the sphere more than a ground drop.

### Shape changes

- **North:** wide envelope from the measured north lip into blocked ground (~30° of latitude), two large lobes and a saddle, back that returns to the sphere. Sector lon edges still fade so R1/A1 doors stay open. `ridgeHeight` test value on the study session is **18** (24 remains the field default; not required).
- **South:** tile `RadialOffset` drop is steeper just south of Cube (2)/(7). Cliff mesh follows that same profile (lip, steep face, lower slope) with only a small face offset so it is not buried in the pit tiles. Lon-edge fade still returns to existing ground — no end-cap walls.
- Hide renderers: Cube **(2)/(3)/(4)/(7)** only.

Re-bake after pulling: **Bake A2 North Ridge** and **Bake A2 South Cliff**.

### CreatureSpawner warning (separate)

`CreatureSpawner` can log that it placed fewer creatures than requested because of **spacing** (`minSeparationDegrees`) or the **walkable / Shadow Grass** tile filter. Spawn dens use scene anchors / Area volumes, not the A2 ridge/cliff meshes. Those meshes have **no colliders** and are not in that filter. Do not treat this warning as caused by the mountain. Do not lower spacing or turn the filter off to silence it.

### Checks still needed in the Editor (not run here)

1. Bake both meshes. Gizmos off. Same Scene angle before/after is on you — this pass has no captured frames.
2. Side view: floor, mountain up, cliff down.
3. Play, **Place Player In A2**, Game View **9:16**: openings clear, mountain does not hide exits/enemies, pit visible, no tile holes at A1/R1, Cube (5) still visible.
4. Save study scene, reload, pit and meshes still there.

---

## 23. Full-ring ridge and cliff (2026-09-08)

Study scene only. `coverFullRing` on the study `TerrainWorkPlan`. Same rock objects as A2 (`NyxaraA2NorthRidge` / `NyxaraA2SouthCliff`); the bake overwrites those assets with a wrapped strip. Border GameObjects stay for layout compare. In study Play, solid Border BoxColliders are turned off (triggers stay). Collision is kinematic: `NyxaraRouteBounds` keeps latitude between the lips and lets the player slide along the edge. The walker also ignores the tile-mesh cliff face, lip curtains, and rock so a capsule cannot snag. Teleport only if the player is past the lip or in the pit. Production `PlanetNyxara` with `workPlan` off still uses Border BoxColliders. No Apply Prefab.

**Game View, Play Mode, and a lap of R1 → A2 → A1 → H → B → A5 → R2 → A4 → A3 were not run.**

### How to bake

1. Open **PlanetNyxaraTerrainStudy**.
2. **BackHome → Nyxara Terrain Study → Bake Full Ring Ridge And Cliff**.
3. Hide Border renderers is applied by that menu. Solid Border colliders are off only while Play is running.
4. Play, Game View **9:16**, walk the corridor (between north and south walls). Do not climb the mountain or walk the pit.

### Rules encoded in the bake

| Rule | Implementation |
|---|---|
| Ridge only north of a north wall | Overlay + `northLip*` presence; skipped mesh columns where presence ≈ 0 |
| Cliff / tile pit only south of a south wall | `NyxaraA2CliffProfile.RadialOffset` × south presence |
| Gaps stay open | Missing-lip sentinel `999`; no interpolation of a fake wall across a **long** gap. Joints shorter than 16° (Cube 2 / Cube 1 style) are bridged so the pit and cliff mesh do not crack |
| No A2 lon-edge fade | That fade was a sector cut; the ring has no 20°/50° ends |
| Cube (5) | Hidden with the rest of Borders once the ring includes R1 |

To go back to A2-only: uncheck `coverFullRing`, rebuild overlay, bake A2 ridge/cliff, show extra wall renderers if needed.








