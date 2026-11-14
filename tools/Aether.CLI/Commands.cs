using Aether.Installer;
using System.IO.Compression;

namespace Aether.CLI;

public static partial class Program
{
    private static bool DryRun;
    private static bool AssumeYes;
    private static bool SkipBepInEx;
    private static bool NoUi;

    // ---- file helpers that honour --dry-run ---------------------------------------------------

    private static void EnsureDir(string dir)
    {
        if (!DryRun) Directory.CreateDirectory(dir);
    }

    private static void CopyFile(string source, string dest)
    {
        if (DryRun) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, overwrite: true);
    }

    private static void DeleteFile(string path)
    {
        if (DryRun || !File.Exists(path)) return;
        try { File.Delete(path); } catch { }
    }

    private static bool SameContent(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length &&
        BepInExSetup.Sha256Of(a) == BepInExSetup.Sha256Of(b);

    private static void BackupFile(string path, string backupRoot)
    {
        if (DryRun) return;
        Directory.CreateDirectory(backupRoot);
        File.Copy(path, Path.Combine(backupRoot, Path.GetFileName(path)), overwrite: true);
    }

    private static bool Confirm(string question)
    {
        if (AssumeYes) return true;
        if (DryRun) return true;
        if (Console.IsInputRedirected) return false;
        Console.Write($"{question} [y/N] ");
        string? answer = Console.ReadLine();
        return answer != null && answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
    }

    // ---- aether add ---------------------------------------------------------------------------

    private static int RunAdd(string? gameDir, string[] args)
    {
        Console.WriteLine();
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            WriteStatus("ERR", ConsoleColor.Red, "Block Story directory not found. Pass --game-dir <path>.");
            return 1;
        }

        string? input = args.Skip(1).FirstOrDefault(a => !a.StartsWith('-') && !IsOptionValue(args, a));
        if (input == null || !File.Exists(input))
        {
            WriteStatus("ERR", ConsoleColor.Red, "Usage: aether add <mod.dll|mod.zip> [--game-dir <path>] [--dry-run]");
            return 1;
        }

        using AddPlan plan = ModAdder.Prepare(gameDir, input, hosted: !HasFlag(args, "--no-hosted")); // --hosted is the default; kept as a no-op
        foreach (Check note in plan.Notes) WriteStatus(note.Badge, ColorOf(note.Level), note.Message);
        if (plan.AlreadyProvided) { WriteStatus("OK", ConsoleColor.Green, "Aether already includes Aether Nano: nothing to install."); return 0; }
        if (!plan.CanInstall)
        {
            WriteStatus("ERR", ConsoleColor.Red, plan.Error ?? "Nothing to install.");
            return 1;
        }

        Console.WriteLine("Plan:");
        foreach (RoutedFile r in plan.Routed)
            Console.WriteLine($"  {r.Info.FileName,-34} -> {r.DestDirRelative.Replace('\\', '/'),-20} ({r.Reason})");

        if (!Confirm("Install these files?"))
        {
            WriteStatus("INFO", ConsoleColor.Cyan, "Cancelled. Nothing was changed.");
            return 1;
        }

        ModAdder.Apply(gameDir, plan, DryRun, c => WriteStatus(c.Badge, ColorOf(c.Level), c.Message));
        Console.WriteLine();
        WriteStatus("DONE", ConsoleColor.Magenta, "Launch the game once, then run 'aether doctor' to check for conflicts.");
        return 0;
    }

    private static bool IsOptionValue(string[] args, string value)
    {
        int i = Array.IndexOf(args, value);
        return i > 0 && (args[i - 1] is "--game-dir" or "--source-dir" or "--output" or "--lattice-url");
    }


    // ---- aether uninstall ---------------------------------------------------------------------

    private static int RunUninstall(string? gameDir, bool purge)
    {
        Console.WriteLine();
        if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
        {
            WriteStatus("ERR", ConsoleColor.Red, "Block Story directory not found. Pass --game-dir <path>.");
            return 1;
        }

        var existing = Uninstaller.Plan(gameDir, purge);
        if (existing.Count == 0)
        {
            WriteStatus("INFO", ConsoleColor.Cyan, "Nothing to remove: Aether is not installed here.");
            return 0;
        }

        foreach (string f in existing)
            Console.WriteLine($"  {(DryRun ? "would remove" : "remove")}  {Path.GetRelativePath(gameDir, f)}");

        if (!Confirm($"Remove {existing.Count} file(s)? Third-party mods and BepInEx are kept."))
        {
            WriteStatus("INFO", ConsoleColor.Cyan, "Cancelled. Nothing was changed.");
            return 1;
        }

        var result = Uninstaller.Run(gameDir, existing, DryRun);
        if (result.ThirdPartyLeft)
            WriteStatus("INFO", ConsoleColor.Cyan, "Third-party mods in Aether/Mods were left in place.");

        WriteStatus("DONE", ConsoleColor.Magenta, DryRun ? "Dry run complete; nothing was removed." : $"Removed {result.Removed} file(s).");
        return 0;
    }

}
