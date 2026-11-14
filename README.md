<p align="center"><img src="docs/img/banner.png" alt="Aether: a mod loader for Block Story" width="100%"></p>

<p align="center">
<a href="https://github.com/ultimaibrahim/aether-loader/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/ultimaibrahim/aether-loader?include_prereleases&sort=semver&color=C2500A&label=release"></a>
<a href="LICENSE"><img alt="License: Apache-2.0" src="https://img.shields.io/github/license/ultimaibrahim/aether-loader?color=555"></a>
<img alt=".NET 9" src="https://img.shields.io/badge/.NET-9-555">
<a href="https://aether.ultimaibrahim.dev"><img alt="Website" src="https://img.shields.io/badge/web-aether.ultimaibrahim.dev-555"></a>
<a href="https://lattice.ultimaibrahim.dev"><img alt="Mods on Lattice" src="https://img.shields.io/badge/mods-Lattice-555"></a>
</p>

# Aether

A mod loader and a set of mods for [Block Story](https://store.steampowered.com/app/270110/) (Unity 6, BepInEx 5).
Aether loads mods from `<game>/Aether/Mods`, gives them a small API (events, registries, config, UI helpers) and ships
four mods of its own: an inspector, a map, a recipe browser and a lighting mod.

> **`1.1.0-beta`.** Tested on Linux and Windows 11. On Windows, SmartScreen may warn because the executables are not code-signed yet: choose “More info”, then “Run anyway”, after checking the SHA-256 in `SHA256SUMS.txt`.
> APIs and mod contracts can still change.

## What is in it

| Mod | What it does |
| :--- | :--- |
| **Lens** | Card for the block or creature you aim at: name, owner mod, health, the tool that breaks it. |
| **Wayfinder** | Corner map and fullscreen map built from 32x32-block chunks, cave/slice layers, waypoints, death points, PNG export. |
| **Blueprint** | Item catalog and recipe browser inside the inventory: search, recipe card, "used in", bookmarks. |
| **Dusk** | Matte terrain, darker caves, handheld torchlight, presets. |

Plus a keybind manager (F7) that lists vanilla, Aether and other BepInEx plugins' keys together and flags clashes,
and a **Mods** tab in the pause menu.

Performance mods are not part of the releases: they will ship only once each one is measured to actually help.

Each mod is its own assembly with its own config section and contract in [`docs/mods/`](docs/mods/README.md). All four
are built on **Aether Nano**, a small shared library that is a plain BepInEx plugin, so each mod also runs without Aether
(with BepInEx alone); each mod zip carries `Aether.Nano.dll`.

## Install

You need Block Story from Steam. The installer fetches BepInEx 5.4.23.6 if it is missing (SHA-256 checked).

Download the zip for your system from the [Download page](https://aether.ultimaibrahim.dev/descargar/), unzip it and open
`aether-installer` (`aether-installer.exe` on Windows): pick the game folder and press Install. The window shows each step.
For a terminal there is a separate, optional download, `aether-cli-<version>-<system>.zip` (`./install.sh --dry-run` previews).

On Linux set the Steam launch option to `./run_bepinex.sh %command%`. (Experimental: `aether install --with-launcher` also writes `aether-launch`, which runs `aether preflight` first and refuses to start the game if an installed Aether mod cannot load. Not enabled by default; see `docs/plans/BLAME.md`.) After a launch,
`BepInEx/config/aether_conflicts.txt` lists duplicate ids, missing dependencies and Harmony patch overlaps.

The installer puts **only the loader** in your game. Mods are your choice: Aether's own are separate downloads
(`aether-lens-<version>.zip`, and so on), and any mod, official or not, is added from Aether Monitor (**Add mod…**) or, from a terminal, with `./aether add SomeMod.zip`.
It reads the DLL and puts it in `Aether/Mods` or `BepInEx/plugins` depending on what it is, warning about missing
dependencies first.

From a checkout you can install some of the official mods straight from the build: `./scripts/install.sh --mods lens,dusk`
(or `--with-mods` for all four). Re-running the installer keeps an already-installed official mod in step with the loader. The UI package (`Aether.UI.dll`: F10 menu, HUD, widgets the official mods use) installs by default but is separate from the core; `--no-ui` skips it, and `aether doctor` reports it on its own.

## Keys

| Key | Action |
| :--- | :--- |
| `F10` | Mod menu: list, enable/disable, per-mod settings, Mod Health |
| `F7` | Keybind manager (rebind any key, see conflicts) |
| `F9` | HUD drag editor |
| `F4` | Lens on/off |
| `M` / `N` | Wayfinder fullscreen / corner map on-off |
| `L` / `B` | Map layer / quick waypoint |
| `O` | Blueprint browser (`R` recipe, `U` uses, `A` bookmark while hovering an item) |

All of them can be changed in the game's own keybinding window (Pause → Controls → Edit keybindings), or in F7 with Aether.

## Layout

```
src/Aether.API            contract for third-party mods (small, SemVer)
src/Aether.Core           lifecycle, event bus, config, shared helpers
src/Aether.Loader         BepInEx plugin: discovery, dependencies, hosting
src/Aether.Registry       block / item / recipe / mob registries
src/Aether.UI             shared UI shell (menu, HUD, keybind manager)
src/Aether.Mods/*         the four mods above (+ a QA-only kit)
tools/Aether.CLI          install, add, doctor, new
site/                     the project website (static; see site/README.md)
docs/                     contracts, guides, known traps, QA notes
```

## Known limits

- One platform tested. Single maintainer.
- A few crashes and hangs belong to the game itself; they are recorded in [`docs/KNOWN_TRAPS.md`](docs/KNOWN_TRAPS.md)
  so they are not mistaken for Aether bugs.

## License

[Apache-2.0](LICENSE). Written by Ibrahim Garcia ([@ultimaibrahim](https://github.com/ultimaibrahim)).

