# Aether Mod Contracts

> **Renamed 2026-11-05:** Jade -> Lens, JourneyMap -> Wayfinder, REI -> Blueprint, Lighting -> Dusk.
> Config sections, ids (`aether_lens`, `aether_wayfinder`, `aether_blueprint`, `aether_dusk`) and the per-world folder
> (`aether_journeymap/` -> `aether_wayfinder/`) changed; existing settings and waypoints are migrated on first launch.

> Only shipped mods have a contract here.

> One contract per shipped mod. A mod is **done** only when its contract is current, its gates pass
> and its live checks have been recorded. Contracts are living documents: they change with the mod.

This directory is the second pillar of the refinement era: **per-mod accountability**. It turns
"I think this mod is fine" into a small set of invariants, budgets, tests and known debt.

---

## 1. Status legend

| Badge | Meaning |
| :--- | :--- |
| ✅ | Stable: contract current, gates green, live check recorded for the current build. |
| 🟡 | In refinement: known debt or a pending live/visual pass. |
| 📦 | Manifest: the assembly declares the mod metadata and is discoverable by the loader; the runtime lives in `Aether.UI/NativeSuite`. |
| 🧪 | Not verified in live play for the current build. |
| ⛔ | Reserved / not implemented. Kept only for config compatibility; must never be advertised as working. |

---

## 2. The refinement loop

Every mod, current or future, goes through the same six steps:

1. **Contract** — update this file before writing code: scope, invariants, budget, debt.
2. **Slice** — one user-visible behaviour at a time, small enough to verify in one run.
3. **Measure** — FPS/frame time, allocations, config surface, hotkeys, error paths.
4. **Gate** — add or update an executable check (behaviour or architecture).
5. **Live check** — record the scene, hotkey and expected result in the mod's QA table.
6. **Retro** — add a line to `Log / refinement history`: symptom → root cause → rule.


No mod is promoted to **✅ Stable** without steps 4 and 5.

---

## 3. Architecture status: per-mod target, Wayfinder migrated (2026-11-04)

> **Correction (2026-11-03):** the approved master plan (`aether_master_plan.md`, Phase 5) requires a
> **per-mod runtime** (`Aether.Mods.*`), not a monolithic `Aether.UI/NativeSuite`. Commit `e4ac807`
> consolidated gameplay into NativeSuite to stop duplicate runners and stabilize the build; that is
> **technical debt**, not the final architecture. The loader already supports per-mod assemblies.

**Current split status (2026-11-04):**

- **Wayfinder is a real runtime assembly.** `Aether.Mods.Wayfinder` owns
  `Runtime/WayfinderModule.cs`, the chunk atlas, beacons and PNG export, and registers a real
  `WayfinderEntrypoint : IClientModInitializer`. The shell consumes it through
  `IWayfinderUiModule` and no longer contains its gameplay types.
- **Blueprint is a real runtime assembly.** `Aether.Mods.Blueprint` owns the browser/catalog/recipe card and
  `[Blueprint]` config; the shell sees `IBlueprintUiModule`.
- **The old performance suite is split into five independent mods:** Sodium (chunk render),
  Lithium (physics), EntityCulling (mobs/particles), FerriteCore (memory/F8) and Dusk
  (atmosphere). Each has its own `[assembly: AetherMod]`, entrypoint and config section.
- **Every flagship mod is now a real runtime assembly**, including Lens
  (`Aether.Mods.Lens/Runtime/LensModule.cs`). The shared isometric renderer remains in
  `Aether.UI/Rendering`; the rich inventory tooltip remains a shared `aether_ui` service.
- The shell provides shared UI/services through `SuiteShared`, `AetherUiModuleRegistry`,
  `SafeVoxels` and `PerfMetrics`.
- **Each split mod owns its config section:** the module binds it on the existing suite `.cfg`;
  old values load unchanged.
- **Gates:** `MigratedMod_OwnsRuntimeAndEntrypoint`, `BlueprintMod_OwnsRuntimeAndEntrypoint`,
  `PerformanceMods_OwnRuntimeAndEntrypoints`, `AetherUI_DoesNotContainWayfinderGameplay`,
  `AetherUI_DoesNotContainBlueprintGameplay`, `AetherUI_DoesNotContainPerformanceGameplay`,
  `AllModAssemblies_HaveRuntimeEntrypoints`, `NativeUi_KeepsTheFullPortedSuiteAcrossAssemblies`.

Why the temporary consolidation happened:

- The 540 KB `Aether.UI/NativeSuite` is the working, native port (R3). Splitting it per mod would
  require extracting shared UI/theme/rendering first and carries a high regression risk.
- The flagship assemblies' parallel `*RunnerComponent` classes were never instantiated. Their
  duplicate engines/catalogs were dead code and a trap for future fixes.
- One runtime means one place to verify visual/input/modal behaviour and one place to measure. The
  mod health panel (next pillar) can measure a single implementation instead of guessing which copy
  is live.

**Consequences (current):**

- The suite `.cfg` (`com.ultimaibrahim.blockstory.suite.cfg`) remains the file, but Wayfinder binds
  only its own `[Wayfinder]` section. The other mods still bind their sections from the shell until
  they are split.
- The F10 catalog groups each suite panel under the real assembly that owns it
  (`aether_lens`, `aether_wayfinder`, `aether_blueprint`, `aether_sodium`, `aether_ui`). A panel whose
  owning assembly is not loaded is hidden instead of showing null config entries.
- Adding a new gameplay mod means: (a) create the assembly with `[assembly: AetherMod]` and an
  `IClientModInitializer`/`IModInitializer`, (b) implement `IAetherUiModule` and register it with
  `AetherUiModuleRegistry`, (c) add its contract and gates. Do not add a new `NativeSuite` partial.
- **Next split order:** Blueprint → Sodium/Dusk → Lens; then decide whether UI/HUD/Keybinds/Mod Health
  stay in the `aether_ui` runtime.

### Runtime ownership matrix (2026-11-04)

| Concern | Canonical runtime | Assembly | Removed dead copy |
| :--- | :--- | :--- | :--- |
| Lens WAILA (rich tooltip stays shared) | `Aether.Mods.Lens/Runtime/LensModule.cs` | `aether_lens` (**migrated**) | `LensRunnerComponent`, `LensInventoryTooltip`, `IsometricBlockRenderer` |
| Wayfinder + waypoints | `Aether.Mods.Wayfinder/Runtime/*.cs` | `aether_wayfinder` (**migrated**) | `WayfinderRunnerComponent`, second `WayfinderEngine`, `WaypointManager` |
| Blueprint browser + recipe card | `Aether.Mods.Blueprint/Runtime/BlueprintModule.cs` | `aether_blueprint` (**migrated**) | `BlueprintRunnerComponent`, `BlueprintCatalog`, `BlueprintIconCache`, `BlueprintRecipeScraper` |
| Dusk / atmosphere | `Aether.Mods.Dusk/Runtime/DuskModule.cs` | `aether_dusk` (**migrated**) | `DuskEngine` |
| HUD layout + keybind manager | `Aether.UI/NativeSuite/HudCustomizerModule.cs` + `KeybindManagerModule.cs` | `aether_ui` | — |
| Mod Health dashboard | `Aether.UI/NativeSuite/ModHealthModule.cs` | `aether_ui` | — |
| Framework, loader, crash guard | `Aether.Loader`, `Aether.Core` | `aether` | — |

The old configuration JSON files (`aether/aether_lens.json`, etc.) may still exist in a user's
`BepInEx/config/aether/` folder; they are inert and can be deleted. The suite `.cfg` is the only file
that changes behaviour.

---

## 4. Index

| Mod | ID | Version | Assembly | Runtime owner | Status | Contract |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Aether Core | `aether` | 1.0.0 | `Aether.Loader` (+ Core/API/Registry/Compat) | Loader | ✅ | [aether.md](aether.md) |
| Aether UI & Native Suite | `aether_ui` | 1.0.0 | `Aether.UI` | NativeSuite | 🟡🧪 | [aether_ui.md](aether_ui.md) |
| Lens | `aether_lens` | 1.0.0 | `Aether.Mods.Lens` | **own assembly** | 🟡🧪 | [aether_lens.md](aether_lens.md) |
| Aether Wayfinder | `aether_wayfinder` | 1.0.0 | `Aether.Mods.Wayfinder` | **own assembly** | 🟡🧪 | [aether_wayfinder.md](aether_wayfinder.md) |
| Aether Blueprint | `aether_blueprint` | 1.0.0 | `Aether.Mods.Blueprint` | **own assembly** | 🟡🧪 | [aether_blueprint.md](aether_blueprint.md) |
| Aether Dusk | `aether_dusk` | 1.0.0 | `Aether.Mods.Dusk` | **own assembly** | 🟡🧪 | [aether_dusk.md](aether_dusk.md) |

---

## 5. Definition of done (per mod)

A change is done only when all of these are true:

- [ ] Scope and invariants in the contract match the code.
- [ ] `dotnet build Aether.sln -c Release` → 0 warnings, 0 errors.
- [ ] No new direct `world[x,y,z]`, no `Start()` on `AetherBootstrap`, no `autoSyncTransforms = false`.
- [ ] Config keys added/changed are documented in the contract and keep old files loading.
- [ ] Hotkeys are registered in the Keybind Manager and listed in the contract.
- [ ] The mod's live-check row is updated for the current build.
- [ ] Known debt is either fixed or explicitly listed with a next action.

---

## 6. Contract template

Copy [`_TEMPLATE.md`](_TEMPLATE.md) for every new mod. Keep the section order stable so contracts can
be diffed and reviewed quickly.
