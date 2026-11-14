using System.Text.Json;

namespace Aether.Installer.Lattice;

/// <summary>What a mod needs: another catalog mod (by slug) or a GitHub repository outside the catalog (informational).</summary>
public sealed record LatticeRequirement(bool IsMod, string Name, string? Url, string? MinVersion);

public sealed record LatticeVersion(
    string Slug, string Tag, string? Version, string Level, string Sha256, long Size,
    string DownloadUrl, IReadOnlyList<LatticeRequirement> Requires)
{
    /// <summary>The version people compare: the declared one, else the tag.</summary>
    public string Label => string.IsNullOrWhiteSpace(Version) ? Tag : Version!;
}

public sealed record LatticeMod(string Slug, string Name, IReadOnlyList<LatticeVersion> Versions);

/// <summary>The signed catalog, read from a document whose signature was already checked.</summary>
public sealed class LatticeIndex
{
    public IReadOnlyList<LatticeMod> Mods { get; }
    private LatticeIndex(IReadOnlyList<LatticeMod> mods) => Mods = mods;

    public LatticeMod? Find(string slug) => Mods.FirstOrDefault(m => string.Equals(m.Slug, slug, StringComparison.OrdinalIgnoreCase));

    /// <summary>Null when a field the installer relies on is missing or has the wrong type (the catalog is then not trusted).</summary>
    public static LatticeIndex? Parse(JsonDocument doc)
    {
        try
        {
            var mods = new List<LatticeMod>();
            foreach (var m in doc.RootElement.GetProperty("mods").EnumerateArray())
            {
                string slug = m.GetProperty("slug").GetString()!;
                var versions = new List<LatticeVersion>();
                foreach (var v in m.GetProperty("versions").EnumerateArray())
                {
                    var reqs = new List<LatticeRequirement>();
                    if (v.TryGetProperty("requires", out var rq) && rq.ValueKind == JsonValueKind.Array)
                        foreach (var r in rq.EnumerateArray())
                        {
                            string type = r.GetProperty("type").GetString()!;
                            string? min = r.TryGetProperty("min_version", out var mv) && mv.ValueKind == JsonValueKind.String ? mv.GetString() : null;
                            reqs.Add(type == "mod"
                                ? new(true, r.GetProperty("slug").GetString()!, null, min)
                                : new(false, r.GetProperty("name").GetString()!, r.GetProperty("url").GetString(), min));
                        }
                    versions.Add(new LatticeVersion(slug, v.GetProperty("tag").GetString()!,
                        v.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.String ? ver.GetString() : null,
                        v.GetProperty("level").GetString()!, v.GetProperty("sha256").GetString()!.ToLowerInvariant(),
                        v.GetProperty("size").GetInt64(), v.GetProperty("download_url").GetString()!, reqs));
                }
                mods.Add(new LatticeMod(slug, m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : slug, versions));
            }
            return new LatticeIndex(mods);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>Dotted versions compared number by number (1.10 &gt; 1.9); a leading "v" and anything after "-" or "+" is ignored. Same rule as the Lattice worker.</summary>
    public static int Compare(string a, string b)
    {
        static int[] Parts(string s) => s.Trim().TrimStart('v', 'V').Split('-', '+')[0].Split('.')
            .Select(x => int.TryParse(x, out int n) ? n : 0).ToArray();
        int[] x = Parts(a), y = Parts(b);
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            int d = (i < x.Length ? x[i] : 0) - (i < y.Length ? y[i] : 0);
            if (d != 0) return d < 0 ? -1 : 1;
        }
        return 0;
    }
}
