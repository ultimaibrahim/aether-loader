using System.Diagnostics;
using Aether.Installer.Lattice;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using static Aether.Installer.App.Signs;

namespace Aether.Installer.App;

/// <summary>
/// The optional monitor: status, my mods (on/off with one click), the Lattice catalog (one click to install, a confirmation only
/// when other mods come with it) and problems and logs. It changes the game folder only when asked. Spanish and English.
/// </summary>
public sealed partial class MonitorWindow : Window
{
    private const int MaxLogBytes = 256 * 1024;
    private readonly string? _game;
    private readonly MonitorSettings _settings = MonitorSettings.Load();
    private readonly List<string> _logPaths = new();
    private readonly Dictionary<string, Bitmap?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> _progress = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, LatticeListing> _listing = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, InstalledLatticeMod> _installed = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, LatticeOutdated> _outdated = new(StringComparer.OrdinalIgnoreCase);
    private LatticeSnapshot? _snap;
    private string? _catalogError;
    private AetherUpdate? _update;
    private string _section = "catalog";
    private string _category = "";
    private string _forumCategory = "";
    private string? _selected;
    private string? _busy;
    private Action<int>? _buildCube;
    private StackPanel? _buildLog;
    private int _modCount, _issueCount;
    private Border? _busyBar;
    private Control? _busyCard;

    private bool Es => _settings.Language == "es";
    private string T(string en, string es) => Es ? es : en;

    public MonitorWindow()
    {
        InitializeComponent();
        _game = ResolveGameDir();
        LogoHost.Content = AetherCube(26);
        DropCube.Content = Cube(28, 1, on: false);
        UpdateSign.Content = Rhombus(Ember);
        CatalogSign.Content = Rhombus(Jade);

        LangButton.Click += (_, _) => { _settings.Language = Es ? "en" : "es"; _settings.Save(); ApplyTexts(); RenderAside(); };
        RefreshButton.Click += async (_, _) => { Reload(); await CheckOnline(); };
        DropZone.PointerPressed += async (_, _) => await PickAndAddMod();
        LogFiles.SelectionChanged += (_, _) => ShowSelectedLog();
        OpenLogs.Click += (_, _) => { if (_game != null) Opener.Folder(Path.Combine(_game, "Aether", "Logs")); };
        OpenMods.Click += (_, _) => { if (_game != null) Opener.Folder(Path.Combine(_game, InstallLayout.ModsDir)); };
        UpdateButton.Click += OnUpdateAether;
        UpdateAllButton.Click += async (_, _) => await UpdateAll();
        SearchBox.TextChanged += (_, _) => RenderCatalog();
        ForumSearchBox.TextChanged += (_, _) => RenderForum();
        OpenForumWeb.Click += (_, _) => Opener.Url(Es ? "https://aether.ultimaibrahim.dev/docs/" : "https://aether.ultimaibrahim.dev/en/docs/");
        OpenForumAsk.Click += (_, _) => Opener.Url("https://github.com/ultimaibrahim/aether-loader/issues");
        ViewCards.Click += (_, _) => SetView("cards");
        ViewList.Click += (_, _) => SetView("list");
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None; DropZone.BorderBrush = B(Ember); });
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropZone.BorderBrush = B(Color.Parse("#C9BEAE")));
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            DropZone.BorderBrush = B(Color.Parse("#C9BEAE"));
            string? path = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p != null);
            if (path != null) await AddModFrom(path);
        });

        ApplyTexts(reload: false);
        Opened += async (_, _) =>
        {
            Reload();
            // The one place Aether goes online, and only from this window (never from the game). Off if Aether/no-update-check exists.
            if (_game != null && !File.Exists(Path.Combine(_game, "Aether", "no-update-check"))) await CheckOnline();
            else { CatalogStatus.Text = T("Online checks are off. Press “Refresh” to look.", "La consulta en línea está apagada. Pulsa «Actualizar lista» para buscar."); RenderCatalog(); }
        };
    }

    // ───────────────────────── language and sections ─────────────────────────

    private void ApplyTexts(bool reload = true)
    {
        GameLabel.Text = _game ?? T("Block Story folder not found. Start the monitor with --game-dir <path>.", "No encontré la carpeta de Block Story. Abre el monitor con --game-dir <ruta>.");
        LangButton.Content = Es ? "ES · en" : "es · EN";
        ToolTip.SetTip(LangButton, T("Español", "English"));
        RefreshButton.Content = T("Refresh", "Actualizar lista");
        DropText.Text = T("Drop a .zip or .dll here, or click, to install it", "Suelta aquí un .zip o .dll, o haz clic, para instalarlo");
        StatusTitle.Text = T("Status", "Estado");
        ModsTitle.Text = T("My mods", "Mis mods");
        ModsHint.Text = T("Switching a mod off does not delete it: the file stays and comes back with one click. It applies when the game starts.",
                          "Apagar un mod no lo borra: el archivo se queda y vuelve con un clic. Aplica al abrir el juego.");
        CatalogTitle.Text = T("Catalog", "Catálogo");
        SearchBox.Watermark = T("Search by name, author or what it does…", "Buscar por nombre, autor o lo que hace…");
        ViewCards.Content = ViewLabel(T("Cards", "Tarjetas"), true);
        ViewList.Content = ViewLabel(T("List", "Lista"), false);
        LogsTitle.Text = T("Problems and logs", "Fallos y logs");
        OpenLogs.Content = T("Open logs folder", "Abrir carpeta de logs");
        OpenMods.Content = T("Open mods folder", "Abrir carpeta de mods");
        ForumTitle.Text = T("Modding Guide", "Guía de modding");
        ForumSummary.Text = T(
            "17 pinned guides (pure BepInEx & Aether Nano 1.0.3) and 3 solved reference cases. Click any thread to open it in your browser.",
            "17 guías fijadas (BepInEx puro y Aether Nano 1.0.3) y 3 casos resueltos de referencia. Haz clic en cualquier hilo para abrirlo.");
        OpenForumWeb.Content = T("Open guide on web ↗", "Abrir guía en la web ↗");
        ForumSearchBox.Watermark = T("Filter threads by topic, class (NanoSurface, UIWidget, Digger) or #tag…", "Filtrar hilos por tema, clase (NanoSurface, UIWidget, Digger) o #etiqueta…");
        ForumCliHint.Text = T("New mod:  dotnet new classlib -f netstandard2.1  or  aether new MyFirstMod", "Nuevo mod:  dotnet new classlib -f netstandard2.1  o  aether new MiPrimerMod");
        OpenForumAsk.Content = T("Suggest an edit on GitHub ↗", "Sugerir mejora en GitHub ↗");
        UpdateButton.Content = _update?.ZipUrl != null || _update == null ? T("Update Aether", "Actualizar Aether") : T("Open download page", "Abrir la descarga");
        if (_update != null) UpdateText.Text = T($"Aether {_update.Version} is available.", $"Aether {_update.Version} está disponible.");
        BuildModsHeader();
        ShowSection(_section);
        if (reload) Reload();
        RenderCatalog();
        RenderForum();
    }

    private Control ViewLabel(string text, bool cards)
    {
        // Two small drawings: a 2×2 grid for cards, three bars for the list.
        var c = new Canvas { Width = 12, Height = 12 };
        if (cards) foreach (var (x, y) in new[] { (0.0, 0.0), (7.0, 0.0), (0.0, 7.0), (7.0, 7.0) }) c.Children.Add(new Avalonia.Controls.Shapes.Rectangle { Width = 5, Height = 5, Fill = B(Mute), [Canvas.LeftProperty] = x, [Canvas.TopProperty] = y });
        else foreach (double y in new[] { 1.0, 5.0, 9.0 }) c.Children.Add(new Avalonia.Controls.Shapes.Rectangle { Width = 12, Height = 2, Fill = B(Mute), [Canvas.TopProperty] = y });
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { c, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
    }

    private void SetView(string view)
    {
        _settings.CatalogView = view; _settings.Save();
        RenderCatalog();
    }

    private void ShowSection(string name)
    {
        _section = name;
        StatusPanel.IsVisible = name == "status";
        ModsPanel.IsVisible = name == "mods";
        CatalogPanel.IsVisible = name == "catalog";
        ForumPanel.IsVisible = name == "forum";
        LogsPanel.IsVisible = name == "logs";
        BuildNav();
    }

    private void BuildNav()
    {
        NavPanel.Children.Clear();
        var items = new (string Key, string Label, string? Count, bool Hot)[]
        {
            ("status", T("Status", "Estado"), _issueCount > 0 ? _issueCount.ToString() : null, _issueCount > 0),
            ("mods", T("My mods", "Mis mods"), _modCount > 0 ? _modCount.ToString() : null, false),
            ("catalog", T("Catalog", "Catálogo"), _outdated.Count > 0 ? _outdated.Count.ToString() : null, _outdated.Count > 0),
            ("forum", T("Modding Guide", "Guía de modding"), "20", false),
            ("logs", T("Problems and logs", "Fallos y logs"), null, false),
        };
        foreach (var it in items)
        {
            bool active = it.Key == _section;
            // The active section: its sign filled with ember and a thin ember underline under the name. No side stripe, no box.
            var label = new Border
            {
                BorderBrush = B(Ember), BorderThickness = new Thickness(0, 0, 0, active ? 1.5 : 0), Padding = new Thickness(0, 0, 0, 2),
                Child = new TextBlock { Text = it.Label, FontSize = 14, FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal, Foreground = B(active ? Ink : Mute) },
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            row.Children.Add(NavSign(it.Key, active));
            Grid.SetColumn(label, 1); label.Margin = new Thickness(10, 0, 0, 0); label.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(label);
            if (it.Count != null)
            {
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(999), Padding = new Thickness(7, 1), Background = it.Hot ? B(Ember) : Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = it.Count, FontSize = 11, Classes = { "mono" }, Foreground = it.Hot ? Brushes.White : B(Mute) },
                };
                Grid.SetColumn(pill, 2); row.Children.Add(pill);
            }
            var b = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 10) };
            b.Classes.Add("nav");
            string key = it.Key;
            b.Click += (_, _) => ShowSection(key);
            NavPanel.Children.Add(b);
        }
    }

    // ───────────────────────── reading the game folder ─────────────────────────

    private void Reload()
    {
        if (_game == null || !Directory.Exists(_game)) return;
        if (_snap != null) _installed = LatticeInstaller.ReadInstalled(_game, _snap);
        RenderStatus();
        RenderMods();
        RenderLogs();
        BuildNav();
    }

    private void RenderStatus()
    {
        StatusHost.Children.Clear();
        CheckReport report = Doctor.Run(_game!);
        _issueCount = report.Issues;
        foreach (Check c in report.Checks) StatusHost.Children.Add(StatusRow(c.Level, c.Message));
        if (report.ConflictReport != null)
            foreach (string l in report.ConflictReport.Split('\n').Where(l => l.Trim().Length > 0))
                StatusHost.Children.Add(StatusRow(l.TrimStart().StartsWith("[ISSUE]") ? CheckLevel.Fail : l.TrimStart().StartsWith("[WARN]") ? CheckLevel.Warn : CheckLevel.Info, l.Trim(), mono: true));
        StatusSummary.Text = report.Issues == 0 ? T("Nothing in the way.", "Nada estorba.") : T($"{report.Issues} issue(s) to look at.", $"{report.Issues} cosa(s) por revisar.");
    }

    private static Control StatusRow(CheckLevel level, string text, bool mono = false)
    {
        Color c = level switch { CheckLevel.Ok => Jade, CheckLevel.Warn => Amber, CheckLevel.Fail => Garnet, _ => Mute };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(10, 7) };
        var sign = Rhombus(c, filled: level != CheckLevel.Info);
        sign.VerticalAlignment = VerticalAlignment.Top; sign.Margin = new Thickness(0, 3, 10, 0);
        row.Children.Add(sign);
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = mono ? 12 : 13.5 };
        if (mono) tb.Classes.Add("mono");
        Grid.SetColumn(tb, 1); row.Children.Add(tb);
        return row;
    }

    // ───────────────────────── my mods ─────────────────────────

    private void BuildModsHeader()
    {
        ModsHeader.Children.Clear();
        string[] heads = { "", T("MOD", "MOD"), T("FROM", "DE DÓNDE VIENE"), T("VERSION", "VERSIÓN"), T("ON", "ACTIVO") };
        for (int i = 0; i < heads.Length; i++)
        {
            var tb = new TextBlock { Text = heads[i], FontSize = 10.5, Classes = { "mono" }, Foreground = B(Mute), LetterSpacing = 0.8 };
            Grid.SetColumn(tb, i); ModsHeader.Children.Add(tb);
        }
    }

    private (string Slug, LatticeMod Mod)? CatalogModFor(string fileName)
    {
        if (_snap == null) return null;
        foreach (var m in _snap.Index.Mods)
            if (m.Versions.Any(v => string.Equals(Path.GetFileName(new Uri(v.DownloadUrl).AbsolutePath), fileName, StringComparison.OrdinalIgnoreCase)))
                return (m.Slug, m);
        return null;
    }

    private void RenderMods()
    {
        ModsHost.Children.Clear();
        ModsFoot.Children.Clear();
        var list = ModList.Read(_game!).Assemblies;
        var mods = list.Where(a => a.Role != AssemblyRole.Core).ToList();
        _modCount = mods.Count;
        int on = mods.Count(a => a.Enabled);
        ModsSummary.Text = T($"{on} on · {mods.Count - on} off · Aether itself: {list.Count(a => a.Role == AssemblyRole.Core)} files",
                             $"{on} encendidos · {mods.Count - on} apagados · Aether: {list.Count(a => a.Role == AssemblyRole.Core)} archivos");
        if (mods.Count == 0)
        {
            ModsHost.Children.Add(new TextBlock { Text = T("No mods yet. Install one from the Catalog or drop a file on the left.", "Todavía no hay mods. Instala uno desde el Catálogo o suelta un archivo a la izquierda."), Margin = new Thickness(16), Foreground = B(Mute) });
            return;
        }
        foreach (var a in mods) ModsHost.Children.Add(ModRow(a));

        var foot = new TextBlock { FontSize = 12, Foreground = B(Mute), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Text = T("Mods from the Catalog update from there. Mods installed by hand keep their file as it is.", "Los mods del Catálogo se actualizan desde ahí. Los instalados a mano se quedan como están.") };
        ModsFoot.Children.Add(Rhombus(Ember, filled: false));
        ModsFoot.Children.Add(foot);
    }

    private Control ModRow(InstalledAssembly a)
    {
        var cat = CatalogModFor(a.Name);
        string slug = cat?.Slug ?? Path.GetFileNameWithoutExtension(a.Name).ToLowerInvariant();
        string name = cat != null ? (_listing.TryGetValue(cat.Value.Slug, out var li) ? li.Name : cat.Value.Mod.Name) : Path.GetFileNameWithoutExtension(a.Name);
        string version = cat != null && _installed.TryGetValue(cat.Value.Slug, out var inst) ? inst.Version : (ModInspector.Inspect(a.Path).Version ?? "?");
        string dir = Path.GetRelativePath(_game!, Path.GetDirectoryName(a.Path)!).Replace('\\', '/');
        string source = cat != null ? "Lattice" : dir.StartsWith("BepInEx/plugins", StringComparison.OrdinalIgnoreCase) ? "BepInEx/plugins" : T("By hand", "A mano");
        bool update = cat != null && _outdated.ContainsKey(cat.Value.Slug);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*,170,110,76"), Opacity = a.Enabled ? 1 : 0.6 };
        var icon = IconFor(slug, 40, a.Enabled);
        row.Children.Add(icon);

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = name, Classes = { "display" }, FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
        if (update) title.Children.Add(Tag(T("UPDATE", "ACTUALIZAR"), strong: true));
        else if (a.Role == AssemblyRole.External && cat == null) title.Children.Add(Tag(T("NOT FROM LATTICE", "FUERA DE LATTICE")));
        string note = !a.Enabled ? T("Off: the file is still there, back with one click.", "Apagado: el archivo sigue ahí, vuelve con un clic.")
            : update ? T($"{_outdated[cat!.Value.Slug].Latest} is in the Catalog.", $"Hay {_outdated[cat!.Value.Slug].Latest} en el Catálogo.")
            : _listing.TryGetValue(slug, out var l2) && l2.Tagline != null ? l2.Tagline : a.Name;
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, new TextBlock { Text = note, FontSize = 12, Foreground = B(Mute), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) } } };
        Grid.SetColumn(middle, 1); row.Children.Add(middle);

        var src = new TextBlock { Text = source, Classes = { "mono" }, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Foreground = B(Color.Parse("#3D342C")) };
        Grid.SetColumn(src, 2); row.Children.Add(src);
        var ver = new TextBlock { Text = version, Classes = { "mono" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(ver, 3); row.Children.Add(ver);

        var sw = Switch(a.Enabled, (a.Enabled ? T("Switch off ", "Apagar ") : T("Switch on ", "Encender ")) + name, () => _ = ToggleMod(a, cat?.Slug, name));
        Grid.SetColumn(sw, 4); row.Children.Add(sw);

        return new Border { Padding = new Thickness(16, 10), BorderBrush = B(Color.Parse("#EFE9E0")), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
    }

    private async Task ToggleMod(InstalledAssembly a, string? slug, string name)
    {
        if (a.Enabled && slug != null && _snap != null)
        {
            // Mods that need this one stop working with it: say which, before switching.
            var needs = _snap.Index.Mods.Where(m => _installed.ContainsKey(m.Slug) && m.Versions.Any(v => v.Requires.Any(r => r.IsMod && r.Name.Equals(slug, StringComparison.OrdinalIgnoreCase))))
                .Select(m => _listing.TryGetValue(m.Slug, out var l) ? l.Name : m.Name).ToList();
            if (needs.Count > 0 && !await Dialogs.Confirm(this, T("Switch off", "Apagar"),
                    T($"{string.Join(", ", needs)} need {name}. If you switch it off, they stop working too.", $"{string.Join(", ", needs)} necesita(n) {name}. Si lo apagas, también deja(n) de funcionar."),
                    T("Switch off", "Apagar"), T("Cancel", "Cancelar")))
            { RenderMods(); return; }
        }
        try { ModSwitch.Set(a, !a.Enabled); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        { await Dialogs.Message(this, name, T("Could not switch it: ", "No se pudo cambiar: ") + e.Message + T(" Close the game and try again.", " Cierra el juego y vuelve a intentar.")); }
        // Let the switch finish its slide before the list is redrawn.
        await Task.Delay(240);
        Reload();
    }

    private Control IconFor(string slug, double size, bool on = true)
    {
        if (_icons.TryGetValue(slug, out var bmp) && bmp != null)
        {
            var img = new Image { Source = bmp, Width = size, Height = size, Stretch = Stretch.UniformToFill, Opacity = on ? 1 : 0.5 };
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 5), ClipToBounds = true, Child = img, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        }
        var cube = Cube(size, LatticeDirectory.GemOf(slug), on);
        cube.VerticalAlignment = VerticalAlignment.Center; cube.HorizontalAlignment = HorizontalAlignment.Left;
        return cube;
    }

    // ───────────────────────── catalog ─────────────────────────

    /// <summary>Asks GitHub for a newer Aether, Lattice for the signed catalog and its public listing (names, icons), then redraws.</summary>
    private async Task CheckOnline()
    {
        if (_game == null || !Directory.Exists(_game)) return;
        string game = _game;
        CatalogStatus.Text = T("Checking…", "Consultando…");
        var (snap, error, update, listing) = await Task.Run(() =>
        {
            var fetcher = new HttpLatticeFetcher();
            var s = LatticeStore.Load(game, fetcher, LatticeKey.Default, DateTime.UtcNow, out string? err);
            var u = UpdateChecker.Check(fetcher, UpdateChecker.InstalledVersion(game) ?? Aether.Loader.AetherBootstrap.PluginVersion,
                UpdateChecker.CurrentRid(), UpdateChecker.IsSelfContained());
            Dictionary<string, LatticeListing> l;
            try { l = LatticeDirectory.Fetch(fetcher); }
            catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { l = new(StringComparer.OrdinalIgnoreCase); }
            return (s, err, u, l);
        });
        _snap = snap; _update = update; _catalogError = error;
        if (listing.Count > 0) _listing = listing;
        UpdateBanner.IsVisible = update != null;
        if (update != null)
        {
            UpdateText.Text = T($"Aether {update.Version} is available.", $"Aether {update.Version} está disponible.");
            UpdateButton.Content = update.ZipUrl != null ? T("Update Aether", "Actualizar Aether") : T("Open download page", "Abrir la descarga");
        }
        RefreshCatalogState();
        Reload();
        RenderCatalog();
        _ = LoadIcons();
    }

    private async Task LoadIcons()
    {
        if (_game == null) return;
        string game = _game;
        var todo = _listing.Values.Where(l => l.IconUrl != null && !_icons.ContainsKey(l.Slug)).ToList();
        if (todo.Count == 0) return;
        var files = await Task.Run(() => todo.Select(l => (l.Slug, File: LatticeDirectory.IconFile(game, l, new HttpLatticeFetcher()))).ToList());
        foreach (var (slug, file) in files)
        {
            try { _icons[slug] = file == null ? null : new Bitmap(file); }
            catch (Exception) { _icons[slug] = null; }      // a broken image: the cube is drawn instead
        }
        RenderCatalog();
        RenderMods();
    }

    private void RefreshCatalogState()
    {
        if (_snap == null || _game == null) { _outdated.Clear(); return; }
        _installed = LatticeInstaller.ReadInstalled(_game, _snap);
        _outdated = LatticePlanner.Outdated(_snap, _installed).ToDictionary(o => o.Slug, StringComparer.OrdinalIgnoreCase);
    }

    private LatticeVersion? Newest(LatticeMod m) => _snap == null ? null :
        m.Versions.Where(v => !_snap.RevokedHashes.Contains(v.Sha256)).OrderByDescending(v => v.Label, Comparer<string>.Create(LatticeIndex.Compare)).FirstOrDefault();

    private static readonly string[] CategoryOrder = { "gameplay", "world", "items", "qol", "interface", "visual", "audio", "library", "tools", "other" };
    private string CategoryName(string key) => key switch
    {
        "gameplay" => T("Gameplay", "Jugabilidad"), "world" => T("World & maps", "Mundo y mapas"), "items" => T("Items & blocks", "Objetos y bloques"),
        "qol" => T("Quality of life", "Calidad de vida"), "interface" => T("Interface", "Interfaz"), "visual" => "Visual", "audio" => "Audio",
        "library" => T("Library", "Librería"), "tools" => T("Tools", "Herramientas"), _ => T("Other", "Otros"),
    };

    private void RenderCatalog()
    {
        // Header line and the "update all" button.
        UpdateAllButton.Content = _outdated.Count > 0 ? T($"↑  Update all ({_outdated.Count})", $"↑  Actualizar todo ({_outdated.Count})") : T("✓  All up to date", "✓  Todo al día");
        UpdateAllButton.IsEnabled = _outdated.Count > 0 && _busy == null;
        if (_snap == null) CatalogStatus.Text = _catalogError ?? CatalogStatus.Text;
        else CatalogStatus.Text = _snap.Offline ? (_snap.Warning ?? T("Offline: showing the last catalog.", "Sin conexión: muestro el último catálogo."))
            : T($"Signed by Lattice and checked on your PC · serial {_snap.IndexSerial}", $"Firmado por Lattice y verificado en tu PC · serie {_snap.IndexSerial}");
        CatalogSign.Content = Rhombus(_snap != null && !_snap.Offline ? Jade : Amber);

        // View toggle.
        bool cards = _settings.CatalogView == "cards";
        ViewCards.Classes.Set("on", cards); ViewList.Classes.Set("on", !cards);

        // Category chips: only the categories the catalog has.
        ChipsPanel.Children.Clear();
        var present = _listing.Values.SelectMany(l => l.Categories).Distinct().OrderBy(c => Array.IndexOf(CategoryOrder, c)).ToList();
        if (_category != "" && !present.Contains(_category)) _category = "";
        foreach (var (key, label) in new[] { ("", T("All", "Todos")) }.Concat(present.Select(c => (c, CategoryName(c)))))
        {
            var chip = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0) };
            chip.Classes.Add("chip"); chip.Classes.Set("on", key == _category);
            chip.Click += (_, _) => { _category = key; RenderCatalog(); };
            ChipsPanel.Children.Add(chip);
        }

        if (_snap == null) { CatalogHost.Content = new TextBlock { Text = _catalogError ?? T("The catalog could not be read.", "No se pudo leer el catálogo."), Foreground = B(Mute), Margin = new Thickness(4) }; return; }
        string q = (SearchBox.Text ?? "").Trim();
        var mods = _snap.Index.Mods.Where(m =>
        {
            _listing.TryGetValue(m.Slug, out var l);
            if (_category != "" && (l == null || !l.Categories.Contains(_category))) return false;
            if (q.Length == 0) return true;
            string hay = string.Join(" ", m.Slug, m.Name, l?.Name, l?.Tagline, l?.Owner);
            return hay.Contains(q, StringComparison.OrdinalIgnoreCase);
        }).OrderByDescending(m => _outdated.ContainsKey(m.Slug)).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();

        if (mods.Count == 0) { CatalogHost.Content = new TextBlock { Text = T("Nothing matches.", "Nada coincide."), Foreground = B(Mute), Margin = new Thickness(4) }; return; }
        if (cards)
        {
            var grid = new UniformGrid { Columns = Bounds.Width > 0 && Bounds.Width < 1000 || Aside.IsVisible ? 1 : 2 };
            foreach (var m in mods) grid.Children.Add(Card(m));
            CatalogHost.Content = grid;
        }
        else
        {
            var stack = new StackPanel { Spacing = 0 };
            var box = new Border { Background = B(Card0), BorderBrush = B(Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = stack };
            foreach (var m in mods) stack.Children.Add(ListRow(m));
            CatalogHost.Content = box;
        }
    }

    private (string Label, string Class, bool Enabled) ActionFor(LatticeMod m, LatticeVersion? newest)
    {
        if (_busy == m.Slug) return (T($"Installing… {(int)(_progress.GetValueOrDefault(m.Slug) * 100)}%", $"Instalando… {(int)(_progress.GetValueOrDefault(m.Slug) * 100)}%"), "busy", false);
        if (_outdated.TryGetValue(m.Slug, out var o)) return (o.Revoked ? T("Replace (revoked)", "Reemplazar (revocada)") : T($"Update {o.Installed} → {o.Latest}", $"Actualizar {o.Installed} → {o.Latest}"), "update", _busy == null);
        if (_installed.ContainsKey(m.Slug)) return (T("✓ Installed", "✓ Instalado"), "done", false);
        return (T("Install", "Instalar"), "primary", _busy == null && newest != null);
    }

    private Button ActionButton(LatticeMod m, LatticeVersion? newest)
    {
        var (label, cls, enabled) = ActionFor(m, newest);
        var b = new Button { Content = label, Padding = new Thickness(14, 9), FontSize = 13, IsEnabled = enabled || cls == "done", VerticalAlignment = VerticalAlignment.Center };
        b.Classes.Add(cls);
        string slug = m.Slug;
        b.Click += async (_, e) => { e.Handled = true; if (cls is "primary" or "update") await InstallMod(slug); };
        return b;
    }

    private Control Card(LatticeMod m)
    {
        var newest = Newest(m);
        _listing.TryGetValue(m.Slug, out var l);
        var body = new StackPanel { Spacing = 6 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new TextBlock { Text = l?.Name ?? m.Name, Classes = { "display" }, FontSize = 21, VerticalAlignment = VerticalAlignment.Bottom });
        head.Children.Add(new TextBlock { Text = newest?.Label ?? "—", Classes = { "mono" }, FontSize = 11, Foreground = B(Mute), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) });
        body.Children.Add(head);
        if (l?.Owner is { Length: > 0 } owner) body.Children.Add(new TextBlock { Text = "@" + owner, Classes = { "mono" }, FontSize = 11, Foreground = B(Mute) });
        body.Children.Add(new TextBlock { Text = l?.Tagline ?? T("No description yet.", "Todavía sin descripción."), FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = B(Color.Parse("#3D342C")) });
        var chips = new WrapPanel();
        foreach (string t in Chips(l)) { var tag = Tag(t); tag.Margin = new Thickness(0, 0, 6, 4); chips.Children.Add(tag); }
        body.Children.Add(chips);

        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        int lvl = LevelOf(newest?.Level ?? "");
        var hex = Hexes(lvl); hex.VerticalAlignment = VerticalAlignment.Center; foot.Children.Add(hex);
        var lvlText = new TextBlock { Text = LevelName(newest?.Level), Classes = { "mono" }, FontSize = 11, Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lvlText, 1); foot.Children.Add(lvlText);
        var dl = new TextBlock { Text = l != null ? $"↓ {l.Downloads}" : "", Classes = { "mono" }, FontSize = 11, Foreground = B(Mute), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(dl, 2); foot.Children.Add(dl);
        var act = ActionButton(m, newest); Grid.SetColumn(act, 4); foot.Children.Add(act);
        body.Children.Add(foot);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var icon = IconFor(m.Slug, 58); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(icon);
        Grid.SetColumn(body, 1); row.Children.Add(body);

        // The install progress runs along the card's bottom edge.
        var bar = new Border { Height = 3, Background = B(Ember), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Width = 0,
            Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(240) } } };
        var holder = new Grid { Children = { new Border { Padding = new Thickness(16, 16, 16, 14), Child = row }, bar } };
        var card = new Border { Margin = new Thickness(0, 0, 12, 12), Child = holder, Cursor = new Cursor(StandardCursorType.Hand), ClipToBounds = true };
        card.Classes.Add("card"); card.Classes.Set("selected", _selected == m.Slug);
        if (_busy == m.Slug) { _busyBar = bar; _busyCard = card; card.SizeChanged += (_, e) => bar.Width = e.NewSize.Width * _progress.GetValueOrDefault(m.Slug); }
        card.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) Select(m.Slug); };
        return card;
    }

    private Control ListRow(LatticeMod m)
    {
        var newest = Newest(m);
        _listing.TryGetValue(m.Slug, out var l);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,Auto,Auto,Auto") };
        var icon = IconFor(m.Slug, 34); g.Children.Add(icon);
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new TextBlock { Text = l?.Name ?? m.Name, Classes = { "display" }, FontSize = 17 });
        head.Children.Add(new TextBlock { Text = newest?.Label ?? "—", Classes = { "mono" }, FontSize = 11, Foreground = B(Mute), VerticalAlignment = VerticalAlignment.Center });
        mid.Children.Add(head);
        mid.Children.Add(new TextBlock { Text = l?.Tagline ?? "", FontSize = 12, Foreground = B(Mute), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(mid, 1); g.Children.Add(mid);
        var hex = Hexes(LevelOf(newest?.Level ?? "")); hex.Margin = new Thickness(12, 0); hex.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(hex, LevelName(newest?.Level));
        Grid.SetColumn(hex, 2); g.Children.Add(hex);
        var dl = new TextBlock { Text = l != null ? $"↓ {l.Downloads}" : "", Classes = { "mono" }, FontSize = 11, Foreground = B(Mute), VerticalAlignment = VerticalAlignment.Center, Width = 56 };
        Grid.SetColumn(dl, 3); g.Children.Add(dl);
        var act = ActionButton(m, newest); Grid.SetColumn(act, 4); g.Children.Add(act);
        var row = new Border { Padding = new Thickness(14, 10), BorderBrush = B(Color.Parse("#EFE9E0")), BorderThickness = new Thickness(0, 0, 0, 1), Child = g, Cursor = new Cursor(StandardCursorType.Hand),
            Background = _selected == m.Slug ? B(EmberWash) : Brushes.Transparent };
        row.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) Select(m.Slug); };
        return row;
    }

    private IEnumerable<string> Chips(LatticeListing? l)
    {
        if (l == null) yield break;
        foreach (string c in l.Categories.Take(1)) yield return CategoryName(c);
        foreach (string w in l.WorksWith) yield return w switch { "aether" => "Aether", "bepinex" => "BepInEx", _ => w };
        yield return l.Environment switch { "client" => T("Client", "Cliente"), "server" => T("Server", "Servidor"), _ => T("Client & server", "Cliente y servidor") };
    }

    private string LevelName(string? level) => level switch
    {
        "reviewed" => T("Reviewed", "Revisado"), "reproducible" => "Reproducible", "listed" => T("Listed", "Listado"), _ => T("Unverified", "Sin verificar"),
    };

    // ───────────────────────── the mod's page (right) ─────────────────────────

    private void Select(string slug)
    {
        if (_busy != null) return;
        _selected = _selected == slug ? null : slug;
        RenderAside();
        RenderCatalog();
    }

    private void RenderAside()
    {
        AsideHost.Children.Clear();
        var m = _snap?.Index.Mods.FirstOrDefault(x => x.Slug == (_busy ?? _selected));
        Aside.IsVisible = m != null;
        if (m == null) return;
        var newest = Newest(m);
        _listing.TryGetValue(m.Slug, out var l);

        var close = new Button { Content = "×", FontSize = 18, Padding = new Thickness(8, 0), HorizontalAlignment = HorizontalAlignment.Right, IsVisible = _busy == null };
        close.Classes.Add("bare");
        ToolTip.SetTip(close, T("Close", "Cerrar"));
        close.Click += (_, _) => Select(m.Slug);
        AsideHost.Children.Add(close);

        if (_busy == m.Slug)
        {
            // Installing: the cube builds itself, one face per step.
            AsideHost.Children.Add(new TextBlock { Text = T("INSTALLING", "INSTALANDO"), Classes = { "mono" }, FontSize = 10.5, Foreground = B(Mute), LetterSpacing = 0.8 });
            var (view, show) = BuildingCube(132, LatticeDirectory.GemOf(m.Slug));
            view.HorizontalAlignment = HorizontalAlignment.Center; view.Margin = new Thickness(0, 6);
            _buildCube = show; show(1);
            AsideHost.Children.Add(view);
            AsideHost.Children.Add(new TextBlock { Text = T("Each face is a step. It is complete only if everything checks out.", "Cada cara es un paso. Se arma completo solo si todo cuadra."), FontSize = 13, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = B(Color.Parse("#3D342C")) });
            _buildLog = new StackPanel { Spacing = 8 };
            AsideHost.Children.Add(_buildLog);
            return;
        }

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        top.Children.Add(new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(14), Background = B(Chip), BorderBrush = B(Line), BorderThickness = new Thickness(1), Child = new Panel { Children = { Centered(IconFor(m.Slug, 56)) } } });
        top.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children =
        {
            new TextBlock { Text = l?.Name ?? m.Name, Classes = { "display" }, FontSize = 26, TextWrapping = TextWrapping.Wrap, MaxWidth = 200 },
            new TextBlock { Text = $"{(l?.Owner is { Length: > 0 } o ? "@" + o + " · " : "")}{newest?.Label}", Classes = { "mono" }, FontSize = 11, Foreground = B(Mute), Margin = new Thickness(0, 4, 0, 0) },
        } });
        AsideHost.Children.Add(top);
        if (l?.Tagline != null) AsideHost.Children.Add(new TextBlock { Text = l.Tagline, FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21 });

        AsideHost.Children.Add(Fact(T("VERIFICATION", "VERIFICACIÓN"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Hexes(LevelOf(newest?.Level ?? "")), new TextBlock { Text = LevelName(newest?.Level), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } } }));
        var reqs = newest?.Requires.Where(r => r.IsMod).ToList() ?? new();
        if (reqs.Count > 0)
        {
            var list = new StackPanel { Spacing = 6 };
            foreach (var r in reqs)
            {
                bool have = _installed.ContainsKey(r.Name);
                string rn = _listing.TryGetValue(r.Name, out var rl) ? rl.Name : r.Name;
                list.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
                {
                    Rhombus(have ? Jade : Ember, filled: have),
                    new TextBlock { Text = $"{rn}{(r.MinVersion != null ? " ≥ " + r.MinVersion : "")} · " + (have ? T("you have it", "ya lo tienes") : T("comes with it", "se instala con este")), FontSize = 13, VerticalAlignment = VerticalAlignment.Center },
                } });
            }
            AsideHost.Children.Add(Fact(T("NEEDS", "NECESITA"), list));
        }
        if (newest != null) AsideHost.Children.Add(Fact(T("FILE", "ARCHIVO"), new TextBlock { Text = $"{Path.GetFileName(new Uri(newest.DownloadUrl).AbsolutePath)} · {newest.Size / 1024} KB\nSHA-256 {newest.Sha256[..16]}…", Classes = { "mono" }, FontSize = 11, TextWrapping = TextWrapping.Wrap }));

        var act = ActionButton(m, newest); act.HorizontalAlignment = HorizontalAlignment.Stretch; act.HorizontalContentAlignment = HorizontalAlignment.Center; act.Padding = new Thickness(14, 12);
        AsideHost.Children.Add(act);
        var link = new Button { Content = T("See it on Lattice ↗", "Ver en Lattice ↗"), Foreground = B(Ember), FontWeight = FontWeight.Normal, Padding = new Thickness(0) };
        link.Classes.Add("bare");
        string slug = m.Slug;
        link.Click += (_, _) => Opener.Url($"https://lattice.ultimaibrahim.dev/{(Es ? "" : "en/")}mod/{slug}");
        AsideHost.Children.Add(link);
    }

    private static Control Centered(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Center; return c; }

    private static Control Fact(string label, Control value) => new Border
    {
        Background = B(Card0), BorderBrush = B(Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12),
        Child = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = label, Classes = { "mono" }, FontSize = 10, Foreground = B(Mute), LetterSpacing = 0.8 }, value } },
    };

    // ───────────────────────── installing ─────────────────────────

    /// <summary>One click: installs right away, and asks first only when other mods would be installed with it.</summary>
    private async Task<bool> InstallMod(string slug, bool askedAlready = false)
    {
        if (_game == null || _snap == null || _busy != null) return false;
        string game = _game; var snap = _snap;
        var plan = LatticePlanner.Resolve(snap, LatticeInstaller.ReadInstalled(game, snap), slug, DateTime.UtcNow, upgrade: true);
        if (plan.Errors.Count > 0) { await Dialogs.Message(this, T("Catalog", "Catálogo"), string.Join("\n", plan.Errors)); return false; }
        if (!plan.CanInstall) { RefreshCatalogState(); RenderCatalog(); return true; }

        var extra = plan.Steps.Where(v => !v.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase)).ToList();
        if (extra.Count > 0 && !askedAlready)
        {
            string name = _listing.TryGetValue(slug, out var l) ? l.Name : slug;
            var lines = extra.Select(v => $"  ◆ {(_listing.TryGetValue(v.Slug, out var x) ? x.Name : v.Slug)} {v.Label}  ·  {v.Size / 1024} KB");
            string text = T($"{name} needs other mods. These come with it:", $"{name} necesita otros mods. Se instalan con él:") + "\n\n" + string.Join("\n", lines) +
                          "\n\n" + T("Each one is checked (signature, SHA-256, revocations) before it is copied.", "Cada uno se verifica (firma, SHA-256, revocaciones) antes de copiarlo.");
            if (!await Dialogs.Confirm(this, T("Install with its dependencies", "Instalar con sus dependencias"), text, T("Install all", "Instalar todo"), T("Cancel", "Cancelar"))) return false;
        }

        _busy = slug; _progress[slug] = 0.05;
        _selected = slug;
        RenderAside(); RenderCatalog();
        int total = plan.Steps.Count;
        int verified = 0;
        var log = new List<Check>();
        void Report(Check c)
        {
            log.Add(c);
            Dispatcher.UIThread.Post(() =>
            {
                if (c.Message.Contains("SHA-256 verified", StringComparison.OrdinalIgnoreCase)) { verified++; _buildCube?.Invoke(2); }
                _progress[slug] = Math.Min(0.95, 0.1 + 0.8 * verified / Math.Max(1, total));
                _buildLog?.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children =
                {
                    Rhombus(c.Level switch { CheckLevel.Ok => Jade, CheckLevel.Warn => Amber, CheckLevel.Fail => Garnet, _ => Mute }, filled: c.Level != CheckLevel.Info),
                    new TextBlock { Text = StepText(c), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 },
                } });
                UpdateAllButton.IsEnabled = false;
                RefreshBusyButton(slug);
            });
        }
        var started = Stopwatch.StartNew();
        bool ok = await Task.Run(() => LatticeInstaller.Install(game, plan, snap, new HttpLatticeFetcher(), dryRun: false, Report));
        // A small mod installs in a blink: keep the cube on screen long enough to be read as a moment, not a flicker.
        if (ok && started.ElapsedMilliseconds < 900) await Task.Delay(900 - (int)started.ElapsedMilliseconds);
        if (ok) { _buildCube?.Invoke(3); _progress[slug] = 1; RefreshBusyButton(slug); }
        await Task.Delay(ok ? 650 : 0);       // let the last face land before the panel goes back to the page
        _busy = null; _buildCube = null; _buildLog = null; _busyBar = null; _busyCard = null;
        RefreshCatalogState();
        Reload();
        RenderAside(); RenderCatalog();
        if (!ok)
        {
            string last = log.LastOrDefault(c => c.Level is CheckLevel.Fail or CheckLevel.Warn)?.Message ?? "";
            await Dialogs.Message(this, T("Catalog", "Catálogo"), T("Nothing was installed. ", "No se instaló nada. ") + last);
        }
        return ok;
    }

    /// <summary>The installer's log lines, said as short steps in the player's language; anything else stays as the installer wrote it.</summary>
    private string StepText(Check c)
    {
        string m = c.Message;
        string who = m.Split(':')[0].Split(' ')[0];
        string name = _listing.TryGetValue(who, out var l) ? l.Name : who;
        if (m.Contains("://", StringComparison.Ordinal) && c.Level == CheckLevel.Info) return T($"{name}: downloaded from GitHub", $"{name}: descargado de GitHub");
        if (m.Contains("SHA-256 verified", StringComparison.OrdinalIgnoreCase)) return T($"{name}: fingerprint (SHA-256) matches", $"{name}: la huella SHA-256 coincide");
        if (m.StartsWith("Replaced the older copy in ", StringComparison.Ordinal)) return T("Older copy moved out of ", "Copia vieja retirada de ") + m["Replaced the older copy in ".Length..] + T(" (kept in Aether/backup)", " (respaldada en Aether/backup)");
        if (m.StartsWith("Installed ", StringComparison.Ordinal)) return T("Copied into ", "Copiado a ") + m[(m.IndexOf("->", StringComparison.Ordinal) + 2)..].Trim();
        return m;
    }

    private void RefreshBusyButton(string slug)
    {
        // Only the label and the bar change while installing; the rest of the list is not redrawn.
        if (CatalogHost.Content is Panel p)
            foreach (var b in p.GetLogicalDescendants().OfType<Button>().Where(b => b.Classes.Contains("busy")))
                b.Content = T($"Installing… {(int)(_progress[slug] * 100)}%", $"Instalando… {(int)(_progress[slug] * 100)}%");
        if (_busyBar != null && _busyCard != null) _busyBar.Width = _busyCard.Bounds.Width * _progress[slug];
    }

    private async Task UpdateAll()
    {
        var slugs = _outdated.Keys.ToList();
        if (slugs.Count == 0) return;
        foreach (string s in slugs) if (!await InstallMod(s)) break;
    }

    // ───────────────────────── add a file by hand ─────────────────────────

    private async Task PickAndAddMod()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("Choose a mod (.zip or .dll)", "Elige un mod (.zip o .dll)"), AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Mods") { Patterns = new[] { "*.zip", "*.dll" } } },
        });
        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path != null) await AddModFrom(path);
    }

    /// <summary>Shows what a .zip or .dll would install and where (the same code as 'aether add'), then installs it.</summary>
    private async Task AddModFrom(string path)
    {
        string title = T("Add mod", "Agregar mod");
        if (_game == null || !Directory.Exists(_game)) { await Dialogs.Message(this, title, T("Block Story folder not found.", "No encontré la carpeta de Block Story.")); return; }
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        { await Dialogs.Message(this, title, T("Only .zip and .dll files can be installed.", "Solo se pueden instalar archivos .zip y .dll.")); return; }
        string game = _game;
        using AddPlan plan = await Task.Run(() => ModAdder.Prepare(game, path, hosted: true));
        if (!plan.CanInstall)
        {
            await Dialogs.Message(this, title, plan.Error ?? (plan.AlreadyProvided ? T("Aether already includes Aether Nano: nothing to install.", "Aether ya incluye Aether Nano: no hay nada que instalar.") : T("Nothing to install.", "No hay nada que instalar.")));
            return;
        }
        var text = new List<string>();
        text.AddRange(plan.Notes.Select(n => n.Message));
        if (text.Count > 0) text.Add(string.Empty);
        text.Add(T("This will install:", "Se instalará:"));
        text.AddRange(plan.Routed.Select(r => $"  {r.Info.FileName}  →  {r.DestDirRelative.Replace('\\', '/')}"));
        if (!await Dialogs.Confirm(this, title, string.Join("\n", text), T("Install", "Instalar"), T("Cancel", "Cancelar"))) return;
        await Task.Run(() => ModAdder.Apply(game, plan, dryRun: false, _ => { }));
        RefreshCatalogState();
        Reload();
        RenderCatalog();
        ShowSection("mods");
    }

    // ───────────────────────── Aether update ─────────────────────────

    /// <summary>Downloads the new Aether package, checks its SHA-256, and starts the new installer (the running window is never overwritten).</summary>
    private async void OnUpdateAether(object? sender, RoutedEventArgs e)
    {
        var update = _update;
        if (update == null || _game == null) return;
        if (update.ZipUrl == null) { Opener.Url(update.PageUrl); return; }
        if (!await Dialogs.Confirm(this, T("Update Aether", "Actualizar Aether"),
                T($"Download Aether {update.Version}, check it and open its installer?\nThis window will close.", $"¿Descargar Aether {update.Version}, verificarlo y abrir su instalador?\nEsta ventana se cerrará."),
                T("Update", "Actualizar"), T("Cancel", "Cancelar"))) return;

        UpdateButton.IsEnabled = false; UpdateText.Text = T("Downloading…", "Descargando…");
        string work = Path.Combine(Path.GetTempPath(), "aether-update-" + Guid.NewGuid().ToString("N"));
        string game = _game;
        string? error = null;
        string? exe = await Task.Run(() => UpdateChecker.Fetch(update, new HttpLatticeFetcher(), work, out error));
        if (exe == null)
        {
            UpdateButton.IsEnabled = true; UpdateText.Text = T($"Aether {update.Version} is available.", $"Aether {update.Version} está disponible.");
            await Dialogs.Message(this, T("Update Aether", "Actualizar Aether"), error ?? T("The update could not be fetched.", "No se pudo descargar la actualización."));
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

    // ───────────────────────── problems and logs ─────────────────────────

    private void RenderLogs()
    {
        int keep = LogFiles.SelectedIndex;
        LogFiles.Items.Clear();
        _logPaths.Clear();
        // Newest first: crash reports, then the kept play sessions.
        AddLogs(Path.Combine(_game!, "BepInEx", "crash-reports"), "crash-*.txt", T("crash", "fallo"), Garnet);
        string sessions = Path.Combine(_game!, "Aether", "Logs");
        if (Directory.Exists(sessions))
            foreach (string dir in Directory.GetDirectories(sessions).OrderByDescending(d => d, StringComparer.Ordinal))
                AddLogs(dir, "*.log", Path.GetFileName(dir), Mute);
        AddLogs(Path.Combine(_game!, "BepInEx"), "LogOutput.log", T("current", "actual"), Jade);
        if (LogFiles.Items.Count > 0) LogFiles.SelectedIndex = keep >= 0 && keep < LogFiles.Items.Count ? keep : 0;
    }

    private void AddLogs(string dir, string pattern, string label, Color sign)
    {
        if (!Directory.Exists(dir)) return;
        foreach (string file in Directory.GetFiles(dir, pattern).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            _logPaths.Add(file);
            LogFiles.Items.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            {
                Rhombus(sign, size: 10),
                new TextBlock { Inlines = new InlineCollection { new Run(label + "  ") { FontWeight = FontWeight.SemiBold }, new Run(Path.GetFileName(file)) { Foreground = B(Mute) } }, FontSize = 12 },
            } });
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
        catch (IOException ex) { LogText.Text = T("Could not read the file: ", "No se pudo leer el archivo: ") + ex.Message; }
    }

    // ───────────────────────── modding forum & guide ─────────────────────────

    private sealed record ForumEntry(string Slug, string Cat, int Gem, bool Pinned, string TitleEn, string TitleEs, string Tags, string Meta);

    private static readonly ForumEntry[] ForumThreads =
    {
        new("rules",       "start",   1, true,  "Rules of the game & Big Cube modding license", "Reglas del juego y licencia de Big Cube Interactive", "#licencia #4c #fair-play", "Licencia V2 · 2026"),
        new("intro",       "start",   1, true,  "Your first mod in 60 seconds: with BepInEx alone or with Aether", "Tu primer mod en 60 segundos: solo con BepInEx o con Aether", "#bepinex #nano #aether-new", "BepInEx 5 · Nano 1.0.3"),
        new("inst",        "start",   1, true,  "Where files live & why early-boot Awake crashes", "Dónde vive cada archivo y por qué Awake crashea el arranque", "#carpetas #awake #frame-0", "Unity 6000.5 · Frame 0"),
        new("lifecycle",   "engine",  2, true,  "Session lifecycle: scenes, Frame 0 & world transitions", "Ciclo de una partida: escenas, Frame 0 y transición al mundo", "#escenas #ciclo #in-game", "Unity 6000.5 · Escenas"),
        new("voxels",      "engine",  2, true,  "World & Voxels: reading blocks and 32×32 chunks safely", "Mundo y Voxels: leer bloques y chunks (32×32) sin congelar", "#voxels #chunks #nanoworld", "Chunks 32×32"),
        new("items",       "engine",  2, true,  "Blocks, Items, Inventory & the held tool (Digger) trap", "Bloques, Ítems, Inventario y la trampa de Digger", "#inventario #digger #items", "Inventario · Digger"),
        new("entities",    "engine",  2, true,  "Entities, Mobs, Health & the native POI Radar", "Entidades, Mobs, Salud (Health) y el Radar nativo", "#mobs #health #mobmanagement", "Health · Radar"),
        new("ngui",        "engine",  2, true,  "Native UI (NGUI) vs. IMGUI: fonts, atlases & UIWidget", "Interfaz nativa (NGUI) vs. IMGUI: fuentes, atlas y UIWidget", "#ngui #imgui #zantroke #uiwidget", "NGUI · IMGUI"),
        new("persistence", "engine",  2, true,  "Per-world saves vs. .cfg & keeping PlayerPrefs clean", "Guardado por mundo vs. .cfg y cómo no ensuciar PlayerPrefs", "#guardado #config #playerprefs", "FSWorldPersistence"),
        new("first",       "recipes", 3, true,  "Recipe 1: Project (.csproj) & code anatomy (pure BepInEx vs. Nano)", "Receta 1: Anatomía del .csproj y código (BepInEx puro vs. Nano)", "#csproj #bepinplugin #nanomod", "BepInEx · Nano"),
        new("conf",        "recipes", 3, true,  "Recipe 2: Configuration (.cfg in pure BepInEx & NanoPages UI)", "Receta 2: Configuración (.cfg en BepInEx puro y página con NanoPages)", "#config #nanopages #nanoicons", "Config.Bind · NanoPages"),
        new("keys",        "recipes", 3, true,  "Recipe 3: Keybinds (pure BepInEx Input & NanoKeys registration)", "Receta 3: Teclas de atajo (Input puro en BepInEx y registro con NanoKeys)", "#teclas #input #nanokeys", "KeyCode · NanoKeys"),
        new("assets",      "recipes", 3, true,  "Recipe 4: 60 FPS OnGUI HUD drawing (pure IMGUI & NanoSurface)", "Receta 4: Dibujar HUD en OnGUI a 60 FPS (IMGUI puro y NanoSurface)", "#imgui #nanosurface #hud", "IMGUI · NanoSurface"),
        new("harm",        "recipes", 3, true,  "Recipe 5: Clean Harmony patches & mod coexistence", "Receta 5: Parches de Harmony limpios y convivencia entre mods", "#harmony #prefix #postfix", "Harmony 2.x"),
        new("build",       "publish", 2, true,  "Build, install to BepInEx/plugins or Aether/Mods & test", "Compilar, instalar en BepInEx/plugins o Aether/Mods y probar", "#dotnet-build #plugins #qa", ".NET Standard 2.1"),
        new("pub",         "publish", 2, true,  "Publish on GitHub, Lattice verification tiers & §4c notice", "Publicar en GitHub, niveles de Lattice y aviso §4c", "#lattice #github #4c", "GitHub · Lattice"),
        new("traps",       "publish", 2, true,  "Conflict diagnostics, CLI & Known Traps", "Diagnóstico de conflictos, CLI y Trampas Conocidas", "#doctor #cli #trampas", "Diagnóstico · Trampas"),
        new("c/barra-progreso-borde-inferior-nanosurface", "comm", 4, false, "How to draw a bottom-edge progress bar on a Squircle card with NanoSurface?", "¿Cómo dibujo una barra de progreso en el borde inferior con NanoSurface?", "#nanosurface #hud #lens", "✓ Solved · Nano 1.0.3"),
        new("c/proyectar-posicion-3d-faro-pantalla-detras-camara", "comm", 4, false, "Projecting a 3D world marker with WorldToScreenPoint behind the camera", "Al proyectar un marcador 3D con WorldToScreenPoint aparece invertido detrás de la cámara", "#camera #3d #wayfinder", "✓ Solved · Unity 6000.5"),
        new("c/diferencia-nanopages-setting-comunicacion-mods", "comm", 4, false, "How can I read another Nano mod's setting without referencing its DLL?", "¿Cómo leo la opción de otro mod Nano sin referenciar su DLL?", "#nanopages #config", "✓ Solved · Nano 1.0.3"),
    };

    private void RenderForum()
    {
        if (ForumHost == null || ForumChipsPanel == null) return;
        ForumChipsPanel.Children.Clear();
        var cats = new (string Key, string Label)[]
        {
            ("", T("All", "Todos")),
            ("start", T("Start", "Empezar")),
            ("engine", T("Engine", "Motor")),
            ("recipes", T("Recipes", "Recetas")),
            ("publish", T("Publish", "Publicar")),
            ("comm", T("Cases", "Casos")),
        };
        foreach (var (key, label) in cats)
        {
            var chip = new Button { Content = label, Margin = new Thickness(4, 0, 0, 0) };
            chip.Classes.Add("chip");
            if (_forumCategory == key) chip.Classes.Add("on");
            string k = key;
            chip.Click += (_, _) => { _forumCategory = _forumCategory == k ? "" : k; RenderForum(); };
            ForumChipsPanel.Children.Add(chip);
        }

        ForumHost.Children.Clear();
        string q = (ForumSearchBox?.Text ?? "").Trim();
        foreach (var t in ForumThreads)
        {
            if (_forumCategory.Length > 0 && t.Cat != _forumCategory) continue;
            string title = Es ? t.TitleEs : t.TitleEn;
            if (q.Length > 0 &&
                !title.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !t.Tags.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !t.Meta.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            badges.Children.Add(Tag(t.Pinned ? T("◆ BY AETHER · PINNED", "◆ POR AETHER · FIJADO") : T("✓ SOLVED · CASE", "✓ RESUELTO · CASO"), strong: t.Pinned));
            badges.Children.Add(Tag(t.Meta));
            badges.Children.Add(new TextBlock { Text = t.Tags, FontSize = 11, Classes = { "mono" }, Foreground = B(Mute), VerticalAlignment = VerticalAlignment.Center });

            var openBtn = new Button
            {
                Content = T("Read thread ↗", "Leer hilo ↗"),
                Padding = new Thickness(12, 7),
                VerticalAlignment = VerticalAlignment.Center,
            };
            openBtn.Classes.Add("quiet");
            string url = (Es ? "https://aether.ultimaibrahim.dev/docs/" : "https://aether.ultimaibrahim.dev/en/docs/") + t.Slug + "/";
            openBtn.Click += (_, _) => Opener.Url(url);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,Auto") };
            var sign = Cube(32, t.Gem);
            sign.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(sign);

            var mid = new StackPanel { Spacing = 4, Margin = new Thickness(10, 0, 12, 0) };
            mid.Children.Add(badges);
            mid.Children.Add(new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeight.SemiBold, Foreground = B(Ink), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(mid, 1);
            row.Children.Add(mid);

            Grid.SetColumn(openBtn, 2);
            row.Children.Add(openBtn);

            var card = new Border { Padding = new Thickness(14, 11), Child = row };
            card.Classes.Add("card");
            ForumHost.Children.Add(card);
        }
    }
}
