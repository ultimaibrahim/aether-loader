namespace Aether.Installer;

/// <summary>
/// Which of Aether's official mods <c>aether install</c> should deploy. Pure (no disk access) so it can be unit-tested.
/// The loader never installs mods unless asked: mods are the player's choice.
/// </summary>
public static class InstallSelection
{
    /// <summary>Official mods, in the order they are listed and installed.</summary>
    public static readonly string[] OfficialMods = ["blueprint", "dusk", "lens", "wayfinder"];

    public static string DllName(string mod) => "Aether.Mods." + char.ToUpperInvariant(mod[0]) + mod[1..] + ".dll";

    /// <summary>
    /// Reads the mod-related flags of <c>aether install</c>:
    /// <c>--mods lens,dusk</c> (or <c>--mods=lens</c>), <c>--with-mods</c> / <c>--with-flagships</c> (all four) and
    /// <c>--core-only</c> (explicitly none, which is already the default).
    /// </summary>
    public static bool TryResolve(IReadOnlyList<string> args, out string[] mods, out string? error)
    {
        var chosen = new HashSet<string>();
        bool coreOnly = false;
        mods = [];
        error = null;

        for (int i = 0; i < args.Count; i++)
        {
            string a = args[i];
            if (a == "--core-only") coreOnly = true;
            else if (a is "--with-mods" or "--with-flagships") chosen.UnionWith(OfficialMods);
            else if (a == "--mods" || a.StartsWith("--mods=", StringComparison.Ordinal))
            {
                string? list = a == "--mods" ? (i + 1 < args.Count ? args[++i] : null) : a["--mods=".Length..];
                if (string.IsNullOrWhiteSpace(list) || list.StartsWith("-", StringComparison.Ordinal))
                {
                    error = $"--mods needs a list, for example: --mods lens,dusk. Available: {string.Join(", ", OfficialMods)}.";
                    return false;
                }
                foreach (string raw in list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string name = raw.ToLowerInvariant();
                    if (name == "all") { chosen.UnionWith(OfficialMods); continue; }
                    if (name.StartsWith("aether_", StringComparison.Ordinal)) name = name["aether_".Length..];   // mod ids: aether_lens
                    if (!OfficialMods.Contains(name))
                    {
                        error = $"Unknown official mod '{raw}'. Available: {string.Join(", ", OfficialMods)}.";
                        return false;
                    }
                    chosen.Add(name);
                }
            }
        }

        if (coreOnly && chosen.Count > 0)
        {
            error = "--core-only cannot be combined with --mods or --with-mods.";
            return false;
        }

        mods = OfficialMods.Where(chosen.Contains).ToArray();
        return true;
    }
}
