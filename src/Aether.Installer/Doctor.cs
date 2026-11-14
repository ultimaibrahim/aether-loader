namespace Aether.Installer;

/// <summary>Read-only health check of an install (aether doctor): the same findings for the CLI and for a window.</summary>
public static class Doctor
{
    public static CheckReport Run(string? gameDir)
    {
        var report = new CheckReport();
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            report.GameFound = false;
            report.Add(CheckLevel.Fail, "FAIL", "Block Story installation directory not found.");
            report.Issues = 1;
            return report;
        }

        if (BepInExSetup.IsInstalled(gameDir))
            report.Add(CheckLevel.Ok, "PASS", "BepInEx core present");
        else
        {
            report.Add(CheckLevel.Fail, "FAIL", "BepInEx is not installed. Run 'aether install --yes'.");
            report.Issues++;
        }

        string aetherDir = Path.Combine(gameDir, "BepInEx", "plugins", "Aether");
        string aetherModsDir = Path.Combine(gameDir, "Aether", "Mods");
        CheckMisplacedFiles(gameDir, report);

        foreach (string coreDll in AetherAssemblies.Core)
        {
            if (File.Exists(Path.Combine(aetherDir, coreDll)))
                report.Add(CheckLevel.Ok, "PASS", $"Core assembly verified: {coreDll}");
            else
            {
                report.Add(CheckLevel.Fail, "FAIL", $"Missing core assembly : {coreDll}");
                report.Issues++;
            }
        }

        // The UI package is separate from the core: the loader runs without it, but the official mods do not.
        foreach (string uiDll in AetherAssemblies.Ui)
        {
            if (File.Exists(Path.Combine(aetherDir, uiDll)))
                report.Add(CheckLevel.Ok, "PASS", $"UI package verified: {uiDll}");
            // The official mods are Nano mods: they need Aether.Nano.dll, not Aether's F10 menu.
            else if (uiDll == "Aether.Nano.dll" && AetherAssemblies.OfficialMods.Any(d => File.Exists(Path.Combine(aetherModsDir, d))))
            {
                report.Add(CheckLevel.Fail, "FAIL", $"Missing UI package : {uiDll} (the official mods installed here need it)");
                report.Issues++;
            }
            else
                report.Add(CheckLevel.Info, "INFO", $"UI package not installed ({uiDll}): there is no F10 menu or HUD. Run 'aether install' to add it.");
        }

        // Official mods are optional: report what is installed, never treat a missing one as a problem.
        int officialInstalled = 0;
        foreach (string modDll in AetherAssemblies.OfficialMods)
        {
            if (!File.Exists(Path.Combine(aetherModsDir, modDll))) continue;
            report.Add(CheckLevel.Ok, "PASS", $"Official mod installed : {modDll}");
            officialInstalled++;
        }
        if (officialInstalled == 0)
            report.Add(CheckLevel.Info, "INFO", "No official mods installed (they are optional). Add one with 'aether add <file.zip>'.");

        string conflictReport = Path.Combine(gameDir, "BepInEx", "config", "aether_conflicts.txt");
        if (File.Exists(conflictReport))
        {
            report.ConflictReport = File.ReadAllText(conflictReport);
            report.Issues += report.ConflictReport.Split('\n').Count(l => l.TrimStart().StartsWith("[ISSUE]"));
        }
        else
            report.Add(CheckLevel.Info, "INFO", "Conflict report : not generated yet (launch the game once)");

        // Hotkeys that two mods bind to the same key, read straight from the config files (no game launch needed).
        var keyOverlaps = Aether.Loader.Diagnostics.KeybindOverlaps.Describe(
            Aether.Loader.Diagnostics.KeybindOverlaps.ScanDirectory(Path.Combine(gameDir, "BepInEx", "config")));
        foreach (string overlap in keyOverlaps)
            report.Add(CheckLevel.Warn, "WARN", overlap);
        if (keyOverlaps.Count == 0)
            report.Add(CheckLevel.Ok, "PASS", "Hotkeys        : no key is bound by more than one mod");

        string configDir = Path.Combine(gameDir, "BepInEx", "config", "aether");
        if (Directory.Exists(configDir))
        {
            foreach (string jsonFile in Directory.GetFiles(configDir, "*.json"))
            {
                string content = File.ReadAllText(jsonFile).Trim();
                if (content.StartsWith("{") && content.EndsWith("}"))
                    report.Add(CheckLevel.Ok, "PASS", $"Valid JSON config     : {Path.GetFileName(jsonFile)}");
                else
                {
                    report.Add(CheckLevel.Warn, "WARN", $"Malformed JSON config : {Path.GetFileName(jsonFile)}");
                    report.Issues++;
                }
            }
        }
        return report;
    }

    /// <summary>BepInEx's [Preloader.Entrypoint] Type ("Application" by default; some setups use "Camera"), or null without a config.</summary>
    public static string? EntrypointType(string gameDir)
    {
        string cfg = Path.Combine(gameDir, "BepInEx", "config", "BepInEx.cfg");
        try
        {
            if (!File.Exists(cfg)) return null;
            bool inSection = false;
            foreach (string raw in File.ReadLines(cfg))
            {
                string line = raw.Trim();
                if (line.StartsWith('[')) { inSection = line.Equals("[Preloader.Entrypoint]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!inSection || !line.StartsWith("Type", StringComparison.OrdinalIgnoreCase)) continue;
                int eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim().Equals("Type", StringComparison.OrdinalIgnoreCase)) return line[(eq + 1)..].Trim();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return "Application";
    }

    /// <summary>[Hosting] HostBepInExPlugins in the loader's config; true when the file or the key is absent (the default).</summary>
    public static bool HostingEnabled(string gameDir)
    {
        string cfg = Path.Combine(gameDir, "BepInEx", "config", "dev.ultimaibrahim.aether.cfg");
        if (!File.Exists(cfg)) return true;
        foreach (string line in File.ReadLines(cfg))
        {
            string t = line.Trim();
            if (!t.StartsWith("HostBepInExPlugins", StringComparison.OrdinalIgnoreCase)) continue;
            int eq = t.IndexOf('=');
            return eq < 0 || !t.Substring(eq + 1).Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    /// <summary>Flags the classic install mistakes: core DLLs in Aether/Mods, mods in the core folder, loose files.</summary>
    private static void CheckMisplacedFiles(string gameDir, CheckReport report)
    {
        string mods = Path.Combine(gameDir, InstallLayout.ModsDir);
        string core = Path.Combine(gameDir, InstallLayout.CoreDir);
        string[] all = AetherAssemblies.All;

        if (Directory.Exists(mods))
        {
            foreach (string dll in Directory.GetFiles(mods, "*.dll"))
            {
                string name = Path.GetFileName(dll);
                if (all.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    report.Add(CheckLevel.Warn, "WARN", $"{name} is a core assembly but sits in Aether/Mods (belongs in BepInEx/plugins/Aether).");
                    report.Issues++;
                }
                else if (ModInspector.Inspect(dll).Kind == ModKind.BepInExPlugin)
                {
                    // Hosting is on by default: Aether starts these plugins itself, on frame 0 (see docs/KNOWN_TRAPS.md, R1).
                    if (HostingEnabled(gameDir))
                        report.Add(CheckLevel.Ok, "OK", $"{name} is a BepInEx plugin hosted by Aether from Aether/Mods.");
                    else
                    {
                        report.Add(CheckLevel.Warn, "WARN", $"{name} is a BepInEx plugin in Aether/Mods, but [Hosting] HostBepInExPlugins is off, so it will not run (move it to BepInEx/plugins).");
                        report.Issues++;
                    }
                }
            }
        }

        if (OperatingSystem.IsLinux() && SteamLaunchOption.ScriptIsOutdated(gameDir))
        {
            report.Add(CheckLevel.Warn, "WARN",
                "run_bepinex.sh is an older BepInEx script, which cannot start the game from current Steam. " +
                "Reinstall BepInEx with Aether's installer.");
            report.Issues++;
        }

        if (Directory.Exists(core))
        {
            foreach (string dll in Directory.GetFiles(core, "*.dll"))
            {
                string name = Path.GetFileName(dll);
                if (!all.Contains(name, StringComparer.OrdinalIgnoreCase) && ModInspector.Inspect(dll).Kind == ModKind.AetherMod)
                {
                    report.Add(CheckLevel.Warn, "WARN", $"{name} is a mod but sits in the core folder (belongs in Aether/Mods).");
                    report.Issues++;
                }
            }
        }
    }
}
