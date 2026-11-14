using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aether.Installer.Lattice;
using Aether.Loader.SemVer;

namespace Aether.Installer;

/// <summary>A newer Aether release than the one installed, with the zip for this system when the release has one.</summary>
public sealed record AetherUpdate(string Version, string PageUrl, string? ZipUrl, string? ZipName, string? ZipSha256);

/// <summary>
/// Looks for a newer Aether release on GitHub (public API, no account) and can fetch it safely: the zip is checked against the
/// SHA256SUMS.txt published with the release before anything is run. Nothing here talks to the game; the game itself never uses the network.
/// </summary>
public static class UpdateChecker
{
    public const string ReleasesUrl = "https://api.github.com/repos/ultimaibrahim/aether-loader/releases";
    const long MaxJson = 4 * 1024 * 1024, MaxZip = 400L * 1024 * 1024;

    /// <summary>"win-x64" or "linux-x64" for the running system; null where there is no package.</summary>
    public static string? CurrentRid() =>
        OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsLinux() ? "linux-x64" : null;

    /// <summary>True when this program carries its own .NET runtime (the "full" package); false when it uses the one installed on the machine.</summary>
    public static bool IsSelfContained()
    {
#pragma warning disable IL3000 // empty in a single-file app: that is exactly how a bundled runtime shows
        string location = typeof(object).Assembly.Location;
#pragma warning restore IL3000
        return string.IsNullOrEmpty(location) || location.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>File name of the zip for a version: light (needs .NET installed) by default, "-full" for the self-contained package.</summary>
    public static string ZipName(string version, string rid, bool full) => full ? $"aether-{version}-{rid}-full.zip" : $"aether-{version}-{rid}.zip";

    /// <summary>The version of the Aether loader installed in the game folder, or null when it is not installed.</summary>
    public static string? InstalledVersion(string gameDir)
    {
        string dll = Path.Combine(gameDir, InstallLayout.CoreDir, "Aether.Loader.dll");
        if (!File.Exists(dll)) return null;
        string? v = FileVersionInfo.GetVersionInfo(dll).ProductVersion;
        return string.IsNullOrWhiteSpace(v) ? Aether.Loader.AetherBootstrap.PluginVersion : v;
    }

    /// <summary>Null when there is nothing newer, or when GitHub could not be reached.</summary>
    public static AetherUpdate? Check(ILatticeFetcher fetcher, string installedVersion, string? rid, bool full, string url = ReleasesUrl)
    {
        if (!SemanticVersion.TryParse(Clean(installedVersion), out var have) || have == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(fetcher.Get(url, MaxJson));
            JsonElement? best = null; SemanticVersion? bestV = null;
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
                if (!SemanticVersion.TryParse(Clean(r.GetProperty("tag_name").GetString() ?? ""), out var v) || v == null) continue;
                if (bestV == null || v > bestV) { best = r.Clone(); bestV = v; }
            }
            if (best == null || bestV == null || bestV <= have) return null;

            var r2 = best.Value;
            string version = bestV.ToString();
            string page = r2.GetProperty("html_url").GetString() ?? "https://aether.ultimaibrahim.dev/descargar/";
            string? zipName = rid == null ? null : ZipName(version, rid, full), zipUrl = null, sumsUrl = null;
            foreach (var a in r2.GetProperty("assets").EnumerateArray())
            {
                string n = a.GetProperty("name").GetString() ?? "", u = a.GetProperty("browser_download_url").GetString() ?? "";
                if (n == zipName) zipUrl = u;
                else if (n == "SHA256SUMS.txt") sumsUrl = u;
            }
            string? sha = null;
            if (zipUrl != null && sumsUrl != null)
                sha = ParseSums(Encoding.UTF8.GetString(fetcher.Get(sumsUrl, 64 * 1024)), zipName!);
            // No verifiable zip for this system: still tell the player there is a new version, but only to open the page.
            if (sha == null) zipUrl = null;
            return new AetherUpdate(version, page, zipUrl, zipUrl == null ? null : zipName, sha);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return null; }
    }

    /// <summary>The SHA-256 listed for <paramref name="fileName"/> in a "hash  file" list, lowercase; null when absent or malformed.</summary>
    public static string? ParseSums(string sums, string fileName)
    {
        foreach (string line in sums.Split('\n'))
        {
            var parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*').Trim() == fileName && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>
    /// Downloads the update zip, checks its SHA-256 and unpacks it into <paramref name="workDir"/>. Returns the new installer to start
    /// (it updates the game folder itself), or null with <paramref name="error"/> set. The running program is never replaced.
    /// </summary>
    public static string? Fetch(AetherUpdate update, ILatticeFetcher fetcher, string workDir, out string? error)
    {
        error = null;
        if (update.ZipUrl == null || update.ZipSha256 == null) { error = "This release has no verified package for your system."; return null; }
        try
        {
            byte[] zip = fetcher.Get(update.ZipUrl, MaxZip);
            if (Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant() != update.ZipSha256) { error = "The download does not match its published SHA-256. Nothing was run."; return null; }
            Directory.CreateDirectory(workDir);
            string file = Path.Combine(workDir, update.ZipName!);
            File.WriteAllBytes(file, zip);
            string dir = Path.Combine(workDir, "pkg");
            SafeZip.Extract(file, dir);
            string exe = OperatingSystem.IsWindows() ? "aether-installer.exe" : "aether-installer";
            string? found = Directory.GetFiles(dir, exe, SearchOption.AllDirectories).FirstOrDefault();
            if (found == null) { error = "The package does not contain the installer."; return null; }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(found, File.GetUnixFileMode(found) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            return found;
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or InvalidDataException)
        { error = e.Message; return null; }
    }

    // "v1.0.3-beta" and "1.0.3-beta+<commit>" (the SDK appends the commit to ProductVersion) both become "1.0.3-beta".
    internal static string Clean(string v) { v = v.Trim().TrimStart('v', 'V'); int plus = v.IndexOf('+'); return plus < 0 ? v : v[..plus]; }
}
