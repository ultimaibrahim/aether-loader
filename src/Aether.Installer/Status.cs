using System.Diagnostics;

namespace Aether.Installer;

/// <summary>Read-only summary of what is installed (aether status).</summary>
public static class Status
{
    public static CheckReport Read(string? gameDir)
    {
        var report = new CheckReport();
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            report.GameFound = false;
            report.Add(CheckLevel.Fail, "ERR", "Block Story installation not found automatically. Pass --game-dir <path>.");
            return report;
        }

        report.Add(CheckLevel.Info, "OK", $"Game Directory : {gameDir}");

        string bepInExCore = Path.Combine(gameDir, "BepInEx", "core", "BepInEx.dll");
        if (File.Exists(bepInExCore))
        {
            var ver = FileVersionInfo.GetVersionInfo(bepInExCore).FileVersion ?? "5.4.x";
            report.Add(CheckLevel.Ok, "OK", $"BepInEx Core   : Installed (v{ver})");
        }
        else
            report.Add(CheckLevel.Warn, "WARN", "BepInEx Core   : Not found in BepInEx/core/BepInEx.dll");

        string aetherPluginDir = Path.Combine(gameDir, "BepInEx", "plugins", "Aether");
        string[] core = AetherAssemblies.Core;
        int coreInstalled = core.Count(dll => File.Exists(Path.Combine(aetherPluginDir, dll)));
        report.Add(coreInstalled == core.Length ? CheckLevel.Ok : CheckLevel.Warn, coreInstalled == core.Length ? "OK" : "WARN",
            coreInstalled == core.Length
                ? $"Aether Runtime : Ready ({coreInstalled}/{core.Length} core modules verified)"
                : $"Aether Runtime : Incomplete ({coreInstalled}/{core.Length} core modules found)");

        bool uiInstalled = AetherAssemblies.Ui.All(dll => File.Exists(Path.Combine(aetherPluginDir, dll)));
        report.Add(uiInstalled ? CheckLevel.Ok : CheckLevel.Info, uiInstalled ? "OK" : "INFO",
            uiInstalled ? "UI package     : Installed (menu, HUD, shared widgets)" : "UI package     : Not installed (optional; the official mods need it)");

        string aetherModsDir = Path.Combine(gameDir, "Aether", "Mods");
        int official = AetherAssemblies.OfficialMods.Count(dll => File.Exists(Path.Combine(aetherModsDir, dll)));
        report.Add(CheckLevel.Info, "INFO", $"Official mods : {official}/{AetherAssemblies.OfficialMods.Length} installed in Aether/Mods/ (optional)");

        string logsDir = Path.Combine(gameDir, "Aether", "Logs");
        if (Directory.Exists(logsDir))
            report.Add(CheckLevel.Info, "INFO", $"Session logs   : {Directory.GetDirectories(logsDir).Length} session(s) kept in Aether/Logs/");

        string crashDir = Path.Combine(gameDir, "BepInEx", "crash-reports");
        if (Directory.Exists(crashDir))
            report.Add(CheckLevel.Info, "INFO", $"Crash Reports  : {Directory.GetFiles(crashDir, "crash-*.txt").Length} report(s) in BepInEx/crash-reports/");

        return report;
    }
}
