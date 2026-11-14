namespace Aether.Installer;

public sealed class InstallOptions
{
    public required string GameDir { get; init; }

    /// <summary>A release package or a checkout; null = the checkout above this program.</summary>
    public string? SourceDir { get; init; }

    /// <summary>Official mods to install, by name (see <see cref="InstallSelection"/>).</summary>
    public IReadOnlyCollection<string> SelectedMods { get; init; } = Array.Empty<string>();

    public bool NoUi { get; init; }
    public bool DryRun { get; init; }

    /// <summary>Linux, experimental: also write aether-launch.</summary>
    public bool WithLauncher { get; init; }

    /// <summary>Also keep a copy of the aether command line tool in Aether/aether. Off by default: players use the window; the tool is for mod makers (and the experimental launcher needs it).</summary>
    public bool WithCli { get; init; }

    /// <summary>The self-contained CLI to keep in Aether/aether; null = look for it (see <see cref="CliInstaller.FindSource"/>).</summary>
    public string? CliSource { get; init; }
}

public sealed class InstallResult
{
    public int Copied { get; init; }
    public int Required { get; init; }
    public bool Success => Copied == Required;
}

/// <summary>
/// Puts the Aether assemblies into a game folder. BepInEx itself is set up by the caller first (it needs a yes/no from the
/// player); everything from the folder layout onwards happens here and is reported through <paramref name="report"/>.
/// </summary>
public static class InstallRunner
{
    public static InstallResult Run(InstallOptions o, Action<Check> report)
    {
        var ops = new FileOps(o.DryRun);
        string gameDir = o.GameDir;
        string repoRoot = o.SourceDir ?? PayloadLocator.FindRepoRoot();
        string coreDir = Path.Combine(gameDir, InstallLayout.CoreDir);
        string modsDir = Path.Combine(gameDir, InstallLayout.ModsDir);

        void Say(CheckLevel level, string badge, string message) => report(new Check(level, badge, message));

        ops.EnsureDir(coreDir);
        ops.EnsureDir(modsDir);

        // Core always; official mods only when asked. A mod that is already installed is kept in step with the loader.
        var copies = new List<(string Dll, string DestDir, bool Optional)>();
        foreach (string dll in AetherAssemblies.Core) copies.Add((dll, coreDir, false));
        if (!o.NoUi)
            foreach (string dll in AetherAssemblies.Ui) copies.Add((dll, coreDir, false));
        else
            Say(CheckLevel.Info, "INFO", "--no-ui: the UI package (Aether.UI.dll) is not installed or updated. The official mods need it; there is no F10 menu without it.");
        var chosen = o.SelectedMods.Select(InstallSelection.DllName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string dll in AetherAssemblies.OfficialMods)
        {
            if (chosen.Contains(dll)) copies.Add((dll, modsDir, false));
            else if (File.Exists(Path.Combine(modsDir, dll))) copies.Add((dll, modsDir, true));
        }

        string backupRoot = Path.Combine(gameDir, "Aether", "backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        int copied = 0, required = copies.Count(c => !c.Optional);
        foreach (var (dll, destDir, optional) in copies)
        {
            string? foundPath = PayloadLocator.LocateCompiledDll(repoRoot, dll);
            if (foundPath == null)
            {
                if (optional)
                    Say(CheckLevel.Info, "INFO", $"{dll} is installed, but this package does not include its update. Download the new zip and run 'aether add <zip>' to upgrade it.");
                else
                    Say(CheckLevel.Warn, "WARN", $"Could not find '{dll}' under '{repoRoot}'. Use a release package or run 'dotnet build -c Release' first.");
                continue;
            }

            string destPath = Path.Combine(destDir, dll);
            string where = destDir == coreDir ? "BepInEx/plugins/Aether" : "Aether/Mods";
            if (File.Exists(destPath) && !FileOps.SameContent(foundPath, destPath))
                ops.Backup(destPath, backupRoot);

            ops.Copy(foundPath, destPath);
            var sizeKb = new FileInfo(foundPath).Length / 1024.0;
            Say(CheckLevel.Ok, o.DryRun ? "DRY" : "COPY", $"{(o.DryRun ? "Would install" : "Installed")} {dll,-28} ({sizeKb:F1} KB) -> {where}");
            if (!optional) copied++;
        }

        // Enforce the split layout: mods live in Aether/Mods, core lives in BepInEx/plugins/Aether.
        if (Directory.Exists(coreDir))
        {
            foreach (string stale in Directory.GetFiles(coreDir, "Aether.Mods.*.dll"))
                ops.Delete(stale);
        }
        foreach (string coreDll in AetherAssemblies.All)
        {
            ops.Delete(Path.Combine(modsDir, coreDll));
        }

        void Retire(string dir, string name, string why)
        {
            string old = Path.Combine(dir, name);
            if (!File.Exists(old)) return;
            ops.Backup(old, backupRoot);
            ops.Delete(old);
            Say(CheckLevel.Info, o.DryRun ? "DRY" : "INFO", $"{(o.DryRun ? "Would retire" : "Retired")} {name} ({why}; backup kept)");
        }
        foreach (string retired in AetherAssemblies.RetiredMods) Retire(modsDir, retired, "renamed or parked");
        foreach (string retired in AetherAssemblies.RetiredCore) Retire(coreDir, retired, "no longer part of Aether");
        LegacyFiles.Remove(gameDir, ops);

        // Ensure config and crash-reports directories exist
        ops.EnsureDir(Path.Combine(gameDir, "BepInEx", "config", "aether"));
        ops.EnsureDir(Path.Combine(gameDir, "BepInEx", "crash-reports"));

        if (!o.DryRun)
            BepInExSetup.PostInstall(gameDir, (b, m) => Say(CheckLevel.Info, b, m));
        if (o.WithCli || o.WithLauncher)
            CliInstaller.Install(gameDir, o.CliSource ?? CliInstaller.FindSource(), o.DryRun, report);

        // Experimental and off by default: BepInEx's run_bepinex.sh stays the launcher unless asked for.
        if (o.WithLauncher)
            Launcher.Install(gameDir, o.DryRun, (b, m) => Say(CheckLevel.Info, b, m));

        Say(CheckLevel.Info, "DONE", $"Successfully deployed {copied}/{required} Aether assemblies (core -> BepInEx/plugins/Aether, mods -> Aether/Mods)");
        if (!AetherAssemblies.OfficialMods.Any(d => File.Exists(Path.Combine(modsDir, d))))
            Say(CheckLevel.Info, "NEXT", "No mods installed yet. Mods are optional and separate: add the ones you want from Aether Monitor (Add mod…) or copy their .dll into Aether/Mods (from a checkout: --with-mods).");
        return new InstallResult { Copied = copied, Required = required };
    }
}
