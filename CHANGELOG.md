# Changelog

All notable changes are listed here, newest first. The project follows [Semantic Versioning](https://semver.org); while it is `0.x-alpha`, APIs and mod contracts may still change.

## Unreleased

## 1.2.1-beta

- The F10 menu (and Mod Health) also lists the mods BepInEx loads from `BepInEx/plugins`, which is where the Catalog installs them. Before, only mods in `Aether/Mods` were listed, so mods installed from the Catalog worked but did not show up.
- All components move to 1.2.1-beta. The four official mods (1.1.1) and Aether Nano (1.0.1) do not change.

## 1.2.0-beta

- The compatibility layer for another mod loader (the `Aether.Compat.*` assemblies) is removed, together with everything built around it: the related CLI commands, Monitor tab and config, and the list Aether used to download. Updating removes its files.
- Aether Nano only shows its pages in Aether's menu or in its own "Mods" button.
- All components move to 1.2.0-beta; Aether Nano to 1.0.1. The four official mods move to 1.1.1 (rebuilt on Nano 1.0.1, no other changes).

## 1.1.2-beta

- Installing a mod that needs **Aether Nano** from the Catalog tab (or `aether lattice install`) no longer stops with "nano: No installable .NET assemblies found in the package". The copy of Nano that Aether ships counts as installed, so it is not downloaded, and a package that only holds files Aether already has is skipped instead of cancelling the whole install.
- The Catalog does not offer to update Aether Nano on its own: it is updated with Aether.
- "Add mod…" and `aether add` say that Aether already includes Nano when given only `Aether.Nano.dll`.

## 1.1.1-beta

- The Lattice catalog is checked with its new signing key (`t51y3GrJ…`); the previous key was retired. Aether 1.1.0-beta no longer accepts the catalog, so the Catalog tab in Aether Monitor needs this version.
- The four official mods (1.1.0) and Aether Nano (1.0.0) are unchanged: this release carries the same files as 1.1.0-beta.

## 1.1.0-beta

The four official mods (Dusk, Lens, Blueprint, Wayfinder, now 1.1.0) are plain BepInEx plugins on **Aether Nano** and run without Aether: with Aether or with BepInEx alone. Each mod zip carries `Aether.Nano.dll`, and Nano is also published on its own. Aether stays as the manager (Monitor, doctor, Lattice).

- **Aether looks like Aether inside the game** (same identity as the website): ember on warm ink, Gloock for names, Schibsted Grotesk for text, JetBrains Mono for versions and keys, and the open cube. The game's wood menus stay the game's.
  - The menu badge is now a seal: cube, "aether", version, mod count and `F10`, on one line. Clicking it still opens the menu.
  - The F10 and F7 headers (an ink plate on the wood) and the crash guard use the brand; the violet "Atelier" colours and the lime titles are gone.
  - Aether Monitor and the installer embed the same fonts.
  - Nano carries the fonts (Latin subsets, about 250 KB) and loads each on first use: `NanoBrand` gives mods the palette, the fonts, the cube and Aether's card.
- **Origin badges in the F10 list:** "By Aether" for Aether's own mods, "Nano" for mods built on Aether Nano. "By Aether" is not a name match: the official mods are strong-named with Aether's key and the menu checks the public key token (`ModOrigin`), so a copied name or GUID cannot claim it. Builds without the key (forks) show as Nano. Nano itself stays unsigned, so mods built against it keep loading.
- **Mod keys in the game's own keybinding window** (Pause → Controls → Edit keybindings), like Fabric's key mappings: `NanoKeys.Register` adds a header per mod and a row per key after the game's rows. Rebinding there writes the mod's config, and "Reset All" resets mod keys too. Wayfinder, Lens and Blueprint register theirs, and `aether new` shows how. With Aether, F7 still replaces that window, now with the same key names.
- **Aether Nano is now a DLL** (`Aether.Nano.dll`, about 300 KB with its brand fonts), the one dependency of the official mods and of new mods (`aether new` writes one): a plain BepInEx plugin with no Aether dependency. It gives mods the game's look (wood panels and buttons, Zantroke font, text styles), shared game state (camera, world, "underground"), safe voxel reads, a mod base class that never runs at boot, and settings pages that show up in Aether's F10 menu, or in a "Mods" button of its own without Aether. It ships with Aether's UI package and on its own (for Lattice); Aether.UI now builds on it. The older compiled-in Nano (`src/Aether.Nano.Source`) still builds but is obsolete.
- **Aether Lens 1.1.0** is a Nano mod too: it draws its card and handles F4 itself, runs with BepInEx alone, and keeps working with Aether's HUD editor and owner-mod line. Nano gained what it needed, for every mod: items and icons, UI themes, the block palette, HUD insets and movable cards, owner lookups (filled by Aether) and the game's font (Aether's own menus now get it from Nano too, not from Lens).
- **Aether Wayfinder 1.1.0** is a Nano mod too, so **all four official mods now run without Aether** (only `Aether.Nano.dll`). Wayfinder draws its minimap, beacons and map, handles its keys, and finds caves, interiors and nearby mobs itself. Nano gained `NanoInput` (taking the game's input for a full-screen window, shared by count with Aether's menus, plus the game's cursor), `NanoPerf`, more text styles and HUD hints. Aether's "HUD & Keybinds" page keeps the HUD presets and the diamonds/top-buttons toggles. `aether doctor` now only requires `Aether.Nano.dll` for the official mods.
- **Aether Blueprint 1.1.0** is a Nano mod too: catalog and recipe card on Nano; Aether respects its panel through `NanoPanels`. Nano also gained themed frames, inventory slots, menu text styles and the item under the cursor. Nano's text styles use the game's font only where Aether's menu did (titles, tool and author lines), so tab labels fit again.
- **Aether Dusk 1.1.0** is the first official mod on Nano: it runs with Aether or with BepInEx alone (its zip carries `Aether.Nano.dll`). Fixes on the way: its Harmony patches (handheld light, sky in caves) are now applied (they never were), and caves darken without Wayfinder installed. Settings carry over from the old file.

- `aether doctor` and the loader's report no longer flag each official mod as clashing with itself: the old suite file's copy of a section the mod already adopted is ignored.
- The mod zips are named with the mods' own version (`aether-dusk-1.1.0.zip`), not the loader's; `package.sh` stops if a plugin declares another one.
- All components move to 1.1.0-beta (the official mods to 1.1.0, Aether Nano starts at 1.0.0).

## 1.0.4-beta

- Linux: when Steam does not start Block Story through BepInEx yet, the installer's last screen shows the launch option `./run_bepinex.sh %command%` with a Copy button.
- Linux: the download page leads with the full package. The light one needs .NET 9 and, without it, does not open at all (Windows shows a dialog instead).
- All components move to 1.0.4-beta.

## 1.0.3-beta

- License changed from MIT to Apache-2.0 (adds an explicit patent grant; `NOTICE` ships in every package). Versions up to 1.0.2-beta stay under MIT.
- Aether Monitor no longer offers the version you already have: `Aether.Loader.dll` now carries the real Aether version (it said 1.0.0), taken from one place (`Directory.Build.props`).
- Catalog updates now show for official mods added by hand (a dropped `.dll`, "Add mod…" or an older release), not only for mods installed from the Catalog. `aether doctor` also checks those against Lattice revocations.
- Installing from the Catalog no longer says a mod "also needs Aether… install it yourself".
- "Add mod…" and `aether add` put BepInEx plugins in `Aether/Mods` by default, where they are known to work; `--no-hosted` keeps the old behaviour.
- All components move to 1.0.3-beta.

## 1.0.2-beta

- A `.dll` in `Aether/Mods` that is neither an Aether mod nor a BepInEx plugin now leaves a note in the loader report instead of being skipped silently.
- All components move to 1.0.2-beta.

## 1.0.1-beta

- Official mods are now published as a single `.dll` (`Aether.Mods.<Name>.dll`): download it and drop it into `Aether/Mods`. The `.zip` packages stay in the release too.
- All components move to 1.0.1-beta.

## 1.0.0-beta

First beta (the loader, Aether Monitor, the installer and the four official mods are all 1.0.0-beta).

- The installer window is light, with Aether's identity; Aether Monitor offers an optional desktop shortcut and shows where it was installed.
- Terms of use and a privacy notice on the website, and a short notice in the installer (no data is collected; the window only talks to GitHub and Lattice).
- Version numbers: Aether, the Monitor and the official mods move to 1.0.0-beta. Mods that declare an exact Aether version (`aether@0.1.1-alpha`) must update the range; `aether@>=0.1.0-alpha` keeps working.

## 0.1.1-alpha

- The installer window has Aether's identity (icon, colors), a step-by-step log and a real progress bar while BepInEx downloads.
- Two downloads per system: **light** (needs the free .NET 9 runtime, small) and **full** (carries its own runtime). The command line tool is now a separate, optional download (`aether-cli-<version>-<system>.zip`).
- Aether Monitor: **Add mod…** (installs a .zip or .dll into the right folder) and a **Catalog** tab with the verified Lattice catalog, install and update buttons.
- Update notices: Aether Monitor tells you when a newer Aether release exists (GitHub) and can fetch it, checking its SHA-256 first. Mods installed from Lattice can be updated (`aether lattice update` too). The game itself never uses the network.
- Windows: software rendering, so ANGLE is no longer shipped.

## 0.1.0-alpha

First alpha: the loader, four official mods (Lens, Wayfinder, Blueprint, Dusk), and the keybind manager. The installer installs only the loader; official mods are separate zips. Tested on Linux and Windows 11.

`aether lattice install <mod>` installs from the Lattice catalog: the signed index (Ed25519) and revocation list are verified, required mods are installed first, and every download is checked against its SHA-256 before anything is copied. Without a revocation list checked in the last 24 h, installs are blocked. `aether lattice check` reports installed mods that were revoked later.
