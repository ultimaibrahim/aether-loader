# Aether Lens

A card for the block or creature you aim at: name, source mod, health and the tool that breaks it.

## Requirements

[Aether Nano](https://github.com/ultimaibrahim/aether-loader/tree/main/src/Aether.Nano) (`Aether.Nano.dll`), a small shared library. That is all: this mod runs with the Aether loader or with BepInEx alone. Aether's install already includes Nano.

## Install

- **With Aether:** open Aether Monitor and install it from the **Catalog** tab (it checks the signature and SHA-256), or press **Add mod…** and pick `aether-lens-1.1.1.zip`. Settings are in the F10 menu.
- **Without Aether:** download `aether-lens-1.1.1.zip` from the release and copy both DLLs (`Aether.Mods.Lens.dll` and `Aether.Nano.dll`) into `<game>/BepInEx/plugins`. Settings are in the "Mods" button Nano adds to the main menu.

Then start Block Story.

## Keys

| Key | What it does |
| :--- | :--- |
| `F4` | Turn on or off |

Keys can be changed in the game's own keybinding window (Pause → Controls → Edit keybindings), or in Aether's keybind manager (F7) when Aether is installed.

## Source and license

Source code is public in this repository (`src/Aether.Mods/Aether.Mods.Lens`), Apache-2.0 license. This mod does not change multiplayer rules and gives no advantage over other players.
