# Aether UI & Native Suite — Mod Contract

| Field | Value |
| :--- | :--- |
| **ID** | `aether_ui` |
| **Version** | `1.0.0` |
| **Author** | Ibrahim García |
| **Assembly** | `Aether.UI.dll` |
| **Status** | 🟡🧪 (shared shell; 7 mod runtimes split out) |
| **Canonical runtime** | `Aether.UI/NativeSuite/` (`BlockStorySuitePlugin`) |
| **Last reviewed** | 2026-11-04 |

## 1. Scope

Owns the shared native Block Story UI shell and the QoL services that remain in-game:

- Wood-panel/UIAtlas renderer, modal input isolation and native cursor handling.
- Shared contracts/facades for the split mods (`IAetherUiModule`, `SuiteShared`, `PerfMetrics`, `SafeVoxels`).
- **Lens WAILA + rich hover tooltips** (the last mod still in the shell).
- HUD customizer (presets, drag editor, themes) and unified Keybind Manager.
- F10 mod catalog, Diagnostics/QA harness and Mod Health dashboard.
- Crash overlay, title watermark and loading-screen gating.

Wayfinder, Blueprint, Sodium, Lithium, EntityCulling, FerriteCore and Dusk are now separate
`Aether.Mods.*` assemblies.
- F10 mod catalog (real `LoadedMods`), Diagnostics/QA harness and the live Mod Health dashboard.
- Menu chrome gating: the MODS/HOTKEYS buttons, gameplay HUDs and the bottom watermark hide during Block Story's loading/fade screen; both mod counters share `AetherBootstrap.LoadedModCount`.
- Aether crash overlay and title watermark.

It does **not** own the loader, dependency resolution or crash blame attribution; those are `aether`
core.

## 2. Entry points and public surface

| Type | Role |
| :--- | :--- |
| `AetherUIService : IUIService, IModInitializer, IClientModInitializer` | Registers the UI service, spawns the suite on frame 0, renders the crash overlay/watermark. |
| `BlockStorySuitePlugin : MonoBehaviour` | The native suite. Partial across 9 files. |
| `IUIService.RenderGUI()` | Global OnGUI bridge from `AetherBootstrap`. |
| `AetherCrashOverlay` | Fatal crash modal (`GUI.depth = -9999`). |
| `TitleWatermarkOverlay` | Menu-only Aether badge. |

## 3. Runtime map

`AetherUIService.OnInitializeClient()` creates `Aether_NativeBlockStorySuite` and adds
`BlockStorySuitePlugin`. The plugin's `Awake()` binds the suite `.cfg`, initializes each module and
applies Harmony patches; `Update()` handles hotkeys/state; `OnGUI()` draws the menu/HUD.

| File | Responsibility |
| :--- | :--- |
| `Plugin.cs` | WoodPanel/UIAtlas renderer, modal isolation, Lens, hover tooltips, F10 mod catalog, Harmony patches. |
| `WayfinderBridge.cs` / `BlueprintBridge.cs` / `PerformanceBridge.cs` / `DuskBridge.cs` | Thin forwarding of existing shell call sites to the per-mod interfaces. |
| `Modules/` + `Runtime/` | `IAetherUiModule`, `AetherUiModuleRegistry`, per-mod contracts, `SuiteShared`; shared services for the runtimes. |
| `Diagnostics/PerfMetrics.cs` | Shared FPS/RAM and per-engine counters for F8 and QA. |
| `KeybindManagerModule.cs` | Vanilla + mod key registry, conflicts, live rebinding, extra hotkeys (K/H/L/B). |
| `HudCustomizerModule.cs` | HUD presets, drag editor, UI themes, reset. |
| `QaDiagnosticsRunner.cs` | Live self-tests + file-triggered QA commands (off by default). |
| `ModHealthModule.cs` | F10 → Mod Health: live FPS/RAM, Harmony/crash state, every manifest path/size and a written health report. |
| `IsometricBlockIconRenderer.cs` | 3D affine isometric block icons from the NGUI atlas. |

Removed 2026-11-04: `Overlays/AetherModMenuModal.cs`, `Input/KeybindRegistry.cs`, `Input/DualBusInputRouter.cs`,
`Cursor/GuaranteedCursorOverlay.cs`, `Layout/HudAnchorManager.cs`, `Resources/AetherResources.cs` and `ISquircleCanvas`
were compiled but never used. F10 is the native suite (`Plugin.cs` + `ModHealthModule.cs`); that is the only mod menu.

## 4. Configuration

| Surface | File | Keys |
| :--- | :--- | :--- |
| Suite cfg | `BepInEx/config/com.ultimaibrahim.blockstory.suite.cfg` | See sections below |
| `AetherConfig` | `BepInEx/config/aether/aether_ui.json` | None today (the UI service declares no JSON keys) |

| Section | Keys |
| :--- | :--- |
| `General` | `MenuKey` (F10) |
| `HUDLayout` | `Preset`, `MoveDiamondsUnderXP`, `MoveTopRightButtonsToBottomRight`, `HudDragKey` |
| `Lens` | `Enabled`, `ToggleKey`, `BgOpacity`, `Anchor`, `OffsetX`, `OffsetY`, `Theme`, `ShowCoordinates`, `3DBlockIcon` |
| `Durability` | `Enabled`, `HoverTooltipEnabled` |
| `Wayfinder` | `Enabled`, `ToggleKey`, `FullscreenMapKey`, `MapLayerModeKey`, `QuickWaypointKey`, `RadarRange`, `ShowCoords`, `Shape`, `Anchor`, `OffsetX`, `OffsetY`, `Theme`, `LayerMode`, `RenderMode`, `ShowChunkGrid`, `SliceY`, `ShowWorldWaypoints`, `WaypointsData`, `MinimapSize`, `MinimapScale`, `FullscreenMapScale` |
| `Blueprint` | `Enabled`, `ToggleKey`, `RecipeKey`, `UsageKey`, `BookmarkKey`, `AutoOpenWithInventory`, `SavedBookmarks` |
| `Sodium` | `Enabled`, `ShadowCulling`, `ShadowDistance`, `UndergroundShadowStripper`, `FrustumCulling`, `VerticalCaveOcclusion`, `VerticalDepthThreshold`, `SphericalCulling`, `WaterMode`, `ShadowCascades`, `AnisotropicFiltering`, `TextureQuality`, `VSync`, `OverlayKey` |
| `Lithium` | `Enabled`, `PhysicsTimestep`; deprecated: `AutoSyncOptimization`; reserved ⛔: `ChunkTickThrottler`, `TickThrottleDistance`, `AiSpawnerPacing`, `TargetFps` |
| `EntityCulling` | `Enabled`, `CullDistance`, `OffscreenBoneCulling`, `VoxelWallOcclusion`, `ParticleCulling`, `ParticleCullDistance` |
| `FerriteCore` | `Enabled`, `AutoGcOnMenu`, `AutoGcInterval`, `PerfOverlayEnabled`, `OverlayCorner`, `SuppressVanillaHudFps` |
| `Dusk` | `Preset`, `CaveDarknessDepth`, `BlockMatteAntiGloss`, `MinAmbientPlayerLight`, `SunlightUndergroundSuppression`, `AmbientFloorSuppression`, `DynamicHandTorchlight`, `DynamicTorchIntensity`, `DynamicTorchRange`, `WarmFirelightColor`, `TorchFlameFlicker` |
| `Keybinds` | `ManagerKey` |
| `Diagnostics` | `EnableLiveQA` (default `false`), `QaTriggerFile` |

## 5. Hotkeys

| Action | Default | Owner |
| :--- | :--- | :--- |
| Suite mod menu | `F10` | `Plugin.Update` |
| Lens toggle | `F4` | `Plugin.Update` |
| Minimap toggle | `N` | `Plugin.Update` |
| Fullscreen Wayfinder | `J` | `Plugin.Update` |
| Map layer cycle (Auto/Surface/Cave/Slice) | `L` | `KeybindManagerModule` |
| Quick waypoint | `B` | `KeybindManagerModule` |
| Blueprint overlay | `O` | `BlueprintModule` |
| Blueprint recipe / usage / bookmark | `R` / `U` / `A` | `BlueprintModule` (inventory context) |
| HUD drag editor | `H` | `KeybindManagerModule` |
| Keybind Manager | `K` | `KeybindManagerModule` |
| Performance overlay | `F8` | `PerformanceSuiteModule` |
| Close modal | `Escape` | `Plugin.Update` |

All of them are registered in the Keybind Manager and participate in the overlap detector.

## 6. Invariants (must never break)

- **R3:** never replace `NativeSuite` with simplified IMGUI boxes. The 9 files and ~540 KB are the
  ported native UI.
- **R2:** voxel reads go through `WayfinderEngine.GetBlockSafe/GetBlockFast`; never
  `world[x,y,z]`.
- **R4:** `Physics.autoSyncTransforms` stays `true`.
- Modal isolation is symmetric: every `AcquireModalIsolation()` has a matching release, including the
  HUD-drag transitions. Setting `isDraggingHudElements` directly is forbidden.
- `GUI.depth` for modals stays above the game HUD and below the native cursor (`-5000`).
- QA harness stays off by default (`EnableLiveQA = false`); no startup self-test, no scene-change
  self-test, no 0.25 s polling.
- The F10 catalog reads `AetherBootstrap.LoadedMods`; it must never go back to a hardcoded-only list.
- No per-scene `Resources.FindObjectsOfTypeAll`/GC spikes in `OnGUI`; caches are static or rebuilt only
  on demand.
- Every config key that is reserved/not implemented must be labeled ⛔ and hidden from the UI.

## 7. Performance budget

| Metric | Budget | How measured |
| :--- | :--- | :--- |
| Frame rate | 60+ FPS at 1920×1080 in normal play | In-game F8 HUD |
| Minimap lookup | O(1), ~0.001 ms per tile | `WayfinderTileCache` |
| Telemetry refresh | 0.25 s | `PerformanceSuiteModule` |
| Hot-loop allocations | 0 B after warm-up | `ZeroAllocTests` + F8 RAM |
| Modal open | No scene-wide GC | F8 during open/close |

## 8. QA gates

| Gate | Kind | Command / check |
| :--- | :--- | :--- |
| Native UI preserved | Automated | `ArchitectureTests.NativeUi_KeepsTheFullPortedSuiteAcrossAssemblies` |
| Split mods own their runtime | Automated | `ArchitectureTests.MigratedMod_OwnsRuntimeAndEntrypoint`, `BlueprintMod_OwnsRuntimeAndEntrypoint`, `PerformanceMods_OwnRuntimeAndEntrypoints` |
| No gameplay in the shell | Automated | `AetherUI_DoesNotContainWayfinderGameplay` / `...BlueprintGameplay` / `...PerformanceGameplay` |
| No direct world indexer | Automated | `ArchitectureTests.VoxelCode_MustNotUseDirectWorldIndexers` |
| Safe chunk helpers exist | Automated | `ArchitectureTests.VoxelCode_KeepsTheSafeChunkHelpers` |
| autoSync never disabled | Automated | `ArchitectureTests.Physics_AutoSyncTransformsIsNeverDisabled` |
| Real mod catalog | Automated | `ArchitectureTests.NativeModCatalog_ReadsLoadedMods` |
| Mod Health reads live state | Automated | `ArchitectureTests.ModHealthPanel_ReadsLiveLoaderAndRuntimeState` |
| One mod count + loading guard | Automated | `ArchitectureTests.Menu_UsesOneModCountAndHidesChromeWhileLoading` |
| Menu opens/closes | Live | F10 in Main Menu and pause; `Escape` and outside-click close it |
| Mod Health panel | Live | F10 → Mod Health; all manifests listed, Write report creates `BepInEx/config/aether_mod_health.txt` |
| Loading screens | Live | Start/load a world; no MODS/HOTKEYS buttons or watermark over the fade/progress bar |
| Mod count | Live | Bottom badge and top MODS button both show 10 (15 with the QA kit) |
| Modal isolation | Live | Open each tab, then inventory/pause; vanilla UI must still respond |
| HUD drag | Live | H at least once; all widgets draggable; exit restores input |
| Crash overlay | Live | Diagnostics → simulate crash; overlay opens and closes |

Menu/HUD was visually verified at 1920×1080 on 2026-11-03 (real-mod catalog, module chips, Mod Health, drag mode, boot/loading gating). Module settings live checks remain.

## 9. Known debt / limits

| Item | Impact | Next action |
| :--- | :--- | :--- |
| Third-party `AetherConfig` entries are not rendered in the native menu | Mod authors cannot tune JSON keys in-game | Add a declarative config tab or document the JSON path |
| Module settings live checks | Wayfinder/Blueprint/Sodium/Lithium/EntityCulling/FerriteCore/Dusk/Lens panels were exercised live on 2026-11-04 | Remaining: mob-card and lighting cave visual pass |
| Old `aether/*.json` files may remain on disk | Users may think they still work | Single-runtime decision: suite `.cfg` is canonical; old JSON is inert |
| H6 partial: tick throttling / spawner pacing | Advertised nowhere; reserved keys remain | Keep ⛔ or implement with a measured budget |

## 10. Refinement history

| Date | Symptom | Root cause | Rule / change |
| :--- | :--- | :--- | :--- |
| 2026-11-03 | Mod menu showed a hardcoded catalog | UI never read `LoadedMods` | Real catalog + `SourcePath` + gate |
| 2026-11-03 | Lithium panel advertised autoSync=false and unimplemented throttling | Copy drift after the autoSync fix | Corrected description; reserved features labeled |
| 2026-11-03 | QA harness ran at startup | Always-on diagnostics | Gated behind `EnableLiveQA=false`; trigger file moved |
| 2026-11-03 | Duplicate runtime/config surfaces | Parallel runner copies in `Aether.Mods.*` | Single-runtime decision: suite is canonical, manifests are metadata-only |
| 2026-11-03 | No single place to prove mod/loader health | Diagnostics existed only in logs | Added F10 → Mod Health with live metrics, manifest paths and a written report |
| 2026-11-03 | Bottom said 4 mods, top said 6; chrome over loading | Watermark excluded core/UI and `Fade` was not detected | Shared `LoadedModCount`; `LoadingScreenState` hides menu chrome |
| 2026-11-03 | Sidebar clipped names, header scrolled away, chip labels clipped, drag banner overlapped Lens | Fixed-width layout and emoji glyphs unsupported by the native font | 1000×640 window, scroll reset, compact names/paths, short chips, safe glyphs, banner/handle fixes |
