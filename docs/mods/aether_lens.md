# Lens — Mod Contract

| Field | Value |
| :--- | :--- |
| **ID** | `dev.ultimaibrahim.aether.lens` (BepInEx GUID; was `aether_lens`) |
| **Version** | `1.1.0` |
| **Author** | Ibrahim García |
| **Assembly** | `Aether.Mods.Lens.dll` (+ `Aether.Nano.dll`) |
| **Status** | 🟢 checked in a world with Aether and with BepInEx alone (2026-11-12, Block Story 14.5.4) |
| **Canonical runtime** | `src/Aether.Mods/Aether.Mods.Lens/Runtime/LensModule.cs` (+ `LensSettings.cs`, `IsometricBlockIconRenderer.cs`) |
| **Entrypoint** | `LensPlugin : Aether.Nano.NanoMod` (`[BepInDependency(Nano.Guid)]`) |
| **Host contract** | Nano: settings page (`NanoPages`), movable card (`NanoHud`), owner mod (`NanoOwners`) |
| **Last reviewed** | 2026-11-12 |

Since 1.1.0 Lens is a Nano mod: it draws its card and handles its toggle key itself, and only needs `Aether.Nano.dll`.
With Aether, the card keeps clear of the game's top HUD, hides under Aether's menus, can be moved in Aether's HUD editor,
and shows which mod a block or creature comes from. Without Aether it shows "Block Story" for game content.
Its settings (including `[Lens] Scale`, formerly in Aether's HUD section) carry over once from the UI suite's file.

## 1. Scope

Runtime for WAILA-style block/creature inspection:

- Target block/creature name, coordinates, health/level, optimal harvest tool.
- True 3D isometric cube sampled from the real block atlas (8x20 tiles of 128x128, top/left/right `Surface.Texture`), with surface hue tint and a 2:1 isometric silhouette. The card is 300x68 by default (`Lens.CardWidth`, 240–420) with a 56 px slot, **no item frame**, a translucent themed background, and a blue source line (`Block Story`/mod name) like Lens.
- Native/mod origin line: blue `Block Story` for native blocks, the owning mod name for `DynamicBlockRegistry` custom blocks. No badge.
- 4 screen anchors, 5 visual themes, pixel offsets, HUD drag.
- Floating item tooltip with name, category, slot, durability, damage/mining/armor and origin.

This assembly owns the **WAILA runtime and its `[Lens]` config**. The shared isometric renderer
(`IsometricBlockIconRenderer`) stays in `Aether.UI/Rendering`; the rich inventory tooltip remains a
shared `aether_ui` service for now.

## 2. Entry points and public surface

| Type | Role |
| :--- | :--- |
| `[assembly: AetherMod("aether_lens", ...)]` | Discovery metadata and real-catalog row. |
| `LensEntrypoint` | Real loader entrypoint; registers the module with the shell. |
| `LensModule : ILensUiModule` | Canonical WAILA renderer, card helpers, tool badge, hearts, quest/level options and `[Lens]` config. |
| `IsometricBlockIconRenderer` | Shared 3D icon renderer (`Aether.UI/Rendering`). |
| `BlockStorySuitePlugin.DrawHoveredItemRichTooltip()` | Shared rich tooltip (`aether_ui`), not part of this assembly yet. |

No `MonoBehaviour`, no `AddComponent` and no duplicate runner in this assembly.

## 3. Runtime map

`LoadedMods` includes `aether_lens` from the assembly attribute. The suite then draws Lens from
`BlockStorySuitePlugin.OnGUI()` and reads the suite `.cfg`. The old `LensRunnerComponent`,
`LensInventoryTooltip`, `IsometricBlockRenderer`, `BlockStoryColorPalette` and `LensConfig` were
deleted as dead duplicates (git history keeps them).

## 4. Configuration

| Surface | File | Keys |
| :--- | :--- | :--- |
| Suite cfg | `BepInEx/config/com.ultimaibrahim.blockstory.suite.cfg` | `[Lens] Enabled`, `ToggleKey`, `BgOpacity`, `CardWidth`, `Anchor`, `OffsetX`, `OffsetY`, `Theme`, `ShowCoordinates`, `3DBlockIcon`, `MobHealthStyle`, `ShowMobLevel`, `ShowMobQuests`, `HideNativeMobHealth`; `[Durability] Enabled`, `HoverTooltipEnabled` |

`aether/aether_lens.json` is no longer generated or read. If an old file exists, it is inert.

## 5. Hotkeys

| Action | Default | Owner |
| :--- | :--- | :--- |
| Toggle Lens HUD | `F4` | `Plugin.Update` (`LensToggleKey`) |
| Suite menu | `F10` | `Plugin.Update` (`MenuKey`) |

`F4` is registered as `mod_lens_toggle` in the Keybind Manager.

## 6. Invariants (must never break)

- Voxel targeting uses the safe raycast path; never `world[x,y,z]` (R2).
- Block/creature names respect the game language (`T.ItemNames`, `T.Mobs`) with a raw fallback.
- The 3D cube uses the native `UIAtlas` sprite; a failed lookup falls back to the item's 2D icon,
  never to an empty/transparent box.
- The tooltip disappears immediately when the inventory closes or the slot is no longer hovered;
  no stuck tooltip across scene changes.
- Health bars only for living entities; the local player is excluded.
- HUD coordinates and offsets are clamped/safe at all anchors; no off-screen card.
- Toggling Lens off must not leave the cursor locked or the HUD in drag mode.

## 7. Performance budget

| Metric | Budget | How measured |
| :--- | :--- | :--- |
| Raycast/target refresh | Throttled; no per-frame chunk generation | In-game F8 |
| OnGUI allocations | 0 B after warm-up | `ZeroAllocTests` + F8 RAM |
| 3D icon | Reuses atlas/material cache; no per-frame texture creation | Code review + F8 |
| Frame time | Within the 60 FPS suite budget | F8 |

## 8. QA gates

| Gate | Kind | Command / check |
| :--- | :--- | :--- |
| No direct world indexer | Automated | `ArchitectureTests.VoxelCode_MustNotUseDirectWorldIndexers` |
| Safe chunk helpers | Automated | `ArchitectureTests.VoxelCode_KeepsTheSafeChunkHelpers` |
| Native UI preserved | Automated | `ArchitectureTests.NativeUi_KeepsTheFullPortedSuiteAcrossAssemblies` |
| Own runtime + entrypoint | Automated | `ArchitectureTests.LensMod_OwnsRuntimeAndEntrypoint` |
| Contract exists | Automated | `ArchitectureTests.EveryAetherMod_HasAContract` |
| Lens themes implemented | Automated | `ArchitectureTests.LensVisualThemes_AreImplementedInThePalette` |
| Card has no item frame | Automated | `ArchitectureTests.LensCard_HasNoItemFrame` |
| Mob health/quest options | Automated | `ArchitectureTests.LensMobCard_HasHealthAndQuestOptions` |
| Tool check + native font | Live | Compare `Digger.diggerType` with the card; names use the game font |
| Native hearts + green bar | Live | Toggle `Hearts`/`Bar`; verify the game heart sprites and the green HP bar text |
| Native mob HP isolation | Live | Hide native HP; name/level must stay visible |
| Motion tokens | Manual | Card reveal 140–180 ms ease-out, 5 px settle; no linear/bounce; `LensReduceMotion` still pending |
| Style review | Manual | Contrast, sizes, badge shapes, no `OnGUI` allocations |
| Real block atlas | Automated | `ArchitectureTests.IsometricRenderer_SamplesTheRealBlockAtlas` |
| Native/mod source line | Live | Sand Stone shows blue `Block Story`; a registered custom block must show its mod name |
| Best tool from strengths | Automated | `ArchitectureTests.LensBestTool_UsesBlockStrengthsNotNameHeuristics` |
| Block card | Live | **2026-11-03:** Snow card + 3D icon + `Tool: Shovel` verified at 1920×1080. Remaining: dirt/stone/ore/water/air. |
| Creature card | Live | Aim at a mob; name, health bar and level correct; player excluded |
| Language | Live | Switch EN/ES; names and tool labels follow the game language |
| Tooltip cleanup | Live | Hover slots in inventory/chest/creative, then close; no stuck tooltip |
| F4 + drag | Live | **2026-11-03:** F4 hide/show verified. Remaining: drag mode move/restore. |

## 9. Known debt / limits

| Item | Impact | Next action |
| :--- | :--- | :--- |
| WAILA split done; tooltip still shared | The rich hover tooltip remains in `Aether.UI` | Move it to Lens after the WAILA split is QA-stable |
| Entity card live QA | Block card verified; mob hearts/level not re-captured after the split | Aim at a mob and record hearts/level/quest line |
| Atlas tile layout fixed to 8x20 (128x128) | Earlier 8x32/128x80 mapping showed the wrong/transparent tiles | Verified live via `H=8, V=20, tile=128x128`; new 2:1 cube/UI capture still pending |
| Material-camera cube render disabled | `UBuild/Earth` needs the full chunk context; standalone render was flat | Keep the direct atlas path; revisit only with a full chunk-ish render setup |
| Entity card, inventory tooltip and language matrix still unverified | Those paths have no live check in this build | Run the remaining QA rows and record them |
| Old `aether_lens.json` may remain on disk | Users may think it still works | Documented here; safe to delete |
| `LensReduceMotion` not implemented | Motion tokens exist in the guide but there is no accessibility switch yet | Add a `Lens` config bool, default `false`; when on, all reveals/press animations are instant |
| Native font may be a bitmap `UIFont` | Forcing it into `GUIStyle.font` can render tofu | Probe dynamic fonts only; if none, keep the default font and log once |

## 10. Refinement history

| Date | Symptom | Root cause | Rule / change |
| :--- | :--- | :--- | :--- |
| 2026-11-03 | Two Lens implementations | Native suite port kept a parallel runner | Single-runtime decision; dead runner/config deleted |
| 2026-11-03 | Hardcoded menu catalog | Menu did not read real mods | Real `LoadedMods` catalog groups this manifest |
| 2026-11-03 | TopCenter WAILA card clipped by the top bar | Safe offset was only 14 px | Raised to 56 px; full card verified at 1920×1080 |
| 2026-11-03 | Cube looked like a flat/hardcoded block and tool was name-guessed | Renderer used the 2D item icon; tool used string heuristics | Real block atlas + surface hues; best tool from `BlockType` strengths + icon |
| 2026-11-03 | No indication of native vs mod block | No origin metadata was displayed | Added `NATIVE`/`MOD` badge from `DynamicBlockRegistry` |
| 2026-11-03 | Atlas cube sampled transparent pixels | `Graphics.DrawTexture` was clipped and the atlas is packed at the bottom | `Graphics.Blit` full readback + direct bottom-up tile sampling; verified Sand Stone |
| 2026-11-03 | Icon did not match the block (bookshelf/swamp stone) | Assumed 8x32 atlas; real material scale is 8x20 of 128x128 | Read `_MainTex` scale, use 128x128 tiles; enlarged the card/icon |
| 2026-11-03 | `NATIVE` badge felt inorganic; block icon had an item frame; mob card looked like a bar | Not Lens-like | Removed the slot, dimmed the background, blue source line, 2:1 cube, mob hearts |
| 2026-11-03 | Themes did nothing; mob health had no options | Lens theme IDs were missing from `GetThemePalette`; no mob config | Added palettes, hearts/bar/none, level/quest toggles and native HP hide |
| 2026-11-03 | Tool icon too small; no best-tool feedback; wrong Antique Window tile | No player-tool comparison; transparent colored atlas tiles are masks | 32 px shadowed icon, check/X, native font, item-icon fallback for Tr/GlowTr blocks |
| 2026-11-03 | Check looked scribbled; native font not loading; native HP hid name/level; Level/Quest seemed inert | Low-res 9×7 mask, font probe too narrow, `SetHealthBarsVisible` hid the whole overhead panel, no visible quest state | 16×16 badge, UILabel dynamic-font probe, bar/HP-only hide, `No quests` line + config save |

## Entity detection (2026-11-05)

Lens resolves the pointed entity through `HealthBase` (common base of native `Health`, pets' `PetHealth` and mod
mobs from other mods) and accepts body-sized **trigger** colliders (`Aether.Core.Entities.EntityHitRules`):
mod mobs often use a trigger capsule as hitbox. Oversized triggers (aggro ranges, > 4.5 blocks half-diagonal) are
ignored. Name/level come from `GetDisplayName`/`GetName`/`Level` when the health component has them (reflection),
else from the GameObject name. A mob that does not derive from `HealthBase` is still not recognised.
Packaging: this lives in `Aether.Mods.Lens` (own assembly), not NativeSuite.
