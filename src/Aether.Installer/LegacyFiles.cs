using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Aether.Installer;

/// <summary>
/// Files of the compatibility layer Aether shipped before 1.2.0: every <c>Aether.Compat.*</c> assembly and any assembly in
/// Aether's own folders that depends on one. They are Aether's own files, so an update removes them outright, with no backup.
/// Nothing outside BepInEx/plugins/Aether and Aether/backup is looked at.
/// </summary>
public static class LegacyFiles
{
    const string Prefix = "Aether.Compat.";

    public static IEnumerable<string> Find(string gameDir)
    {
        var roots = new[]
        {
            (Path.Combine(gameDir, InstallLayout.CoreDir), SearchOption.TopDirectoryOnly),
            (Path.Combine(gameDir, "Aether", "backup"), SearchOption.AllDirectories),
        };
        foreach (var (root, depth) in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (string file in Directory.GetFiles(root, "*", depth))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) { yield return file; continue; }
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && DependsOnCompat(file))
                {
                    yield return file;
                    foreach (string side in new[] { ".pdb", ".xml", ".deps.json" })
                    {
                        string companion = Path.ChangeExtension(file, null) + side;
                        if (File.Exists(companion)) yield return companion;
                    }
                }
            }
        }
    }

    /// <summary>Removes the files <see cref="Find"/> returns, and backup folders left empty. Returns how many files went.</summary>
    public static int Remove(string gameDir, FileOps ops)
    {
        var files = Find(gameDir).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string f in files) ops.Delete(f);
        string backup = Path.Combine(gameDir, "Aether", "backup");
        if (!ops.DryRun && Directory.Exists(backup))
            foreach (string dir in Directory.GetDirectories(backup))
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (IOException) { }
        return files.Count;
    }

    static bool DependsOnCompat(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return false;
            MetadataReader md = pe.GetMetadataReader();
            foreach (AssemblyReferenceHandle h in md.AssemblyReferences)
                if (md.GetString(md.GetAssemblyReference(h).Name).StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException or UnauthorizedAccessException) { }
        return false;
    }
}
