namespace Aether.Installer;

/// <summary>Finds where the Aether DLLs to install come from: a release package ("payload"), or a build of a checkout.</summary>
public static class PayloadLocator
{
    /// <summary>The checkout root (the folder with Aether.sln) above this program, or the current folder.</summary>
    public static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Aether.sln"))) return dir;
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

    public static string? LocateCompiledDll(string repoRoot, string dllName)
    {
        string direct = Path.Combine(repoRoot, dllName);
        if (File.Exists(direct)) return direct;

        string srcDir = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(srcDir))
        {
            // Release package: payload/ holds the DLLs in their final layout.
            return Directory.GetFiles(repoRoot, dllName, SearchOption.AllDirectories).FirstOrDefault();
        }

        return Directory.GetFiles(srcDir, dllName, SearchOption.AllDirectories)
                        .Where(p => p.Contains(Path.Combine("bin", "Release")) || p.Contains("bin"))
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
    }
}
