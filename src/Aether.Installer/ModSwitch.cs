namespace Aether.Installer;

/// <summary>
/// Switching a mod off without deleting it: the .dll is renamed to name.dll.disabled, which BepInEx and Aether skip (they load
/// *.dll only), and renamed back to switch it on. Settings, saved data and the file stay. Aether's own assemblies are never switched.
/// </summary>
public static class ModSwitch
{
    public const string DisabledSuffix = ".disabled";

    public static bool IsDisabled(string path) => path.EndsWith(".dll" + DisabledSuffix, StringComparison.OrdinalIgnoreCase);

    public static string EnabledPath(string path) => IsDisabled(path) ? path[..^DisabledSuffix.Length] : path;

    public static string DisabledPath(string path) => IsDisabled(path) ? path : path + DisabledSuffix;

    /// <summary>Aether's core files (loader, API, UI, Nano copy) keep the game modded; switching them off is "uninstall", not a switch.</summary>
    public static bool CanSwitch(InstalledAssembly a) => a.Role != AssemblyRole.Core;

    /// <summary>Turns the mod on or off and returns its new path. Refuses Aether's own files and anything that is not a mod file.</summary>
    public static string Set(InstalledAssembly a, bool enabled)
    {
        if (!CanSwitch(a)) throw new InvalidOperationException($"{a.Name} is part of Aether and cannot be switched off.");
        string from = a.Path, to = enabled ? EnabledPath(from) : DisabledPath(from);
        if (from == to) return to;
        if (!from.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !IsDisabled(from)) throw new InvalidOperationException($"{a.Name} is not a mod file.");
        // A newer copy may have been installed while it was off: the active file wins, the old switched-off copy goes.
        if (File.Exists(to)) { if (enabled) { File.Delete(from); return to; } File.Delete(to); }
        File.Move(from, to);
        return to;
    }
}
