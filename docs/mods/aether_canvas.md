# Canvas — Mod Contract

| Field | Value |
| :--- | :--- |
| **ID** | `dev.ultimaibrahim.aether.canvas` (`aether_canvas`) |
| **Version** | `1.1.1` |
| **Author** | Ibrahim García |
| **Assembly** | `Aether.Mods.Canvas.dll` (+ `Aether.Nano.dll`) |
| **Status** | 🟢 built on Aether Nano, standalone or with Aether |
| **Canonical runtime** | `src/Aether.Mods/Aether.Mods.Canvas/Runtime/CanvasModule.cs` |
| **Entrypoint** | `CanvasPlugin : Aether.Nano.NanoMod` (`[BepInDependency(Nano.Guid)]`) |
| **Host contract** | Nano: `NanoPages`, `NanoPanels`, `NanoKeys`, `NanoHud` (`All`, `OpenCanvasEditor`, `ExternalLayoutActive`) |
| **Last reviewed** | 2026-11-19 |

## 1. Scope

Dedicated WYSIWYG HUD layout editor for Block Story and its mod ecosystem:

- Detects and repositions both **Block Story's native HUD** (`Stats`, `Diamonds`, `Menu Buttons`, `Action Buttons`) and **every mod HUD element registered in `NanoHud.All`** (Aether Lens, Aether Wayfinder, and any third-party Nano mod).
- **Live WYSIWYG editing:** pauses the game (`Inventory.isPaused` + `Time.timeScale = 0`), hides Block Story's pause menu (`settingsCamera` / `mainSettingsButton`) temporarily, applies a light translucent scrim (~22%), and renders the real native HUD and real mod previews (`NanoHudElement.DrawPreview`) completely unobstructed inside thin transparent action frames.
- **Draggable floating inspector window:** lists detected native & mod elements, lets the player adjust scale/size, anchor, quick presets (`ModernModpack`, `CompactLeft`, `BottomHUD`, `VanillaClassic`) and per-element or full reset.
- **Non-destructive vanilla layout:** remembers every native element's exact vanilla state before touching it and restores it cleanly when switching to `VanillaClassic` or resetting.

## 2. Hotkeys

| Action | Default | Owner |
| :--- | :--- | :--- |
| Open / Close HUD Editor | `F9` | `CanvasPlugin` (`OpenEditorKey`) |
| Nudge selected element | `Arrow keys` (+ `Shift` for 10 px) | `CanvasModule` (while editor is open) |
| Save & Exit editor | `Esc` / `F9` | `CanvasPlugin` |

## 3. Invariants

- Never leaves `Time.timeScale == 0` or `settingsCamera` hidden after closing the editor or changing scenes.
- Never modifies vanilla HUD elements when `Preset == VanillaClassic`, or elements that do not collide with active mods in `ModernModpack`.
- Zero dependency on `Aether.UI`, `Aether.Loader`, `Aether.Core`, `Aether.API`, or `Aether.Registry` — runs with BepInEx + `Aether.Nano.dll` alone.
