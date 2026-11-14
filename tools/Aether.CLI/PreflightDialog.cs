using Aether.Installer;
using System.Diagnostics;
using System.Text;

namespace Aether.CLI;

/// <summary>
/// Shows the pre-launch notice outside the game: a desktop dialog when one is available, text on stderr otherwise.
/// Two choices only, as in other mod loaders: open the mods folder, or close. There is no way to continue.
/// </summary>
public static class PreflightDialog
{
    public static void Show(PreflightReport report, string gameDir)
    {
        string message = report.BuildMessage();
        string modsDir = Path.Combine(gameDir, InstallLayout.ModsDir);
        Directory.CreateDirectory(modsDir);

        if (OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(message);
            return;
        }

        while (true)
        {
            int? answer = AskWithDialog(message);
            if (answer == null) { Console.Error.WriteLine(message); return; } // no dialog tool: the text above is all we can show
            if (answer != 0) return;                                           // "Close"
            OpenFolder(modsDir);                                               // "Open mods folder", then ask again
        }
    }

    /// <summary>0 = open folder, anything else = close, null = no dialog tool found.</summary>
    private static int? AskWithDialog(string message)
    {
        const string title = "Aether - Blame!";
        if (Which("zenity") is { } zenity)
            return Run(zenity, "--question", "--no-markup", "--title", title, "--width", "560",
                "--text", message, "--ok-label", "Open mods folder", "--cancel-label", "Close");
        if (Which("kdialog") is { } kdialog)
            return Run(kdialog, "--title", title, "--yes-label", "Open mods folder", "--no-label", "Close", "--yesno", message);
        return null;
    }

    private static int Run(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static void OpenFolder(string dir)
    {
        string? opener = OperatingSystem.IsMacOS() ? "open" : Which("xdg-open");
        if (opener == null) return;
        try { Process.Start(new ProcessStartInfo(opener, $"\"{dir}\"") { UseShellExecute = false }); } catch { }
    }

    private static string? Which(string tool)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            string candidate = Path.Combine(dir, tool);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
