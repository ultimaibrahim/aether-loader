using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Aether.Core.Diagnostics
{
    /// <summary>
    /// Represents a detailed, structured crash report for Aether.
    /// Modeled after modern Fabric/Forge crash logs with mod blame detection.
    /// </summary>
    public sealed class CrashReport
    {
        public string Id { get; }
        public DateTime Timestamp { get; }
        public string? BlamedMod { get; set; }
        public string? HookName { get; set; }
        public string ExceptionType { get; }
        public string Message { get; }
        public string StackTrace { get; }
        public List<string> LoadedMods { get; } = new();
        public Dictionary<string, string> SystemInfo { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? SavedFilePath { get; private set; }

        public CrashReport(Exception ex, string? blamedMod = null, string? hookName = null)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));

            Id = Guid.NewGuid().ToString("N")[..8];
            Timestamp = DateTime.Now;
            BlamedMod = blamedMod;
            HookName = hookName;
            ExceptionType = ex.GetType().FullName ?? ex.GetType().Name;
            Message = ex.Message;
            StackTrace = ex.StackTrace ?? Environment.StackTrace;

            PopulateDefaultSystemInfo();
        }

        public CrashReport(string exceptionType, string message, string stackTrace, string? blamedMod = null, string? hookName = null)
        {
            Id = Guid.NewGuid().ToString("N")[..8];
            Timestamp = DateTime.Now;
            BlamedMod = blamedMod;
            HookName = hookName;
            ExceptionType = exceptionType ?? "System.Exception";
            Message = message ?? "Unknown fatal error";
            StackTrace = stackTrace ?? string.Empty;

            PopulateDefaultSystemInfo();
        }

        private void PopulateDefaultSystemInfo()
        {
            SystemInfo["OS"] = RuntimeInformation.OSDescription;
            SystemInfo["Architecture"] = RuntimeInformation.ProcessArchitecture.ToString();
            SystemInfo["Framework"] = RuntimeInformation.FrameworkDescription;
            SystemInfo["Process"] = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            SystemInfo["ProcessId"] = System.Diagnostics.Process.GetCurrentProcess().Id.ToString();
        }

        public string GenerateText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("---- Aether Crash Report ----");
            sb.AppendLine("// Don't panic! Aether Crash Guard caught this before it terminated silently.");
            sb.AppendLine();
            sb.AppendLine($"Report ID: {Id}");
            sb.AppendLine($"Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine();

            sb.AppendLine("-- Suspected Culprit --");
            if (!string.IsNullOrWhiteSpace(BlamedMod))
            {
                sb.AppendLine($"Blamed Mod: {BlamedMod}");
                if (!string.IsNullOrWhiteSpace(HookName))
                {
                    sb.AppendLine($"Intercepted Hook: {HookName}");
                }
            }
            else
            {
                sb.AppendLine("Blamed Mod: Unknown (Native Engine or Unidentified Stack Frame)");
            }
            sb.AppendLine();

            sb.AppendLine("-- Exception Details --");
            sb.AppendLine($"{ExceptionType}: {Message}");
            sb.AppendLine(StackTrace);
            sb.AppendLine();

            sb.AppendLine($"-- Loaded Mods ({LoadedMods.Count}) --");
            if (LoadedMods.Count == 0)
            {
                sb.AppendLine("  (No external mods registered; pure Aether environment)");
            }
            else
            {
                foreach (var mod in LoadedMods)
                {
                    sb.AppendLine($"  - {mod}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("-- System Information --");
            foreach (var kvp in SystemInfo)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }
            sb.AppendLine();
            sb.AppendLine("---- End of Crash Report ----");

            return sb.ToString();
        }

        public string Save(string? targetDirectory = null)
        {
            string dir = targetDirectory ?? Path.Combine(Environment.CurrentDirectory, "crash-reports");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string fileName = $"crash-{Timestamp:yyyy-MM-dd_HH.mm.ss}-{Id}.txt";
            string filePath = Path.Combine(dir, fileName);

            File.WriteAllText(filePath, GenerateText(), Encoding.UTF8);
            SavedFilePath = Path.GetFullPath(filePath);
            return SavedFilePath;
        }
    }
}
