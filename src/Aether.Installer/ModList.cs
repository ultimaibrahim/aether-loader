namespace Aether.Installer;

public enum AssemblyRole { Core, OfficialMod, External }

public sealed record InstalledAssembly(string Name, string Path, double SizeKb, AssemblyRole Role);

public sealed class InstalledList
{
    public List<InstalledAssembly> Assemblies { get; } = new();
    public List<string> ConfigFiles { get; } = new();
    public bool HasConfigFolder { get; set; }
}

/// <summary>What is installed in Aether/Mods and BepInEx/plugins (aether list).</summary>
public static class ModList
{
    public static InstalledList Read(string gameDir)
    {
        var list = new InstalledList();
        var dlls = new List<string>();
        foreach (string root in new[] { Path.Combine(gameDir, "Aether", "Mods"), Path.Combine(gameDir, "BepInEx", "plugins") })
            if (Directory.Exists(root)) dlls.AddRange(Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories));

        foreach (string dll in dlls.OrderBy(p => Path.GetFileName(p)))
        {
            string name = Path.GetFileName(dll);
            AssemblyRole role = AetherAssemblies.All.Contains(name, StringComparer.OrdinalIgnoreCase) ? AssemblyRole.Core
                : AetherAssemblies.OfficialMods.Contains(name, StringComparer.OrdinalIgnoreCase) ? AssemblyRole.OfficialMod
                : AssemblyRole.External;
            list.Assemblies.Add(new InstalledAssembly(name, dll, new FileInfo(dll).Length / 1024.0, role));
        }

        string configDir = Path.Combine(gameDir, "BepInEx", "config", "aether");
        if (Directory.Exists(configDir))
        {
            list.HasConfigFolder = true;
            list.ConfigFiles.AddRange(Directory.GetFiles(configDir, "*.json").Select(Path.GetFileName)!);
        }
        return list;
    }
}
