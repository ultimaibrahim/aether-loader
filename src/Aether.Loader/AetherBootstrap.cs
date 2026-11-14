using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Aether.Core;
using Aether.Loader.Diagnostics;
using Aether.Loader.Discovery;
using Aether.Loader.Entrypoints;
using Aether.Loader.Hosting;
using Aether.Loader.Resolution;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aether.Loader
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersionNumeric)]
    public class AetherBootstrap : BaseUnityPlugin
    {
        public const string PluginGuid = "dev.ultimaibrahim.aether";
        public const string PluginName = "Aether";
        /// <summary>Aether's public version. Pre-release until the API and mod contracts settle.</summary>
        public const string PluginVersion = "1.2.1-beta";
        /// <summary>BepInEx parses plugin versions with System.Version, which rejects "-beta".</summary>
        public const string PluginVersionNumeric = "1.2.1";

        public static AetherBootstrap Instance { get; private set; } = null!;
        public static IReadOnlyList<AetherModMetadata> LoadedMods { get; private set; } = Array.Empty<AetherModMetadata>();

        /// <summary>Single source of truth for the "loaded mods" count shown in the UI.</summary>
        public static int LoadedModCount => LoadedMods.Count + Hosting.ExternalModCatalog.CachedCount();

        private readonly List<IClientModInitializer> _clientInitializers = new();
        private readonly List<ISceneModInitializer> _sceneInitializers = new();
        private bool _clientInitialized;
        private readonly ModConflictReport _conflictReport = new();
        private bool _conflictScanPending;
        private int _conflictScanFrames;
        private BepInEx.Configuration.ConfigEntry<string>? _harmonyPolicy;
        private BepInEx.Configuration.ConfigEntry<string>? _harmonyDisable;
        private LogArchive? _logArchive;
        private float _nextLogSync;
        private const float LogSyncSeconds = 15f;
        private BepInEx.Configuration.ConfigEntry<bool>? _hostBepInExPlugins;
        private void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _hostBepInExPlugins = Config.Bind("Hosting", "HostBepInExPlugins", true,
                "When true (default), plain BepInEx plugins (BaseUnityPlugin) dropped into Aether/Mods are started by " +
                "Aether on frame 0, so third-party mods can be installed by drag and drop. This also avoids the native " +
                "crash some plugins cause when BepInEx starts them during Unity's early boot (see docs/KNOWN_TRAPS.md). " +
                "Their Awake runs on frame 0 instead of at boot. Set false to ignore BepInEx plugins in Aether/Mods.");

            _harmonyPolicy = Config.Bind("Conflicts", "HarmonyPolicy", nameof(HarmonyConflictMode.Warn),
                "Warn = only report Harmony overlaps. DisableConflictingTranspilers = also unpatch later owners " +
                "when several mods install a transpiler on the same method.");
            _harmonyDisable = Config.Bind("Conflicts", "DisablePatches", "",
                "Resolve a known overlap by hand: list 'owner@Type.Method' entries separated by ';' (for example " +
                "'com.example.othermod@PetNames.OnChangePetName'). That owner's patch is removed from the method, but only " +
                "when Aether found another mod patching it too. The names come from the Harmony overlap lines in aether_conflicts.txt.");

            var keepSessions = Config.Bind("Logs", "KeepSessions", 10,
                "How many sessions of logs to keep in Aether/Logs. Each session folder holds a live copy of LogOutput.log and " +
                "Player.log, so the log of a game that crashed natively is still there after the next launch. 0 turns it off.");
            if (keepSessions.Value > 0)
            {
                _logArchive = new LogArchive(Path.Combine(Paths.GameRootPath, "Aether", "Logs"), DateTime.Now, keepSessions.Value);
                if (!_logArchive.Start()) _logArchive = null;
                Aether.Core.Diagnostics.CrashReporter.OnCrashReportCreated += _ => SyncLogs(force: true);
            }

            Logger.LogInfo($"[Aether] {PluginVersion} starting");

            // 1. Install AssemblyResolve hook for mod dependencies living outside BepInEx/plugins
            InstallAssemblyResolveHook();

            // 2. Initialize Core Runtime
            AetherRuntime.Initialize();

            // 2b. Wire Crash Guard and Global Exception Trap to CrashReporter
            InstallCrashHandlers();

            // 3. Hook SceneManager for ISceneModInitializer
            SceneManager.sceneLoaded += OnUnitySceneLoaded;

            // NOTE: mod discovery / instantiation is deferred to frame 0 of Update().
            // Running third-party OnInitialize code here executes inside Chainloader.Start(),
            // which is the early-boot window that crashes Unity 6000.5.6f1 (see R1).

            Logger.LogInfo("=== [Aether] Core Runtime Ready (mods deferred to frame 0) ===");
        }

        private IEnumerable<string> LogSources()
        {
            yield return Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
            string player = string.Empty;
            try { player = Application.consoleLogPath; } catch { }
            if (!string.IsNullOrEmpty(player)) yield return player;
        }

        private void SyncLogs(bool force) => _logArchive?.Sync(LogSources(), force);

        private void OnApplicationQuit() => SyncLogs(force: true);

        private void Update()
        {
            if (_logArchive != null && Time.unscaledTime >= _nextLogSync)
            {
                _nextLogSync = Time.unscaledTime + LogSyncSeconds;
                SyncLogs(force: false);
            }

            // One write per mod per frame at most (binds are deferred, see AetherConfig.Bind).
            Aether.Core.Config.AetherConfig.FlushPendingSaves();

            // Execute Client initializers on the first frame once Unity environment has fully settled
            if (!_clientInitialized)
            {
                _clientInitialized = true;

                // First frame: Unity subsystems exist, so third-party mod code is safe to run.
                DiscoverAndResolveMods();

                try
                {
                    HostedBepInExPlugins.Run(AetherPaths.ModsDirectory, _hostBepInExPlugins?.Value ?? false, _conflictReport,
                        m => Logger.LogInfo(m), m => Logger.LogWarning(m), m => Logger.LogError(m));
                    WriteConflictReport();
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[Aether] Hosting BepInEx plugins failed: {ex}");
                }

                Logger.LogInfo("[Aether] Executing client mod initializers...");
                foreach (var clientInit in _clientInitializers)
                {
                    try
                    {
                        clientInit.OnInitializeClient();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[Aether] Error in client initializer '{clientInit.GetType().FullName}': {ex}");
                    }
                }

                _conflictScanPending = true;
            }

            // Harmony patches from the UI shell and mods are applied during frame 0; scan after
            // one more frame so the report sees the final patch table.
            if (_conflictScanPending)
            {
                _conflictScanFrames++;
                if (_conflictScanFrames >= 2)
                {
                    _conflictScanPending = false;
                    ScanHarmonyConflicts();
                    ScanKeybindOverlaps();
                    WriteConflictReport();
                }
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnUnitySceneLoaded;
            Aether.Core.Config.AetherConfig.FlushPendingSaves();
        }

        private void OnGUI()
        {
            if (Aether.Core.SPI.AetherServiceLocator.TryGet<Aether.API.UI.IUIService>(out var ui))
            {
                ui.RenderGUI();
            }
        }

        private void OnUnitySceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var sceneInit in _sceneInitializers)
            {
                try
                {
                    sceneInit.OnSceneLoaded(scene.name, scene.buildIndex);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[Aether] Error in scene initializer '{sceneInit.GetType().FullName}': {ex}");
                }
            }
        }

        private void ScanHarmonyConflicts()
        {
            try
            {
                HarmonyConflictMode mode = HarmonyConflictPolicy.ParseMode(_harmonyPolicy?.Value);
                _conflictReport.Notes.Add("Harmony conflict policy: " + mode);
                var rules = HarmonyDisableRules.Parse(_harmonyDisable?.Value);
                var overlaps = new List<HarmonyOverlap>();

                foreach (MethodBase method in Harmony.GetAllPatchedMethods().ToList())
                {
                    Patches? info = Harmony.GetPatchInfo(method);
                    if (info == null) continue;

                    var entries = new List<HarmonyPatchEntry>();
                    entries.AddRange(info.Prefixes.Select(p => new HarmonyPatchEntry(p.owner, HarmonyPatchKind.Prefix,
                        p.PatchMethod?.ReturnType == typeof(bool))));
                    entries.AddRange(info.Postfixes.Select(p => new HarmonyPatchEntry(p.owner, HarmonyPatchKind.Postfix)));
                    entries.AddRange(info.Transpilers.Select(p => new HarmonyPatchEntry(p.owner, HarmonyPatchKind.Transpiler)));
                    entries.AddRange(info.Finalizers.Select(p => new HarmonyPatchEntry(p.owner, HarmonyPatchKind.Finalizer)));

                    string name = $"{method.DeclaringType?.FullName}.{method.Name}";
                    HarmonyOverlap? overlap = HarmonyConflictPolicy.Analyze(name, entries, mode);
                    if (overlap == null) continue;

                    overlaps.Add(overlap);

                    foreach (string owner in overlap.OwnersToDisable.Concat(HarmonyDisableRules.OwnersToDisable(rules, name, overlap.Owners)).Distinct())
                    {
                        try
                        {
                            new Harmony(owner).Unpatch(method, HarmonyPatchType.All, owner);
                            Logger.LogWarning($"[Aether] Policy unpatched '{owner}' from {name}.");
                        }
                        catch (Exception ex)
                        {
                            _conflictReport.Warnings.Add($"Could not unpatch '{owner}' from {name}: {ex.Message}");
                        }
                    }
                }

                foreach (HarmonyOverlapSummary.Group g in HarmonyOverlapSummary.Build(overlaps))
                {
                    switch (g.Severity)
                    {
                        case HarmonyConflictSeverity.Issue: _conflictReport.Issues.Add(g.Describe()); break;
                        case HarmonyConflictSeverity.Warning: _conflictReport.Warnings.Add(g.Describe()); break;
                        default: _conflictReport.Notes.Add(g.Describe()); break;
                    }
                }
            }
            catch (Exception ex)
            {
                _conflictReport.Warnings.Add("Harmony scan failed: " + ex.Message);
            }
        }

        private void ScanKeybindOverlaps()
        {
            try
            {
                var bindings = KeybindOverlaps.ScanDirectory(Paths.ConfigPath, id =>
                    Chainloader.PluginInfos.TryGetValue(id, out PluginInfo? info) && info.Metadata != null ? info.Metadata.Name : id);
                foreach (string line in KeybindOverlaps.Describe(bindings))
                {
                    _conflictReport.Warnings.Add(line);
                    Logger.LogWarning("[Aether] " + line);
                }
            }
            catch (Exception ex)
            {
                _conflictReport.Warnings.Add("Keybind scan failed: " + ex.Message);
            }
        }

        private void WriteConflictReport()
        {
            _conflictReport.Write(Path.Combine(Paths.ConfigPath, "aether_conflicts.txt"));
        }

        private void InstallAssemblyResolveHook()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var reqName = new AssemblyName(args.Name);
                // General dependency resolution for mods living outside BepInEx/plugins.
                foreach (string dir in AetherPaths.AssemblySearchDirectories)
                {
                    string candidate = Path.Combine(dir, reqName.Name + ".dll");
                    if (File.Exists(candidate))
                    {
                        try { return Assembly.LoadFrom(candidate); } catch { }
                    }
                }
                return null;
            };
        }

        private void InstallCrashHandlers()
        {
            // 1. Listen to CrashGuard suspension events
            Aether.Core.Diagnostics.CrashGuard.OnCrashIntercepted += (modId, hook, ex, suspended) =>
            {
                if (suspended && !Aether.Core.Diagnostics.CrashReporter.HasActiveCrash)
                {
                    var modNames = new List<string>();
                    foreach (var m in LoadedMods) modNames.Add(m.Id);
                    Aether.Core.Diagnostics.CrashReporter.ReportFatal(ex, modId, hook, modNames);
                }
            };

            // 2. Global Unity unhandled exception trap
            Application.logMessageReceived += (condition, stackTrace, type) =>
            {
                if (type == LogType.Exception && !Aether.Core.Diagnostics.CrashReporter.HasActiveCrash)
                {
                    var modNames = new List<string>();
                    foreach (var m in LoadedMods) modNames.Add(m.Id);

                    string? blamed = Aether.Core.Diagnostics.CrashReporter.BlameMod(stackTrace, modNames);
                    if (!string.IsNullOrEmpty(blamed))
                    {
                        // Log-only exception from a mod: write the report but do not hijack the screen.
                        // The fullscreen overlay is reserved for exceptions CrashGuard actually intercepted.
                        Aether.Core.Diagnostics.CrashReporter.ReportFatal(
                            "Unity.UnhandledException",
                            condition,
                            stackTrace,
                            blamed,
                            "UnityEngineLoop",
                            modNames,
                            requestOverlay: false);
                    }
                }
            };
        }

        private void DiscoverAndResolveMods()
        {
            var graph = new DependencyGraph();
            var candidates = new List<(AetherModMetadata Meta, Assembly Asm)>();

            // Always add Aether Core as root candidate
            var aetherMeta = new AetherModMetadata
            {
                Id = "aether",
                Name = "Aether Core",
                Version = SemVer.SemanticVersion.Parse(PluginVersion),
                Author = "Ibrahim García",
                Description = "Mod loader and shared services for Block Story.",
                SourcePath = typeof(AetherBootstrap).Assembly.Location
            };
            graph.AddCandidate(aetherMeta);

            // Scan Aether/Mods first (the user-facing drop folder), then BepInEx/plugins for
            // backward compatibility and for the core runtime. The same assembly name is only
            // loaded once, even if a user copied a DLL to both locations.
            var seenAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string scanDir in new[] { AetherPaths.ModsDirectory, Paths.PluginPath })
            {
                if (string.IsNullOrEmpty(scanDir) || !Directory.Exists(scanDir)) continue;
                _conflictReport.ScanRoots.Add(scanDir);

                foreach (var dllPath in Directory.GetFiles(scanDir, "*.dll", SearchOption.AllDirectories))
                {
                    string assemblyName = Path.GetFileNameWithoutExtension(dllPath);
                    if (!seenAssemblies.Add(assemblyName))
                    {
                        _conflictReport.Notes.Add($"Duplicate assembly skipped: {assemblyName} ({dllPath})");
                        continue;
                    }

                    try
                    {
                        var asm = Assembly.LoadFrom(dllPath);
                        var attr = asm.GetCustomAttribute<AetherModAttribute>();
                        if (attr != null)
                        {
                            var meta = new AetherModMetadata
                            {
                                Id = attr.Id,
                                Name = attr.Name ?? attr.Id,
                                Version = SemVer.SemanticVersion.Parse(attr.Version),
                                Author = attr.Author ?? "Unknown",
                                Description = attr.Description ?? string.Empty,
                                SourcePath = dllPath
                            };
                            ModDependencyParser.ParseInto(attr.Depends, meta.Depends);
                            ModDependencyParser.ParseInto(attr.Suggests, meta.Suggests);
                            ModDependencyParser.ParseIdListInto(attr.LoadAfter, meta.LoadAfter);
                            ModDependencyParser.ParseIdListInto(attr.Breaks, meta.Breaks);
                            try
                            {
                                graph.AddCandidate(meta);
                                candidates.Add((meta, asm));
                            }
                            catch (InvalidOperationException ex)
                            {
                                _conflictReport.Issues.Add(ex.Message);
                                Logger.LogError("[Aether] " + ex.Message);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore unmanaged or non-.NET assemblies
                    }
                }
            }

            try
            {
                LoadedMods = graph.ResolveLoadOrderLenient(out var excluded);
                foreach (var left in excluded)
                {
                    string msg = $"Mod '{left.Mod.Id}' was not loaded: it {left.Reason}.";
                    _conflictReport.Issues.Add(msg);
                    Logger.LogWarning("[Aether] " + msg);
                }
            }
            catch (Exception ex)
            {
                _conflictReport.Issues.Add(ex.Message);
                WriteConflictReport();
                Logger.LogError($"[Aether] Dependency resolution failed (cycle or incompatible mods); no Aether mods will be loaded: {ex}");
                return;
            }

            foreach (var mod in LoadedMods)
            {
                _conflictReport.Resolved.Add($"{mod.Id} v{mod.Version} -> {mod.SourcePath}");
                foreach (string afterId in mod.LoadAfter)
                {
                    if (!LoadedMods.Any(m => string.Equals(m.Id, afterId, StringComparison.OrdinalIgnoreCase)))
                        _conflictReport.Warnings.Add($"LoadAfter target '{afterId}' is not installed (soft order ignored) for '{mod.Id}'.");
                }
            }
            WriteConflictReport();

            Logger.LogInfo($"[Aether] Resolved {LoadedMods.Count} mods in topological order:");
            foreach (var mod in LoadedMods)
            {
                Logger.LogInfo($"  -> {mod.Name} ({mod.Id} v{mod.Version})");
            }

            // Instantiate and run entrypoints in topological order. A failure in one mod must never
            // prevent the remaining mods from loading.
            foreach (var mod in LoadedMods)
            {
                var match = candidates.Find(c => c.Meta.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase));
                if (match.Asm == null) continue;

                Type[] types = SafeGetTypes(match.Asm, mod.Id);
                for (int t = 0; t < types.Length; t++)
                {
                    Type type = types[t];
                    if (type == null || type.IsAbstract || !type.IsClass) continue;

                    // Only entrypoint types. Without this filter the loader tried to instantiate
                    // compiler-generated state machines, attributes and helper engines, filling
                    // the log with MissingMethodException noise (e.g. a compiler-generated coroutine type).
                    if (!typeof(IPreLaunchEntrypoint).IsAssignableFrom(type) &&
                        !typeof(IModInitializer).IsAssignableFrom(type) &&
                        !typeof(IClientModInitializer).IsAssignableFrom(type) &&
                        !typeof(ISceneModInitializer).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    try
                    {
                        object? instance = Activator.CreateInstance(type);
                        if (instance == null) continue;

                        // One instance per type, shared by every interface it implements, so that
                        // OnInitialize state is visible to OnInitializeClient.
                        if (instance is IPreLaunchEntrypoint preLaunch) preLaunch.OnPreLaunch();
                        if (instance is IModInitializer initializer) initializer.OnInitialize();
                        if (instance is IClientModInitializer clientInitializer) _clientInitializers.Add(clientInitializer);
                        if (instance is ISceneModInitializer sceneInitializer) _sceneInitializers.Add(sceneInitializer);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[Aether] Entrypoint '{type.FullName}' in mod '{mod.Id}' failed: {ex}");
                    }
                }
            }
        }

        /// <summary>
        /// asm.GetTypes() throws ReflectionTypeLoadException when a single type fails to load, which
        /// used to abort every remaining mod. Returns only the types that loaded successfully.
        /// </summary>
        private Type[] SafeGetTypes(Assembly asm, string modId)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                int loaded = 0;
                if (ex.Types != null)
                {
                    for (int i = 0; i < ex.Types.Length; i++)
                    {
                        if (ex.Types[i] != null) loaded++;
                    }
                }

                Logger.LogWarning($"[Aether] Mod '{modId}' has {ex.LoaderExceptions?.Length ?? 0} unloadable type(s); continuing with {loaded}.");
                return ex.Types ?? Array.Empty<Type>();
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Aether] Could not enumerate types of mod '{modId}': {ex.Message}");
                return Array.Empty<Type>();
            }
        }
    }
}
