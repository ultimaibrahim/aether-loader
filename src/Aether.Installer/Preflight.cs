namespace Aether.Installer;

/// <summary>Result of the pre-launch check: what would stop Aether mods from loading, found without starting the game.</summary>
public sealed class PreflightReport
{
    /// <summary>Process exit code of <c>aether preflight</c> when the game should not start.</summary>
    public const int BlockedExitCode = 3;

    public List<string> Problems { get; } = new();
    public int ModsChecked { get; set; }
    public bool Blocked => Problems.Count > 0;

    /// <summary>The notice shown to the player (text; a window or the terminal adds the buttons).</summary>
    public string BuildMessage()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Block Story was not started because some Aether mods cannot load:");
        sb.AppendLine();
        foreach (string problem in Problems) sb.AppendLine("  - " + problem);
        sb.AppendLine();
        sb.Append("Install the missing mods, remove the ones that conflict, or run 'aether doctor' for details.");
        return sb.ToString();
    }
}

/// <summary>
/// Static checks that run before Block Story starts, independent of BepInEx and Unity: it reads mod manifests from the DLL
/// metadata (nothing is loaded or executed) and applies the same rules as the loader's dependency solver.
/// What only fails at run time (a mod that throws on start, Harmony patches that clash) is not visible here.
/// </summary>
public static class Preflight
{
    public static PreflightReport Check(string gameDir)
    {
        var report = new PreflightReport();
        var infos = new List<ModInfo>();
        var seenAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Same scan order as the loader: Aether/Mods first, then BepInEx/plugins; one assembly name counts once.
        foreach (string dir in new[] { Path.Combine(gameDir, InstallLayout.ModsDir), Path.Combine(gameDir, InstallLayout.PluginsDir) })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string dll in Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!seenAssemblies.Add(Path.GetFileNameWithoutExtension(dll))) continue;
                infos.Add(ModInspector.Inspect(dll));
            }
        }

        var aetherMods = infos.Where(i => i is { Kind: ModKind.AetherMod, Id: not null }).ToList();
        report.ModsChecked = aetherMods.Count;

        foreach (var group in aetherMods.GroupBy(m => m.Id!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            report.Problems.Add($"Mod id '{group.Key}' is declared by more than one file: {string.Join(", ", group.Select(m => m.FileName))}.");

        // The loader itself counts as the mod "aether" (a mod may depend on it).
        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ModInfo? loader = infos.FirstOrDefault(i => i.FileName.Equals("Aether.Loader.dll", StringComparison.OrdinalIgnoreCase));
        if (loader?.Version != null) installed["aether"] = loader.Version;

        report.Problems.AddRange(ModRouter.CheckDependencies(
            aetherMods.GroupBy(m => m.Id!, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList(), installed));
        return report;
    }
}
