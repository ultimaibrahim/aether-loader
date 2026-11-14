using System.Security.Cryptography;
using System.Text.Json;

namespace Aether.Installer.Lattice;

/// <summary>Downloads every file of a plan, checks size and SHA-256 against the signed index, and only then installs anything.</summary>
public static class LatticeInstaller
{
    public static string InstalledPath(string gameDir) => Path.Combine(LatticeStore.CacheDir(gameDir), "installed.json");

    public static Dictionary<string, InstalledLatticeMod> ReadInstalled(string gameDir)
    {
        var result = new Dictionary<string, InstalledLatticeMod>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var d = JsonDocument.Parse(File.ReadAllText(InstalledPath(gameDir)));
            foreach (var p in d.RootElement.EnumerateObject())
                result[p.Name] = new InstalledLatticeMod(p.Name, p.Value.GetProperty("version").GetString()!, p.Value.GetProperty("sha256").GetString()!.ToLowerInvariant());
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException) { }
        return result;
    }

    /// <summary>
    /// The recorded catalog installs plus catalog mods the player put in Aether/Mods by hand (a dropped .dll, "Add mod…", an
    /// older Aether release). A file counts when its name is the .dll a catalog version publishes; its version is the one whose
    /// SHA-256 matches, else the [AetherMod] version inside it. Without this, those mods never showed their updates.
    /// </summary>
    public static Dictionary<string, InstalledLatticeMod> ReadInstalled(string gameDir, LatticeSnapshot snap)
    {
        var result = ReadInstalled(gameDir);
        // Aether/Mods first; then the copies Aether ships itself (Aether Nano), which satisfy a catalog requirement without a download.
        foreach (var (dir, bundled) in new[] { (Path.Combine(gameDir, InstallLayout.ModsDir), false), (Path.Combine(gameDir, InstallLayout.PluginsDir, "Aether"), true) })
            if (Directory.Exists(dir)) AddFound(result, snap, dir, bundled);
        return result;
    }

    static void AddFound(Dictionary<string, InstalledLatticeMod> result, LatticeSnapshot snap, string dir, bool bundled)
    {
        foreach (var mod in snap.Index.Mods)
        {
            if (result.ContainsKey(mod.Slug)) continue;
            foreach (string name in mod.Versions.Select(v => Path.GetFileName(new Uri(v.DownloadUrl).AbsolutePath))
                         .Where(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.Combine(dir, name);
                if (!File.Exists(file)) continue;
                string sha;
                try { using var s = File.OpenRead(file); sha = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); }
                catch (IOException) { continue; }
                string? version = mod.Versions.FirstOrDefault(v => v.Sha256.Equals(sha, StringComparison.OrdinalIgnoreCase))?.Label
                                  ?? ModInspector.Inspect(file).Version;
                result[mod.Slug] = new InstalledLatticeMod(mod.Slug, version ?? "0.0.0", sha, bundled);
                break;
            }
        }
    }

    static void WriteInstalled(string gameDir, Dictionary<string, InstalledLatticeMod> all)
    {
        Directory.CreateDirectory(LatticeStore.CacheDir(gameDir));
        var root = new System.Text.Json.Nodes.JsonObject();
        foreach (var kv in all) root[kv.Key] = new System.Text.Json.Nodes.JsonObject { ["version"] = kv.Value.Version, ["sha256"] = kv.Value.Sha256 };
        File.WriteAllText(InstalledPath(gameDir), root.ToJsonString());
    }

    /// <summary>Hashes of installed catalog mods that Lattice has since revoked (for aether doctor).</summary>
    public static List<InstalledLatticeMod> RevokedInstalled(string gameDir, LatticeSnapshot snap) =>
        ReadInstalled(gameDir, snap).Values.Where(m => snap.RevokedHashes.Contains(m.Sha256)).ToList();

    public static bool Install(string gameDir, LatticePlan plan, LatticeSnapshot snap, ILatticeFetcher fetcher, bool dryRun, Action<Check> report)
    {
        if (!plan.CanInstall) return false;
        string work = Path.Combine(Path.GetTempPath(), "aether-lattice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var plans = new List<(LatticeVersion v, AddPlan add)>();
        try
        {
            foreach (var v in plan.Steps)
            {
                if (snap.RevokedHashes.Contains(v.Sha256)) { report(new Check(CheckLevel.Fail, "ERR", $"{v.Slug} {v.Label} is revoked.")); return false; }
                string ext = Path.GetExtension(new Uri(v.DownloadUrl).AbsolutePath).ToLowerInvariant();
                if (ext is not (".zip" or ".dll")) { report(new Check(CheckLevel.Fail, "ERR", $"{v.Slug}: the download is not a .zip or .dll.")); return false; }
                report(new Check(CheckLevel.Info, "GET", $"{v.Slug} {v.Label}: {v.DownloadUrl}"));
                byte[] data;
                try { data = fetcher.Get(v.DownloadUrl, v.Size); }
                catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException)
                { report(new Check(CheckLevel.Fail, "ERR", $"{v.Slug}: download failed ({e.Message}). Nothing was installed.")); return false; }
                if (data.LongLength != v.Size)
                { report(new Check(CheckLevel.Fail, "ERR", $"{v.Slug}: size is {data.LongLength}, the catalog says {v.Size}. Nothing was installed.")); return false; }
                string actual = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                if (actual != v.Sha256)
                { report(new Check(CheckLevel.Fail, "ERR", $"{v.Slug}: SHA-256 mismatch (expected {v.Sha256[..12]}…, got {actual[..12]}…). Nothing was installed.")); return false; }
                report(new Check(CheckLevel.Ok, "OK", $"{v.Slug}: SHA-256 verified."));

                // Keep the file's own name: a .dll is installed under it. Each download gets its own folder so names never collide.
                string dir = Path.Combine(work, plans.Count.ToString());
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, Path.GetFileName(new Uri(v.DownloadUrl).AbsolutePath));
                File.WriteAllBytes(file, data);
                var add = ModAdder.Prepare(gameDir, file, hosted: false);
                foreach (var n in add.Notes) report(n);
                // Everything in it is already part of Aether (Aether Nano): nothing to copy, and no reason to stop the other mods.
                if (add.AlreadyProvided) { report(new Check(CheckLevel.Info, "INFO", $"{v.Slug}: Aether already includes it, skipped.")); add.Dispose(); continue; }
                if (!add.CanInstall) { report(new Check(CheckLevel.Fail, "ERR", $"{v.Slug}: {add.Error ?? "nothing to install"}")); add.Dispose(); return false; }
                plans.Add((v, add));
            }

            var installed = ReadInstalled(gameDir);
            foreach (var (v, add) in plans)
            {
                ModAdder.Apply(gameDir, add, dryRun, report);
                installed[v.Slug] = new InstalledLatticeMod(v.Slug, v.Label, v.Sha256);
            }
            if (!dryRun) WriteInstalled(gameDir, installed);
            return true;
        }
        finally
        {
            foreach (var (_, add) in plans) add.Dispose();
            try { Directory.Delete(work, recursive: true); } catch { }
        }
    }
}
