# Aether Wayfinder — Mod Contract

| Field | Value |
| :--- | :--- |
| **ID** | `dev.ultimaibrahim.aether.wayfinder` (BepInEx GUID; was `aether_wayfinder`) |
| **Version** | `1.1.0` |
| **Author** | Ibrahim García |
| **Assembly** | `Aether.Mods.Wayfinder.dll` (+ `Aether.Nano.dll`); the map storage (`Aether.Core.Mapping`, plain .NET) is compiled in |
| **Status** | 🟢 checked in a world with Aether (minimap, M map, Esc, N, settings page, HUD editor) and with BepInEx alone (2026-11-12, Block Story 14.5.4) |
| **Canonical runtime** | `src/Aether.Mods/Aether.Mods.Wayfinder/Runtime/` (`WayfinderModule`, `WayfinderCaves`, `WayfinderSettings`, beacons, export, chunk atlas) |
| **Entrypoint** | `WayfinderPlugin : Aether.Nano.NanoMod` (`[BepInDependency(Nano.Guid)]`) |
| **Host contract** | Nano: settings page, fullscreen map as a modal (`NanoPanels`), movable minimap (`NanoHud`), input isolation (`NanoInput`) |
| **Last reviewed** | 2026-11-12 |

Since 1.1.0 Wayfinder is a Nano mod: it draws its minimap, beacons and fullscreen map, handles M/N/L/B and Esc, and works out
caves, interiors and nearby mobs itself (`WayfinderCaves`, formerly in Aether's UI shell). With Aether, its menus give way to
the map, its HUD editor moves the minimap and its HUD layout (diamonds under XP, top buttons moved) is respected. Settings
(and `MapLayerModeKey`/`QuickWaypointKey`, formerly bound by Aether's keybind manager) carry over once from the UI suite's
file; explored maps and waypoints stay in each world's folder.

## 1. Scope

Manifest for the topographical minimap, fullscreen interactive map, terrain layer slicing and
waypoints:

- **Chunk-atlas rasterizer** (`WayfinderChunkAtlas.cs` + `Aether.Core/Mapping/WayfinderChunks.cs`):
  32×32-block map chunks, each owning a 32×32 `Texture2D`, drawn as world-anchored quads. Zoom and
  pan only move/scale quads, so there is no resample flicker, no zoom distortion and no sweep.
- The corner map and the fullscreen map draw the same atlas (`DrawView`) and share
  `WayfinderTileCache` (512×512, O(1)) entries. The corner map still shows **exactly
  `2 × RadarRange` blocks**, so terrain pixels and marker projection share one scale.
- Auto / Surface / Cave / Slice layers, 69-biome detection, lava/acid/ore highlighting.
- Slice altitude is fixed and bounded (0–255); Auto/Cave follow the player's Y in 16-block bands
  (`band = floor(y / 16)`), so a chunk texture stays stable while walking inside a band.
- **Interior cutaway:** inside a roofed structure or shallow cave, open rooms/corridors render in
  detail while solid ground is replaced by the already-explored surface, dimmed/desaturated as an
  opaque context layer. Auto follows the player indoors (roofed) as well as underground.
- **No x-ray:** surface cells are only recorded where the player actually explores the surface
  (never indoors/underground); isolated store cells are rejected by a density test; native radar
  POIs and mob dots are only drawn when the terrain pixel under them is actually rendered.
- **Progressive fill:** up to 4 missing/stale chunks per Repaint (2.5 ms budget), nearest first,
  with a 0.28 s smoothstep fade on first render (no hard pop).
- **True round minimap:** the visible chunks are CPU-composited into a texture with a circular
  alpha cut, so Circle has transparent corners instead of a square card.
- **Map grid styles:** Off / Chunks (32-block) / Regions (256-block) / Both, baked into the chunk
  texture and keyed in the mode byte (bits 16/64). Default Off.
- **Info slots:** optional FPS/frame time, in-game clock, day/night and sun/block light; a compact
  card on the fullscreen map and one line under the minimap coordinates.
- **Deathpoints:** a red waypoint is created at the last position before death (`Death N`), max 3
  by default; it survives respawn and is stored in the waypoint file (optional 7th field).
- **Auto enclosure:** the map separates deep caves / genuinely enclosed spaces from semi-open
  structures: 8 horizontal rays at **both head heights** (feet+1 and feet+2), ≥7/8 directions must
  hit a wall within 32 blocks, plus a roof within 32. Small igloos, cave rooms and underworld/space
  structures count; a large open side, a single overhang or a 1-block fence does not. The interior
  cutaway is only applied when the explored surface is within 8 blocks of the player: a shallow
  cave under a mountain renders as cave, not as the overworld above it.
- **Cave fog of war:** cave/slice chunks outside the 3×3 chunk area the player has approached stay
  black; the visited set is per band and is seeded lazily from saved cave cells after a reload.
  Both floor/round bands are marked at boundaries, and negative store probes are cached.
- **Manual `Interior` layer:** forces the current-Y slice with the explored surface dimmed. Use it
  in icebergs, skylit rooms and ambiguous semi-open structures where Auto cannot infer intent;
  Auto stays Surface under a single roof/skylight. Works in surface, cave, underworld and space
  structures because it does not depend on an enclosure test. `Interior` samples the **exact
  player Y** (Wayfinder-style precision), not the 16-block band floor; Auto/Cave keep bands.
- **Waypoint beacons:** opt-in vertical beams (`ShowWaypointBeacons`, default off), independent of
  the `ShowWorldWaypoints` master. Beams within **96 blocks** (nearest 24, rotated billboard,
  near/far fades); waypoints up to **384 blocks** get a name pill only, so the horizon is not a wall
  of columns. Labels sit at the beam midpoint (near) or above the projected waypoint (far); death
  waypoints use the procedural skull. No depth occlusion (IMGUI) — beams show through terrain,
  hence opt-in; anything farther lives on the fullscreen map edge markers.
- **Waypoint anchor:** new quick/form/death waypoints store the **feet Y** (ground scan), not the
  camera Y. Old head-Y waypoints are resolved at render time for beacons, so their beam still
  starts at the floor.
- **Off-screen waypoints:** on the fullscreen map, an enabled waypoint outside the panned/zoomed
  canvas is clamped to the map edge along its true direction, with name and map-plane distance;
  death waypoints show the skull there and on the minimap/fullscreen markers.
- **Export PNG:** fullscreen `PNG` button; 1024×1024, 1 px = one 4×4-block cell (4096×4096 blocks),
  explored surface only, waypoints/player/legend, timestamped under `<world>/aether_wayfinder/`.
- Directional player arrow, chunk grid (baked per chunk, not a per-frame overlay), day/night/topo/biome
  render modes and pixel-snapped chunk quads.
- Interactive map: wheel zoom anchored at the cursor (25%–400%), drag pan bounded to ±2048 blocks,
  double-click waypoint, right-click recenter.
- Saved waypoints, 3D world markers and a quick waypoint. Wayfinder deliberately has **no teleport action**: teleporting is a cheat in multiplayer, and the game's modding license forbids tools that give unfair advantages.
- **Per-world persistence:** explored surface cells are written to `<world>/aether_wayfinder/r.<rx>.<rz>.bin`
  (4×4-block cells, 256×256-block regions, sparse binary records, atomic writes). Cave/slice cells are
  written to `<world>/aether_wayfinder/cave/b<band>/c.<rx>.<rz>.bin` (magic `AERC`, 14-byte records,
  4×4 cells, one 16-block band per file). Waypoints live in
  `<world>/aether_wayfinder/waypoints.txt`; the legacy global `[Wayfinder] WaypointsData` is imported
  once into the first world and then ignored.
- **Waypoint editor:** name, X/Y/Z, colour and enabled state; New/Cancel/Apply; click a row name to
  Find (centre the map); sort by distance/name; All ON/OFF; Delete.
- **Native radar POIs:** the native `Radar` point registry supplies beds, vehicles, teleporters, NPCs
  and the spawn point, including its `maxDistance` / `maxYDistance` semantics.
- **Surface above caves:** in Cave/Slice mode, explored surface cells render dimmed instead of black.
- **Progressive rendering:** cold passes are budgeted at 1024 uncached columns per frame; maps fill
  over a few frames instead of a single 99 ms hitch.

This assembly owns **the full runtime**. The UI shell only forwards lifecycle/drawing calls through
`IWayfinderUiModule`; it contains no Wayfinder gameplay types.

## 2. Entry points and public surface

| Type | Role |
| :--- | :--- |
| `[assembly: AetherMod("aether_wayfinder", ...)]` | Discovery metadata and real-catalog row. |
| `WayfinderEntrypoint` | Real loader entrypoint; registers the module with the UI shell in frame 0. |
| `WayfinderModule : IWayfinderUiModule` | Canonical state, config, minimap/fullscreen/waypoints. |
| `WayfinderChunkAtlas` | Canonical rasterizer: per-chunk textures, halo shading, LRU slots. |
| `WayfinderEngine` / `WayfinderTileCache` / `TerrainSample` | Sampling, cache and layer cell types (same assembly). |
| `Aether.Core.Mapping.WayfinderChunks` | Chunk/band math and the 64-bit chunk key. |

There is no `MonoBehaviour`, no `AddComponent` and no duplicate renderer in this assembly. The shell
is the single Unity host; the module is a plain class.

## 3. Runtime map

`LoadedMods` includes `aether_wayfinder` from the assembly attribute. `WayfinderEntrypoint` runs in
frame 0 and registers one `WayfinderModule`; the shell calls `Initialize`, `OnUpdate`,
`OnSceneChanged`, `IsModalOpen` and the draw hooks through `IWayfinderUiModule`. Terrain is rendered
by `WayfinderChunkAtlas.Shared` (one chunk texture per map chunk, used by both the minimap and the
fullscreen map). There are no `WayfinderEngine` instances anymore.

The old `WayfinderRunnerComponent`, second `WayfinderEngine`, `WaypointManager` and
`WayfinderConfig` were deleted as dead duplicates. The phantom default waypoints written by
`WaypointManager.Initialize()` no longer exist.

The shell keeps only `WayfinderBridge` (thin forwarding
of the existing call sites to `IWayfinderUiModule`), `SuiteShared` and the shared menu chrome.

## 4. Configuration

| Surface | File | Owner | Keys |
| :--- | :--- | :--- | :--- |
| Suite cfg, namespaced section | `BepInEx/config/com.ultimaibrahim.blockstory.suite.cfg` | **`Aether.Mods.Wayfinder`** binds `[Wayfinder]` | `Enabled`, `ToggleKey`, `FullscreenMapKey` (default M), `FullscreenMapKeyMigrated`, `MapLayerModeKey`, `QuickWaypointKey`, `RadarRange`, `ShowCoords`, `Shape`, `Anchor`, `OffsetX`, `OffsetY`, `Theme`, `LayerMode`, `RenderMode`, `GridStyle`, `ShowMapInfo`, `ShowChunkGrid` (legacy alias), `SliceY`, `ShowWorldWaypoints`, `MinimapSize`, `MinimapScale`, `FullscreenMapScale`, `MinimapRefreshSeconds`, `FullscreenRefreshSeconds`, `EntityVerticalRange`, `ShowMobsOnMinimap`, `ShowWaypointLabelsOnMinimap`, `SaveExploredMap`, `ShowSurfaceAboveCaves`, `ShowNativeRadarPois`, `ShowWaypointBeacons`, `Deathpoints`, `DeathpointLimit`, `WaypointsMigrated`, `WaypointsData` (legacy/migration only) |

Migration: the keys/defaults/ranges are unchanged, so an existing suite `.cfg` loads without a reset.
`MinimapSize`/`MinimapScale` moved from `HudCustomizerModule` to the module; they are the same
`[Wayfinder]` entries. `aether/aether_wayfinder.json` is not generated or read; if an old file
exists it is inert and safe to delete.

Range contract: `RadarRange` is 24–96 blocks (default 48), `MinimapSize` 110–340 px,
`MinimapScale` 0.5–2.0, `FullscreenMapScale` 0.65–1.0 and `SliceY` 0–255. Old `.cfg` files load
unchanged; out-of-range slice values are clamped on read.

## 5. Hotkeys

| Action | Default | Owner |
| :--- | :--- | :--- |
| Minimap toggle | `N` | `Plugin.Update` (`MinimapToggleKey`) |
| Fullscreen map | `M` | `WayfinderModule` (`WayfinderKey`; old J migrated once) |
| Layer cycle (Auto/Surface/Cave/Slice) | `L` | `KeybindManagerModule` (`MapLayerModeKey`) |
| Quick waypoint at player | `B` | `KeybindManagerModule` (`QuickWaypointKey`) |
| Close fullscreen map | `Escape` | `Plugin.Update` |

The old `M` (minimap) and `F7` (cave mode) bindings lived only in the deleted runner. The active
bindings are `N` and `L`.

## 6. Invariants (must never break)

- **R2:** every voxel read uses `WayfinderEngine.GetBlockSafe/GetBlockFast`; never `world[x,y,z]`.
  This is the difference between 60 FPS and 3 FPS.
- Null/empty chunks return `default` (air) immediately; the map never forces chunk generation.
- The tile cache is the only path for repeated tiles; no per-frame full-world rescans.
- **Projection:** minimap terrain, mob dots and waypoints must share one mapping: the same view
  centre and the same `pxPerBlock` that `DrawView` receives. Never project markers from a different
  span or centre than the chunk quads.
- **Zoom/pan:** zoom only changes `pxPerBlock`; the atlas rescales world-anchored quads and never
  resamples terrain for a camera move. The only zoom clamp is 0.25×–4.0×, shared by wheel, toolbar
  and bottom bar.
- **Uploads:** a chunk texture uploads exactly once when rendered (Repaint path), never per frame.
  Up to 4 missing/stale chunks are rendered per Repaint (2.5 ms budget, nearest to the centre
  first), so a cold map fills in ~1 s at 25 % zoom without a hitch.
- **Interior state:** `cachedIsIndoors` = the topmost solid of the player's column is 1–32 blocks
  above the feet (structure roof / shallow terrain), with 0.4 s hysteresis. It is part of the atlas
  mode key (bit 32) and of the tile-cache key, so entering/leaving never reuses the other context's
  colours. Auto uses it to switch to the interior view even when the roof is beyond the 45-block
  underground probe.
- **Open-room probe:** Case B scans the whole 16-block band (`sliceY + 2 … + 15`) for an enclosed
  air pocket, not just 5 blocks, so a house floor high inside the band still becomes interior.
- **Marker visibility:** radar POIs and mob dots require `WayfinderChunkAtlas.IsRenderedExplored`
  (the terrain pixel exists and is past half of its fade); waypoints are player knowledge and stay.
- **Store density:** a cached surface cell is only trusted with at least 3 known neighbours
  (3×3 cell test, cached per chunk). Isolated 4×4 cells are x-ray leftovers from the old
  underground sampler and render as black.
- **Fade:** the first render of a chunk starts at alpha 0 and smoothsteps to 1 over 0.28 s; the
  Circle compositor blends with the dark background. Stale re-renders do not re-fade.
- **Grid style:** `GridStyle` ∈ Off/Chunks/Regions/Both; the chunk/region bits are part of the
  atlas mode/tile-cache key, so toggling clears the atlas once. Chunk lines are cyan (32 blocks),
  region lines are gold (256 blocks) and are drawn after the chunk lines. `ShowChunkGrid` is a
  legacy alias mirrored by the grid UI.
- **Info slots:** `ShowMapInfo` draws FPS/frame time, the `DayTimeManager` clock/day flag and the
  sun/block light at the player's head. The minimap line only appears at ≥220 px. No uploads.
- **Deathpoint hook:** poll `PlayerHealth.Instance.IsDead()`; cache the last alive position; create
  once per death at that position, red (index 3), `IsDeath=true`; keep at most `DeathpointLimit`
  (oldest removed). `Deathpoints` toggles the feature. No Harmony patch.
- **Waypoint file:** optional 7th field `IsDeath` after `Enabled`; old 6-field files load with
  `false`; the legacy config fallback writes it too.
- **Auto cave state:** `cachedMapCave` = deep (`roofDepth > 32 && cachedIsUnderground`) or enclosed
  (`roofDepth 1–32 && ≥7/8 wall directions at BOTH feet+1 and feet+2`), 0.45 s hysteresis.
  `cachedIsUnderground` stays as the lighting/performance flag; the map uses `cachedMapCave`
  everywhere. This is the layer contract:
  - **Auto:** surface outside/semi-open, cave+cutaway inside an enclosed structure or deep cave.
  - **Surface:** force the outdoor surface.
  - **Cave:** force the cave/slice at the player's Y; the cutaway applies when the engine detects
    an enclosure, otherwise the cave sample + dimmed surface fallback.
  - **Interior:** force the current-Y slice **and** the cutaway, regardless of enclosure. This is
    the manual answer for a skylit iceberg or a semi-open structure; Auto would stay Surface.
  - **Slice:** force a chosen Y.
  The interior cutaway is the "interior detailed + exterior surface dimmed" view; a single roof
  with no walls has no interior, so Auto stays Surface there — use Interior when you explicitly
  want the inside.
- **Beacons:** independent of `ShowWorldWaypoints`; opt-in (`ShowWaypointBeacons=false`), Repaint
  only. Beams within 96 blocks (nearest 24, near fade 4–10, far fade); name-only pills for waypoints
  up to 384 blocks; death waypoints show the procedural skull. No terrain sampling for beams; the
  ground anchor scans at most six blocks under the waypoint to support old head-Y entries.
- **Export:** `ExportWayfinderPng`/`RequestWayfinderExport`/`DrawExportWayfinderButton`; reads only
  the surface store (no x-ray), on demand; the 1024² scan is ~150–250 ms and must not run per frame.
- **Cave fog:** `MarkCaveVisited` marks a 3×3 chunk area for the rendered band every 0.35 s (floor
  and rounded bands at boundaries); chunks outside render black; slots are invalidated when a chunk
  becomes visited. After a reload, `IsCaveChunkVisited` seeds a chunk from
  `CaveMapStore.HasAnyCaveCell`, and a negative probe cache stops repeat disk lookups.
- **Layers:** Auto/Cave follow the player's Y; `Interior` uses the exact player Y (precision);
  only Slice uses `SliceY`. Auto/Cave apply the interior cutaway only when the explored surface at
  the player's column is within 8 blocks; deep/shallow caves stay cave-only. Interior always forces it.
  Y controls live in the right panel so they survive narrow windows.
- **Pan/zoom:** pan is clamped to ±2048 blocks; wheel zoom keeps the block under the cursor fixed.
- **Layout:** optional minimap control groups and the fullscreen layer badge are hidden by measured
  width instead of being drawn outside their panels.
- **Persistence:** map files live under the native world directory; never write map data to the global
  BepInEx config. Surface cells are 4×4 blocks; cave/slice cells are 4×4 blocks in one file per
  16-block Y band. Both stores are flushed on the 5 s budget, on world change, on quit and by
  "Save map". Opening the cave store invalidates the tile atlas once so cached (non-persistable)
  pixels are replaced by live samples.
- **Waypoints:** one file per world/dimension. Migration from the legacy global list happens exactly
  once (`WaypointsMigrated`), and a missing file in a new world means an empty list, never a copy.
  New quick/form/My Pos/death waypoints store the feet Y from a six-block ground scan.
- **Waypoint markers:** death waypoints get the procedural skull on the minimap, fullscreen map,
  3D world markers, edge indicators and beacon labels. Fullscreen waypoints outside the viewport are
  drawn at the map edge along their true direction, with name + map-plane distance.
- **Cache:** never invalidate the tile cache on layer/render/grid/slice changes (keys already encode
  them). Invalidate on a world/dimension change and once when the cave store opens. Never cache an
  unsampled/unloaded column.
- **Chunk keys:** every texture is keyed `(chunkX, chunkZ, mode, band)`; `mode` encodes cave / render
  type / grid / exact-Y Interior / cutaway. A render-style change `Clear()`s the atlas so a stale
  layer can never be drawn; the band/Y is part of the key, so moving between Interior Y levels
  reuses old slots instead of clearing and re-rendering the whole atlas. The exact-Y bit prevents an
  Interior Y=5 key from colliding with Cave band 5. A world/dimension change also clears it. The
  tile cache is *not* cleared on layer switches.
- **Texture:** each chunk is 32×32 RGBA32 with `FilterMode.Point` and `TextureWrapMode.Clamp`; one
  `SetPixels32` + `Apply(false)` per rendered chunk.
- **Halo:** shading and cave wall-edge detection sample a 1-block halo (stride 34) around the chunk,
  so slope/wall shading uses real neighbour data at chunk borders. Only the 32×32 interior is
  uploaded and cached; night light sources are stored in chunk-local coordinates.
- **Sentinel-free pixels:** never encode “already coloured” in `BlockId`. Block ids are real
  `ushort`s (`BlockType.id`), so a parallel `bool[] finalPixel` marks cache-hit/unexplored pixels and
  `TerrainSample.FromCache` lets neighbours count cached pixels as real blocks.
- **Band quantization:** Cave/Slice sample `band = floor(sliceY / 16)` at `band * 16`. Moving inside
  a band never re-renders; crossing it clears and resamples once.
- **Quad geometry:** chunk rect edges are rounded to integers so adjacent chunks share the same
  rounded world edge, and textures are drawn with `StretchToFill`. `ScaleToFit` on a non-square
  rounded rect (e.g. 179×180) draws at `min(w,h)` and leaves a 1 px strip of canvas fill → seam.
- **Cutaway rules:** a pixel is interior when the sampled slice is open (`IsOpenRoom`, cached as
  `FlagOpenRoom`) or within 2 blocks of an open column. Interior keeps the detailed cave/slice
  colour; everything else uses the surface store (`IsSurfaceFallback`), dimmed ~30 %, desaturated
  and slightly cooled. No store cell → black (unexplored).
- **Circle minimap:** the visible chunks are composited into a CPU texture (Repaint only) and the
  pixels outside `0.890625 × bezel radius` get alpha 0; the composed texture replaces the square
  card, and the bezel is drawn on top. No per-pixel IMGUI mask.
- **Unexplored:** black/navy tiles mean “not explored/no store data”, not a render failure; the UI
  states this explicitly. Failed columns are not cached; the chunk is retried after `StaleSeconds`
  (5 s). Standing indoors never reveals the surface above unknown terrain.
- **Stale/eviction:** a chunk older than 5 s is re-rendered when it is the nearest stale one; slots
  are LRU-evicted past 512. The atlas is shared between minimap and fullscreen.
- **Layers:** surface cells are persisted in `r.*.bin`; cave/slice cells are persisted per 16-block
  band in `cave/b<band>/c.*.bin`. Cave/Slice render live first and fall back to the saved band when
  live data is missing. `AlwaysMapSurface` is **deprecated/no-op** (it used to sample the surface
  above you while underground, i.e. an x-ray); surface recording now requires the player to be
  neither underground nor indoors. `ShowSurfaceAboveCaves` dims the dense cells that were actually
  explored as the exterior context.
- **Native radar:** read its point dictionary only for POIs; mobs keep using `MOBManagement` children
  so they are not drawn twice. Honour native `maxDistance`/`maxYDistance`.
- Waypoint persistence is stable across saves/scenes; no phantom defaults from the removed manager.
- The minimap never overlaps the vanilla buttons or Lens card at supported anchors/resolutions.
- The fullscreen map canvas alone consumes wheel/drag events; Escape/outside-click close it.

## 7. Performance budget

| Metric | Budget | How measured |
| :--- | :--- | :--- |
| Tile lookup | O(1), ~0.001 ms | `WayfinderTileCache` |
| Chunk texture | 32×32 px, one upload per chunk, Repaint only | Code review / F8 |
| Chunk render budget | ≤ 4 missing/stale chunks per Repaint or 2.5 ms, nearest to centre | Code review + live |
| Cold open (chunk atlas) | 716 px canvas at 100 % = 5×5 chunks ≈ 7 Repaints (~0.1 s) | Live screenshots |
| Cold fill at 25 % | 16×16 chunks fill in roughly 1 s instead of ~4 s | Live screenshots |
| Circle minimap compose | ~60k pixels + one upload per Repaint at 255 px | Code review / F8 |
| Grid / info slots | Baked lines + one text card + one minimap line | Code review / F8 |
| Auto enclosure | 8 rays × up to 32 blocks every 0.35 s (~256 reads/s) | Code review / F8 |
| Cave fog | 3×3 chunk keys + slot invalidation every 0.35 s | Code review / F8 |
| Zoom/pan | Zero terrain resampling; quads only | Code review + screenshots |
| Cave band change | One atlas clear + refill; no per-Y-step re-render | Live minimap observation |
| Underground surface scan | ≤128 canonical surface samples per chunk render | Code review |
| Minimap motion | World-anchored chunk quads; no jump/sweep | Screenshots at 0.3 s apart |
| Fullscreen reopen | Cached tiles reused; no world regen | F8 |
| Entity scan | `MOBManagement` children, no `FindObjectsOfType<Health>` hot path | Code review |
| Map store flush | ≤ 2 dirty regions / 5 s + flush on close/quit; one region ≈ 2–62 KB | Code review |
| Toolbar allocations | No per-event `new[]` arrays | Code review |
| Hot-loop allocations | 0 B after warm-up | `ZeroAllocTests` + F8 |
| Frame rate | 60+ FPS at 1080p | F8 |

## 8. QA gates

| Gate | Kind | Command / check |
| :--- | :--- | :--- |
| No direct world indexer | Automated | `ArchitectureTests.VoxelCode_MustNotUseDirectWorldIndexers` |
| Safe chunk helpers | Automated | `ArchitectureTests.VoxelCode_KeepsTheSafeChunkHelpers` |
| Native UI preserved across assemblies | Automated | `ArchitectureTests.NativeUi_KeepsTheFullPortedSuiteAcrossAssemblies` |
| Own runtime + entrypoint | Automated | `ArchitectureTests.MigratedMod_OwnsRuntimeAndEntrypoint` |
| No gameplay in the shell | Automated | `ArchitectureTests.AetherUI_DoesNotContainWayfinderGameplay` |
| Contract exists | Automated | `ArchitectureTests.EveryAetherMod_HasAContract` |
| Chunk coords match the game grid | Automated | `WayfinderMathTests.ChunkCoords_FollowTheGameChunkGrid` |
| Cave band quantization | Automated | `WayfinderMathTests.BandCoords_QuantizeCaveSlices` |
| Chunk keys are unique | Automated | `WayfinderMathTests.ChunkKeys_DifferAcrossChunksModesAndBands` |
| Cursor-anchored zoom | Automated | `WayfinderMathTests.ZoomAroundCursor_KeepsTheAnchoredBlockUnderTheCursor` (math primitive; runtime zoom keeps the cursor block fixed) |
| Range clamps | Automated | `WayfinderMathTests.Clamps_KeepAllUserFacingRangesInsideTheContract` |
| Store round-trip | Automated | `WayfinderStoreTests.Store_RoundTripsAcrossInstances` |
| Store negative regions | Automated | `WayfinderStoreTests.RegionMath_HandlesNegativeCoordinates` |
| Store index/counts | Automated | `WayfinderStoreTests.Store_RoundTripsAcrossInstances` (KnownRegion/CellCount) |
| Store corruption | Automated | `WayfinderStoreTests.Store_IgnoresCorruptRegionFiles` |
| Sparse/distant regions | Automated | `WayfinderStoreTests.Store_SeparatesDistantRegions` |
| Canonical cell updates | Automated | `WayfinderStoreTests.CanonicalSamples_UpdateButNonCanonicalSamplesOnlySeed` |
| Split build startup | Live pending (2026-11-04) | Aether loads `aether_wayfinder` from `Aether.Mods.Wayfinder.dll`; log has no `TypeLoadException`/`MissingMethodException`; minimap appears |
| Config migration | Automated + live pending | Existing `[Wayfinder]` values load unchanged; `MinimapSize`/`MinimapScale` still bind under the same keys |
| Minimap / fullscreen render | Live 2026-11-03 | 1080p: default 260 px card, 25%/100%/400% fullscreen and Slices all render; no exception |
| Atlas flicker | Live 2026-11-03 | 4 rapid walk frames: stable content, labels anchored, no sweep |
| Zoom fidelity | Live 2026-11-03 | 100 % → 195 % → 25 %: same content cleanly rescaled, no mixed scales or blur |
| Chunk seams | Live 2026-11-03 | 100 %: pixel scan found 0 canvas-fill pixels adjacent to bright terrain (the old 1 px line is gone) |
| Cave semantics | Live 2026-11-03 | Dark = cave walls (dimmed), bright = floors/flowers sampled at the band floor |
| Chunk-atlas math | Automated | `WayfinderMathTests` chunk coord / band / 64-bit key round-trips |
| Circle minimap | Live 2026-11-03 | Circle at 255 px: terrain clipped at the bezel's inner edge, transparent corners |
| Cave seams (cache flags) | Live 2026-11-03 | Cave at 305 %: no canvas-fill gaps across chunk borders; wall state restored from the cache flag |
| Interior cutaway | Live 2026-11-03 | Orc Town hall: sand interior + furniture detailed; exterior dim surface; unexplored stays black |
| Auto indoors | Live 2026-11-03 | Roofed hall enters the interior/cave view; surface cells keep their normal layer |
| No x-ray | Live 2026-11-03 | Cave mode did not grow the store; isolated store cells and POIs over unexplored black are not drawn |
| Marker fog | Live 2026-11-03 | Bed/NPC icons remain on explored terrain, disappear over black; markers wait for half of the chunk fade |
| Fill fade | Live 2026-11-03 | Layer switch captured: black → half-transparent → full in ~0.3 s (smoothstep) |
| Interior probe | Live 2026-11-03 | Case B scans +2…+15 in the band; low structures keep their detailed rooms |
| Grid styles | Live 2026-11-03 | Off → Chunk → Region → Both; chunk cyan, region gold; minimap follows |
| Info slots | Live 2026-11-03 | Fullscreen `60 FPS · 16.7 ms · 06:02 · Day · Light 15/0`; minimap `60 FPS · 08:52` |
| Auto enclosure | Live 2026-11-03 | Igloo: Auto ▼ Cave with interior + dim exterior; a single overhang/1-block fence is Surface |
| Platform under a column | Live 2026-11-03 | Wooden platform with 2+ block walls → Auto Cave cutaway; choose Surface to force the outdoor view |
| Manual Interior | Live 2026-11-03 | Skylit iceberg at 1326,28,-51: Auto Surface; Interior shows the current-Y slice + dim surface |
| Interior exact Y | Build/tests; live pending | `Interior` keys/samples the exact player Y (not the band floor); a dedicated temple/iceberg capture is still pending |
| Export PNG | Live 2026-11-03 | `export_20261103_214608.png`: 1024×1024, 4096×4096 blocks, 38,599 cells, 145 ms |
| Cave store round-trip | Automated | `WayfinderCaveStoreTests.Store_RoundTripsAcrossInstances` |
| Cave store negatives/bands | Automated | Negative coords, band isolation, distant regions, out-of-range band clamp |
| Cave store corruption | Automated | Bad magic/version/length/coords are ignored with warnings; no phantom counts |
| Cave store canonical updates | Automated | Canonical samples update; non-canonical only seed empty cells |
| Cave store fog seed | Automated | `Store_SeedsFogOnlyForChunksWithSavedCells` (chunk/band isolation) |
| Cave runtime reload | Live 2026-11-03 | Restart loaded `Cave map ready: 8 regions, 3,776 cells`; `cave/b0..b2` files render before live chunks |
| Cave cutaway fix | Live 2026-11-03 | At `1172,29,-241` Auto/Cave renders the cave; at the reported `1196,33,-249` the surface store is `TopY 49` (16 blocks above), so the cutaway gate is off |
| Beacons | Live 2026-11-03 | Three beams visible at once, each name at the visible midpoint and anchored at the floor; far `sex 3`/`Death 1` (216/225 m) show names only |
| Beacon range | Live 2026-11-03 | No full beams beyond 96 blocks; the 96–384 band draws name pills, the map handles the rest |
| Death skull | Live/automated | Procedural 16×16 skull on the fullscreen marker and edge labels; the death beam label path is covered by code/tests |
| Off-screen waypoints | Live 2026-11-03 | `home` edge marker + stacked `Point 2`, `Point 3`, `Sex 2`, `sex 3`, `Death 1` labels at the top edge; no overlap after stacking |
| Waypoint feet Y | Build/tests | New quick/form/My Pos/death waypoints use the six-block ground scan; old head-Y entries resolve to the floor for beacons |
| Cave fog | Live 2026-11-03 | Cave at 26 %: only the 3×3 visited chunks render; the rest is black |
| Deathpoints | Live 2026-11-03 | Forced death → `Death 1` at 1327,28,-78, red, file field `|True`; survives respawn |
| Cold fill at 25 % | Live 2026-11-03 | Surface→Cave reset: interior filled in <1 s, only genuinely unexplored chunks stayed black |
| True circle | Live 2026-11-03 | Circle: transparent corners, terrain cut at the bezel's inner edge, markers aligned |
| Slice controls | Live 2026-11-03 | Slice button shows `Y: -10 [64] +10 [My Y]`; badge `▼ SLICE Y:64` |
| Zoom/pan | Live 2026-11-03 | Bottom `Zoom +/-` clamps at 25%/400%; `Center Player` recenters; wheel path covered by the math gate |
| Minimap layers | Live partial | Auto and Slice rendered; Surface/Cave at the same coordinates pending |
| Waypoint persistence | Live 2026-11-03 | Three saved waypoints survived restarts; ON/OFF toggle persisted to `.cfg` |
| Waypoint create/delete | Live pending | Form save, double-click creation and `X` deletion |
| Per-world waypoint file | Live 2026-11-03 | `worlds/ultimaStory/aether_wayfinder/waypoints.txt`; 3 legacy points migrated once; config cleared |
| Waypoint editor | Live 2026-11-03 | E → Apply changed `Sex` colour (0→3) and name; file updated on disk |
| Explored map files | Live 2026-11-03 | 12 sparse region files under `aether_wayfinder/`; 10,868 cells indexed after restart |
| Surface above caves | Live 2026-11-03 | Cave layer at 35 % shows the explored surface island dimmed instead of black |
| Native radar POIs | Live 2026-11-03 | Bed/NPC markers rendered from the native registry; no duplicate mob dots |
| Hitch removed | Live 2026-11-03 | Cold pass was 99 ms; progressive budget removed the `Slow map pass` log in normal play |
| Drag smoothness | Live 2026-11-03 | 100 %: anchored texture follows the cursor; no terrain pass while held; settles/fills on release |
| Minimap smoothness | Live 2026-11-03 | Frames 0.3 s apart: Z 677→671 with continuous terrain shift, no black sweep |
| Unexplored legend | Live 2026-11-03 | Right panel states “Black = unexplored. Surface and caves are saved.” |
| Cave miss cache | Live 2026-11-03 | Cave mode with no data settles instead of re-rendering every Repaint |
| Underground surface fill | Live 2026-11-03 | Surface cell count grew 10,568 → 11,892 while travelling underground |
| Layer behavior | Live 2026-11-03 | Cave/Slice live + dimmed surface fallback; Surface reads the saved store |
| Responsive layout | Live pending | 1280×720 and 800×600: badge/optional controls hide instead of overlapping |
| Performance | Live partial | No slow-pass logs at 1080p; F8 walk/fly budget still pending |

## 9. Known debt / limits

| Item | Impact | Next action |
| :--- | :--- | :--- |
| Split build not live-verified yet | The runtime moved to its own assembly; only build/tests are green | Install with the game closed, restart and run the full Wayfinder QA matrix (see §8) |
| `WayfinderBridge` remains in the shell | Thin forwarding of existing call sites; not gameplay | Delete it when the menu/HUD/keybind call sites are moved to module-owned panels |
| `J` conflicted with the game native minimap key | Pressing `J` opened the native pause/minimap instead of the Aether map | **Fixed:** default is now `M`; an old `J` value is migrated once (`FullscreenMapKeyMigrated`). Users can rebind in the Keybind Manager |
| 1280×720 / 800×600 pass pending | Badge and optional control thresholds are code-verified only | Resize and repeat the minimap/fullscreen checks |
| Waypoint create/delete not exercised | Double-click and form paths unverified this build | Create from the form, double-click the map, delete, relaunch |
| Live TP landing offset once (Z 333 → 384) | One destination landed ~51 blocks off; the second was exact | Reproduce with a waypoint inside a structure; check `FollowPlayer` safe-spawn behavior |
| **Layer/engine heuristics are intentional debt** | Auto is a 2-height wall-ray + roof heuristic, not a room-volume/skylight solver. A skylit iceberg is Surface by design; `Interior` is the manual escape hatch. Do not keep tuning thresholds. | Future rewrite: authored room volumes or a baked skylight/occlusion map; keep `Interior` as the contract until then |
| Interior exact-Y re-samples on every Y block | Precision costs chunk slots while walking vertically; the 4-chunk/2.5 ms budget bounds it | Keep bands for Auto/Cave; only optimize if vertical walking shows hitches |
| Cave store is 4×4-block resolution | Reloaded caves are blockier than the live 1:1 render | Accept for fallback; a 1-block store would multiply files by 16 |
| Block edits do not invalidate cave cells | Placing/breaking a block can leave a stale saved cave cell until live data wins | Hook the game's place/destroy events to update/remove the touched cell |
| Cave store growth / startup scan | One file per band/region; thousands of files add scan latency and disk use | Add prune/compaction; consider a higher flush cadence or region consolidation |
| Cave store shares `SaveExploredMap` | No independent cave persistence toggle/disk budget | Add `SaveCaveMap` only if requested |
| Interior exact-Y stored into its 16-block band | Reloading the same band at another Y shows a slightly different slice | Band-quantize the store key or add a per-Y store if precision persistence is needed |
| Beacons have no depth occlusion | IMGUI composites after the world; beams show through terrain | Opt-in default off; a true test needs a throttled voxel ray, not `Physics.Linecast` on unloaded chunks |
| Beacon ranges are fixed constants | Beams 96 blocks / labels 384 blocks; more distant WPs live on the map | Add `BeaconBeamRange`/`BeaconLabelRange` config if the user wants to tune them |
| Beacon labels can overlap | Nearest 12 near labels + 8 far labels; dense clusters still collide | Add collision/stacking if it becomes noisy |
| Old waypoints store head Y | New waypoints store feet Y; old ones are resolved visually for beacons | Edit/recreate an old waypoint for exact feet Y |
| Saved cave liquids lose the bed block | Fallback approximates stone; water/lava color may differ slightly from the live render | Add a bed field with a store format bump if liquid fidelity matters |
| Existing cave regions are still opened on a fog probe | `HasAnyCaveCell` avoids absent regions, but a present region loads synchronously on first probe | Precompute a chunk-presence index in `ScanIndex` if panning shows hitches |
| Realms get their own folder | `aether_wayfinder/` follows `FSWorldPersistence.directory` | Treat as per-dimension data, or resolve the world root explicitly |
| Map edits not re-sampled | Block place/destroy waits for the next live pass | Hook the game's place/destroy events to invalidate the touched cell/tile |
| Rooms above the 16-block band | Case B now scans the whole band, but a room entirely above `band × 16 + 15` still belongs to the next band | `Interior` samples the exact Y; Auto/Cave remain band-based |
| Exterior dim strength | 30 % + desaturation is deliberately strong to read as “context” | Tune to 0.35–0.40 if the user wants more surface detail |
| Legacy x-ray cells | Sparse store cells are hidden by density, but still occupy region files/counts | Optional prune of isolated cells on load/flush |
| Deathpoint QA used a forced health trigger | The trigger file/route was removed after verification | Real deaths use the same `IsDead`/last-alive path; no debug code shipped |
| Old `aether_wayfinder.json` may remain on disk | Users may think it still works | Documented here; safe to delete |

## 10. Refinement history

| Date | Symptom | Root cause | Rule / change |
| :--- | :--- | :--- | :--- |
| 2026-11-03 | Minimap could starve terrain loading | Synchronous `world[x,y,z]` on unloaded chunks | R2 safe helpers + architecture gate |
| 2026-11-03 | Two engines/waypoint stores | Native suite port kept a parallel implementation | Single-runtime decision; dead copy deleted |
| 2026-11-03 | Waypoints/mobs drifted against minimap terrain | 72 px texture drawn as if it spanned `2 × RadarRange` (96 blocks) | 128×128 sample + `StepForRadar`/`UvSpan` crop; math gate |
| 2026-11-03 | Fullscreen markers drifted between 50 % and 100 % zoom | Step thresholds `0.35/0.70` let `visibleSpan > sampledSpan`, so UV clamped | `StepForZoom` guarantees `step × zoom ≥ 1`; 0.25×–4.0× everywhere |
| 2026-11-03 | Texture uploads ran 2–3× per frame | Render called on Layout/input events, and every event while dragging | Gate on `Repaint`; 0.05 s drag throttle + final pass on release |
| 2026-11-03 | Cave Y buttons appeared to do nothing | `RenderTerrain` uses `playerY` for Cave; only Slice reads `SliceY` | Y controls moved to the right panel and shown only in Slice |
| 2026-11-03 | Minimap control bar overflowed at default size | Fixed-width groups needed ~220 px | Priority layout: mode, J, zoom, size; optional groups hidden by measured width |
| 2026-11-03 | Layer badge covered zoom controls at 1280×720 | Badge pinned at `canvas - 116` assumed a ≥562 px canvas | Badge drawn only when it fits; layer button remains the indicator |
| 2026-11-03 | Slice Y could leave 0–255 and not persist | Ad-hoc `++/--`, no config range, per-path cache handling | `SliceY` 0–255, `SetSliceY` single entry point, persists + invalidates |
| 2026-11-03 | Right-panel labels clipped (`Minimap: 26`, `J Window: 90%`) | Labels were narrower than their text and sliders overlapped | 120/130 px labels with sliders moved right |
| 2026-11-03 | Minimap froze with 99 ms hitches on update | Cold 128×128 pass sampled 16k columns + uploaded in one frame | Progressive 1024-column budget, Repaint-only uploads, no idle uploads |
| 2026-11-03 | Unexplored-looking black tiles never filled in | Unloaded columns were cached as dark forever | Never cache unsampled columns; store fallback only |
| 2026-11-03 | Fullscreen map opened grey / stale layer after cache optimizations | Fresh engine could be 100% cache hits and never upload | `hasUploaded` + mode/step change force an upload |
| 2026-11-03 | Waypoints were global across every world | Single `[Wayfinder] WaypointsData` config entry | Per-world `waypoints.txt`, one-time migration, editor |
| 2026-11-03 | Map forgot explored zones after reload | Only the in-memory tile cache existed | Sparse region store + surface-above-caves fallback |
| 2026-11-03 | Minimap entity scan caused periodic stutter | `FindObjectsOfType<Health>()` every 0.5 s | Iterate `MOBManagement` children; vertical filter |
| 2026-11-03 | Reopening/mode switching re-sampled everything | `WayfinderTileCache.Invalidate()` on open/mode/slice/grid | Keys already encode mode; invalidate only on world change |
| 2026-11-03 | Fullscreen re-rendered constantly while dragging and showed black bands | Each drag event reset the view/progressive pass | Freeze terrain passes while dragging; draw the old texture anchored/offset; render on release |
| 2026-11-03 | Minimap moved in jumps instead of flowing | Texture was recentred only on each 0.5 s refresh | Draw the anchored texture at the player offset between refreshes |
| 2026-11-03 | Progressive fill cleared the whole map into a sweep | Reset cleared pixels/samples on every center change | Scan all cache hits, budget only new columns, never clear the whole texture |
| 2026-11-03 | User thought black map areas were a render bug | No on-screen explanation for fog of war | Added “Black = unexplored” legend; documented surface-only persistence |
| 2026-11-03 | Minimap became more aggressive every block/height change in Cave | Failed samples were never cached, so `HasPendingWork` stayed true and the 1024-column pass repeated forever | 1 s miss cache per cell/mode/slice; failed columns stop consuming the budget |
| 2026-11-03 | Persistence looked partial/layer-specific | Only Surface was recorded, so Cave play produced no new cells | `AlwaysMapSurface`: sampled surface cells while underground (≤128/pass) |
| 2026-11-03 | User unsure why Cave/Slice looked different | Layering was implicit | Panel states “Black = unexplored. Surface and caves are saved.” |
| 2026-11-03 | Minimap flickered, zoom distorted, filled bottom-to-top | One texture re-rendered and re-uploaded per refresh while the camera moved | Chunk atlas: 32×32-block textures drawn as world-anchored quads; zoom rescales only |
| 2026-11-03 | Shading seams on every chunk border | Slope/wall shading only saw the chunk interior | 1-block halo sampling (stride 34); upload/cache only the interior |
| 2026-11-03 | 1 px near-black line on some chunk borders at 100 % | Rounded rect 179×180 + `ScaleToFit` drew 179×179, leaving 1 px of canvas fill | `StretchToFill` (square texture, near-square rect); pixel-snapped edges retained |
| 2026-11-03 | Cache-hit pixels collided with real blocks | Sentinel `BlockId = 1` is a valid `ushort` block id | Parallel `bool[] finalPixel` + `TerrainSample.FromCache` |
| 2026-11-03 | Cave textures re-rendered on every small Y move | Sampling used the live `sliceY`, so each step invalidated chunks | Quantize to the 16-block band floor; `mode:band` key clears once |
| 2026-11-03 | Square chunk corners would show in Circle shape | Chunk quads are square, the bezel is round | Inverse circle mask drawn between the chunks and the bezel |
| 2026-11-03 | First Circle mask painted a dark square over the world | Mask covered the whole `frameRect` with opaque corners | Themed card + dark inset behind, mask clipped to the inner map |
| 2026-11-03 | Cave walls brightened in a 1-block band at chunk borders and on stale re-renders | Tile cache stored colour+height but not `IsCaveWall`, so cached walls were treated as open floors | Cache stores a flag byte (`FlagCaveWall`); `TerrainSample.IsCaveWall` is restored on cache hits |
| 2026-11-03 | Cave/surface styles mixed in one view; exterior surface looked bright/x-ray | Cave fallback was per-column and the underground surface fill recorded unseen surface | Interior cutaway (open rooms detailed, exterior dim store), `AlwaysMapSurface` off, `FlagOpenRoom` |
| 2026-11-03 | Cold map at 25 % took ~4 s to fill; layer switches could stall | One chunk rendered per DrawView | Up to 4 chunks / 2.5 ms per Repaint, nearest first, Repaint-only |
| 2026-11-03 | Circle minimap was a round map in a square card | IMGUI cannot clip a quad to a circle | CPU-composited texture with a circular alpha cut, drawn under the bezel |
| 2026-11-03 | Beds/NPCs visible through unexplored black (x-ray) | Native radar POIs/mobs were drawn regardless of the terrain pixel | `IsRenderedExplored` gate (rendered pixel + half fade) |
| 2026-11-03 | Isolated surface cells showed as a dotted grid | Old underground sampler wrote sparse 4×4 cells; they were still rendered | Density test (3×3 cells, min 4 known); no surface recording indoors/underground |
| 2026-11-03 | Low structures showed the dim exterior instead of their interior | Case B only probed 5 blocks above the band floor | Probe the whole 16-block band (`+2…+15`) |
| 2026-11-03 | Chunks popped in hard on every cold fill | First render was drawn at full alpha | 0.28 s smoothstep fade (square quads and circle compositor) |
| 2026-11-03 | Grid was a single toggle and region boundaries were invisible | Only chunk lines existed | `GridStyle` Off/Chunk/Region/Both; region lines baked at 256 blocks |
| 2026-11-03 | No quick performance/clock context on the map | Nothing displayed | Info card + minimap line with FPS/time/day/light |
| 2026-11-03 | Semi-open structures were treated as closed (Auto showed Cave) | Indoor detection used only the player's column | 8 horizontal rays, ≥7/8 walls within 32, plus depth; separate `cachedMapCave` |
| 2026-11-03 | Small igloos were treated as open by a fixed coverage box | 13×13 sampled the outside of small structures | Wall rays follow the actual room; igloos/cave rooms/space structures work |
| 2026-11-03 | A single overhang/1-block fence triggered the cave cutaway | One-height rays counted low fences and roofs | Require walls at both feet+1 and feet+2; a roof alone is Surface |
| 2026-11-03 | TP to a deathpoint crashed, then landed inside blocks (suffocation) | Player moved before the target chunks loaded; no safe Y | `FollowPlayer` first, wait, find a 2-block air gap with solid floor |
| 2026-11-03 | Auto could not show the inside of a skylit iceberg | Auto uses roof/wall heuristics; a skylight looks outdoors | Manual `Interior` layer forces the current-Y slice + dim surface |
| 2026-11-03 | Beacons hidden when the 3D markers were off | Beams inherited `ShowWorldWaypoints` | Independent `ShowWaypointBeacons` + separate HUD call |
| 2026-11-03 | Export scan could hitch | 1024² store cells and region churn | On-demand cell scan (~145–250 ms), next-frame button, no live sampling |
| 2026-11-03 | Cave layer revealed loaded-but-unvisited terrain | The sampler rendered every loaded chunk | Per-band 3×3 visited set; unvisited chunks stay black |
| 2026-11-03 | Cave/slice progress vanished after reload | Only surface cells were stored; fog was in-memory | `WayfinderCaveStore` per band + lazy `HasAnyCaveCell` fog seeding |
| 2026-11-03 | Interior was blocky/offset vs Wayfinder | The manual layer still sampled the 16-block band floor | `Interior` keys/samples the exact player Y; Auto/Cave keep bands |
| 2026-11-03 | Beacon labels missing; deaths looked like normal WPs | Beams only drew a column | Name pills for nearest 12 + procedural skull from a shared 16×16 texture |
| 2026-11-03 | Far waypoints were invisible on the panned/zoomed map | The fullscreen loop skipped anything outside the canvas | Edge marker at the true ray/rect intersection, name + distance, skull for deaths |
| 2026-11-03 | Cave map showed the surface above a shallow cave | The interior cutaway applied whenever a roof/wall was detected, even underground | Auto/Cave cutaway requires the explored surface within 8 blocks; `Interior` still forces it |
| 2026-11-03 | Beacon started at the head and its name sat off-screen at the top | Waypoints stored camera Y; labels anchored to the beam top | Resolve the floor under the waypoint; anchor the label at the visible midpoint |
| 2026-11-03 | Beams at 100+ blocks looked like noise; nearby WPs barely showed | One 192-block beam range for every waypoint | Beams 96 blocks + name-only pills up to 384 blocks; full map handles the rest |
| 2026-11-03 | New waypoints started at head height | Quick/form/My Pos used the camera Y | Store the feet Y via a six-block ground scan |
| 2026-11-03 | Cave data missed after enabling persistence mid-session | Tile-cache hits cannot be re-recorded | Invalidate the tile cache/atlas when the cave store opens |
| 2026-11-03 | Interior fog went black at a band boundary | Fog used floor(camera Y), renderer used round(camera Y) | Mark both floor and rounded bands; exact-Y mode bit prevents key collisions |
| 2026-11-03 | Cave minimap showed a stale Y after walking up/down | `currentSliceY` was only refreshed when the fullscreen map opened | Auto/Cave/Interior now use the player Y every frame; Slice pins its altitude |
| 2026-11-03 | Deaths left no record of where you died | No death hook | Poll `PlayerHealth.IsDead`, last-alive position, red `Death N` waypoint with limit |
| 2026-11-04 | `J` opened the native pause/minimap | Game native "Toggle Minimap" is also `J` in the saved prefs | Default `FullscreenMapKey` changed to `M`; old `J` values migrate once via `FullscreenMapKeyMigrated` |
