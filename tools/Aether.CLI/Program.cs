using Aether.Installer;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Aether.CLI;

public static partial class Program
{
    private static string[] CoreDlls => AetherAssemblies.Core;
    private static string[] UiDlls => AetherAssemblies.Ui;
    private static string[] AllAetherDlls => AetherAssemblies.All;
    private static string[] FlagshipModDlls => AetherAssemblies.OfficialMods;
    private static string[] RetiredModDlls => AetherAssemblies.RetiredMods;
    private static string[] RetiredCoreDlls => AetherAssemblies.RetiredCore;

    public static int Main(string[] args)
    {
        PrintBanner();

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string? gameDir = GetOption(args, "--game-dir") ?? DetectBlockStoryPath();
        string? sourceDir = GetOption(args, "--source-dir");
        string[] selectedMods = [];
        string? selectionError = null;
        if (command == "install" && !InstallSelection.TryResolve(args, out selectedMods, out selectionError))
        {
            WriteStatus("ERR", ConsoleColor.Red, selectionError!);
            return 1;
        }
        DryRun = HasFlag(args, "--dry-run");
        AssumeYes = HasFlag(args, "--yes") || HasFlag(args, "-y");
        SkipBepInEx = HasFlag(args, "--skip-bepinex");
        NoUi = HasFlag(args, "--no-ui");

        return command switch
        {
            "status" => RunStatus(gameDir),
            "install" => RunInstall(gameDir, sourceDir, selectedMods),
            "list" => RunList(gameDir),
            "doctor" => RunDoctor(gameDir),
            "preflight" => RunPreflight(gameDir, HasFlag(args, "--dialog")),
            "new" => RunNewMod(args, gameDir),
            "add" => RunAdd(gameDir, args),
            "lattice" => RunLattice(gameDir, args),
            "uninstall" => RunUninstall(gameDir, HasFlag(args, "--purge")),
            _ => PrintUnknownCommand(command)
        };
    }

    private static void PrintBanner()
    {
        Console.WriteLine($"Aether CLI {Aether.Loader.AetherBootstrap.PluginVersion}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("Usage: aether <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  status                 Inspect Block Story, BepInEx, Aether Core, and installed mods");
        Console.WriteLine("  install                Install the Aether loader into Block Story (mods are optional: see --mods)");
        Console.WriteLine("  list                   List installed mods and configuration files");
        Console.WriteLine("  doctor                 Run diagnostic verification on Aether runtime & crash reports");
        Console.WriteLine("  lattice <install <mod>|update [mod]|list|check> Install from the Lattice catalog (signature, SHA-256, revocations and required mods are verified)");
        Console.WriteLine("  add <file.dll|file.zip> Install a third-party mod into the right folder (Aether/Mods or BepInEx/plugins)");
        Console.WriteLine("  uninstall              Remove the Aether loader and its official mods (keeps third-party mods and configs)");
        Console.WriteLine("  preflight              Check the installed mods before the game starts (exit 3 = do not start; --dialog shows a window)");
        Console.WriteLine("  new <ModName>          Create a starter mod (plain BepInEx plugin with Aether Nano built in; --aether for the Aether API)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --game-dir <path>      Explicit path to Block Story installation directory");
        Console.WriteLine("  --source-dir <path>    Explicit path to Aether build output or repository root");
        Console.WriteLine("  --with-cli             With 'install': also keep this command line tool in <game>/Aether (not needed for playing)");
        Console.WriteLine("  --with-launcher        With 'install' (Linux, experimental): also write aether-launch, a launcher that runs 'aether preflight' first");
        Console.WriteLine("  --no-ui                With 'install': skip the UI package (Aether.UI.dll: F10 menu, HUD, shared widgets)");
        Console.WriteLine("  --core-only            Install only the loader (this is already the default)");
        Console.WriteLine("  --mods <list>          Also install some official mods: blueprint, dusk, lens, wayfinder (comma-separated)");
        Console.WriteLine("  --with-mods            Also install all four official mods (alias: --with-flagships)");
        Console.WriteLine("  --output <path>        Target folder for 'aether new <ModName>'");
        Console.WriteLine("  --dry-run              Show what would change without touching any file");
        Console.WriteLine("  --yes, -y              Do not ask for confirmation (also installs BepInEx if missing)");
        Console.WriteLine("  --no-hosted            With 'add': put BepInEx plugins in BepInEx/plugins instead of Aether/Mods (default: hosted by Aether,");
        Console.WriteLine("                         which needs [Hosting] HostBepInExPlugins, on by default; some plugins crash Unity when started at boot)");
        Console.WriteLine("  --aether               With 'new': write a mod against the Aether API instead of the default Nano mod");
        Console.WriteLine("  --force                With 'new': overwrite an existing project");
        Console.WriteLine("  --purge                With 'uninstall': also delete Aether configs and the conflict report");
        Console.WriteLine();
    }

    private static int PrintUnknownCommand(string cmd)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] Unknown command: '{cmd}'");
        Console.ResetColor();
        PrintUsage();
        return 1;
    }

    public static string? DetectBlockStoryPath() => GameLocator.Detect();

    private static int RunStatus(string? gameDir)
    {
        Console.WriteLine();
        CheckReport report = Status.Read(gameDir);
        PrintChecks(report);
        if (report.GameFound) Console.WriteLine();
        return report.GameFound ? 0 : 1;
    }

    private static ConsoleColor ColorOf(CheckLevel level) => level switch
    {
        CheckLevel.Ok => ConsoleColor.Green,
        CheckLevel.Warn => ConsoleColor.Yellow,
        CheckLevel.Fail => ConsoleColor.Red,
        _ => ConsoleColor.Cyan
    };

    private static void PrintChecks(CheckReport report)
    {
        foreach (Check c in report.Checks) WriteStatus(c.Badge, ColorOf(c.Level), c.Message);
    }

    private static int RunInstall(string? gameDir, string? sourceDir, string[] selectedMods)
    {
        Console.WriteLine();
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            WriteStatus("ERR", ConsoleColor.Red, "Target Block Story directory does not exist. Specify --game-dir <path>.");
            return 1;
        }

        if (!SkipBepInEx && !BepInExSetup.IsInstalled(gameDir))
        {
            WriteStatus("WARN", ConsoleColor.Yellow, $"BepInEx is not installed in '{gameDir}'.");
            if (!Confirm($"Download BepInEx {BepInExSetup.Version} (SHA-256 verified) and install it?"))
            {
                WriteStatus("ERR", ConsoleColor.Red, "Aether needs BepInEx 5.x. Re-run with --yes to let the installer set it up.");
                return 1;
            }
            if (!BepInExSetup.Install(gameDir, DryRun, (b, m) => WriteStatus(b, b == "ERR" ? ConsoleColor.Red : ConsoleColor.Cyan, m)))
                return 1;
        }

        var result = InstallRunner.Run(new InstallOptions
        {
            GameDir = gameDir,
            SourceDir = sourceDir,
            SelectedMods = selectedMods,
            NoUi = NoUi,
            DryRun = DryRun,
            WithLauncher = HasFlag(Environment.GetCommandLineArgs(), "--with-launcher"),
            WithCli = HasFlag(Environment.GetCommandLineArgs(), "--with-cli")
        }, c =>
        {
            if (c.Badge == "DONE") Console.WriteLine();
            WriteStatus(c.Badge, c.Badge is "DONE" ? ConsoleColor.Magenta : ColorOf(c.Level), c.Message);
        });
        return result.Success ? 0 : 1;
    }

    private static int RunList(string? gameDir)
    {
        Console.WriteLine();
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            WriteStatus("ERR", ConsoleColor.Red, "Block Story directory not found.");
            return 1;
        }

        InstalledList list = ModList.Read(gameDir);
        Console.WriteLine($"Installed Assemblies (Aether/Mods + BepInEx/plugins) ({list.Assemblies.Count}):");
        Console.WriteLine(new string('─', 68));
        foreach (InstalledAssembly a in list.Assemblies)
        {
            (string tag, ConsoleColor color) = a.Role switch
            {
                AssemblyRole.Core => ("[CORE]", ConsoleColor.Cyan),
                AssemblyRole.OfficialMod => ("[MOD] ", ConsoleColor.Magenta),
                _ => ("[EXT] ", ConsoleColor.Gray)
            };
            Console.ForegroundColor = color;
            Console.Write($"  {tag} ");
            Console.ResetColor();
            Console.WriteLine($"{a.Name,-34} {a.SizeKb,7:F1} KB");
        }

        if (list.HasConfigFolder)
        {
            Console.WriteLine();
            Console.WriteLine($"Active Aether Configuration Files ({list.ConfigFiles.Count}):");
            Console.WriteLine(new string('─', 68));
            foreach (string cfg in list.ConfigFiles) Console.WriteLine($"  [CFG]  {cfg}");
        }

        Console.WriteLine();
        return 0;
    }

    private static int RunDoctor(string? gameDir)
    {
        Console.WriteLine();
        CheckReport report = Doctor.Run(gameDir);
        PrintChecks(report);
        if (!report.GameFound) return 1;

        if (report.ConflictReport != null)
        {
            Console.WriteLine();
            Console.WriteLine("Aether conflict report:");
            Console.WriteLine(new string('─', 68));
            Console.WriteLine(report.ConflictReport);
        }

        Console.WriteLine();
        if (report.Issues == 0)
        {
            WriteStatus("OK", ConsoleColor.Cyan, "Aether Doctor completed with 0 issues. Ready to launch Block Story!");
            return 0;
        }
        WriteStatus("WARN", ConsoleColor.Yellow, $"Aether Doctor detected {report.Issues} issue(s). Run 'aether install' to repair.");
        return 1;
    }

    /// <summary>Exit codes: 0 = fine, 3 = the game should not start, anything else = the check itself failed (never block on that).</summary>
    private static int RunPreflight(string? gameDir, bool dialog)
    {
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            WriteStatus("ERR", ConsoleColor.Red, "Block Story directory not found. Pass --game-dir <path>.");
            return 1;
        }

        PreflightReport report = Preflight.Check(gameDir);
        if (!report.Blocked)
        {
            WriteStatus("OK", ConsoleColor.Green, $"Preflight: {report.ModsChecked} Aether mod(s) checked, nothing in the way.");
            return 0;
        }

        if (dialog) PreflightDialog.Show(report, gameDir);
        else Console.Error.WriteLine(report.BuildMessage());
        return PreflightReport.BlockedExitCode;
    }

    private static int RunNewMod(string[] args, string? gameDir)
    {
        if (args.Length < 2 || args[1].StartsWith("-"))
        {
            WriteStatus("ERR", ConsoleColor.Red, "Usage: aether new <ModName> [--aether] [--output <dir>] [--game-dir <path>] [--force]");
            return 1;
        }

        string rawName = args[1];
        bool apiTemplate = HasFlag(args, "--aether");
        var files = ModScaffold.Generate(rawName, gameDir, apiTemplate ? ModTemplate.Aether : ModTemplate.Nano);
        if (files.Count == 0)
        {
            WriteStatus("ERR", ConsoleColor.Red, "ModName must contain letters or digits.");
            return 1;
        }

        string safeName = ModScaffold.SafeName(rawName);
        string outputRoot = GetOption(args, "--output") ?? Path.Combine(Directory.GetCurrentDirectory(), safeName);
        if (!HasFlag(args, "--force") && files.Keys.Any(f => File.Exists(Path.Combine(outputRoot, f))))
        {
            WriteStatus("ERR", ConsoleColor.Red, $"'{outputRoot}' already has project files. Use --force to overwrite them.");
            return 1;
        }

        Directory.CreateDirectory(outputRoot);
        foreach (var (name, content) in files)
        {
            string target = Path.Combine(outputRoot, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
        }

        Console.WriteLine();
        WriteStatus("NEW", ConsoleColor.Green, $"Scaffolded {(apiTemplate ? "Aether" : "Nano")} mod '{rawName}' at {outputRoot}");
        foreach (string name in files.Keys)
            WriteStatus("FILE", ConsoleColor.Cyan, $"  - {name}");

        Console.WriteLine();
        string setting = apiTemplate ? "AetherDir" : "GameDir";
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
            WriteStatus("WARN", ConsoleColor.Yellow, $"Block Story was not found. Open {safeName}.csproj and set {setting} before building (or pass --game-dir).");
        else if (apiTemplate && !File.Exists(Path.Combine(gameDir, "BepInEx", "plugins", "Aether", "Aether.Loader.dll")))
            WriteStatus("WARN", ConsoleColor.Yellow, "Aether is not installed in that game folder yet. Run 'aether install' before building.");
        WriteStatus("NEXT", ConsoleColor.Cyan, $"cd {outputRoot} && dotnet build {safeName}.csproj -c Release -p:InstallToGame=true");
        return 0;
    }

    private static string? GetOption(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteStatus(string badge, ConsoleColor color, string message)
    {
        Console.Write("[");
        Console.ForegroundColor = color;
        Console.Write($"{badge,-5}");
        Console.ResetColor();
        Console.WriteLine($"] {message}");
    }
}
