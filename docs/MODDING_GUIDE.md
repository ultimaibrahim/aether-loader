# Aether Modding Guide

> **Audience:** anyone building a Block Story mod on Aether.
> **Status:** current for the per-mod architecture (2026-11-04).
> **Companion doc:** [`KNOWN_TRAPS.md`](KNOWN_TRAPS.md).

---

## 1. The 60-second version

1. Create a .NET Standard 2.1 class library.
2. Add `[assembly: AetherMod("my_mod", "1.0.0", ...)]`.
3. Add a class implementing `IModInitializer` and/or `IClientModInitializer`.
4. Build it, then copy **only your DLL** (and any private dependencies) to
   `<game>/Aether/Mods/`.
5. Restart Block Story. Aether discovers it, resolves dependencies and runs its entrypoint in
   frame 0. No JSON manifest is required.

```csharp
using Aether.Loader.Discovery;
using Aether.Loader.Entrypoints;

[assembly: AetherMod("my_mod", "1.0.0",
    Name = "My Mod",
    Author = "Your Name",
    Description = "What it does.",
    Depends = "aether_ui@1.1.0-beta")]

namespace MyMod
{
    public sealed class MyEntrypoint : IClientModInitializer
    {
        public void OnInitializeClient()
        {
            // Safe to touch Unity objects here: frame 0, not Awake/Start.
        }
    }
}
```

---

## 2. Where files live

| Path | What goes there |
| :--- | :--- |
| `<game>/Aether/Mods/` | **Aether mods** (flagships and third-party). Drag and drop. |
| `<game>/BepInEx/plugins/Aether/` | **Aether core only**: Loader, Core, API, Registry, UI, Nano. |
| `<game>/BepInEx/config/` | BepInEx `.cfg` files (including the suite cfg). |
| `<game>/BepInEx/config/aether/` | `AetherConfig` JSON files, one per mod id. |
| `<game>/BepInEx/LogOutput.log` | BepInEx + Aether log. |
| `<game>/Aether/Logs/<yyyyMMdd-HHmmss>/` | Live copy of `LogOutput.log` and `Player.log` for each session (last 10, `[Logs] KeepSessions`). Survives native crashes. |
| `<game>/BepInEx/crash-reports/` | Aether crash reports. |

The loader scans **`Aether/Mods` first**, then `BepInEx/plugins` for backward compatibility. The same
assembly name is loaded once even if it exists in both places. The core directory is searched first
for dependency resolution, so a mod in `Aether/Mods` can reference `Aether.UI`/`Aether.Core`.

**Custom folder:** set the `AETHER_MODS_PATH` environment variable to an absolute path to change the
mods directory. The CLI installer still deploys to the default `Aether/Mods`; if you use a custom
path, copy mods there yourself.

**Not sure where a downloaded mod goes?** Run `aether add <file.dll|file.zip>`: it inspects the DLL
(without executing it) and routes it. `[AetherMod]` assemblies and their shared libraries go to
`Aether/Mods`; plain `[BepInPlugin]` assemblies go to `BepInEx/plugins`. `aether doctor` also flags
files that sit in the wrong folder.

### Dropping plain BepInEx plugins into `Aether/Mods`

BepInEx only scans `BepInEx/plugins`, and on this game (Unity `6000.5.6f1`) plugins started from there run
inside the early boot window. A plugin that touches Unity from `Awake` (even `Time.frameCount`), such as
ConfigurationManager, **crashes the game with a native SIGSEGV** (`Chainloader.Start -> AddComponent`).
Aether avoids that window for its own mods and does the same for ordinary BepInEx plugins (**on by default**;
set it to `false` to ignore them):

```ini
# BepInEx/config/dev.ultimaibrahim.aether.cfg
[Hosting]
HostBepInExPlugins = true   # default
```

With this on, every `[BepInPlugin]` DLL in `Aether/Mods` is started by Aether on **frame 0**, registered in
`Chainloader.PluginInfos` like BepInEx does, and ordered by `[BepInDependency]` (hard/soft, with minimum
versions). Rejected plugins are listed in `BepInEx/config/aether_conflicts.txt`: missing hard dependency,
`[BepInIncompatibility]`, dependency cycle, a GUID already loaded by BepInEx, or a duplicate GUID. With the
option off, such plugins are reported as "will NOT run" instead of being silently ignored.

Trade-offs: their `Awake` runs on frame 0 instead of at boot, and a plugin that throws in `Awake` is isolated
(logged in `Player.log`, the others keep running). `aether add <file>` and Monitor's **Add mod…** put plugins there
(`--no-hosted` sends them to `BepInEx/plugins`).

> Do **not** put Aether mods in `BepInEx/plugins/Aether` any more; that folder is the core runtime.
> Do **not** put DLLs for other loaders (not BepInEx 5 plugins, not Aether mods) in `Aether/Mods`; Aether cannot start them.

---

## 3. Project setup

### 3.1 References

A mod references the Aether DLLs that the installer puts in `<game>/BepInEx/plugins/Aether/`, never a copy of this repository:

| Assembly | Why |
| :--- | :--- |
| `Aether.Loader` | `[assembly: AetherMod]`, `IModInitializer`, `IClientModInitializer` |
| `Aether.Core` | `AetherConfig`, events, crash guard, mapping/persistence |
| `Aether.API` | Public contracts (`IUIService`) |
| `Aether.Registry` | `Identifier`, `DynamicBlockRegistry` |
| `Aether.UI` | Shared theme/widgets/`SuiteShared`/`SafeVoxels`/`IAetherUiModule` |
| `Assembly-CSharp` + Unity modules | The game APIs you use (from `<game>/<name>_Data/Managed`) |

Reference every one of them with `Private=false`; never copy them into your mod.

### 3.2 Scaffold

```bash
aether new MyMod                     # from a checkout: ./scripts/aether-cli.sh new MyMod
cd MyMod
dotnet build MyMod.csproj -c Release -p:InstallToGame=true
```

By default `aether new` writes a **Nano mod**: a plain BepInEx plugin (`MyMod.csproj`, `Mod.cs`, `README.md`) that
references `Aether.Nano.dll` (about 300 KB, most of it the brand fonts) and nothing else of Aether. It loads with BepInEx alone or with Aether, and its
settings page appears in Aether's menu or in Nano's own. The project finds `Aether.Nano.dll` in
`BepInEx/plugins/Aether` (Aether installed) or loose in `BepInEx/plugins`. If it finds your Block Story folder (or you
pass `--game-dir <path>`) the project already points at it; otherwise set `GameDir` in the `.csproj`, or build with
`-p:GameDir=<game>`. `-p:InstallToGame=true` copies the DLL into `<game>/BepInEx/plugins`.

Need blocks, items or Aether's UI? `aether new MyMod --aether` writes the Aether-API template instead: it references the
Aether DLLs (`AetherDir`, default `<game>/BepInEx/plugins/Aether`), needs Aether to load, and `InstallToGame` copies the DLL
into `<game>/Aether/Mods`.

`aether new` refuses to overwrite an existing project unless you pass `--force`.

`[AetherMod]` is only valid at assembly level (`[assembly: AetherMod(...)]`): that is the only place the loader looks, so
the compiler rejects it on a class instead of letting the mod silently never load.

---

## 3b. Two ways to write a mod

| | Runs without Aether | What it gets |
| :--- | :--- | :--- |
| **Aether mod** (`[assembly: AetherMod]`) | No | The full Aether API and UI suite. |
| **Nano mod** (BepInEx plugin on `Aether.Nano.dll`) | Yes, with `Aether.Nano.dll` next to it | The game's look, shared state and a settings page shown in any mod menu. The official mods use this; it is what `aether new` writes. |

The older compiled-in Nano (`src/Aether.Nano.Source`) still builds but is obsolete: use the DLL.

A Nano mod in short:

```csharp
[BepInPlugin("you.mymod", "My Mod", "1.0.0")]
[BepInDependency(Aether.Nano.Nano.Guid)]
public sealed class MyMod : Aether.Nano.NanoMod
{
    protected override void OnReady()   // first frame, never at boot
    {
        var strong = Config.Bind("General", "Strong", true, "Example setting.");
        Aether.Nano.NanoPages.Register(new Aether.Nano.NanoPage
        {
            Id = "you.mymod", Name = "My Mod",
            Draw = (area, y) => { if (Aether.Nano.NanoUi.DrawNativeWoodButton(new Rect(12, y, 260, 32), "Strong: " + strong.Value)) strong.Value = !strong.Value; },
            ContentHeight = 120f,
        });
    }
}
```

Ship `Aether.Nano.dll` next to your DLL for players without Aether (Aether's install already has it). The page appears in
Aether's F10 menu, or in a "Mods" button Nano adds when Aether is not installed.

Look: `Aether.Nano.NanoBrand` has Aether's palette, its fonts (`Display`, `Body`, `BodySemi`, `Mono`; each loads on first use
and is null if it cannot load), the open cube and `DrawCard`. Use it for things that are yours to style; keep the game's wood
(`NanoUi`) where your mod sits inside the game's own menus.

In Aether's F10 list a mod built on Nano shows a "Nano" badge. "By Aether" is reserved for Aether's own mods: they are signed
with Aether's strong-name key and the menu checks it, not the name.

Keys: bind a `ConfigEntry<KeyCode>` and register it with `Aether.Nano.NanoKeys.Register(guid, "My Mod", entry, "Open panel", "Abrir panel")`.
It then shows in the game's own keybinding window (Pause → Controls → Edit keybindings) under a header with your mod's name;
rebinding there, or "Reset All", writes your config entry. Keep reading `entry.Value` as usual. With Aether, F7 replaces that
window and lists the same keys with these labels.
See `src/Aether.Mods/Aether.Mods.Dusk` for a full example.

## 4. Mod anatomy

### 4.1 Discovery attribute

```csharp
[assembly: AetherMod("my_mod", "1.0.0",
    Name = "My Mod",
    Author = "Your Name",
    Description = "Short description.",
    Depends = "aether_ui@1.1.0-beta, aether@>=1.1.0-beta",
    Suggests = "aether_wayfinder@1.1.0",
    LoadAfter = "aether_ui",
    Breaks = "old_mod_id")]
```

- `Depends` is a comma/semicolon list of `id@semver-range`; a missing range means `*`.
- Dependencies are resolved **before** entrypoints run; a missing/incompatible dependency blocks the
  mod and is logged.
- `Suggests` is informational; it does not block loading.
- `LoadAfter` is a soft order: the named mod, if installed, loads first. No version is required.
- `Breaks` refuses to load if any named mod is installed.
- The id must be stable; it is used for config, logs and crash blame.

### 4.2 Entrypoints

| Interface | When it runs | Use it for |
| :--- | :--- | :--- |
| `IPreLaunchEntrypoint` | Earliest safe point | Assembly resolve hooks, low-level patches |
| `IModInitializer` | Mod loading | Registries, blocks/items, recipes |
| `IClientModInitializer` | Frame 0, Unity ready | UI modules, scene objects, Harmony |
| `ISceneModInitializer` | Every scene load | Scene-specific setup |

**R1:** never define `Start()` on `AetherBootstrap`; entrypoints already run after Unity is alive.

### 4.3 UI modules

If your mod draws in the shared suite, implement `IAetherUiModule` and register it:

```csharp
using Aether.UI.Modules;

public sealed class MyUiModule : IAetherUiModule
{
    public string Id => "my_mod";
    public bool IsInitialized { get; private set; }
    public bool IsModalOpen => false;

    public void Initialize()
    {
        if (IsInitialized) return;
        // Bind config, cache state.
        IsInitialized = true;
    }

    public void OnUpdate() { /* per-frame */ }
    public void OnLateUpdate() { }
    public void OnSceneChanged(string sceneName) { }
    public void ForceClose() { }
}
```

```csharp
AetherUiModuleRegistry.Register(new MyUiModule());
```

The shell calls `Initialize` after the ConfigFile exists, then `OnUpdate`/`OnLateUpdate` every frame,
`OnSceneChanged` on scene changes, and treats `IsModalOpen` as an input-capturing modal. A module is a
plain class, not a `MonoBehaviour`; the shell is the single Unity host.

For **custom drawing**, either:
- add a small `MonoBehaviour` runner created from `OnInitializeClient` (never from `Awake`), or
- contribute a new interface beside the built-in ones if the UI is part of the shared shell.

The built-in mods expose specific contracts (`IWayfinderUiModule`, `IBlueprintUiModule`,
`IPerformanceUiModule`, `IDuskUiModule`, `ILensUiModule`). Reuse them only if you are extending
that mod; do not reference another mod's runtime types.

---

## 5. Configuration

Two supported surfaces:

### 5.1 `AetherConfig` JSON (recommended for third-party mods)

```csharp
using Aether.Core.Config;

private static ConfigEntry<bool> _enabled;

_enabled = AetherConfig.Bind("my_mod", "General", "Enabled", true,
    "Enable this mod.");
```

Writes `BepInEx/config/aether/my_mod.json`. Binds are deferred and flushed by the bootstrap, so
binding from an entrypoint is safe.

### 5.2 BepInEx section (what the built-in suite uses)

```csharp
var entry = Config.Bind("my_mod", "Enabled", true, "Enable this mod.");
```

The built-in mods use the suite `.cfg` with namespaced sections (`[Sodium]`, `[Lens]`, ...). Each mod
binds only its own section. Existing values must keep loading; add a migration marker rather than
renaming keys silently.

---

## 6. Assets and rendering

- `AetherResources.LoadTexture(modId, "textures/icon.png")` loads/caches a PNG next to your mod.
- Prefer the game's native `UIAtlas` sprites for UI chrome; do not replace the native suite with
  plain IMGUI boxes.
- IMGUI rules: no allocations in `OnGUI`, upload textures on `Repaint` only, cache styles/fonts.
- Never call `PriorityWorld[x,y,z]` on a possibly unloaded chunk. Use
  `Aether.UI.Voxel.SafeVoxels.GetBlockSafe/GetBlockFast`.
- Keep `Physics.autoSyncTransforms = true` (R4).
- Do not add a `Start()` to early-boot objects; defer to frame 0.

---

## 7. Harmony

- Patch from `IClientModInitializer` (frame 0), not `Awake`.
- Use one `Harmony` instance per assembly id, e.g. `new Harmony("my_mod")`.
- Remove/disable patches cleanly when your master switch is off.
- Do not patch the same method from two copies of your mod; the loader loads one instance per id.

---

## 8. Build, install, drag and drop

```bash
dotnet build MyMod.csproj -c Release
cp bin/Release/netstandard2.1/MyMod.dll "<game>/Aether/Mods/"
```

Restart Block Story. The log should show:

```text
[Aether] Resolved N mods in topological order:
  -> My Mod (my_mod v1.0.0)
```

If it does not appear:

1. Is the DLL a .NET assembly (not native)?
2. Does it have `[assembly: AetherMod(...)]`?
3. Is it under `Aether/Mods` or `BepInEx/plugins`?
4. Check `BepInEx/LogOutput.log` for `Entrypoint ... failed`, `MissingMethodException`,
   `TypeLoadException` or a dependency error.
5. Is the mod built against the same Aether major version?

The installer (`./scripts/install.sh`) deploys the Aether loader and, only if asked (`--mods lens,dusk`, `--with-mods`), its official mods. It is not needed for third-party mods: use `aether add`.

---

## 9. Dependencies and load order

- Declare `Depends` on `aether_ui` if you use `SuiteShared`/`IAetherUiModule`.
- Declare `Depends` on `aether` only if you use a Core feature that is not always present; the core is
  always loaded.
- Avoid depending on another gameplay mod's runtime types. If two mods must cooperate, put the shared
  contract in `Aether.UI`/`Aether.Core` or duplicate a small value type.
- The loader topologically sorts `Depends` + `LoadAfter`; a cycle or a missing dependency aborts
  with a clear log.
- After a launch, the loader writes `BepInEx/config/aether_conflicts.txt` with duplicate ids,
  missing/incompatible dependencies, `LoadAfter` misses, `Breaks` hits and Harmony patch overlaps.
  Run `aether doctor` to print it.
- Built-in mods are installed together; third-party mods should declare the minimum they need.

---

## 10. Testing your mod

- Back up the world/character before destructive tests.
- Test in a creative world, and use a copy of the world and the character you can afford to lose.
- A mod is done only when: build 0/0, tests pass, log clean, and a live check is recorded.

---

## 11. Publishing checklist

- Publish the **complete source code and documentation** of your mod (the game's modding license requires it for every public mod) and tell Big Cube Interactive after you release it.
- No malicious code, and nothing that gives unfair advantages in multiplayer. If your mod changes multiplayer, say clearly which incompatibilities it has.
- No game files, decompiled code, leaked keys or extracted assets.
- README: install path, dependencies, config, hotkeys, uninstall.
- Version the DLL and `[AetherMod]` together.
- Credit Aether, Block Story and any third-party libraries.
- State that AI was used if it was.
- Ship the DLL (and only private dependencies), not the game.

---

## 12. Quick reference

| Task | API |
| :--- | :--- |
| Mod metadata | `[assembly: AetherMod("id","1.0.0", Depends="aether_ui@1.1.0-beta")]` |
| Entry point | `IModInitializer` / `IClientModInitializer` |
| UI lifecycle | `IAetherUiModule`, `AetherUiModuleRegistry.Register` |
| Shared UI | `SuiteShared`, `Aether.UI.Modules` |
| Safe voxels | `SafeVoxels.GetBlockSafe/GetBlockFast` |
| Config JSON | `AetherConfig.Bind("my_mod", "General", "Key", default, "desc")` |
| Config section | `Config.Bind("my_mod", "Key", default, "desc")` |
| Assets | `AetherResources.LoadTexture("my_mod", "textures/icon.png")` |
| Log | `BepInEx/LogOutput.log` |
| Mods folder | `<game>/Aether/Mods` (override: `AETHER_MODS_PATH`) |
