using System.Diagnostics;
using Aether.Installer.Lattice;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Aether.Installer.App;

/// <summary>The optional monitor: status, mods, crashes and logs. It changes the game folder only when asked
/// (Add mod, Catalog install).</summary>
public sealed partial class MonitorWindow : Window
{
    private const int MaxLogBytes = 256 * 1024;
    private readonly string? _game;
    private readonly List<string> _logPaths = new();
    private readonly List<LatticeMod> _catalog = new();
    private LatticeSnapshot? _snap;
    private AetherUpdate? _update;

    public MonitorWindow()
    {
        InitializeComponent();
        _game = ResolveGameDir();
        GameLabel.Text = _game ?? "Block Story folder not found. Start the monitor with --game-dir <path>.";
        RefreshButton.Click += (_, _) => Reload();
        AddModButton.Click += OnAddMod;
        LogFiles.SelectionChanged += (_, _) => ShowSelectedLog();
        OpenLogs.Click += (_, _) => { if (_game != null) Opener.Folder(Path.Combine(_game, "Aether", "Logs")); };
        OpenMods.Click += (_, _) => { if (_game != null) Opener.Folder(Path.Combine(_game, InstallLayout.ModsDir)); };
        CatalogRefresh.Click += async (_, _) => await CheckOnline();
        CatalogInstall.Click += OnCatalogInstall;
        UpdateButton.Click += OnUpdateAether;
        Opened += async (_, _) =>
        {
            Reload();
            // The one place Aether goes online, and only from this window (never from the game). Off if Aether/no-update-check exists.
            if (_game != null && !File.Exists(Path.Combine(_game, "Aether", "no-update-check"))) await CheckOnline();
            else CatalogStatus.Text = "Online checks are off. Press “Refresh catalog” to look.";
        };
    }

    /// <summary>Pick a .zip or .dll, show what would happen, and install it into the right folder (the same code as 'aether add').</summary>
    private async void OnAddMod(object? sender, RoutedEventArgs e)
    {
        if (_game == null || !Directory.Exists(_game)) { await Dialogs.Message(this, "Add mod", "Block Story folder not found."); return; }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a mod (.zip or .dll)", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Mods") { Patterns = new[] { "*.zip", "*.dll" } } },
        });
        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path == null) return;

        string game = _game;
        using AddPlan plan = await Task.Run(() => ModAdder.Prepare(game, path, hosted: true));
        if (!plan.CanInstall) { await Dialogs.Message(this, "Add mod", plan.Error ?? (plan.AlreadyProvided ? "Aether already includes Aether Nano: nothing to install." : "Nothing to install.")); return; }

        var text = new List<string>();
        text.AddRange(plan.Notes.Select(n => n.Message));
        if (text.Count > 0) text.Add(string.Empty);
        text.Add("This will install:");
        text.AddRange(plan.Routed.Select(r => $"  {r.Info.FileName}  →  {r.DestDirRelative.Replace('\\', '/')}"));
        if (!await Dialogs.Confirm(this, "Add mod", string.Join("\n", text), "Install")) return;

        await Task.Run(() => ModAdder.Apply(game, plan, dryRun: false, _ => { }));
        Reload();
        await Dialogs.Message(this, "Add mod", "Installed. Start Block Story to use it.");
    }

    /// <summary>Asks GitHub for a newer Aether and Lattice for the signed catalog, then fills the banner and the Catalog tab.</summary>
    private async Task CheckOnline()
    {
        if (_game == null || !Directory.Exists(_game)) return;
        string game = _game;
        CatalogStatus.Text = "Checking…";
        var (snap, error, update) = await Task.Run(() =>
        {
            var fetcher = new HttpLatticeFetcher();
            var s = LatticeStore.Load(game, fetcher, LatticeKey.Default, DateTime.UtcNow, out string? err);
            var u = UpdateChecker.Check(fetcher, UpdateChecker.InstalledVersion(game) ?? Aether.Loader.AetherBootstrap.PluginVersion,
                UpdateChecker.CurrentRid(), UpdateChecker.IsSelfContained());
            return (s, err, u);
        });
        _snap = snap; _update = update;

        UpdateBanner.IsVisible = update != null;
        if (update != null)
        {
            UpdateText.Text = $"Aether {update.Version} is available.";
            UpdateButton.Content = update.ZipUrl != null ? "Update Aether" : "Open download page";
        }
        FillCatalog(error);
    }

    private void FillCatalog(string? error)
    {
        CatalogLines.Items.Clear();
        _catalog.Clear();
        if (_snap == null) { CatalogStatus.Text = error ?? "The catalog could not be read."; CatalogTab.Header = "Catalog"; return; }

        var installed = LatticeInstaller.ReadInstalled(_game!, _snap);
        var outdated = LatticePlanner.Outdated(_snap, installed).ToDictionary(o => o.Slug, StringComparer.OrdinalIgnoreCase);
        foreach (var m in _snap.Index.Mods)
        {
            var newest = m.Versions.Where(v => !_snap.RevokedHashes.Contains(v.Sha256)).OrderByDescending(v => v.Label, Comparer<string>.Create(LatticeIndex.Compare)).FirstOrDefault();
            string status = outdated.TryGetValue(m.Slug, out var o) ? (o.Revoked ? $"installed {o.Installed} was revoked: update" : $"update available (installed {o.Installed})")
                : installed.TryGetValue(m.Slug, out var have) ? $"installed {have.Version}" : "not installed";
            _catalog.Add(m);
            CatalogLines.Items.Add(Line($"{m.Slug,-28} {newest?.Label ?? "—",-12} {status}", outdated.ContainsKey(m.Slug) ? CheckLevel.Warn : installed.ContainsKey(m.Slug) ? CheckLevel.Ok : CheckLevel.Info));
        }
        CatalogTab.Header = outdated.Count > 0 ? $"Catalog ({outdated.Count} update{(outdated.Count == 1 ? "" : "s")})" : "Catalog";
        CatalogStatus.Text = _snap.Offline ? (_snap.Warning ?? "Offline.")
            : _snap.Index.Mods.Count == 0 ? "The catalog is empty for now." : $"Catalog verified (serial {_snap.IndexSerial}).";
    }

    private async void OnCatalogInstall(object? sender, RoutedEventArgs e)
    {
        int i = CatalogLines.SelectedIndex;
        if (_game == null || _snap == null || i < 0 || i >= _catalog.Count) { CatalogStatus.Text = "Pick a mod in the list first."; return; }
        string game = _game; var snap = _snap; string slug = _catalog[i].Slug;

        var plan = LatticePlanner.Resolve(snap, LatticeInstaller.ReadInstalled(game, snap), slug, DateTime.UtcNow, upgrade: true);
        if (plan.Errors.Count > 0) { await Dialogs.Message(this, "Catalog", string.Join("\n", plan.Errors)); return; }
        if (!plan.CanInstall) { await Dialogs.Message(this, "Catalog", string.Join("\n", plan.Notes.Select(n => n.Message).DefaultIfEmpty("Already up to date."))); return; }

        var text = new List<string>();
        text.AddRange(plan.Notes.Select(n => n.Message));
        if (text.Count > 0) text.Add(string.Empty);
        text.Add("This will download, verify (signature, SHA-256, revocations) and install:");
        text.AddRange(plan.Steps.Select(v => $"  {v.Slug} {v.Label}  [{v.Level}]  {v.Size / 1024} KB"));
        if (!await Dialogs.Confirm(this, "Catalog", string.Join("\n", text), "Install")) return;

        CatalogStatus.Text = "Installing…";
        var log = new List<Check>();
        bool ok = await Task.Run(() => LatticeInstaller.Install(game, plan, snap, new HttpLatticeFetcher(), dryRun: false, log.Add));
        Reload();
        FillCatalog(null);
        string last = log.LastOrDefault(c => c.Level is CheckLevel.Fail or CheckLevel.Warn)?.Message ?? "Done.";
        await Dialogs.Message(this, "Catalog", ok ? "Installed. Start Block Story to use it." : "Nothing was installed. " + last);
    }

    /// <summary>Downloads the new Aether package, checks its SHA-256, and starts the new installer (the running window is never overwritten).</summary>
    private async void OnUpdateAether(object? sender, RoutedEventArgs e)
    {
        var update = _update;
        if (update == null || _game == null) return;
        if (update.ZipUrl == null) { Opener.Url(update.PageUrl); return; }
        if (!await Dialogs.Confirm(this, "Update Aether", $"Download Aether {update.Version}, check it and open its installer?\nThis window will close.", "Update")) return;

        UpdateButton.IsEnabled = false; UpdateText.Text = "Downloading…";
        string work = Path.Combine(Path.GetTempPath(), "aether-update-" + Guid.NewGuid().ToString("N"));
        string game = _game;
        string? error = null;
        string? exe = await Task.Run(() => UpdateChecker.Fetch(update, new HttpLatticeFetcher(), work, out error));
        if (exe == null)
        {
            UpdateButton.IsEnabled = true; UpdateText.Text = $"Aether {update.Version} is available.";
            await Dialogs.Message(this, "Update Aether", error ?? "The update could not be fetched.");
            return;
        }
        Process.Start(new ProcessStartInfo(exe, $"--game-dir \"{game}\"") { UseShellExecute = false });
        Close();
    }

    /// <summary>--game-dir, else the folder this copy was installed into (game/Aether/Monitor/aether-monitor), else the usual Steam places.</summary>
    private static string? ResolveGameDir()
    {
        if (Program.GameDirArgument != null) return Program.GameDirArgument;
        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        DirectoryInfo? monitor = exeDir == null ? null : new DirectoryInfo(exeDir);
        if (monitor?.Name == MonitorInstaller.FolderName && monitor.Parent?.Name == "Aether" && monitor.Parent.Parent != null)
            return monitor.Parent.Parent.FullName;
        return GameLocator.Detect();
    }

    private void Reload()
    {
        StatusLines.Items.Clear();
        ModLines.Items.Clear();
        LogFiles.Items.Clear();
        _logPaths.Clear();
        LogText.Text = string.Empty;
        if (_game == null || !Directory.Exists(_game)) return;

        CheckReport report = Doctor.Run(_game);
        foreach (Check c in report.Checks) StatusLines.Items.Add(Line($"{c.Badge,-5}  {c.Message}", c.Level));
        if (report.ConflictReport != null)
            foreach (string l in report.ConflictReport.Split('\n').Where(l => l.Trim().Length > 0))
                StatusLines.Items.Add(Line("      " + l.TrimEnd(), l.TrimStart().StartsWith("[ISSUE]") ? CheckLevel.Fail : l.TrimStart().StartsWith("[WARN]") ? CheckLevel.Warn : CheckLevel.Info));
        StatusLines.Items.Add(Line(report.Issues == 0 ? "Nothing in the way." : $"{report.Issues} issue(s) found.", report.Issues == 0 ? CheckLevel.Ok : CheckLevel.Warn));

        foreach (InstalledAssembly a in ModList.Read(_game).Assemblies)
        {
            string tag = a.Role switch { AssemblyRole.Core => "core    ", AssemblyRole.OfficialMod => "official", _ => "external" };
            ModLines.Items.Add(Line($"{tag}  {a.Name,-36} {a.SizeKb,8:F1} KB", a.Role == AssemblyRole.External ? CheckLevel.Info : CheckLevel.Ok));
        }

        // Newest first: crash reports, then the kept play sessions.
        AddLogs(Path.Combine(_game, "BepInEx", "crash-reports"), "crash-*.txt", "crash");
        string sessions = Path.Combine(_game, "Aether", "Logs");
        if (Directory.Exists(sessions))
            foreach (string dir in Directory.GetDirectories(sessions).OrderByDescending(d => d, StringComparer.Ordinal))
                AddLogs(dir, "*.log", Path.GetFileName(dir));
        AddLogs(Path.Combine(_game, "BepInEx"), "LogOutput.log", "current");
        if (LogFiles.Items.Count > 0) LogFiles.SelectedIndex = 0;
    }

    private void AddLogs(string dir, string pattern, string label)
    {
        if (!Directory.Exists(dir)) return;
        foreach (string file in Directory.GetFiles(dir, pattern).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            _logPaths.Add(file);
            LogFiles.Items.Add(new TextBlock { Text = $"{label}  {Path.GetFileName(file)}", FontSize = 12 });
        }
    }

    private void ShowSelectedLog()
    {
        int i = LogFiles.SelectedIndex;
        if (i < 0 || i >= _logPaths.Count) return;
        try
        {
            using var stream = new FileStream(_logPaths[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // Long logs: show the end, where the failure is.
            if (stream.Length > MaxLogBytes) stream.Seek(-MaxLogBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            LogText.Text = reader.ReadToEnd();
            LogText.CaretIndex = LogText.Text.Length;
        }
        catch (IOException ex) { LogText.Text = "Could not read the file: " + ex.Message; }
    }

    private static TextBlock Line(string text, CheckLevel level) => new()
    {
        Text = text,
        FontFamily = new FontFamily("monospace"),
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(level switch
        {
            CheckLevel.Ok => Color.Parse("#6fcf6f"),
            CheckLevel.Warn => Color.Parse("#e8b84a"),
            CheckLevel.Fail => Color.Parse("#ee6b6b"),
            _ => Color.Parse("#8fb4d9")
        })
    };
}
