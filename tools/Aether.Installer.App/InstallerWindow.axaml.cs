using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Aether.Installer.App;

/// <summary>The installer: pick the folder, press Install, and at the end choose whether to add the Monitor.</summary>
public sealed partial class InstallerWindow : Window
{
    public InstallerWindow()
    {
        InitializeComponent();
        GamePath.Text = Program.GameDirArgument ?? GameLocator.Detect() ?? string.Empty;
        GamePath.TextChanged += (_, _) => Refresh();
        BrowseButton.Click += OnBrowse;
        TermsCheck.IsCheckedChanged += (_, _) => Refresh();
        TermsButton.Click += (_, _) => Opener.Url(LegalUrl("terminos"));
        PrivacyButton.Click += (_, _) => Opener.Url(LegalUrl("privacidad"));
        OpenFolderButton.Click += (_, _) => { if (_monitorDir != null) Opener.Folder(_monitorDir); };
        InstallButton.Click += OnInstall;
        SkipButton.Click += (_, _) => Close();
        CloseButton.Click += (_, _) => Close();
        MonitorButton.Click += OnInstallMonitor;
        CopySteamOption.Click += async (_, _) =>
        {
            if (Clipboard == null) return;
            await Clipboard.SetTextAsync(SteamOption.Text ?? SteamLaunchOption.Value);
            CopySteamOption.Content = "Copied";
        };
        Refresh();
    }

    private string? _monitorDir;

    /// <summary>The legal pages on the website, in the system language when there is a translation (Spanish or English).</summary>
    private static string LegalUrl(string page) =>
        "https://aether.ultimaibrahim.dev/" + (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "" : "en/") + page + "/";

    private string? GameDir => string.IsNullOrWhiteSpace(GamePath.Text) ? null : GamePath.Text.Trim();

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Select the Block Story folder", AllowMultiple = false });
        if (folders.Count > 0) GamePath.Text = folders[0].TryGetLocalPath();
    }

    private void Refresh()
    {
        string? game = GameDir;
        bool exists = game != null && Directory.Exists(game);
        InstallButton.IsEnabled = exists && TermsCheck.IsChecked == true;
        State.Text = !exists ? "Select the folder where Block Story is installed."
            : File.Exists(Path.Combine(game!, "BepInEx", "plugins", "Aether", "Aether.Loader.dll")) ? "Aether is already installed here; Install will update it."
            : "Aether will be installed here.";
    }

    private async void OnInstall(object? sender, RoutedEventArgs e)
    {
        string? game = GameDir;
        if (game == null || !Directory.Exists(game)) return;

        InstallButton.IsEnabled = BrowseButton.IsEnabled = GamePath.IsEnabled = TermsCheck.IsEnabled = false;
        Busy.IsVisible = true;
        State.Text = "Installing…";

        string payload = Path.Combine(AppContext.BaseDirectory, "payload");
        string? source = Directory.Exists(payload) ? payload : null;
        string? lastProblem = null;
        LogBox.Text = string.Empty;
        LogBox.IsVisible = true;
        void Line(string badge, string message) => Dispatcher.UIThread.Post(() =>
        {
            LogBox.Text += $"[{badge,-5}] {message}\n";
            LogBox.CaretIndex = LogBox.Text.Length;
        });
        void Note(Check c) { Line(c.Badge, c.Message); if (c.Level is CheckLevel.Warn or CheckLevel.Fail) lastProblem = c.Message; }
        // A real bar while BepInEx downloads; back to "busy" afterwards.
        void Progress(long done, long? total) => Dispatcher.UIThread.Post(() =>
        {
            if (total is > 0) { Busy.IsIndeterminate = false; Busy.Maximum = total.Value; Busy.Value = done; State.Text = $"Downloading BepInEx {BepInExSetup.Version}… {done / 1024} / {total / 1024} KB"; }
        });

        bool ok = await Task.Run(() =>
        {
            Line("INFO", $"Game folder: {game}");
            if (!BepInExSetup.IsInstalled(game))
            {
                Dispatcher.UIThread.Post(() => State.Text = $"Downloading BepInEx {BepInExSetup.Version}…");
                if (!BepInExSetup.Install(game, dryRun: false, (b, m) => { Line(b, m); if (b == "ERR") lastProblem = m; }, Progress))
                    return false;
            }
            else Line("INFO", "BepInEx is already installed.");
            Dispatcher.UIThread.Post(() => { Busy.IsIndeterminate = true; State.Text = "Installing Aether…"; });
            return InstallRunner.Run(new InstallOptions { GameDir = game, SourceDir = source }, Note).Success;
        });

        Busy.IsVisible = false;
        if (!ok)
        {
            State.Text = "The install did not finish. " + (lastProblem ?? "Run 'aether doctor' for details.");
            InstallButton.IsEnabled = BrowseButton.IsEnabled = GamePath.IsEnabled = TermsCheck.IsEnabled = true;
            return;
        }

        PickPanel.IsVisible = false;
        DonePanel.IsVisible = true;
        InstallButton.IsVisible = false;
        DoneText.Text = $"To add mods, drop them into  {Path.Combine(game, "Aether", "Mods")}  — then start Block Story.";
        SteamOption.Text = SteamLaunchOption.Value;
        SteamHint.IsVisible = OperatingSystem.IsLinux() && !SteamLaunchOption.IsSet(game, null);
        bool canOfferMonitor = File.Exists(Environment.ProcessPath ?? string.Empty);
        MonitorOffer.IsVisible = SkipButton.IsVisible = MonitorButton.IsVisible = ShortcutCheck.IsVisible = canOfferMonitor;
        CloseButton.IsVisible = !canOfferMonitor;
    }

    private async void OnInstallMonitor(object? sender, RoutedEventArgs e)
    {
        string? game = GameDir;
        if (game == null) return;
        MonitorButton.IsEnabled = SkipButton.IsEnabled = ShortcutCheck.IsEnabled = false;
        bool shortcut = ShortcutCheck.IsChecked == true;

        string? exe = Environment.ProcessPath;
        var notes = new List<Check>();
        string? installed = await Task.Run(() => MonitorInstaller.Install(game, exe, AppContext.BaseDirectory, dryRun: false, notes.Add));
        string? shortcutPath = installed != null && shortcut
            ? await Task.Run(() => MonitorInstaller.CreateDesktopShortcut(installed, dryRun: false, notes.Add))
            : null;

        DoneTitle.Text = installed != null ? "Aether Monitor is installed." : "Aether Monitor could not be installed.";
        if (installed != null)
        {
            _monitorDir = Path.GetDirectoryName(installed);
            MonitorOffer.Text = $"It is in:\n{_monitorDir}" + (shortcutPath != null ? "\n\nA shortcut was added to your desktop." : shortcut ? "\n\nThe desktop shortcut could not be created; open it from the folder above." : "")
                + (OperatingSystem.IsLinux() ? "\n\nIt is also in your applications menu as \"Aether Monitor\"." : "");
            OpenFolderButton.IsVisible = true;
        }
        else MonitorOffer.Text = notes.LastOrDefault(c => c.Level is CheckLevel.Warn or CheckLevel.Fail)?.Message ?? "The Aether loader is still installed.";
        ShortcutCheck.IsVisible = MonitorButton.IsVisible = SkipButton.IsVisible = false;
        CloseButton.IsVisible = true;
    }
}
