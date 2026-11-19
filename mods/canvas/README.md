# Aether Canvas

Live WYSIWYG HUD layout editor for Block Story: move and scale both the game's native HUD and any mod built on Aether Nano (Lens, Wayfinder, and third-party mods).

## Requirements

[Aether Nano](https://github.com/ultimaibrahim/aether-loader/tree/main/src/Aether.Nano) (`Aether.Nano.dll`). Runs with the Aether loader or with BepInEx alone.

## Install

- **With Aether:** open Aether Monitor and install it from the **Catalog**, or drop `Aether.Mods.Canvas.dll` into `<game>/Aether/Mods`. Open the editor anytime with **F9** or from F10 → **Edit HUD**.
- **Without Aether:** copy `Aether.Mods.Canvas.dll` and `Aether.Nano.dll` into `<game>/BepInEx/plugins`.

## Keys

| Key | What it does |
| :--- | :--- |
| `F9` | Open / close the live HUD Editor |
| `Scroll wheel` | Scale or resize the hovered HUD element |
| `Arrow keys` | Nudge the selected element (hold `Shift` for 10 px) |
| `Esc` | Save and exit the editor |

## Source and license

Source code is public in this repository (`src/Aether.Mods/Aether.Mods.Canvas`), Apache-2.0 license.
