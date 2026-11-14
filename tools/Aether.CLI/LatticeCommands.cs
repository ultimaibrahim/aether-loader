using Aether.Installer;
using Aether.Installer.Lattice;

namespace Aether.CLI;

public static partial class Program
{
    // ---- aether lattice -----------------------------------------------------------------------

    private static int RunLattice(string? gameDir, string[] args)
    {
        Console.WriteLine();
        string[] words = args.Skip(1).Where(a => !a.StartsWith('-') && !IsOptionValue(args, a)).ToArray();
        string sub = words.FirstOrDefault() ?? "";
        if (sub is not ("install" or "list" or "check" or "update") || (sub == "install" && words.Length < 2))
        {
            WriteStatus("ERR", ConsoleColor.Red, "Usage: aether lattice install <mod> | update [mod] | list | check  [--game-dir <path>] [--dry-run] [--yes] [--lattice-url <https://...>]");
            return 1;
        }
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            WriteStatus("ERR", ConsoleColor.Red, "Block Story directory not found. Pass --game-dir <path>.");
            return 1;
        }

        // A different address only changes where the files come from: the signature must still match the key built into Aether.
        string url = GetOption(args, "--lattice-url") ?? Environment.GetEnvironmentVariable("AETHER_LATTICE_URL") ?? LatticeStore.BaseUrl;
        var fetcher = new HttpLatticeFetcher();
        var now = DateTime.UtcNow;
        var snap = LatticeStore.Load(gameDir, fetcher, LatticeKey.Default, now, out string? error, url.TrimEnd('/'));
        if (snap == null)
        {
            WriteStatus("ERR", ConsoleColor.Red, error ?? "Lattice could not be read.");
            return 1;
        }
        if (snap.Warning != null) WriteStatus("WARN", ConsoleColor.Yellow, snap.Warning);

        switch (sub)
        {
            case "list":
                Console.WriteLine($"Lattice catalog (serial {snap.IndexSerial}): {snap.Index.Mods.Count} mod(s)");
                foreach (var m in snap.Index.Mods)
                {
                    var ok = m.Versions.Where(v => !snap.RevokedHashes.Contains(v.Sha256)).Select(v => v.Label).ToList();
                    Console.WriteLine($"  {m.Slug,-28} {(ok.Count > 0 ? string.Join(", ", ok) : "(all versions revoked)")}");
                }
                return 0;

            case "update":
            {
                var installedNow = LatticeInstaller.ReadInstalled(gameDir, snap);
                var targets = words.Length > 1 ? new List<string> { words[1].ToLowerInvariant() }
                    : LatticePlanner.Outdated(snap, installedNow).Select(o => o.Slug).ToList();
                if (targets.Count == 0) { WriteStatus("PASS", ConsoleColor.Green, "Everything installed from Lattice is up to date."); return 0; }
                int failed = 0;
                foreach (string target in targets)
                {
                    var plan = LatticePlanner.Resolve(snap, LatticeInstaller.ReadInstalled(gameDir, snap), target, now, upgrade: true);
                    foreach (Check n in plan.Notes) WriteStatus(n.Badge, ColorOf(n.Level), n.Message);
                    foreach (string e in plan.Errors) WriteStatus("ERR", ConsoleColor.Red, e);
                    if (plan.Errors.Count > 0) { failed++; continue; }
                    if (!plan.CanInstall) continue;
                    Console.WriteLine("Plan:");
                    foreach (var v in plan.Steps) Console.WriteLine($"  {v.Slug,-28} {v.Label,-12} [{v.Level}]  {v.Size / 1024} KB");
                    if (!Confirm($"Update {target}?")) continue;
                    if (!LatticeInstaller.Install(gameDir, plan, snap, fetcher, DryRun, c => WriteStatus(c.Badge, ColorOf(c.Level), c.Message))) failed++;
                }
                return failed == 0 ? 0 : 1;
            }

            case "check":
            {
                var bad = LatticeInstaller.RevokedInstalled(gameDir, snap);
                if (bad.Count == 0) { WriteStatus("PASS", ConsoleColor.Green, "None of the mods installed from Lattice has been revoked."); return 0; }
                foreach (var b in bad)
                    WriteStatus("FAIL", ConsoleColor.Red, $"{b.Slug} {b.Version} was revoked by Lattice ({snap.RevokedReasons.GetValueOrDefault(b.Sha256)}). Remove it.");
                return 2;
            }

            default:
            {
                var plan = LatticePlanner.Resolve(snap, LatticeInstaller.ReadInstalled(gameDir, snap), words[1], now);
                foreach (Check n in plan.Notes) WriteStatus(n.Badge, ColorOf(n.Level), n.Message);
                foreach (string e in plan.Errors) WriteStatus("ERR", ConsoleColor.Red, e);
                if (!plan.CanInstall)
                {
                    if (plan.Errors.Count == 0) WriteStatus("INFO", ConsoleColor.Cyan, "Nothing to install.");
                    return plan.Errors.Count == 0 ? 0 : 1;
                }
                Console.WriteLine("Plan:");
                foreach (var v in plan.Steps) Console.WriteLine($"  {v.Slug,-28} {v.Label,-12} [{v.Level}]  {v.Size / 1024} KB");
                if (!Confirm("Download and install these?")) { WriteStatus("INFO", ConsoleColor.Cyan, "Cancelled. Nothing was changed."); return 1; }
                bool ok = LatticeInstaller.Install(gameDir, plan, snap, fetcher, DryRun, c => WriteStatus(c.Badge, ColorOf(c.Level), c.Message));
                if (ok) WriteStatus("DONE", ConsoleColor.Magenta, "Launch the game once, then run 'aether doctor' to check for conflicts.");
                return ok ? 0 : 1;
            }
        }
    }
}
