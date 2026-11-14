# Aether Blueprint — Mod Contract

| Field | Value |
| :--- | :--- |
| **ID** | `dev.ultimaibrahim.aether.blueprint` (BepInEx GUID; was `aether_blueprint`) |
| **Version** | `1.1.0` |
| **Author** | Ibrahim García |
| **Assembly** | `Aether.Mods.Blueprint.dll` (+ `Aether.Nano.dll`) |
| **Status** | 🟢 checked in a world with Aether: catalog with the inventory, recipe card (2026-11-12, Block Story 14.5.4) |
| **Canonical runtime** | `src/Aether.Mods/Aether.Mods.Blueprint/Runtime/BlueprintModule.cs` |
| **Entrypoint** | `BlueprintPlugin : Aether.Nano.NanoMod` (`[BepInDependency(Nano.Guid)]`) |
| **Host contract** | Nano: settings page (`NanoPages`), panel and recipe card (`NanoPanels`), hovered item (`NanoItems.HoveredSlotItem`) |
| **Last reviewed** | 2026-11-12 |

Since 1.1.0 Blueprint is a Nano mod: it draws its browser and recipe card itself and only needs `Aether.Nano.dll`. Aether's
tooltips stay out from under its panel and count its recipe card as a modal, through `NanoPanels`. Its keys are listed in
Aether's F7 from its own config. Settings carry over once from the UI suite's file.

## 1. Scope

Manifest for the Roughly Enough Items-style recipe browser and item catalog:

- Inventory-synced item grid with categories, search (EN/ES) and pagination.
- Native wood slots and 2D `UIAtlas` icons.
- 3×3 recipe card (`R` / left click) with ingredient counts and recursive navigation.
- “Uses” lookup (`U` / right click) and bookmarks (`A` / pin).
- Auto-open with inventory/crafting/chest; craftable filter.

This assembly owns **the full runtime**. The shell only forwards lifecycle/drawing through `IBlueprintUiModule`.

## 2. Entry points and public surface

| Type | Role |
| :--- | :--- |
| `[assembly: AetherMod("aether_blueprint", ...)]` | Discovery metadata and real-catalog row. |
| `BlueprintEntrypoint` | Real loader entrypoint; registers the module with the shell. |
| `BlueprintModule : IBlueprintUiModule` | Canonical browser, catalog, recipe card, bookmarks and `[Blueprint]` config. |

No `MonoBehaviour`, no `AddComponent` and no duplicate runner in this assembly.

## 3. Runtime map

`LoadedMods` includes `aether_blueprint` from the assembly attribute. The canonical UI is `BlueprintModule.cs`:
it observes the inventory, builds/searches the catalog, draws the right-side panel and the 3×3 recipe
modal, and persists bookmarks to the suite `.cfg`.

The old `BlueprintRunnerComponent`, `BlueprintCatalog`, `BlueprintIconCache`, `BlueprintRecipeScraper` and `BlueprintConfig` were
deleted as dead duplicates.

## 4. Configuration

| Surface | File | Keys |
| :--- | :--- | :--- |
| Suite cfg | `BepInEx/config/com.ultimaibrahim.blockstory.suite.cfg` | `[Blueprint] Enabled`, `ToggleKey`, `RecipeKey`, `UsageKey`, `BookmarkKey`, `AutoOpenWithInventory`, `SavedBookmarks` |

`aether/aether_blueprint.json` is no longer generated or read. If an old file exists, it is inert.

## 5. Hotkeys

| Action | Default | Owner | Context |
| :--- | :--- | :--- | :--- |
| Toggle Blueprint | `O` | `BlueprintModule` / Keybind Manager | Gameplay/inventory |
| Recipe lookup | `R` | `BlueprintModule` | Inventory |
| Usage lookup | `U` | `BlueprintModule` | Inventory |
| Bookmark | `A` | `BlueprintModule` | Inventory |
| Close card | `Escape` | `BlueprintModule` | Recipe card |

`R` is deliberately **not** the vanilla next-slot shortcut while Blueprint is handling the inventory; the
conflict detector is context-aware (`Inventory` vs `Gameplay`).

## 6. Invariants (must never break)

- **H1:** never mutate a live recipe object while browsing. The catalog works on copies/snapshots.
- **H2:** never index a recipe by a verb/action name treated as a result key (no “Craft”, “Use”, etc.).
- **H3:** a failed lookup must not poison the catalog index; subsequent items remain searchable.
- Item icons use the real `UIAtlas` sprite; failed lookups fall back to a readable label/initial.
- The panel never overlaps Wayfinder at the supported anchor; opening/closing restores vanilla input.
- Bookmarks survive relaunch and unknown/deleted items are ignored safely.
- Recipe navigation must terminate (visited stack/cycle guard), not recurse forever.
- Closing the inventory closes Blueprint; no invisible panel consuming input.

## 7. Performance budget

| Metric | Budget | How measured |
| :--- | :--- | :--- |
| Catalog search | No full-rescan per frame; cached per query | F8/frame time |
| Page draw | Cached icons; no texture creation per slot | Code review + F8 |
| Recipe lookup | Bounded, no unbounded recursion | Code review |
| Hot-loop allocations | 0 B after warm-up | `ZeroAllocTests` + F8 |

## 8. QA gates

| Gate | Kind | Command / check |
| :--- | :--- | :--- |
| Own runtime + entrypoint | Automated | `ArchitectureTests.BlueprintMod_OwnsRuntimeAndEntrypoint` |
| No gameplay in shell | Automated | `ArchitectureTests.AetherUI_DoesNotContainBlueprintGameplay` |
| Contract exists | Automated | `ArchitectureTests.EveryAetherMod_HasAContract` |
| Startup/catalog | Live 2026-11-04 | `[Blueprint]` module initialized; 617 items / 763 result keys / 407 ingredient keys |
| Inventory browser | Live 2026-11-04 | Inventory opens Blueprint sidebar; categories, pagination (1/15), icons render |
| Tooltip | Live 2026-11-04 | Hovering an item shows name/stats/origin; no stuck tooltip |
| Catalog/search | Live | Search EN/ES, switch category, page forward/back |
| Recipe card | Live | R on a craftable item; 3×3 ingredients/counts; drill down and back |
| Uses | Live | U on an ingredient; list is non-empty and opens the right recipe |
| Bookmarks | Live | A on several items, relaunch, verify order and persistence |
| Overlap/input | Live | Open inventory + Blueprint, then Escape; vanilla inventory still responds |

## 9. Known debt / limits

| Item | Impact | Next action |
| :--- | :--- | :--- |
| No runtime code in this assembly | By design under the single-runtime decision | Any Blueprint change belongs in the suite module |
| No live check in the current build | Recipe card layout unverified | Run the 1920×1080 pass and fill the table |
| Old `aether_blueprint.json` may remain on disk | Users may think it still works | Documented here; safe to delete |

## 10. Refinement history

| Date | Symptom | Root cause | Rule / change |
| :--- | :--- | :--- | :--- |
| 2026-11-03 | Recipe lookup mutated live recipes / poisoned the index | Index built from live objects and verb keys | H1–H3 fixes + regression tests |
| 2026-11-03 | Two catalogs/scrapers | Native suite port kept a parallel implementation | Single-runtime decision; dead copy deleted |
