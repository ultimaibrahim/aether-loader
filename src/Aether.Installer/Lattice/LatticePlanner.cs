namespace Aether.Installer.Lattice;

/// <summary>A mod from the catalog the installer has put in the game (kept in Aether/lattice/installed.json).</summary>
/// <param name="Bundled">True for a file Aether itself ships (Aether Nano in BepInEx/plugins/Aether): it counts as installed and is updated with Aether, never on its own.</param>
public sealed record InstalledLatticeMod(string Slug, string Version, string Sha256, bool Bundled = false);

public sealed class LatticePlan
{
    /// <summary>Versions to install, dependencies first.</summary>
    public List<LatticeVersion> Steps { get; } = new();
    public List<Check> Notes { get; } = new();
    public List<string> Errors { get; } = new();
    public bool CanInstall => Errors.Count == 0 && Steps.Count > 0;
}

/// <summary>An installed catalog mod that has a newer allowed version (or whose installed file was revoked).</summary>
public sealed record LatticeOutdated(string Slug, string Installed, string Latest, bool Revoked);

/// <summary>Decides what to install for a slug: newest allowed version, required mods first, every hash checked against the revocation list. Pure: no disk, no network.</summary>
public static class LatticePlanner
{
    const int MaxSteps = 32;

    /// <summary>Installed catalog mods with something newer to offer. Revoked installed files are listed first.</summary>
    public static List<LatticeOutdated> Outdated(LatticeSnapshot snap, IReadOnlyDictionary<string, InstalledLatticeMod> installed)
    {
        var list = new List<LatticeOutdated>();
        foreach (var have in installed.Values)
        {
            if (have.Bundled) continue;
            var mod = snap.Index.Find(have.Slug);
            if (mod == null) continue;
            var best = Newest(snap, mod);
            bool revoked = snap.RevokedHashes.Contains(have.Sha256);
            if (best != null && (revoked || LatticeIndex.Compare(best.Label, have.Version) > 0))
                list.Add(new LatticeOutdated(have.Slug, have.Version, best.Label, revoked));
        }
        return list.OrderByDescending(o => o.Revoked).ThenBy(o => o.Slug).ToList();
    }

    static LatticeVersion? Newest(LatticeSnapshot snap, LatticeMod mod) =>
        mod.Versions.Where(v => !snap.RevokedHashes.Contains(v.Sha256))
            .OrderByDescending(v => v.Label, Comparer<string>.Create(LatticeIndex.Compare)).FirstOrDefault();

    /// <summary>With <paramref name="upgrade"/> the mod is updated when a newer allowed version exists, instead of being skipped as "already installed".</summary>
    public static LatticePlan Resolve(LatticeSnapshot snap, IReadOnlyDictionary<string, InstalledLatticeMod> installed, string slug, DateTime nowUtc, bool upgrade = false)
    {
        var plan = new LatticePlan();
        if (snap.Warning != null) plan.Notes.Add(new Check(CheckLevel.Warn, "WARN", snap.Warning));
        slug = slug.Trim().ToLowerInvariant();

        if (!snap.RevocationsFresh(nowUtc))
        {
            plan.Errors.Add($"Cannot install '{slug}': the revocation list was last checked on {snap.RevocationsCheckedUtc:u} (more than 24 h ago) and Lattice cannot be reached to refresh it. Connect and try again.");
            return plan;
        }
        Visit(snap, installed, slug, null, plan, new HashSet<string>(), new HashSet<string>(), upgrade);
        return plan;
    }

    static void Visit(LatticeSnapshot snap, IReadOnlyDictionary<string, InstalledLatticeMod> installed, string slug, string? minVersion,
        LatticePlan plan, HashSet<string> path, HashSet<string> done, bool upgradeThis = false)
    {
        if (done.Contains(slug)) return;
        if (!path.Add(slug)) { plan.Errors.Add($"'{slug}' depends on itself through a cycle."); return; }

        bool newerExists = false;
        if (upgradeThis && installed.TryGetValue(slug, out var cur) && snap.Index.Find(slug) is { } curMod && Newest(snap, curMod) is { } top)
            newerExists = LatticeIndex.Compare(top.Label, cur.Version) > 0;

        if (!newerExists && installed.TryGetValue(slug, out var have) && !snap.RevokedHashes.Contains(have.Sha256)
            && (minVersion == null || LatticeIndex.Compare(have.Version, minVersion) >= 0))
        {
            plan.Notes.Add(new Check(CheckLevel.Info, "INFO", $"{slug} {have.Version} is already installed."));
            done.Add(slug); path.Remove(slug);
            return;
        }

        var mod = snap.Index.Find(slug);
        if (mod == null) { plan.Errors.Add($"'{slug}' is not in the Lattice catalog."); path.Remove(slug); return; }

        var candidates = mod.Versions.Where(v => !snap.RevokedHashes.Contains(v.Sha256)).ToList();
        if (candidates.Count == 0)
        {
            string why = mod.Versions.Select(v => snap.RevokedReasons.GetValueOrDefault(v.Sha256)).FirstOrDefault(r => !string.IsNullOrEmpty(r)) ?? "withdrawn";
            plan.Errors.Add($"Every version of '{slug}' is revoked ({why}). Nothing was installed.");
            path.Remove(slug); return;
        }
        var best = candidates.OrderByDescending(v => v.Label, Comparer<string>.Create(LatticeIndex.Compare)).First();
        if (minVersion != null && LatticeIndex.Compare(best.Label, minVersion) < 0)
        {
            plan.Errors.Add($"A mod needs '{slug}' {minVersion} or newer, but the newest allowed version is {best.Label}.");
            path.Remove(slug); return;
        }

        foreach (var r in best.Requires)
        {
            if (r.IsMod) Visit(snap, installed, r.Name.ToLowerInvariant(), r.MinVersion, plan, path, done);
            // Aether itself is always there: only Aether resolves these plans.
            else if (!r.Name.Equals("Aether", StringComparison.OrdinalIgnoreCase)) plan.Notes.Add(new Check(CheckLevel.Warn, "NEEDS",
                $"{slug} also needs {r.Name}{(r.MinVersion != null ? " " + r.MinVersion + "+" : "")} from {r.Url}. It is not in Lattice; install it yourself."));
        }

        if (plan.Steps.Count >= MaxSteps) { plan.Errors.Add("Too many dependencies to install at once."); path.Remove(slug); return; }
        plan.Steps.Add(best);
        done.Add(slug); path.Remove(slug);
    }
}
