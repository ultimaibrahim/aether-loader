using Avalonia;
using Aether.Installer;

namespace Aether.Installer.App;

internal static class Program
{
    /// <summary>One program, two faces: the installer, and (started as aether-monitor or with --monitor) the monitor.</summary>
    public static bool MonitorMode { get; private set; }

    /// <summary>The game folder the monitor was installed into (--game-dir), when it was given.</summary>
    public static string? GameDirArgument { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        MonitorMode = MonitorInstaller.IsMonitorMode(Environment.ProcessPath, args);
        int i = Array.IndexOf(args, "--game-dir");
        if (i >= 0 && i + 1 < args.Length) GameDirArgument = args[i + 1];
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Windows: software rendering only. These windows are plain forms, and it lets the package leave out ANGLE (av_libglesv2.dll, 4 MB).
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = new[] { Win32RenderingMode.Software } });
}
