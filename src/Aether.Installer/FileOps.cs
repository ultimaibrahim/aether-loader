namespace Aether.Installer;

/// <summary>File operations that honour a dry run: with <see cref="DryRun"/> set nothing on disk is touched.</summary>
public sealed class FileOps
{
    public bool DryRun { get; }
    public FileOps(bool dryRun) => DryRun = dryRun;

    public void EnsureDir(string dir)
    {
        if (!DryRun) Directory.CreateDirectory(dir);
    }

    public void Copy(string source, string dest)
    {
        if (DryRun) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, overwrite: true);
    }

    public void Delete(string path)
    {
        if (DryRun || !File.Exists(path)) return;
        try { File.Delete(path); } catch { }
    }

    public static bool SameContent(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && BepInExSetup.Sha256Of(a) == BepInExSetup.Sha256Of(b);

    public void Backup(string path, string backupRoot)
    {
        if (DryRun) return;
        Directory.CreateDirectory(backupRoot);
        File.Copy(path, Path.Combine(backupRoot, Path.GetFileName(path)), overwrite: true);
    }
}
