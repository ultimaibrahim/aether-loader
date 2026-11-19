# Aether Modding Guide & Forum Reference

> **Audience:** anyone building a Block Story mod (with BepInEx alone, Aether Nano, or the Aether API).
> **Status:** current for **Aether 1.2.5-beta** and **Aether Nano 1.0.3** (2026-11-19).
> **Live Mini Forum & Guide:** [`aether.ultimaibrahim.dev/docs/`](https://aether.ultimaibrahim.dev/docs/) (ES) · [`aether.ultimaibrahim.dev/en/docs/`](https://aether.ultimaibrahim.dev/en/docs/) (EN).
> **Companion doc:** [`KNOWN_TRAPS.md`](KNOWN_TRAPS.md).

---

## I · Getting started

### 1. Rules of the game & Big Cube Interactive modding license

- **Thread slug:** `/docs/rules/` · **Verified on:** `Licencia V2 · 2026` · **Tags:** `#licencia` `#terminos` `#4c` `#fair-play` · **Source:** `Block Story Modding & Community Server License Agreement (© 2026 Big Cube Interactive LLC)`

Before writing your first line of code or posting a snippet in this forum, review the terms of the *Block Story Modding & Community Server License Agreement* (© 2026 **Big Cube Interactive LLC**). Every guide thread, snippet and mod shared here is governed strictly by the official text of the Agreement.

#### 1. What the Agreement grants (§1–§4)

  - **License to create Modifications (§1, §2, §4a):** Grants a non-transferable, non-exclusive, revocable license to create Modifications (any plugin, script, gameplay modification, asset pack, user interface modification, or derivative work) solely for modifying the Game, created using only the official tools and APIs provided by the Licensor unless otherwise authorized (§4a).
  - **Ownership of your original content (§3):** You retain ownership of any original content created as part of a Modification. The Game, SDK, and all associated intellectual property remain the exclusive property of Big Cube Interactive.
  - **Fair play and multiplayer integrity (§6):** Informational and quality-of-life modifications are valid provided they do not cheat, provide unfair gameplay advantages, automate gameplay, modify game memory during multiplayer sessions, or exploit bugs for competitive gain.

#### 2. Five obligations when publishing a mod (§3, §4, §18)

| Clause | Official obligation | How to comply |
| :--- | :--- | :--- |
| **§4b, §4f** | **Public source code and documentation** | The complete source code and documentation for every publicly released Modification must be made publicly available, and the Modification must be used only for modifying the Game. Closed-source DLLs are not allowed. |
| **§4c** | **Notify the Licensor** | You must notify Big Cube Interactive after publicly releasing a Modification. |
| **§4e** | **Disclose multiplayer incompatibilities** | Modifications that alter multiplayer functionality must clearly disclose any incompatibilities with official multiplayer services. |
| **§3** | **Perpetual license to Licensor** | By publicly distributing a Modification for Block Story, you grant Big Cube Interactive a perpetual, worldwide, non-exclusive, royalty-free license to host, archive, distribute, adapt as necessary for compatibility, and include it in official distributions or curated collections while preserving reasonable attribution. |
| **§18** | **Private security reporting** | If you discover a security vulnerability affecting multiplayer functionality, reasonable efforts must be made to report it **privately** to the Licensor before publicly disclosing it. |

#### 3. Express prohibitions under the Agreement (§4d, §6–§9, §14, §16–§17)

> **Forum rule (§16b):** The Agreement prohibits reverse engineering, decompiling, or disassembling the Game except where expressly permitted by law or the Agreement. Never paste decompiled game code in this forum: name only the public method to patch and post your own original code.

  - **No redistribution of the Game or Official Assets (§16a, §16e, §17):** Do not distribute or sell the original Game, SDK, or official artwork, music, textures, models, animations, sounds, dialogue, or other assets outside their intended use within Block Story. Reference local assemblies in your `.csproj` with `<Private>false</Private>`.
  - **No misleading branding (§16c, §16d):** Do not remove copyright notices or use official logos, trademarks, or branding in a manner that suggests official endorsement without written permission.
  - **No cheating or multiplayer disruption (§6a–§6f):** Do not create, distribute, promote, or use software intended to cheat or provide unfair advantages (§6a), automate gameplay (§6b), modify game memory during multiplayer sessions (§6c), exploit bugs (§6d), crash or overload multiplayer servers (§6e), or circumvent bans and moderation systems (§6f).
  - **No malicious code (§4d):** Modifications must not contain malicious code, spyware, ransomware, cryptocurrency miners, hidden backdoors, or intentionally introduced security vulnerabilities.
  - **No real-money trading of virtual items or progress (§14), no impersonation (§8–§9), and no machine-learning training on Game assets or player data without authorization (§7d).**

### 2. Your first mod in 60 seconds: with BepInEx alone or with Aether

- **Thread slug:** `/docs/intro/` · **Verified on:** `BepInEx 5 · Nano 1.0.3` · **Tags:** `#bepinex` `#nano` `#aether-new` `#inicio` · **Source:** `samples/Nano.Sample · src/Aether.Installer/ModScaffold.cs`

This guide is neutral: everything it teaches about how Block Story works inside applies **whether you use Aether or not**. A Block Story mod is a C# class library (`.dll`) compiled for **.NET Standard 2.1**. You can write it **with BepInEx alone** (zero external dependencies) or use **Aether Nano / Aether** if you want ready-made helpers for UI, keybinds and world access.

#### 1. Honest comparison of the three paths

| Path | Dependencies | Where it runs | When to choose it |
| :--- | :--- | :--- | :--- |
| **A · BepInEx alone** (pure `BaseUnityPlugin`) | **None** (only `BepInEx.dll` and the game’s `UnityEngine*.dll`). | Any BepInEx 5 setup (and Aether Loader too). | You want a 100% standalone single-DLL plugin with no external library dependency. |
| **B · With Aether Nano** (`NanoMod`) | `Aether.Nano.dll` (~300 KB). | BepInEx alone (with `Aether.Nano.dll` next to your DLL) or Aether Loader. | You want an in-game settings page, native Controls keybinds, Squircle cards and safe world helpers without requiring players to install Aether. |
| **C · With Aether API** (`[assembly: AetherMod]`) | Aether Loader core. | Aether Loader installations. | You need dynamic block registration (`DynamicBlockRegistry`) or phased loader entrypoints. |

#### 2. Path A · Your first mod using BepInEx alone (without Aether)

All you need is the **.NET SDK** (`dotnet new classlib -f netstandard2.1 -n MyBepInExMod`). The one essential rule on Block Story (Unity `6000.5.6f1`) is **never calling Unity APIs inside `Awake()`**; wait until the first `Update()` (Frame 0):

```csharp
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace MyBepInExMod
{
    [BepInPlugin("dev.yourname.mybepinexmod", "My BepInEx Mod", "1.0.0")]
    public sealed class PurePlugin : BaseUnityPlugin
    {
        private bool _initialized;
        private ConfigEntry<string> _greeting;
        private ConfigEntry<KeyCode> _key;

        private void Update()
        {
            // Initialize on Frame 0 (first Update), never in Awake()
            if (!_initialized)
            {
                _initialized = true;
                _greeting = Config.Bind("General", "Greeting", "Hello from pure BepInEx!", "Log message.");
                _key      = Config.Bind("General", "Key", KeyCode.F8, "Hotkey to log the greeting.");
                Logger.LogInfo($"{_greeting.Value} (frame {Time.frameCount})");
            }

            if (_key.Value != KeyCode.None && Input.GetKeyDown(_key.Value))
            {
                Logger.LogInfo(_greeting.Value);
            }
        }
    }
}
```

#### 3. Path B · The same mod with `aether new` and Aether Nano

```bash
./aether new MyFirstMod
cd MyFirstMod
dotnet build MyFirstMod.csproj -c Release -p:InstallToGame=true
```

```csharp
using Aether.Nano;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace MyFirstMod
{
    [BepInPlugin("dev.yourname.myfirstmod", "My First Mod", "1.0.0")]
    [BepInDependency(Nano.Guid)]
    public sealed class ModPlugin : NanoMod
    {
        private ConfigEntry<string> _greeting;
        private ConfigEntry<KeyCode> _key;

        protected override void OnReady()
        {
            _greeting = Config.Bind("General", "Greeting", "Hello Block Story!", "Message written to the log.");
            _key      = Config.Bind("General", "GreetKey", KeyCode.None, "Key that logs the greeting.");

            NanoKeys.Register(Info.Metadata.GUID, "My First Mod", _key, "Say hello", "Saludar");
            NanoPages.Register(new NanoPage
            {
                Id = Info.Metadata.GUID,
                Name = "My First Mod",
                Description = Nano.Tr("A starter mod.", "Un mod inicial."),
                ContentHeight = 120f,
                Draw = (area, y) =>
                {
                    if (NanoSurface.DrawActionButton(new Rect(12f, y + 10f, 220f, 34f),
                        Nano.Tr("Log greeting", "Escribir saludo"), primary: true))
                    {
                        Logger.LogInfo(_greeting.Value);
                    }
                }
            });
        }

        protected override void OnTick()
        {
            if (!NanoHud.KeyboardCaptured && _key.Value != KeyCode.None && Input.GetKeyDown(_key.Value))
                Logger.LogInfo(_greeting.Value);
        }
    }
}
```

### 3. Where files live & why early-boot Awake crashes

- **Thread slug:** `/docs/inst/` · **Verified on:** `Unity 6000.5 · Frame 0` · **Tags:** `#carpetas` `#awake` `#frame-0` `#sigsegv` · **Source:** `docs/KNOWN_TRAPS.md · src/Aether.Nano/NanoMod.cs`

Understanding Block Story’s folder layout and the early boot window of **Unity 6000.5.6f1** prevents the most common crash (whether you use BepInEx alone or Aether): an instant native `SIGSEGV` before the game window opens.

#### 1. Game folder map

| Path inside Block Story | What lives there |
| :--- | :--- |
| `BepInEx/plugins/` | Standard folder scanned by BepInEx. For players using BepInEx alone (without Aether), plugin `.dll` files go here. |
| `Aether/Mods/` | Mods folder when Aether Loader is installed. Hosts Aether mods, Nano mods and plain BepInEx plugins (starting them safely on Frame 0). |
| `BepInEx/config/` | Per-mod `.cfg` files created by `Config.Bind`. |
| `BepInEx/LogOutput.log` | BepInEx console and plugin log output. |
| `Aether/Logs//` | When Aether is installed, live copy of `LogOutput.log` and `Player.log` updated every 15 s (last 10 sessions kept). |

#### 2. The Unity 6000.5.6f1 trap: never touch Unity in `Awake` or `Start`

> **What happens if a plugin in `BepInEx/plugins` touches Unity in `Awake()` or defines `Start()`:** even reading `Time.frameCount` or creating a `GameObject` during `Chainloader.Start()` triggers a native segmentation fault (`SIGSEGV` at `0x28`).

#### 3. Safe Frame 0 initialization in pure BepInEx and in Aether Nano

```csharp
// ✅ OPTION 1: BepInEx alone (no Aether or Nano dependency)
public sealed class SafePureBepInExPlugin : BepInEx.BaseUnityPlugin
{
    private bool _ready;

    private void Update()
    {
        if (_ready) return;
        _ready = true;
        Logger.LogInfo("Frame 0 ready in pure BepInEx: " + UnityEngine.Time.frameCount);
    }
}
```

```csharp
// ✅ OPTION 2: With Aether Nano (NanoMod automatically defers to Frame 0)
public sealed class SafeNanoPlugin : Aether.Nano.NanoMod
{
    protected override void OnReady()
    {
        Logger.LogInfo("Frame 0 ready in NanoMod: " + UnityEngine.Time.frameCount);
    }
}
```

---

## II · Inside the game

### 4. Session lifecycle: scenes, Frame 0 & world transitions

- **Thread slug:** `/docs/lifecycle/` · **Verified on:** `Unity 6000.5 · Escenas` · **Tags:** `#escenas` `#frame-0` `#ciclo` `#in-game` · **Source:** `src/Aether.Nano/Nano.cs · src/Aether.Nano/NanoHud.cs`

A well-behaved mod never assumes the player is already inside a world when the process starts. Block Story moves from the initial splash screen to the main menu, character/world selection and gameplay, and can return to the main menu multiple times in one session.

#### 1. Checking game state in pure BepInEx vs. Aether Nano

| What you want to check | With BepInEx alone (`Assembly-CSharp`) | With `Aether.Nano` |
| :--- | :--- | :--- |
| Is a world currently loaded? | `TerrainLoader.instance != null && (TerrainLoader.instance.world ?? TerrainLoader.instance.getWorld()) != null` | `Nano.InGame` and `NanoWorld.Active()` |
| Is the pause menu open? | Check the settings camera on `Inventory` | `Nano.PauseMenuOpen` |
| Is an inventory/chest/shop window open? | Check active windows on `Inventory` | `Nano.NativeWindowOpen` |
| Should I draw my gameplay HUD right now? | When a world is active and no pause/inventory window is open | `NanoHud.CanDrawGameplayHud` |

#### 2. Detecting world changes cleanly

```csharp
using Aether.Nano;
using BepInEx;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;

namespace LifecycleSample
{
    [BepInPlugin("dev.yourname.lifecycle", "World Watcher", "1.0.0")]
    [BepInDependency(Nano.Guid)]
    public sealed class LifecyclePlugin : NanoMod
    {
        private IWorld _lastWorld;

        protected override void OnReady() { }

        protected override void OnTick()
        {
            // In pure BepInEx without Nano: TerrainLoader.instance?.world ?? TerrainLoader.instance?.getWorld()
            IWorld current = NanoWorld.Active();
            if (!ReferenceEquals(current, _lastWorld))
            {
                _lastWorld = current;
                if (current != null)
                    Logger.LogInfo("Entered an active world.");
                else
                    Logger.LogInfo("Returned to main menu: clearing world caches.");
            }
        }
    }
}
```

> **Physics invariant:** Never set `Physics.autoSyncTransforms = false`. In Block Story that desynchronizes newly generated chunk colliders and makes the player’s `CharacterController` fall straight through the ground.

### 5. World & Voxels: reading blocks and 32×32 chunks safely

- **Thread slug:** `/docs/voxels/` · **Verified on:** `Chunks 32×32 · Voxels` · **Tags:** `#voxels` `#chunks` `#nanoworld` `#rendimiento` · **Source:** `src/Aether.Nano/NanoWorld.cs · docs/KNOWN_TRAPS.md`

Block Story’s terrain is a 3D voxel grid divided into **32×32×32 block chunks**. How you query a block determines whether your mod runs at 120 FPS or freezes the main thread for seconds.

#### 1. The fatal trap: never use the direct world indexer

> **Why direct world indexing is forbidden:** querying `(x, y, z)` through the raw world indexer on a chunk that is not loaded yet forces **synchronous chunk generation on the main thread**. In an area scan, it freezes the game completely.

#### 2. Safe O(1) reads of already-loaded chunks

```csharp
using Aether.Nano;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using UnityEngine;

public static class SafeTerrainReader
{
    public static ushort GetBlockBelowPlayer()
    {
        IWorld world = NanoWorld.Active();
        if (world == null || Camera.main == null) return 0;

        Vector3 pos = Camera.main.transform.position;
        int wx = Mathf.FloorToInt(pos.x);
        int wy = Mathf.FloorToInt(pos.y) - 2;
        int wz = Mathf.FloorToInt(pos.z);

        Block block = NanoWorld.GetBlockSafe(world, wx, wy, wz);
        return block.id;
    }
}
```

#### 3. Column scans and frame budgets

```csharp
using Aether.Nano;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;

public static class ColumnScanner
{
    public static int FindSafeSurfaceY(IWorld world, int wx, int wz, int startY, int minY)
    {
        if (world == null) return minY;
        for (int y = startY; y >= minY; y--)
        {
            Block b = NanoWorld.GetBlockSafe(world, wx, y, wz);
            if (b.id != 0 && NanoWorld.IsSolidCeiling(b.id))
                return y;
        }
        return minY;
    }
}
```

### 6. Blocks, Items, Inventory & the held tool (Digger) trap

- **Thread slug:** `/docs/items/` · **Verified on:** `Inventario · Digger` · **Tags:** `#inventario` `#digger` `#items` `#i18n` · **Source:** `src/Aether.Nano/NanoItems.cs · docs/KNOWN_TRAPS.md`

Resolving what tool the player is holding or displaying localized item names involves three engine quirks that apply to every mod (with or without Aether).

#### 1. The held tool (`Digger`) trap (100% pure C# solution)

  - **Trap A (`TerrainLoader.digger`):** It is a default hand `Digger` that always reports `Shovel`, even with an empty hand.
  - **Trap B (`InvEquipment.GetItem(InvBaseItem.Slot.Weapon)`):** Holds any active hotbar item (blocks, food, spawners).
  - **Trap C (non-tool prefabs with a default `Digger` & combat axes):** `Cauldron` and `Bucket` carry a default `Digger` component, while `Battle Axe`, `Dark Axe`, `Fire Axe` and `Ice Axe` are combat weapons, not woodcutting axes.

```csharp
public static class RealDiggingTool
{
    public static bool TryGetToolKind(string itemName, out string kind)
    {
        kind = "Hand";
        if (string.IsNullOrEmpty(itemName)) return false;

        switch (itemName.Trim())
        {
            case "Dig":
            case "Iron Dig":
            case "Goblin PickAxe":
                kind = "Pickaxe";
                return true;
            case "Shovel":
            case "Iron Shovel":
                kind = "Shovel";
                return true;
            case "Axe":
            case "Iron Axe":
                kind = "Axe";
                return true;
            case "Hammer":
            case "Iron Hammer":
                kind = "Hammer";
                return true;
            case "Multitool":
                kind = "Multitool";
                return true;
            default:
                return false;
        }
    }
}
```

#### 2. Avoiding `{0}` format strings in item translations

```csharp
using Aether.Nano;
using UnityEngine;

public static class InventorySample
{
    public static void DrawHoveredItem(Rect slotRect)
    {
        InvGameItem hovered = NanoItems.HoveredSlotItem();
        if (hovered == null || hovered.baseItem == null) return;

        InvBaseItem baseItem = hovered.baseItem;
        string cleanName = NanoItems.LocalizedName(baseItem.name, baseItem);

        NanoItems.DrawIcon(new Rect(slotRect.x, slotRect.y, 32f, 32f), baseItem);
        NanoSurface.DrawCrispLabel(new Rect(slotRect.x + 38f, slotRect.y + 6f, 220f, 20f),
            cleanName, NanoStyles.Title, NanoBrand.Paper);
    }
}
```

### 7. Entities, Mobs, Health & the native POI Radar

- **Thread slug:** `/docs/entities/` · **Verified on:** `Health · MOBManagement` · **Tags:** `#mobs` `#health` `#radar` `#mobmanagement` · **Source:** `src/Aether.Nano/NanoWorld.cs · docs/KNOWN_TRAPS.md`

When scanning nearby creatures (mobs) or customizing overhead health bars in any BepInEx mod, follow two key rules to avoid periodic CPU hitches and keep mob names intact.

#### 1. Never call `FindObjectsOfType<Health>()` on a timer

Iterate the direct children of `GameObject.Find("MOBManagement").transform` instead:

```csharp
using System.Collections.Generic;
using UnityEngine;

public static class MobScanner
{
    private static Transform _mobRoot;

    public static void GetNearbyMobs(Vector3 center, float maxRadius, List<Health> results)
    {
        results.Clear();
        if (_mobRoot == null)
        {
            GameObject go = GameObject.Find("MOBManagement");
            if (go != null) _mobRoot = go.transform;
        }
        if (_mobRoot == null) return;

        float r2 = maxRadius * maxRadius;
        int count = _mobRoot.childCount;
        for (int i = 0; i < count; i++)
        {
            Transform child = _mobRoot.GetChild(i);
            if (child == null || !child.gameObject.activeInHierarchy) continue;
            if ((child.position - center).sqrMagnitude > r2) continue;

            Health h = child.GetComponent<Health>();
            if (h != null && !h.IsDead())
                results.Add(h);
        }
    }
}
```

#### 2. Filtering out large AI aggro triggers on Raycast

```csharp
using Aether.Nano;
using UnityEngine;

public static class EntityRaycaster
{
    public static Health RaycastMob(Camera cam, float maxDist = 18f)
    {
        if (cam == null) return null;
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDist);

        float bestDist = float.MaxValue;
        Health bestMob = null;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (!NanoWorld.AcceptsEntityHit(col)) continue;

            Health h = col.GetComponentInParent<Health>();
            if (h != null && !h.IsDead() && hits[i].distance < bestDist)
            {
                bestDist = hits[i].distance;
                bestMob = h;
            }
        }
        return bestMob;
    }
}
```

### 8. Native UI (NGUI) vs. IMGUI: fonts, atlases & UIWidget

- **Thread slug:** `/docs/ngui/` · **Verified on:** `NGUI · IMGUI · UIWidget` · **Tags:** `#ngui` `#imgui` `#zantroke` `#uiwidget` · **Source:** `src/Aether.Mods/Aether.Mods.Canvas · docs/KNOWN_TRAPS.md`

Block Story renders its native HUD and menus with **NGUI** (`UIPanel`, `UIWidget`, `UISprite`, `UILabel` drawn by a 2D orthographic camera), while most mods draw overlays with **IMGUI** (`OnGUI`).

#### 1. Game fonts and texture atlases (`GUI` and `TerrainMobile`)

  - **Native serif font:** The game’s real serif font is **`Zantroke`** (prefer it over `BlockStoryInfinity`).
  - **Hearts:** Exact sprite names in the `GUI` atlas are `HeartFull` and `HeartEmpty`.
  - **Block atlas:** Material `TerrainMobile` (shader `UBuild/Earth`, `1024×2560`) laid out as **8 columns × 20 rows of 128×128 tiles**.

#### 2. Projecting an NGUI `UIWidget` to screen pixels

```csharp
using Aether.Nano;
using UnityEngine;

public static class NguiProjection
{
    public static bool TryGetScreenRect(UIWidget widget, out Rect screenRect)
    {
        screenRect = default;
        Inventory inv = Nano.Inventory;
        Camera guiCam = inv != null ? inv.guiCamera : null;
        if (widget == null || guiCam == null) return false;

        Vector2 relSize = widget.relativeSize;
        Vector2 pivot = widget.pivotOffset;
        if (relSize.x <= 0.0001f || relSize.y <= 0.0001f) return false;

        float cx = (pivot.x + 0.5f) * relSize.x;
        float cy = (pivot.y - 0.5f) * relSize.y;
        float hx = relSize.x * 0.5f;
        float hy = relSize.y * 0.5f;

        Transform t = widget.cachedTransform;
        Vector3 c0 = guiCam.WorldToScreenPoint(t.TransformPoint(new Vector3(cx - hx, cy - hy, 0f)));
        Vector3 c3 = guiCam.WorldToScreenPoint(t.TransformPoint(new Vector3(cx + hx, cy + hy, 0f)));

        float minX = Mathf.Min(c0.x, c3.x);
        float maxX = Mathf.Max(c0.x, c3.x);
        float minY = Screen.height - Mathf.Max(c0.y, c3.y);
        float maxY = Screen.height - Mathf.Min(c0.y, c3.y);

        screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return screenRect.width > 1f && screenRect.height > 1f;
    }
}
```

### 9. Per-world saves vs. .cfg & keeping PlayerPrefs clean

- **Thread slug:** `/docs/persistence/` · **Verified on:** `FSWorldPersistence` · **Tags:** `#guardado` `#config` `#playerprefs` `#mundos` · **Source:** `src/Aether.Core/Mapping · src/Aether.UI/NativeSuite/HudCustomizerModule.cs`

When a player disables or removes a mod, the game’s own UI and settings must return to normal, and data belonging to one world must never bleed into other worlds.

#### 1. Atomic per-world file writes (pure .NET)

```csharp
using System.IO;
using System.Text;

public static class WorldSaveHelper
{
    public static void WriteAtomicText(string worldDir, string modSubfolder, string fileName, string content)
    {
        if (string.IsNullOrEmpty(worldDir) || !Directory.Exists(worldDir)) return;

        string dir = Path.Combine(worldDir, modSubfolder);
        Directory.CreateDirectory(dir);

        string target = Path.Combine(dir, fileName);
        string temp = target + ".tmp";

        File.WriteAllText(temp, content, Encoding.UTF8);
        if (File.Exists(target)) File.Delete(target);
        File.Move(temp, target);
    }
}
```

#### 2. Backup & restore when moving game objects

```csharp
using System.Collections.Generic;
using UnityEngine;

public sealed class TransformRestorer
{
    private readonly Dictionary<Transform, Vector3> _originals = new Dictionary<Transform, Vector3>();

    public void MoveWithBackup(Transform target, Vector3 newLocalPos)
    {
        if (target == null) return;
        if (!_originals.ContainsKey(target))
            _originals[target] = target.localPosition;

        target.localPosition = newLocalPos;
    }

    public void RestoreOriginal(Transform target)
    {
        if (target == null) return;
        if (_originals.TryGetValue(target, out Vector3 orig))
            target.localPosition = orig;
    }
}
```

---

## III · Step-by-step recipes

### 10. Recipe 1: Project (.csproj) & code anatomy (pure BepInEx vs. Nano)

- **Thread slug:** `/docs/first/` · **Verified on:** `BepInEx · Nano · Aether` · **Tags:** `#csproj` `#bepinplugin` `#nanomod` `#receta` · **Source:** `src/Aether.Nano/NanoMod.cs · src/Aether.Installer/ModScaffold.cs`

Here is the complete `.csproj` and entrypoint anatomy whether you want to build a mod **with BepInEx alone** (via `dotnet new classlib`) or with **Aether Nano / Aether API**.

#### 1. Pure BepInEx `.csproj` (no Aether or Nano dependency)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <AssemblyName>MyMod</AssemblyName>
    <RootNamespace>MyMod</RootNamespace>
    <GameDir Condition="'$(GameDir)' == ''">/path/to/BlockStory</GameDir>
    <ManagedDir>$(GameDir)/BlockStoryLinux_Data/Managed</ManagedDir>
    <ManagedDir Condition="!Exists('$(ManagedDir)')">$(GameDir)/BlockStory_Data/Managed</ManagedDir>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="BepInEx"><HintPath>$(GameDir)/BepInEx/core/BepInEx.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="0Harmony"><HintPath>$(GameDir)/BepInEx/core/0Harmony.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="Assembly-CSharp"><HintPath>$(ManagedDir)/Assembly-CSharp.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine"><HintPath>$(ManagedDir)/UnityEngine.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.CoreModule"><HintPath>$(ManagedDir)/UnityEngine.CoreModule.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.IMGUIModule"><HintPath>$(ManagedDir)/UnityEngine.IMGUIModule.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.TextRenderingModule"><HintPath>$(ManagedDir)/UnityEngine.TextRenderingModule.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.InputLegacyModule"><HintPath>$(ManagedDir)/UnityEngine.InputLegacyModule.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>
</Project>
```

#### 2. Entrypoint with `NanoMod` (BepInEx + Aether Nano)

```csharp
using Aether.Nano;
using BepInEx;

namespace MyMod
{
    [BepInPlugin("dev.yourname.mymod", "My Mod", "1.0.0")]
    [BepInDependency(Nano.Guid)]
    public sealed class MyModPlugin : NanoMod
    {
        protected override void OnReady() { /* Frame 0 */ }
        protected override void OnTick()  { /* Every Update after OnReady */ }
        protected override void OnDraw()  { /* Every OnGUI after OnReady */ }
    }
}
```

#### 3. Entrypoint with `[assembly: AetherMod]` (Aether API)

```csharp
using Aether.Loader.Discovery;
using Aether.Loader.Entrypoints;

[assembly: AetherMod("my_mod", "1.0.0",
    Name = "My Mod",
    Author = "Your Name",
    Description = "Mod built on the Aether API.",
    Depends = "aether_ui@1.2.5-beta")]

namespace MyMod
{
    public sealed class MyEntrypoint : IModInitializer, IClientModInitializer
    {
        public void OnInitialize() { /* Block/item registries */ }
        public void OnInitializeClient() { /* Frame 0: UI, keys and Harmony */ }
    }
}
```

### 11. Recipe 2: Configuration (.cfg in pure BepInEx & NanoPages UI)

- **Thread slug:** `/docs/conf/` · **Verified on:** `Config.Bind · NanoPages` · **Tags:** `#config` `#nanopages` `#nanoicons` `#opciones` · **Source:** `src/Aether.Nano/NanoPages.cs · src/Aether.Nano/NanoSurface.cs`

Every BepInEx plugin gets a `BepInEx/config/<your-guid>.cfg` file via `Config.Bind`. You can use it in pure BepInEx (with range constraints via `AcceptableValueRange`) or add an in-game settings page and `128×128` icon with `NanoPages`.

#### 1. Path A · Pure BepInEx configuration with value ranges

```csharp
using BepInEx;
using BepInEx.Configuration;

namespace PureConfigSample
{
    [BepInPlugin("dev.yourname.pureconfig", "Pure Config", "1.0.0")]
    public sealed class PureConfigPlugin : BaseUnityPlugin
    {
        private bool _ready;
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _scale;

        private void Update()
        {
            if (_ready) return;
            _ready = true;

            _enabled = Config.Bind("General", "Enabled", true, "Enable the mod.");
            _scale   = Config.Bind("General", "Scale", 1.0f,
                new ConfigDescription("HUD scale (0.5 to 2.0).", new AcceptableValueRange<float>(0.5f, 2.0f)));
        }
    }
}
```

#### 2. Path B · In-game settings page & `128×128` icon with `NanoPages`

```csharp
using Aether.Nano;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace SettingsSample
{
    [BepInPlugin("dev.yourname.solarcompass", "Solar Compass", "1.0.0")]
    [BepInDependency(Nano.Guid)]
    public sealed class SolarCompassPlugin : NanoMod
    {
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _scale;
        private ConfigEntry<string> _mode;

        private static readonly NanoSegmentOption<string>[] Modes =
        {
            new NanoSegmentOption<string>("Compact", "Compact", NanoBrand.Ember),
            new NanoSegmentOption<string>("Detailed", "Detailed", NanoBrand.Ok),
            new NanoSegmentOption<string>("Minimal", "Minimal")
        };

        protected override void OnReady()
        {
            _enabled = Config.Bind("General", "Enabled", true, "Show the compass.");
            _scale   = Config.Bind("General", "Scale", 1.0f, "HUD scale (0.75 to 1.50).");
            _mode    = Config.Bind("General", "Mode", "Compact", "Display style.");

            NanoPages.Register(new NanoPage
            {
                Id = Info.Metadata.GUID,
                Name = "Solar Compass",
                Author = "YourName",
                Version = Info.Metadata.Version.ToString(),
                Description = Nano.Tr("Solar compass and time readout.", "Brújula solar y lectura de hora."),
                Icon = NanoIcons.LoadFromAssembly(typeof(SolarCompassPlugin).Assembly),
                ContentHeight = 260f,
                Draw = DrawSettingsPage
            });
        }

        private void DrawSettingsPage(Rect area, float y)
        {
            float w = area.width - 24f;
            y = NanoSurface.DrawSectionHeader(area, y, "GENERAL", "v1.0.0");

            if (NanoSurface.DrawToggleRow(new Rect(12f, y, w, 36f),
                Nano.Tr("Enable Solar Compass", "Activar Brújula Solar"), _enabled.Value, "F8"))
            {
                _enabled.Value = !_enabled.Value;
            }
            y += 44f;

            if (NanoSurface.DrawSegmented(new Rect(12f, y, w, 32f), Modes, _mode.Value, out string nextMode))
            {
                _mode.Value = nextMode;
            }
            y += 42f;

            string scaleLabel = Mathf.RoundToInt(_scale.Value * 100f) + "%";
            float nextScale = NanoSurface.DrawSlider(new Rect(12f, y, w, 32f),
                Nano.Tr("HUD Scale", "Escala del HUD"), _scale.Value, 0.75f, 1.50f, scaleLabel, step: 0.05f);
            if (Mathf.Abs(nextScale - _scale.Value) > 0.001f)
            {
                _scale.Value = nextScale;
            }
        }
    }
}
```

### 12. Recipe 3: Keybinds (pure BepInEx Input & NanoKeys registration)

- **Thread slug:** `/docs/keys/` · **Verified on:** `KeyCode · NanoKeys` · **Tags:** `#teclas` `#input` `#nanokeys` `#nanoinput` · **Source:** `src/Aether.Nano/NanoKeys.cs · src/Aether.Nano/NanoInput.cs`

Always bind hotkeys to a `ConfigEntry<KeyCode>` so players can rebind them or set `KeyCode.None`.

#### 1. Path A · Configurable hotkey in pure BepInEx

```csharp
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace PureKeySample
{
    [BepInPlugin("dev.yourname.purekey", "Pure Key", "1.0.0")]
    public sealed class PureKeyPlugin : BaseUnityPlugin
    {
        private bool _ready;
        private ConfigEntry<KeyCode> _toggleKey;
        private bool _open;

        private void Update()
        {
            if (!_ready)
            {
                _ready = true;
                _toggleKey = Config.Bind("Controls", "ToggleKey", KeyCode.K, "Key to toggle the panel.");
            }

            if (_toggleKey.Value != KeyCode.None && Input.GetKeyDown(_toggleKey.Value))
                _open = !_open;
        }
    }
}
```

#### 2. Path B · Registering in Block Story’s native Controls window & F7 (`NanoKeys`)

```csharp
using Aether.Nano;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace KeybindSample
{
    [BepInPlugin("dev.yourname.notes", "Field Notes", "1.0.0")]
    [BepInDependency(Nano.Guid)]
    public sealed class FieldNotesPlugin : NanoMod
    {
        private ConfigEntry<KeyCode> _toggleKey;
        private bool _isOpen;

        protected override void OnReady()
        {
            _toggleKey = Config.Bind("Controls", "ToggleKey", KeyCode.K, "Open or close the notes panel.");
            NanoKeys.Register(Info.Metadata.GUID, "Field Notes", _toggleKey,
                labelEn: "Toggle notes window", labelEs: "Abrir o cerrar notas");
        }

        protected override void OnTick()
        {
            if (NanoHud.KeyboardCaptured) return;
            if (_toggleKey.Value != KeyCode.None && Input.GetKeyDown(_toggleKey.Value))
                SetOpen(!_isOpen);
        }

        private void SetOpen(bool open)
        {
            if (_isOpen == open) return;
            _isOpen = open;
            if (_isOpen) { NanoHud.CloseHostMenus?.Invoke(); NanoInput.AcquireIsolation(); }
            else NanoInput.ReleaseIsolation();
        }
    }
}
```

### 13. Recipe 4: 60 FPS OnGUI HUD drawing (pure IMGUI & NanoSurface)

- **Thread slug:** `/docs/assets/` · **Verified on:** `IMGUI · NanoSurface` · **Tags:** `#imgui` `#nanosurface` `#hud` `#repaint` · **Source:** `src/Aether.Nano/NanoSurface.cs · src/Aether.Nano/NanoHud.cs`

Whether you draw with `UnityEngine.GUI` in pure BepInEx or with `NanoSurface`, the golden rule for 60/120 FPS in `OnGUI()` is **zero per-frame allocations**.

#### 1. Path A · Pure IMGUI HUD card (BepInEx & UnityEngine only)

```csharp
using BepInEx;
using UnityEngine;

namespace PureHudSample
{
    [BepInPlugin("dev.yourname.purehud", "Pure HUD", "1.0.0")]
    public sealed class PureHudPlugin : BaseUnityPlugin
    {
        private Texture2D _bgTex;
        private GUIStyle _labelStyle;

        private void OnGUI()
        {
            if (Camera.main == null) return;

            if (_bgTex == null)
            {
                _bgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.DontUnloadUnusedAsset
                };
                _bgTex.SetPixel(0, 0, new Color(0.06f, 0.05f, 0.04f, 0.90f));
                _bgTex.Apply();
            }
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
                _labelStyle.normal.textColor = new Color(0.92f, 0.87f, 0.78f);
            }

            Rect card = new Rect(18f, 86f, 160f, 38f);
            GUI.DrawTexture(card, _bgTex, ScaleMode.StretchToFill, true, 0f, Color.white, Vector4.zero, new Vector4(8f, 8f, 8f, 8f));
            int altitude = Mathf.FloorToInt(Camera.main.transform.position.y);
            GUI.Label(new Rect(card.x + 12f, card.y + 8f, card.width - 24f, 22f), "Y = " + altitude + " m", _labelStyle);
        }
    }
}
```

#### 2. Path B · Squircle HUD card with `NanoSurface` & HUD editor support (`NanoHud`)

```csharp
using Aether.Nano;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace SquircleHudSample
{
    [BepInPlugin("dev.yourname.altimeter", "Squircle Altimeter", "1.0.0")]
    [BepInDependency(Nano.Guid)]
    public sealed class AltimeterPlugin : NanoMod
    {
        private ConfigEntry<float> _offsetX, _offsetY, _scale;

        protected override void OnReady()
        {
            _offsetX = Config.Bind("HUD", "OffsetX", 18f, "Horizontal offset.");
            _offsetY = Config.Bind("HUD", "OffsetY", 86f, "Vertical offset.");
            _scale   = Config.Bind("HUD", "Scale", 1.0f, "Card scale.");

            NanoHud.Register(new NanoHudElement
            {
                Id = Info.Metadata.GUID,
                Name = "Altimeter",
                GetRect = GetCardRect,
                OffsetX = _offsetX,
                OffsetY = _offsetY,
                Scale = _scale,
                DrawPreview = rect => DrawCard(rect, 128, false),
                OnReset = () => { _offsetX.Value = 18f; _offsetY.Value = 86f; _scale.Value = 1.0f; }
            });
        }

        private Rect GetCardRect()
        {
            float s = Mathf.Clamp(_scale.Value, 0.75f, 1.50f);
            return new Rect(_offsetX.Value, _offsetY.Value, 168f * s, 54f * s);
        }

        protected override void OnDraw()
        {
            if (!NanoHud.CanDrawGameplayHud || Nano.CachedCamera == null) return;
            int y = Mathf.FloorToInt(Nano.CachedCamera.transform.position.y);
            bool underground = NanoWorld.IsUnderground(NanoWorld.Active(), Nano.CachedCamera.transform.position);
            DrawCard(GetCardRect(), y, underground);
        }

        private static void DrawCard(Rect r, int altitude, bool underground)
        {
            NanoSurface.DrawCardSurface(r,
                bgTop: NanoBrand.InkElevated, bgBottom: NanoBrand.Ink,
                border: NanoBrand.Line, topRule: NanoBrand.Ember,
                specularHighlight: new Color(1f, 1f, 1f, 0.08f),
                radius: 10f, opacity: 0.94f);

            Color accent = underground ? NanoBrand.Warn : NanoBrand.Ok;
            NanoSurface.DrawRhombus(new Rect(r.x + 12f, r.y + 11f, 10f, 10f), accent, filled: true);
            NanoSurface.DrawCrispLabel(new Rect(r.x + 28f, r.y + 7f, r.width - 36f, 18f),
                underground ? "UNDERGROUND" : "SURFACE", NanoBrand.Style(NanoBrand.BodySemi, 12, NanoBrand.Mute), NanoBrand.Mute);
            NanoSurface.DrawCrispLabel(new Rect(r.x + 12f, r.y + 26f, r.width - 24f, 22f),
                "Y = " + altitude + " m", NanoBrand.Style(NanoBrand.Mono, 15, NanoBrand.Paper), NanoBrand.Paper);
        }
    }
}
```

### 14. Recipe 5: Clean Harmony patches & mod coexistence

- **Thread slug:** `/docs/harm/` · **Verified on:** `Harmony 2.x · Deps` · **Tags:** `#harmony` `#prefix` `#postfix` `#dependencias` · **Source:** `src/Aether.Mods/Aether.Mods.Dusk/DuskPlugin.cs · docs/KNOWN_TRAPS.md`

Harmony (`0Harmony.dll`, shipped in `BepInEx/core/`) works identically in a pure BepInEx plugin and in an Aether Nano mod.

#### 1. Pure BepInEx Harmony example (zero external dependencies)

```csharp
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace PureHarmonySample
{
    [BepInPlugin("dev.yourname.pureharmony", "Pure Harmony", "1.0.0")]
    public sealed class PureHarmonyPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> Enabled;
        private bool _ready;
        private Harmony _harmony;

        private void Update()
        {
            if (_ready) return;
            _ready = true;

            Enabled = Config.Bind("General", "Enabled", true, "Enable the patch.");
            _harmony = new Harmony(Info.Metadata.GUID);
            _harmony.PatchAll(typeof(PureHarmonyPlugin).Assembly);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    [HarmonyPatch(typeof(Health), "RefreshHealthBarUI")]
    internal static class PureHealthPostfix
    {
        private static void Postfix(Health __instance)
        {
            if (PureHarmonySample.PureHarmonyPlugin.Enabled == null || !PureHarmonySample.PureHarmonyPlugin.Enabled.Value) return;
            if (__instance == null || __instance.IsDead()) return;
        }
    }
}
```

#### 2. The same pattern with `NanoMod`

```csharp
using Aether.Nano;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace HarmonySample
{
    [BepInPlugin("dev.yourname.harmonysample", "Harmony Sample", "1.0.0")]
    [BepInDependency(Nano.Guid)]
    public sealed class HarmonySamplePlugin : NanoMod
    {
        internal static ConfigEntry<bool> Enabled;
        private Harmony _harmony;

        protected override void OnReady()
        {
            Enabled = Config.Bind("General", "Enabled", true, "Enable the sample patch.");
            _harmony = new Harmony(Info.Metadata.GUID);
            _harmony.PatchAll(typeof(HarmonySamplePlugin).Assembly);
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
```

---

## IV · Diagnostics & Lattice

### 15. Build, install to BepInEx/plugins or Aether/Mods & test

- **Thread slug:** `/docs/build/` · **Verified on:** `.NET Standard 2.1` · **Tags:** `#dotnet-build` `#plugins` `#pruebas` `#qa` · **Source:** `docs/DEV_SETUP.md · src/Aether.Installer/ModAdder.cs`

Whether you build a pure BepInEx plugin or a mod on Aether Nano, compile in Release mode and test in a dedicated world:

> **Safe deployment:** Always close Block Story from its own menu (*Esc → Exit → Main Menu → Exit*) before copying DLLs.

```bash
# 1. Build any .csproj in Release mode
dotnet build MyMod.csproj -c Release

# 2A. Using BepInEx alone: copy your DLL into BepInEx/plugins/
cp bin/Release/netstandard2.1/MyMod.dll "<game-folder>/BepInEx/plugins/"

# 2B. With the Aether CLI: install with one command
./aether add bin/Release/netstandard2.1/MyMod.dll
```

### 16. Publish on GitHub, Lattice verification tiers & §4c notice

- **Thread slug:** `/docs/pub/` · **Verified on:** `GitHub · Lattice` · **Tags:** `#lattice` `#publicar` `#github` `#4c` · **Source:** `lattice.json · docs/MODDING_GUIDE.md §11`

When your mod is ready, publish it on GitHub and list it on **Lattice** ([lattice.ultimaibrahim.dev](https://lattice.ultimaibrahim.dev)), the neutral catalog for Block Story mods (pure BepInEx plugins, Aether Nano mods and others alike).

#### 1. Pre-release checklist

  - **Public repository with full source code, README and LICENSE (§4b, §4f).**
  - **Zero game binaries, extracted assets or decompiled code (§16a, §16b, §17).**
  - **Notify Big Cube Interactive after release (§4c).**

#### 2. Lattice verification tiers (the 3 hexagons)

| Tier | Sign | What it means |
| :--- | :--- | :--- |
| **1 · Listed** | `⬢ ⬡ ⬡` | Public repo, license, README, and static metadata validator passes (never executes the DLL). |
| **2 · Reproducible** | `⬢ ⬢ ⬡` | Built on GitHub Actions with cryptographic provenance attestation (`actions/attest-build-provenance`). |
| **3 · Reviewed** | `⬢ ⬢ ⬢` | Source code reviewed by a human and signed into the Ed25519 index. |

```json
{
  "mods": [
    {
      "id": "my-mod",
      "path": "src/MyMod",
      "asset": "my-mod-{version}.zip",
      "icon": "src/MyMod/icon.png",
      "category": "ui",
      "summary": {
        "es": "Descripción corta de tu mod en español.",
        "en": "Short description of your mod in English."
      }
    }
  ]
}
```

### 17. Conflict diagnostics, CLI & Known Traps

- **Thread slug:** `/docs/traps/` · **Verified on:** `Diagnóstico · Trampas` · **Tags:** `#doctor` `#cli` `#conflictos` `#trampas` · **Source:** `docs/KNOWN_TRAPS.md · src/Aether.Installer`

Checklist of engine traps that affect any Block Story mod (pure BepInEx or Aether) and optional CLI commands.

| Trap | Symptom | Fix |
| :--- | :--- | :--- |
| Touching Unity in `Awake()` or defining `Start()` in `BepInEx/plugins` | Instant native `SIGSEGV` on boot; empty `LogOutput.log`. | Defer initialization to the first `Update()` (Frame 0) or use `NanoMod.OnReady()`. |
| Direct world indexing on unloaded chunks | Multi-second freeze or exception. | Read only loaded chunks (e.g. `NanoWorld.GetBlockSafe(world, x, y, z)`). |
| Calling `FindObjectsOfType()` on a timer | Periodic frame hitch. | Iterate children of `GameObject.Find("MOBManagement").transform`. |
| Uploading textures (`Texture2D.Apply`) on every `OnGUI` event | 3× GPU upload cost per frame. | Upload only on `Event.current.type == EventType.Repaint`. |
| Writing HUD/camera tweaks to `PlayerPrefs` | Game stays altered after removing the mod. | Store mod settings in your `.cfg` and restore original values in memory. |

```bash
./aether install --game-dir "<path>"
./aether status  --game-dir "<path>"
./aether list    --game-dir "<path>"
./aether add     <file.dll|.zip>
./aether remove  <id-or-file>
./aether doctor  --game-dir "<path>"
./aether new     <ModName>
```

