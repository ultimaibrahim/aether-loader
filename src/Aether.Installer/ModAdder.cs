namespace Aether.Installer;

/// <summary>
/// What adding a mod package would do: inspected and routed, nothing copied yet. Disposing removes the temporary
/// folder the package was unpacked into.
/// </summary>
public sealed class AddPlan : IDisposable
{
    private readonly string? _workDir;

    internal AddPlan(string? workDir) => _workDir = workDir;

    /// <summary>Set when the package cannot be added at all (rejected archive, nothing installable).</summary>
    public string? Error { get; internal set; }

    /// <summary>Things the player should read before saying yes (skipped files, missing dependencies, duplicate ids...).</summary>
    public List<Check> Notes { get; } = new();

    public IReadOnlyList<RoutedFile> Routed { get; internal set; } = Array.Empty<RoutedFile>();
    public bool CanInstall => Error == null && Routed.Count > 0;
    /// <summary>The package only held files Aether already ships (Aether.Nano.dll): nothing to install, and that is not an error.</summary>
    public bool AlreadyProvided { get; set; }

    public void Dispose()
    {
        if (_workDir == null) return;
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }
}

/// <summary>Adds a third-party mod (a .dll or a .zip) to the right folder (aether add).</summary>
public static class ModAdder
{
    public static AddPlan Prepare(string gameDir, string input, bool hosted)
    {
        string work = Path.Combine(Path.GetTempPath(), "aether-add-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var plan = new AddPlan(work);

        string scanDir = Path.Combine(work, "pkg");
        if (input.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try { SafeZip.Extract(input, scanDir); }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                plan.Error = $"Rejected archive: {ex.Message}";
                return plan;
            }
        }
        else
        {
            Directory.CreateDirectory(scanDir);
            File.Copy(input, Path.Combine(scanDir, Path.GetFileName(input)));
        }

        var infos = Directory.GetFiles(scanDir, "*.dll", SearchOption.AllDirectories).Select(ModInspector.Inspect).ToList();
        foreach (ModInfo bad in infos.Where(i => i.Kind == ModKind.NotManaged))
            plan.Notes.Add(new Check(CheckLevel.Warn, "SKIP", $"{bad.FileName}: not a managed .NET assembly, ignored."));

        // A Nano mod's zip carries Aether.Nano.dll for players without Aether; Aether already ships its own copy.
        string ownNano = Path.Combine(gameDir, InstallLayout.PluginsDir, "Aether", "Aether.Nano.dll");
        bool skippedOwn = false;
        foreach (ModInfo nano in infos.Where(i => i.Kind != ModKind.NotManaged && string.Equals(i.FileName, "Aether.Nano.dll", StringComparison.OrdinalIgnoreCase)).ToList())
            if (File.Exists(ownNano))
            {
                infos.Remove(nano); skippedOwn = true;
                plan.Notes.Add(new Check(CheckLevel.Info, "INFO", "Aether.Nano.dll: Aether already has it, not copied."));
            }

        var routed = ModRouter.Route(infos.Where(i => i.Kind != ModKind.NotManaged).ToList(), hosted);
        if (hosted && routed.Any(r => r.Info.Kind == ModKind.BepInExPlugin))
            plan.Notes.Add(new Check(CheckLevel.Info, "INFO",
                "Hosted plugins only run while [Hosting] HostBepInExPlugins = true (the default) in BepInEx/config/dev.ultimaibrahim.aether.cfg."));
        if (routed.Count == 0 && skippedOwn) { plan.AlreadyProvided = true; return plan; }
        if (routed.Count == 0)
        {
            plan.Error = "No installable .NET assemblies found in the package.";
            return plan;
        }

        foreach (string problem in ModRouter.CheckDependencies(infos, InstalledAetherMods(gameDir)))
            plan.Notes.Add(new Check(CheckLevel.Warn, "WARN", problem));

        foreach (RoutedFile r in routed.Where(r => r.Info is { Kind: ModKind.AetherMod, Id: not null }))
        {
            string? existing = InstalledFileById(gameDir, r.Info.Id!);
            if (existing != null && !string.Equals(Path.GetFileName(existing), r.Info.FileName, StringComparison.OrdinalIgnoreCase))
                plan.Notes.Add(new Check(CheckLevel.Warn, "WARN",
                    $"Mod id '{r.Info.Id}' is already provided by {Path.GetRelativePath(gameDir, existing)}; both would load and the loader would report a duplicate id. Remove one."));
        }

        plan.Routed = routed;
        return plan;
    }

    public static void Apply(string gameDir, AddPlan plan, bool dryRun, Action<Check> report)
    {
        var ops = new FileOps(dryRun);
        string backupRoot = Path.Combine(gameDir, "Aether", "backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        foreach (RoutedFile r in plan.Routed)
        {
            string dest = Path.Combine(gameDir, r.DestDirRelative, r.Info.FileName);
            if (File.Exists(dest) && !FileOps.SameContent(r.Info.FilePath, dest)) ops.Backup(dest, backupRoot);
            ops.Copy(r.Info.FilePath, dest);
            report(new Check(CheckLevel.Ok, dryRun ? "DRY" : "COPY",
                $"{(dryRun ? "Would install" : "Installed")} {r.Info.FileName} -> {r.DestDirRelative.Replace('\\', '/')}"));
        }
    }

    public static string? InstalledFileById(string gameDir, string id)
    {
        foreach (string root in ScanRoots(gameDir))
        {
            foreach (string dll in Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories))
            {
                ModInfo info = ModInspector.Inspect(dll);
                if (info.Kind == ModKind.AetherMod && string.Equals(info.Id, id, StringComparison.OrdinalIgnoreCase)) return dll;
            }
        }
        return null;
    }

    /// <summary>id -> version of every [AetherMod] DLL already installed.</summary>
    public static Dictionary<string, string> InstalledAetherMods(string gameDir)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in ScanRoots(gameDir))
        {
            foreach (string dll in Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories))
            {
                ModInfo info = ModInspector.Inspect(dll);
                if (info is { Kind: ModKind.AetherMod, Id: not null }) result[info.Id] = info.Version ?? "0.0.0";
            }
        }
        // The loader registers itself as the pseudo-mod "aether".
        result.TryAdd("aether", Aether.Loader.AetherBootstrap.PluginVersion);
        return result;
    }

    private static IEnumerable<string> ScanRoots(string gameDir) =>
        new[] { Path.Combine(gameDir, InstallLayout.ModsDir), Path.Combine(gameDir, InstallLayout.PluginsDir) }.Where(Directory.Exists);
}
