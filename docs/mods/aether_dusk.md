# Aether Dusk — Mod Contract

| Field | Value |
| :--- | :--- |
| **ID** | `dev.ultimaibrahim.aether.dusk` (BepInEx GUID; was `aether_dusk`) |
| **Version** | `1.1.0` |
| **Author** | Ibrahim García |
| **Assembly** | `Aether.Mods.Dusk.dll` (+ `Aether.Nano.dll`) |
| **Status** | 🟢 runs with Aether and with BepInEx alone (checked 2026-11-12, Block Story 14.5.4) |
| **Canonical runtime** | `src/Aether.Mods/Aether.Mods.Dusk/Runtime/DuskModule.cs` |
| **Entrypoint** | `DuskPlugin : Aether.Nano.NanoMod` (`[BepInPlugin]`, `[BepInDependency(Nano.Guid)]`) |
| **Settings page** | `NanoPages.Register`: inline in Aether's F10 menu, or Nano's own "Mods" button |
| **Last reviewed** | 2026-11-12 |

Since 1.1.0 Dusk is the first official mod on **Aether Nano**: it no longer needs Aether. It applies its own Harmony patches
(`PlayerLight`, `DayTimeManager`; up to 1.0.x they were never applied, because only Aether.UI's assembly was patched), and
reads "underground" from Nano, so caves darken without Wayfinder installed.

## 1. Scope

Independent lighting/atmosphere engine, separated from Sodium as requested:

- True matte terrain (removes plastic specular highlights).
- Underground ambient/sunlight suppression and cave darkness.
- Zero-ambient player floor fix.
- Dynamic handheld torchlight (torch/candle/lantern/fire weapon).
- Dusk presets and sliders; Harmony patches for `PlayerLight` / `DayTimeManager`.

## 2. Configuration

| Section | Keys |
| :--- | :--- |
| `[Dusk]` in `BepInEx/config/dev.ultimaibrahim.aether.dusk.cfg` (carried over once from `com.ultimaibrahim.blockstory.suite.cfg`) | `Preset`, `CaveDarknessDepth`, `BlockMatteAntiGloss`, `MinAmbientPlayerLight`, `SunlightUndergroundSuppression`, `AmbientFloorSuppression`, `DynamicHandTorchlight`, `DynamicTorchIntensity`, `DynamicTorchRange`, `WarmFirelightColor` |

## 3. Invariants

- On the surface it never writes `RenderSettings`; ambient is only modulated underground.
- `Vanilla` preset touches nothing.
- Patches are installed once by the shell's Harmony instance (no duplicate patches).
- No `MonoBehaviour`; the module is a plain class registered with `AetherUiModuleRegistry`.
- Material/shader scans are throttled; no per-frame `FindObjectsOfType`.

## 4. QA gates

| Gate | Kind | Check |
| :--- | :--- | :--- |
| Own runtime + entrypoint | Automated | `ArchitectureTests.PerformanceMods_OwnRuntimeAndEntrypoints` |
| No gameplay in shell | Automated | `ArchitectureTests.AetherUI_DoesNotContainPerformanceGameplay` |
| Live self-test | Live | F10 → Dusk → PASS with Torch/Candle match and preset value |
| Live visual | Live | Cave is dark; torches light walls; surface lighting unchanged with `Vanilla` |
