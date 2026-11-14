namespace Aether.Installer;

public sealed class UninstallResult
{
    public int Removed { get; init; }

    /// <summary>True when other mods remain in Aether/Mods (they are never removed).</summary>
    public bool ThirdPartyLeft { get; init; }
}

/// <summary>Removes Aether and its official mods. BepInEx, third-party mods and the player's settings are kept unless purging.</summary>
public static class Uninstaller
{
    /// <summary>The files an uninstall would remove (only those that exist), so the player can be asked before anything is touched.</summary>
    public static IReadOnlyList<string> Plan(string gameDir, bool purge)
    {
        var targets = new List<string>();
        foreach (string dll in AetherAssemblies.All) targets.Add(Path.Combine(gameDir, InstallLayout.CoreDir, dll));
        foreach (string dll in AetherAssemblies.OfficialMods) targets.Add(Path.Combine(gameDir, InstallLayout.ModsDir, dll));
        if (purge)
        {
            targets.Add(Path.Combine(gameDir, "BepInEx", "config", "aether_conflicts.txt"));
            string cfg = Path.Combine(gameDir, "BepInEx", "config", "aether");
            if (Directory.Exists(cfg)) targets.AddRange(Directory.GetFiles(cfg));
        }
        return targets.Where(File.Exists).ToList();
    }

    public static UninstallResult Run(string gameDir, IReadOnlyList<string> files, bool dryRun)
    {
        var ops = new FileOps(dryRun);
        foreach (string f in files) ops.Delete(f);
        if (!dryRun) RemoveIfEmpty(Path.Combine(gameDir, InstallLayout.CoreDir));

        string mods = Path.Combine(gameDir, InstallLayout.ModsDir);
        bool left = Directory.Exists(mods) && Directory.GetFiles(mods, "*", SearchOption.AllDirectories).Length > 0;
        return new UninstallResult { Removed = files.Count, ThirdPartyLeft = left };
    }

    private static void RemoveIfEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch { }
    }
}
