# Aether Blueprint

Item catalog and recipe browser inside the inventory: search, recipe, “used in” and bookmarks.

## Requirements

[Aether Nano](https://github.com/ultimaibrahim/aether-loader/tree/main/src/Aether.Nano) (`Aether.Nano.dll`), a small shared library. That is all: this mod runs with the Aether loader or with BepInEx alone. Aether's install already includes Nano.

## Install

- **With Aether:** open Aether Monitor and install it from the **Catalog** tab (it checks the signature and SHA-256), or press **Add mod…** and pick `aether-blueprint-1.1.1.zip`. Settings are in the F10 menu.
- **Without Aether:** download `aether-blueprint-1.1.1.zip` from the release and copy both DLLs (`Aether.Mods.Blueprint.dll` and `Aether.Nano.dll`) into `<game>/BepInEx/plugins`. Settings are in the "Mods" button Nano adds to the main menu.

Then start Block Story.

## Keys

| Key | What it does |
| :--- | :--- |
| `O` | Open the catalog |
| `R` | Recipe (over an item) |
| `U` | Uses (over an item) |
| `A` | Bookmark (over an item) |

Keys can be changed in the game's own keybinding window (Pause → Controls → Edit keybindings), or in Aether's keybind manager (F7) when Aether is installed.

## Source and license

Source code is public in this repository (`src/Aether.Mods/Aether.Mods.Blueprint`), Apache-2.0 license. This mod does not change multiplayer rules and gives no advantage over other players.
