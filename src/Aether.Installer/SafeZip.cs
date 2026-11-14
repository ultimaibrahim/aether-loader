using System.IO.Compression;

namespace Aether.Installer;

public static class SafeZip
{
    /// <summary>
    /// Extracts a zip refusing any entry that would land outside <paramref name="destDir"/> (zip-slip),
    /// absolute paths, and archives with an absurd number of entries or uncompressed size.
    /// </summary>
    public static void Extract(string zipPath, string destDir, long maxTotalBytes = 512L * 1024 * 1024, int maxEntries = 20_000)
    {
        string root = Path.GetFullPath(destDir);
        Directory.CreateDirectory(root);
        string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > maxEntries)
            throw new InvalidDataException($"Archive has {zip.Entries.Count} entries (limit {maxEntries}).");
        if (zip.Entries.Sum(e => e.Length) > maxTotalBytes)
            throw new InvalidDataException("Archive uncompressed size exceeds the safety limit.");

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(rootWithSep, StringComparison.Ordinal) && target != root)
                throw new InvalidDataException($"Unsafe path in archive: '{entry.FullName}'");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
