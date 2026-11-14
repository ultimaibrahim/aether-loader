using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Aether.Loader.Diagnostics;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Aether.Loader.Hosting
{
    /// <summary>
    /// Lets plain BepInEx plugins live in Aether/Mods. BepInEx's Chainloader only scans
    /// BepInEx/plugins, so Aether starts these plugins itself, on frame 0 (never during the early
    /// boot window, see R1), registering them in <see cref="Chainloader.PluginInfos"/> exactly like the
    /// Chainloader does. Controlled by [Hosting] HostBepInExPlugins (default true).
    /// </summary>
    public static class HostedBepInExPlugins
    {
        public sealed class Result
        {
            public int Started;
            public int Found;
        }

        public static Result Run(string modsDirectory, bool enabled, ModConflictReport report, Action<string> logInfo,
            Action<string> logWarning, Action<string> logError)
        {
            var result = new Result();
            if (string.IsNullOrEmpty(modsDirectory) || !Directory.Exists(modsDirectory)) return result;

            var typesByGuid = new Dictionary<string, (Type Type, BepInPlugin Meta)>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<HostedPluginCandidate>();
            string processName = ProcessName();

            foreach (string dll in Directory.GetFiles(modsDirectory, "*.dll", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                Assembly asm;
                try { asm = Assembly.LoadFrom(dll); }
                catch { continue; } // native or non-.NET file

                // An Aether mod is started by the loader's own entrypoint pass, never as a BepInEx plugin. Reading its types here
                // only produces load errors when something it needs (such as the UI package) is absent.
                if (IsAetherMod(asm)) continue;

                int before = candidates.Count;
                foreach (Type type in SafeTypes(asm))
                {
                    try
                    {
                        if (type == null || type.IsAbstract || !typeof(BaseUnityPlugin).IsAssignableFrom(type)) continue;
                        var meta = (BepInPlugin?)Attribute.GetCustomAttribute(type, typeof(BepInPlugin));
                        if (meta == null) continue;

                        // Honour [BepInProcess] like the Chainloader.
                        var processes = Attribute.GetCustomAttributes(type, typeof(BepInProcess)).Cast<BepInProcess>().ToList();
                        if (processes.Count > 0 && !processes.Any(p => string.Equals(
                                Path.GetFileNameWithoutExtension(p.ProcessName), processName, StringComparison.OrdinalIgnoreCase)))
                        {
                            report.Notes.Add($"Hosted plugin '{meta.GUID}' skipped: [BepInProcess] does not match '{processName}'.");
                            continue;
                        }

                        var c = new HostedPluginCandidate
                        {
                            Guid = meta.GUID,
                            Name = meta.Name,
                            Version = meta.Version?.ToString() ?? "0.0.0",
                            Location = dll
                        };
                        foreach (BepInDependency d in Attribute.GetCustomAttributes(type, typeof(BepInDependency)).Cast<BepInDependency>())
                        {
                            if ((d.Flags & BepInDependency.DependencyFlags.HardDependency) != 0)
                                c.HardDependencies[d.DependencyGUID] = d.MinimumVersion?.ToString();
                            else
                                c.SoftDependencies.Add(d.DependencyGUID);
                        }
                        foreach (BepInIncompatibility i in Attribute.GetCustomAttributes(type, typeof(BepInIncompatibility)).Cast<BepInIncompatibility>())
                            c.Incompatibilities.Add(i.IncompatibilityGUID);

                        if (typesByGuid.ContainsKey(meta.GUID)) { /* planner reports the duplicate */ }
                        else typesByGuid[meta.GUID] = (type, meta);
                        candidates.Add(c);
                    }
                    catch (Exception ex)
                    {
                        // One broken plugin (e.g. built against API this install lacks) must not stop the rest.
                        string first = ex.Message.Split('\n')[0];
                        string msg = $"Plugin type '{type?.FullName}' in {Path.GetFileName(dll)} cannot be loaded and was skipped: {ex.GetType().Name}: {first}";
                        report.Issues.Add(msg);
                        logWarning("[Aether] " + msg);
                    }
                }

                if (candidates.Count == before)
                    report.Notes.Add($"{Path.GetFileName(dll)} in Aether/Mods has no [AetherMod] and no BepInEx plugin ([BepInPlugin] on a BaseUnityPlugin), so there is nothing to run. It is only available as a library.");
            }

            result.Found = candidates.Count;
            if (candidates.Count == 0) return result;

            if (!enabled)
            {
                foreach (HostedPluginCandidate c in candidates)
                    report.Warnings.Add(
                        $"'{c.Name}' ({c.Guid}) is a BepInEx plugin inside Aether/Mods and will NOT run. " +
                        "Set [Hosting] HostBepInExPlugins = true in BepInEx/config/dev.ultimaibrahim.aether.cfg, " +
                        "or move it to BepInEx/plugins.");
                return result;
            }

            var loaded = Chainloader.PluginInfos.ToDictionary(
                kv => kv.Key, kv => kv.Value.Metadata?.Version?.ToString() ?? "0.0.0", StringComparer.OrdinalIgnoreCase);

            HostedPluginPlan plan = HostedPluginPlanner.Plan(candidates, loaded);
            foreach (var rejected in plan.Rejected)
            {
                string msg = $"Hosted plugin '{rejected.Key.Name}' ({rejected.Key.Guid}) not started: {rejected.Value}.";
                report.Issues.Add(msg);
                logWarning("[Aether] " + msg);
            }

            GameObject? manager = Chainloader.ManagerObject;
            if (manager == null)
            {
                report.Issues.Add("BepInEx manager object is missing; hosted plugins cannot start.");
                return result;
            }

            foreach (HostedPluginCandidate c in plan.Order)
            {
                (Type type, BepInPlugin meta) = typesByGuid[c.Guid];
                PluginInfo? info = null;
                try
                {
                    info = new PluginInfo();
                    SetInternal(info, nameof(PluginInfo.Metadata), meta);
                    SetInternal(info, nameof(PluginInfo.Location), c.Location);
                    SetInternal(info, nameof(PluginInfo.Dependencies),
                        Attribute.GetCustomAttributes(type, typeof(BepInDependency)).Cast<BepInDependency>().ToList());
                    SetInternal(info, nameof(PluginInfo.Processes),
                        Attribute.GetCustomAttributes(type, typeof(BepInProcess)).Cast<BepInProcess>().ToList());
                    SetInternal(info, nameof(PluginInfo.Incompatibilities),
                        Attribute.GetCustomAttributes(type, typeof(BepInIncompatibility)).Cast<BepInIncompatibility>().ToList());

                    // Register first: BaseUnityPlugin's constructor looks itself up here.
                    Chainloader.PluginInfos[c.Guid] = info;
                    // Before AddComponent: a plugin may register items from Awake.
                    ExternalModCatalog.AddHosted(c.Guid, c.Name, c.Version, type.Assembly.GetName().Name ?? string.Empty, c.Location);
                    var instance = (BaseUnityPlugin)manager.AddComponent(type);
                    SetInternal(info, nameof(PluginInfo.Instance), instance);

                    result.Started++;
                    report.Resolved.Add($"{c.Guid} v{c.Version} (hosted BepInEx plugin) -> {c.Location}");
                    logInfo($"[Aether] Hosted BepInEx plugin started: {c.Name} ({c.Guid} v{c.Version})");
                }
                catch (Exception ex)
                {
                    Chainloader.PluginInfos.Remove(c.Guid);
                    ExternalModCatalog.RemoveHosted(c.Guid);
                    report.Issues.Add($"Hosted plugin '{c.Guid}' failed to start: {ex.GetType().Name}: {ex.Message}");
                    logError($"[Aether] Hosted plugin '{c.Guid}' failed to start: {ex}");
                }
            }

            return result;
        }

        private static bool IsAetherMod(Assembly asm)
        {
            try { return asm.GetCustomAttribute<Aether.Loader.Discovery.AetherModAttribute>() != null; }
            catch { return false; }
        }

        private static string ProcessName()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().ProcessName; }
            catch { return string.Empty; }
        }

        private static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
            catch { return Array.Empty<Type>(); }
        }

        private static void SetInternal(object target, string property, object? value)
        {
            PropertyInfo prop = target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            MethodInfo setter = prop.GetSetMethod(true)!;
            setter.Invoke(target, new[] { value });
        }
    }
}
