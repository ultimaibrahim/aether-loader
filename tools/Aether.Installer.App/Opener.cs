using System.Diagnostics;

namespace Aether.Installer.App;

/// <summary>Opens a folder in the system file manager.</summary>
internal static class Opener
{
    public static void Url(string url)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", $"\"{url}\"") { UseShellExecute = false });
        }
        catch { /* no browser: nothing to do */ }
    }

    public static void Folder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", $"\"{path}\"") { UseShellExecute = false });
        }
        catch { /* no file manager: nothing to do */ }
    }
}
