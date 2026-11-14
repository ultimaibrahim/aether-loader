using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Aether.Loader.Diagnostics
{
    /// <summary>
    /// Machine-readable summary of loader conflicts: duplicate ids, missing/incompatible
    /// dependencies, Breaks, LoadAfter warnings and Harmony patch overlaps. Written next to the
    /// BepInEx config so `aether doctor` can show it after a launch.
    /// </summary>
    public sealed class ModConflictReport
    {
        public List<string> Issues { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Notes { get; } = new();
        public List<string> Resolved { get; } = new();
        public List<string> ScanRoots { get; } = new();

        public string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Aether conflict report");
            sb.AppendLine("Generated: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
            sb.AppendLine("Scan roots: " + string.Join(", ", ScanRoots));
            sb.AppendLine();
            sb.AppendLine($"Resolved mods ({Resolved.Count}):");
            foreach (string mod in Resolved) sb.AppendLine("  " + mod);
            sb.AppendLine();
            sb.AppendLine($"Issues ({Issues.Count}):");
            foreach (string issue in Issues) sb.AppendLine("  [ISSUE] " + issue);
            sb.AppendLine();
            sb.AppendLine($"Warnings ({Warnings.Count}):");
            foreach (string warning in Warnings) sb.AppendLine("  [WARN] " + warning);
            sb.AppendLine();
            sb.AppendLine($"Notes ({Notes.Count}):");
            foreach (string note in Notes) sb.AppendLine("  [NOTE] " + note);
            return sb.ToString();
        }

        public void Write(string path)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, Build());
            }
            catch
            {
                // Reporting must never break mod loading.
            }
        }
    }
}
