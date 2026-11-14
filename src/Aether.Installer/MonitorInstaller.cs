namespace Aether.Installer;

/// <summary>
/// Installs the optional Aether Monitor: the same graphical program the installer is, started in monitor mode (a copy named
/// <c>aether-monitor</c> that shows crashes, mods and logs). It goes to Aether/Monitor and, on Linux, gets a menu entry.
/// </summary>
public static class MonitorInstaller
{
    public const string FolderName = "Monitor";

    public static string ExeName => OperatingSystem.IsWindows() ? "aether-monitor.exe" : "aether-monitor";

    public static string InstalledExe(string gameDir) => Path.Combine(gameDir, "Aether", FolderName, ExeName);

    /// <summary>True when the running program was started as the monitor (its file name says so, or --monitor was passed).</summary>
    public static bool IsMonitorMode(string? processPath, IEnumerable<string> args) =>
        args.Contains("--monitor", StringComparer.OrdinalIgnoreCase) ||
        (processPath != null && Path.GetFileNameWithoutExtension(processPath).Contains("monitor", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Copies the program (<paramref name="exePath"/> and the native libraries beside it in <paramref name="sourceDir"/>) into
    /// Aether/Monitor. Returns the installed executable, or null when there was nothing to copy.
    /// </summary>
    public static string? Install(string gameDir, string? exePath, string sourceDir, bool dryRun, Action<Check> report, string? menuEntryDir = null)
    {
        if (exePath == null || !File.Exists(exePath))
        {
            report(new Check(CheckLevel.Warn, "WARN", "Aether Monitor could not be installed: the program to copy was not found."));
            return null;
        }

        string target = InstalledExe(gameDir);
        string targetDir = Path.GetDirectoryName(target)!;
        if (!dryRun)
        {
            Directory.CreateDirectory(targetDir);
            // The program, plus the native libraries that sit next to it. Never the payload, the CLI or debug symbols.
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string name = Path.GetFileName(file);
                if (file == exePath || name.Equals(CliInstaller.FileName, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsNativeLibrary(name)) File.Copy(file, Path.Combine(targetDir, name), overwrite: true);
            }
            File.Copy(exePath, target, overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        report(new Check(CheckLevel.Ok, dryRun ? "DRY" : "COPY", $"{(dryRun ? "Would install" : "Installed")} Aether Monitor -> Aether/{FolderName}/{ExeName}"));

        if (OperatingSystem.IsLinux())
        {
            string dir = menuEntryDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
            if (!dryRun)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "aether-monitor.desktop"), DesktopEntry(target));
            }
            report(new Check(CheckLevel.Info, "INFO", "Added \"Aether Monitor\" to the applications menu."));
        }
        return target;
    }

    /// <summary>
    /// Puts an "Aether Monitor" shortcut on the desktop (a .lnk on Windows, a .desktop file on Linux). Returns its path, or null when it
    /// could not be made (the monitor is still installed and reachable from Aether/Monitor).
    /// </summary>
    public static string? CreateDesktopShortcut(string exe, bool dryRun, Action<Check> report, string? desktopDir = null)
    {
        string dir = desktopDir ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            report(new Check(CheckLevel.Warn, "WARN", "No desktop folder was found, so no shortcut was created."));
            return null;
        }
        try
        {
            string path = Path.Combine(dir, OperatingSystem.IsWindows() ? "Aether Monitor.lnk" : "Aether Monitor.desktop");
            if (!dryRun)
            {
                if (OperatingSystem.IsWindows()) WriteWindowsShortcut(path, exe);
                else
                {
                    File.WriteAllText(path, DesktopEntry(exe));
                    File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
                }
            }
            report(new Check(CheckLevel.Ok, dryRun ? "DRY" : "COPY", $"{(dryRun ? "Would create" : "Created")} a desktop shortcut: {path}"));
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            report(new Check(CheckLevel.Warn, "WARN", $"The desktop shortcut could not be created ({e.Message})."));
            return null;
        }
    }

    // The path and target travel in environment variables, so nothing a folder name contains can end up in the script text.
    private static void WriteWindowsShortcut(string lnkPath, string exe)
    {
        const string script = "$s=(New-Object -ComObject WScript.Shell).CreateShortcut($env:AETHER_LNK);$s.TargetPath=$env:AETHER_EXE;" +
                              "$s.WorkingDirectory=Split-Path $env:AETHER_EXE;$s.IconLocation=$env:AETHER_EXE;$s.Description='Aether Monitor';$s.Save()";
        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", script })
        { UseShellExecute = false, CreateNoWindow = true };
        psi.Environment["AETHER_LNK"] = lnkPath;
        psi.Environment["AETHER_EXE"] = exe;
        using var proc = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("PowerShell did not start.");
        if (!proc.WaitForExit(15000)) { try { proc.Kill(); } catch { } throw new InvalidOperationException("PowerShell took too long."); }
        if (proc.ExitCode != 0 || !File.Exists(lnkPath)) throw new InvalidOperationException("PowerShell could not write the shortcut.");
    }

    private static bool IsNativeLibrary(string name) =>
        name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase);

    public static string DesktopEntry(string exe) =>
        "[Desktop Entry]\nType=Application\nName=Aether Monitor\nComment=Crashes, mods and logs of your Block Story install\n" +
        $"Exec=\"{exe}\" --monitor\nTerminal=false\nCategories=Game;Utility;\n";
}
