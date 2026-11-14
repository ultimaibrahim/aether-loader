# Aether Nano

A small shared library for Block Story mods: a plain BepInEx plugin (`Aether.Nano.dll`) with no dependency on the Aether loader.
The four official mods (Lens, Wayfinder, Blueprint, Dusk) are built on it, and so is every mod that `aether new` creates.

## What it gives a mod

- The game's look (wood panels and buttons, its font) and Aether's own (palette, fonts, the open cube) for IMGUI.
- Shared game state (camera, world, "underground"), safe voxel reads, a mod base class that never runs at boot.
- A settings page that shows up in Aether's F10 menu or in a "Mods" button of its own.
- Keys in the game's own keybinding window (Pause → Controls → Edit keybindings) with `NanoKeys.Register`.

## Install

You rarely install it by hand: every official mod zip carries it, and Aether's install includes it. Without Aether, copy
`Aether.Nano.dll` into `<game>/BepInEx/plugins` once; all Nano mods share it.

## Source and license

Source code is public in this repository (`src/Aether.Nano`), Apache-2.0 license. It embeds Gloock, Schibsted Grotesk and
JetBrains Mono (SIL Open Font License 1.1, texts in `src/Aether.Nano/Brand`).
