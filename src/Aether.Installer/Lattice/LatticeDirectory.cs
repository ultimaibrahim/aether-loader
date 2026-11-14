using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aether.Installer.Lattice;

/// <summary>What the Monitor shows about a mod that the signed index does not carry: its tagline, icon, category and downloads.</summary>
public sealed record LatticeListing(string Slug, string Name, string? Tagline, string? IconUrl, string Owner, int Downloads, IReadOnlyList<string> Categories, string Environment, IReadOnlyList<string> WorksWith);

/// <summary>
/// The catalog's public listing (lattice /api/mods), read for display only. Installing never trusts it: what gets downloaded,
/// its hash and its requirements come from the signed index (LatticeStore). Icons are cached next to the index and only taken
/// from raw.githubusercontent.com, the address Lattice itself accepts for them.
/// </summary>
public static class LatticeDirectory
{
    public const string ApiBase = "https://lattice.ultimaibrahim.dev/api/mods";
    public const int MaxPages = 20;
    public const long MaxIconBytes = 512 * 1024;

    public static Dictionary<string, LatticeListing> Fetch(ILatticeFetcher fetcher)
    {
        var all = new Dictionary<string, LatticeListing>(StringComparer.OrdinalIgnoreCase);
        for (int page = 1; page <= MaxPages; page++)
        {
            byte[] data = fetcher.Get($"{ApiBase}?sort=name&page={page}", 2_000_000);
            int pages = Parse(data, all);
            if (page >= pages) break;
        }
        return all;
    }

    /// <summary>Adds the mods of one page and returns how many pages there are.</summary>
    public static int Parse(byte[] json, Dictionary<string, LatticeListing> into)
    {
        using var d = JsonDocument.Parse(json);
        var root = d.RootElement;
        if (root.TryGetProperty("mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
            foreach (var m in mods.EnumerateArray())
            {
                string? slug = Str(m, "slug");
                if (string.IsNullOrEmpty(slug)) continue;
                var cats = Strings(m, "categories");
                string? icon = Str(m, "icon");
                if (icon != null && !icon.StartsWith("https://raw.githubusercontent.com/", StringComparison.Ordinal)) icon = null;
                into[slug] = new LatticeListing(slug, Str(m, "name") ?? slug, Str(m, "tagline") ?? Str(m, "description"), icon, Str(m, "owner") ?? "",
                    m.TryGetProperty("downloads", out var dl) && dl.TryGetInt32(out int n) ? n : 0, cats, Str(m, "environment") ?? "both", Strings(m, "works_with"));
            }
        return root.TryGetProperty("pages", out var p) && p.TryGetInt32(out int pages) ? Math.Max(1, pages) : 1;
    }

    /// <summary>The icon's local copy, downloading it once. Null when the mod has none or it could not be fetched (the cube is drawn instead).</summary>
    public static string? IconFile(string gameDir, LatticeListing mod, ILatticeFetcher fetcher)
    {
        if (mod.IconUrl == null) return null;
        string ext = Path.GetExtension(new Uri(mod.IconUrl).AbsolutePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp")) return null;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mod.IconUrl)))[..16].ToLowerInvariant();
        string dir = Path.Combine(LatticeStore.CacheDir(gameDir), "icons");
        string file = Path.Combine(dir, $"{mod.Slug}-{key}{ext}");
        if (File.Exists(file)) return file;
        try
        {
            byte[] data = fetcher.Get(mod.IconUrl, MaxIconBytes);
            if (data.Length == 0 || data.Length > MaxIconBytes) return null;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(file, data);
            return file;
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException or InvalidOperationException) { return null; }
    }

    /// <summary>Which of the four gem colours a mod's cube takes: the same hash the Lattice website uses, so a mod looks the same in both.</summary>
    public static int GemOf(string slug)
    {
        uint n = 0;
        foreach (char ch in slug) n = unchecked(n * 31 + ch);
        return (int)(n % 4) + 1;
    }

    private static List<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : new List<string>();

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
