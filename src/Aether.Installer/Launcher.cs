namespace Aether.Installer;

/// <summary>
/// <c>aether-launch</c>: the script Steam runs instead of <c>run_bepinex.sh</c>. It asks <c>aether preflight</c> first and starts
/// the game only if nothing is wrong, so a broken mod set never reaches Unity. It never blocks when the check itself cannot run.
/// </summary>
public static class Launcher
{
    public const string FileName = "aether-launch";
    public const string LaunchOption = "./aether-launch %command%";

    /// <summary>Where the installer keeps its own copy of the CLI for the launcher (self-contained builds only).</summary>
    public static readonly string CliRelativePath = Path.Combine("Aether", "aether");

    public const string Script = """
#!/usr/bin/env bash
# Written by 'aether install'. Steam launch option:  ./aether-launch %command%
# Checks the installed mods before the game starts; if something would stop them from loading, the game does not start.
# AETHER_SKIP_PREFLIGHT=1 skips the check. AETHER_CLI=<path> uses another aether binary.
dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="${AETHER_CLI:-$dir/Aether/aether}"
if [ -z "$AETHER_SKIP_PREFLIGHT" ] && [ -x "$cli" ]; then
    "$cli" preflight --game-dir "$dir" --dialog
    if [ $? -eq 3 ]; then
        echo "[aether-launch] Block Story was not started (see above). Fix the mods, or set AETHER_SKIP_PREFLIGHT=1." >&2
        exit 1
    fi
fi
exec "$dir/run_bepinex.sh" "$@"
""";

    /// <summary>Writes the launcher next to the game (Linux only; Windows has no launch script to wrap).</summary>
    public static bool Install(string gameDir, bool dryRun, Action<string, string> log)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(Path.Combine(gameDir, "run_bepinex.sh"))) return false;

        string path = Path.Combine(gameDir, FileName);
        if (dryRun) { log("DRY", $"Would write {FileName}"); return true; }

        File.WriteAllText(path, Script.Replace("\r\n", "\n") + "\n");
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        // The launcher runs the CLI that every install keeps in Aether/aether (see CliInstaller).
        if (File.Exists(CliInstaller.InstalledPath(gameDir)))
            log("INFO", $"{FileName} installed. Use this Steam launch option instead of run_bepinex.sh:  {LaunchOption}");
        else
            log("INFO", $"{FileName} installed, but {CliRelativePath} does not exist, so the pre-launch check is idle (a release package installs it). Steam launch option:  {LaunchOption}");
        return true;
    }
}
