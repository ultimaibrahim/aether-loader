namespace Aether.Installer;

/// <summary>
/// Keeps a copy of the <c>aether</c> command line tool inside the game folder (Aether/aether). It is part of Aether: doctor,
/// list, add and the logs all live there, and a player who skips the optional monitor still has it.
/// </summary>
public static class CliInstaller
{
    public static string FileName => OperatingSystem.IsWindows() ? "aether.exe" : "aether";

    public static string InstalledPath(string gameDir) => Path.Combine(gameDir, "Aether", FileName);

    /// <summary>
    /// The self-contained CLI to copy: the running program when it is the CLI, otherwise the one shipped next to it
    /// (a release package puts <c>aether</c> beside the installer). Null when there is none, e.g. in a development build run with 'dotnet'.
    /// </summary>
    public static string? FindSource(string? nextToDirectory = null)
    {
        string? self = Environment.ProcessPath;
        if (self != null && Path.GetFileName(self).Equals(FileName, StringComparison.OrdinalIgnoreCase)) return self;

        string beside = Path.Combine(nextToDirectory ?? AppContext.BaseDirectory, FileName);
        return File.Exists(beside) ? beside : null;
    }

    /// <summary>Copies the CLI into the game folder. Returns false (and says why) when there is nothing to copy.</summary>
    public static bool Install(string gameDir, string? source, bool dryRun, Action<Check> report)
    {
        if (source == null || !File.Exists(source))
        {
            report(new Check(CheckLevel.Info, "INFO",
                "The aether command line tool was not copied into the game folder (this build is not a self-contained release; a release package includes it)."));
            return false;
        }

        string target = InstalledPath(gameDir);
        if (!dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        report(new Check(CheckLevel.Ok, dryRun ? "DRY" : "COPY",
            $"{(dryRun ? "Would install" : "Installed")} the aether command line tool -> Aether/{FileName}"));
        return true;
    }
}
