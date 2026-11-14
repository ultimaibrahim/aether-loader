using Aether.Loader.Discovery;
using Aether.Loader.SemVer;

namespace Aether.Installer;

/// <summary>Where a mod file ends up, relative to the game directory.</summary>
public static class InstallLayout
{
    public static readonly string ModsDir = Path.Combine("Aether", "Mods");
    public static readonly string PluginsDir = Path.Combine("BepInEx", "plugins");
    public static readonly string CoreDir = Path.Combine("BepInEx", "plugins", "Aether");
}

public sealed record RoutedFile(ModInfo Info, string DestDirRelative, string Reason);

public static class ModRouter
{
    /// <summary>
    /// Decides the destination of every file of a package. Rules:
    ///   [AetherMod]   -> Aether/Mods
    ///   [BepInPlugin] -> BepInEx/plugins (or Aether/Mods when <c>hosted</c>: Aether starts it on frame 0)
    ///   library       -> next to the mod(s) it ships with (Aether/Mods unless the package only
    ///                    contains BepInEx plugins)
    ///   not managed   -> skipped (the caller reports it)
    /// </summary>
    public static IReadOnlyList<RoutedFile> Route(IReadOnlyList<ModInfo> files, bool hosted = false)
    {
        bool hasAether = files.Any(f => f.Kind == ModKind.AetherMod);
        bool hasBepInEx = files.Any(f => f.Kind == ModKind.BepInExPlugin);
        string pluginDir = hosted ? InstallLayout.ModsDir : InstallLayout.PluginsDir;
        string libDir = hasBepInEx && !hasAether ? pluginDir : InstallLayout.ModsDir;

        var routed = new List<RoutedFile>();
        foreach (ModInfo f in files)
        {
            switch (f.Kind)
            {
                case ModKind.AetherMod:
                    routed.Add(new RoutedFile(f, InstallLayout.ModsDir, $"Aether mod '{f.Id}' {f.Version}"));
                    break;
                case ModKind.BepInExPlugin:
                    routed.Add(new RoutedFile(f, pluginDir,
                        hosted ? $"BepInEx plugin '{f.Id}' {f.Version}, hosted by Aether" : $"BepInEx plugin '{f.Id}' {f.Version}"));
                    break;
                case ModKind.Library:
                    routed.Add(new RoutedFile(f, libDir, "shared library"));
                    break;
            }
        }
        return routed;
    }

    /// <summary>
    /// Checks the <c>Depends</c> / <c>Breaks</c> of the incoming Aether mods against what is already
    /// installed plus what the package itself ships. Returns human-readable problems.
    /// </summary>
    public static IReadOnlyList<string> CheckDependencies(
        IReadOnlyList<ModInfo> incoming,
        IReadOnlyDictionary<string, string> installedAetherMods)
    {
        var known = new Dictionary<string, string>(installedAetherMods, StringComparer.OrdinalIgnoreCase);
        foreach (ModInfo m in incoming.Where(m => m is { Kind: ModKind.AetherMod, Id: not null }))
            known[m.Id!] = m.Version ?? "0.0.0";

        var problems = new List<string>();
        foreach (ModInfo mod in incoming.Where(m => m.Kind == ModKind.AetherMod))
        {
            var depends = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ModDependencyParser.ParseInto(mod.Depends, depends);
            foreach (var (id, range) in depends)
            {
                if (!known.TryGetValue(id, out string? have))
                {
                    problems.Add($"'{mod.Id}' needs '{id}' ({range}), which is not installed.");
                    continue;
                }

                if (SemanticVersion.TryParse(have, out SemanticVersion? v) && v != null &&
                    !VersionRange.Parse(range).IsSatisfiedBy(v))
                    problems.Add($"'{mod.Id}' needs '{id}' {range}, but {have} is installed.");
            }

            var breaks = new List<string>();
            ModDependencyParser.ParseIdListInto(mod.Breaks, breaks);
            foreach (string id in breaks.Where(known.ContainsKey))
                problems.Add($"'{mod.Id}' declares it breaks '{id}', which is installed.");
        }

        return problems;
    }
}
