# Aether Core — Mod Contract

| Field | Value |
| :--- | :--- |
| **ID** | `aether` |
| **Version** | `1.0.0` (`AetherBootstrap.PluginVersion`) |
| **Author** | Ibrahim García |
| **Assembly** | `Aether.Loader.dll` + `Aether.Core.dll` + `Aether.API.dll` + `Aether.Registry.dll` |
| **Status** | ✅ |
| **Canonical runtime** | `Aether.Loader/AetherBootstrap.cs` (frame 0 of `Update()`) |
| **Last reviewed** | 2026-11-03 |

## 1. Scope

Owns the framework, not the gameplay features:

- BepInEx entrypoint, early-boot safety, plugin lifecycle.
- Mod discovery (`[assembly: AetherMod]`), dependency validation (SemVer 2.0 / ranges), topological
  load order (Kahn DAG), one instance per entrypoint type.
- Declarative configuration (`AetherConfig`, JSON per mod).
- Fault isolation (`CrashGuard`), crash reports (`CrashReporter`) and the in-game crash overlay trigger.
- Service locator (`AetherServiceLocator`), zero-alloc event primitives, rented arrays, fast voxel raycast.
- Namespaced identifiers and deterministic dynamic block IDs.

It does **not** own gameplay UI, rendering or the flagship mods' behaviour.

## 2. Entry points and public surface

| Type | Role |
| :--- | :--- |
| `AetherBootstrap : BaseUnityPlugin` | BepInEx entrypoint. `Awake()` only wires core; mods load in frame 0 of `Update()`. |
| `[assembly: AetherMod(id, version)]` | Discovery metadata (`Name`, `Author`, `Description`, `SourcePath`). |
| `IPreLaunchEntrypoint` | Runs before `OnInitialize`. |
| `IModInitializer` | Main initialization. |
| `IClientModInitializer` | Client initialization, frame 0 of `Update()`. |
| `ISceneModInitializer` | Scene-loaded hook. |
| `AetherConfig` | Declarative `ConfigEntry<T>` binding and lazy JSON persistence. |
| `AetherServiceLocator` | Typed service registry (`IUIService`, etc.). |
| `CrashGuard` | Per-mod/per-hook exception isolation and suspension. |
| `CrashReporter` | Blame attribution, report files, overlay request. |
| `FastVoxelRaycast` | Allocation-free voxel raycast for safe block targeting. |
| `Identifier` / `DynamicBlockRegistry` | Namespaced IDs and deterministic block ID mapping. |

## 3. Runtime map

1. `Awake()`: installs `AssemblyResolve` for mod dependencies in Aether's folders, calls `AetherRuntime.Initialize()`,
   wires `CrashGuard`/`Application.logMessageReceived`, hooks `SceneManager.sceneLoaded`.
2. Frame 0 of `Update()`: `DiscoverAndResolveMods()`, then hosted BepInEx plugins, then every
   `IClientModInitializer.OnInitializeClient()` in a per-mod try/catch.
3. Every frame: `AetherConfig.FlushPendingSaves()` (at most one write per dirty mod per frame).
4. `OnGUI()`: delegates to `IUIService.RenderGUI()` when registered.

## 4. Configuration

| Surface | File | Keys |
| :--- | :--- | :--- |
| `AetherConfig` | `BepInEx/config/aether/<modId>.json` | One file per mod; category/key/value; loaded once per mod, saved lazily. |
| BepInEx | `BepInEx/config/dev.ultimaibrahim.aether.cfg` | Framework plugin entry only (no gameplay keys). |

Aether Core itself declares no gameplay config keys. `SourcePath` for the pseudo-mod points at the
`Aether.Loader` assembly.

## 5. Hotkeys

None. Hotkeys belong to the suite and its mods.

## 6. Invariants (must never break)

- **R1:** `AetherBootstrap` must never declare `Start()`. Unity `6000.5.6f1` runs BepInEx
  `Chainloader.Start()` before the native `TimeManager` exists; a delayed start call crashes with
  `SIGSEGV addr:0x28`.
- Mod discovery/instantiation stays in frame 0 of `Update()`, never in `Awake()`.
- Only types implementing an entrypoint interface are instantiated, one instance per type, shared across every interface it implements. Attributes and compiler-generated state machines are skipped.
- A failing mod entrypoint is logged and skipped; it must never abort the loader or the remaining mods.
- `SafeGetTypes()` must survive `ReflectionTypeLoadException` and continue with the loaded types.
- Block IDs are deterministic (FNV-1a + probing); load order must not change IDs.
- `AetherConfig` writes are deferred (one flush per frame), never per `Bind`.

## 7. Performance budget

| Metric | Budget | How measured |
| :--- | :--- | :--- |
| Mod discovery | Once, frame 0 | Log line `Resolved N mods` appears once |
| Config writes | ≤ 1 per dirty mod per frame | `FlushPendingSaves()` |
| Hot-loop allocations | 0 B after warm-up | `ZeroAllocTests` |
| Loader overhead | No per-frame reflection | Code review + `Update()` body |

## 8. QA gates

| Gate | Kind | Command / check |
| :--- | :--- | :--- |
| No `Start()` on `AetherBootstrap` | Automated | `ArchitectureTests.AetherBootstrap_MustNotDeclareUnityStart` |
| Discovery in `Update()` | Automated | `ArchitectureTests.AetherBootstrap_DefersModDiscoveryToUpdate` |
| Only entrypoint types instantiated | Automated | `ArchitectureTests.Loader_OnlyInstantiatesEntrypointTypes` |
| SourcePath exposed | Automated | `ArchitectureTests.LoaderMetadata_ExposesAssemblySourcePath` |
| Every mod declares `[AetherMod]` | Automated | `ArchitectureTests.EveryAetherModAssembly_DeclaresAetherModAttribute` |
| SemVer + ranges | Automated | `SemVerTests` |
| DAG / cycles / missing deps | Automated | `DependencyGraphTests` |
| Crash isolation / blame | Automated | `PillarTests`, `CrashReporterTests` |
| Zero-alloc hot loops | Automated | `ZeroAllocTests` |
| Live boot | Live | Launch game; `LogOutput.log` shows `Resolved 6 mods` once and no exceptions. |

## 9. Known debt / limits

| Item | Impact | Next action |
| :--- | :--- | :--- |
| `AetherRuntime.Version = "1.0.0-beta"` vs plugin `1.0.0` | Two version strings in logs/reports | Unify or derive one from the other |
| No reload/unload path | Mods are loaded for the process lifetime | Document as by design; do not advertise hot reload |
| `Aether.Core`/`Aether.API` are separate assemblies without their own `[AetherMod]` | They are framework libraries, not selectable mods | Keep as-is; document in this contract |
| BepInEx plugin `.cfg` has no user-facing gameplay keys | Users may look for settings there | Point users to the suite `.cfg` and `aether/*.json` |

## 10. Refinement history

| Date | Symptom | Root cause | Rule / change |
| :--- | :--- | :--- | :--- |
| 2026-11-03 | Early-boot `SIGSEGV addr:0x28` | `Start()` on a `BaseUnityPlugin` during `Chainloader.Start()` | R1; architecture gate added |
| 2026-11-03 | Mod menu listed a hardcoded catalog | UI did not read the loader | `LoadedMods` + `SourcePath`; architecture gate added |
| 2026-11-03 | Stale Lithium description/config | Copy drift after autoSync fix | Corrected text; dead config removed; contract records it |
| 2026-11-03 | 7 `Entrypoint ... failed` errors per launch | Loader instantiated every class, including attributes and compiler-generated state machines | Interface filter + architecture gate; live log now shows 0 |
