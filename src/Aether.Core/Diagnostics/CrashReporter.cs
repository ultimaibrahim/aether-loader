using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Aether.Core.Diagnostics
{
    /// <summary>
    /// Central manager for generating, blaming, storing, and presenting crash reports.
    /// Provides platform-agnostic file management to open crash log locations.
    /// </summary>
    public static class CrashReporter
    {
        public static CrashReport? ActiveCrashReport { get; private set; }
        public static bool HasActiveCrash => ActiveCrashReport != null;
        public static bool IsOverlayRequested { get; set; }

        public static event Action<CrashReport>? OnCrashReportCreated;

        /// <summary>
        /// Analyzes a stack trace against a list of known mod IDs or assembly names to infer the offending mod.
        /// Framework ids are never blamed heuristically: "aether" appears in every frame the framework
        /// touches, which used to blame the core for exceptions thrown by the game itself.
        /// </summary>
        public static string? BlameMod(string stackTrace, IEnumerable<string>? knownMods)
        {
            if (string.IsNullOrWhiteSpace(stackTrace) || knownMods == null) return null;

            string? weakMatch = null;

            foreach (var mod in knownMods)
            {
                if (string.IsNullOrWhiteSpace(mod)) continue;
                if (IsFrameworkModId(mod)) continue;

                string token = NormalizeToken(mod);
                if (token.Length < 3) continue;

                if (NormalizeToken(stackTrace).Contains(token))
                {
                    return mod;
                }

                // "aether_lens" never appears verbatim, but "Lens" does: match significant segments
                // (>= 4 chars, not generic words) against the normalised stack trace.
                string[] segments = mod.Split(new[] { '_', '-', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < segments.Length; i++)
                {
                    string segment = NormalizeToken(segments[i]);
                    if (segment.Length < 4 || IsGenericModSegment(segment)) continue;
                    if (NormalizeToken(stackTrace).Contains(segment))
                    {
                        weakMatch ??= mod;
                        break;
                    }
                }
            }

            return weakMatch;
        }

        /// <summary>True for ids owned by the framework itself (never blamed by heuristics).</summary>
        public static bool IsFrameworkModId(string modId)
        {
            string token = NormalizeToken(modId);
            switch (token)
            {
                case "aether":
                case "aetherui":
                case "aethercore":
                case "aetherloader":
                case "aetherapi":
                case "aetherregistry":
                case "aetherrendering":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsGenericModSegment(string segment)
        {
            switch (segment)
            {
                case "aether":
                case "mods":
                case "mod":
                case "core":
                case "api":
                case "ui":
                case "loader":
                case "compat":
                case "modhelpers":
                case "library":
                    return true;
                default:
                    return false;
            }
        }

        private static string NormalizeToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '_' || c == '-' || c == '.' || c == ' ' || c == ':' || c == '/' || c == '\\')
                    continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Generates a crash report, persists it to disk under `crash-reports/`, and flags the crash state.
        /// </summary>
        public static CrashReport ReportFatal(
            Exception ex,
            string? explicitModId = null,
            string? hookName = null,
            IEnumerable<string>? loadedMods = null,
            string? customReportsDirectory = null,
            bool requestOverlay = true)
        {
            string? blamed = explicitModId;
            var modsList = new List<string>();

            if (loadedMods != null)
            {
                modsList.AddRange(loadedMods);
                if (string.IsNullOrWhiteSpace(blamed))
                {
                    blamed = BlameMod(ex.StackTrace ?? string.Empty, modsList);
                }
            }

            var report = new CrashReport(ex, blamed, hookName);
            report.LoadedMods.AddRange(modsList);

            report.Save(customReportsDirectory);
            ActiveCrashReport = report;
            if (requestOverlay) IsOverlayRequested = true;

            OnCrashReportCreated?.Invoke(report);
            return report;
        }

        /// <summary>
        /// Reports a fatal error from string condition and stack trace (e.g. from Unity log callbacks).
        /// </summary>
        public static CrashReport ReportFatal(
            string exceptionType,
            string message,
            string stackTrace,
            string? explicitModId = null,
            string? hookName = null,
            IEnumerable<string>? loadedMods = null,
            string? customReportsDirectory = null,
            bool requestOverlay = true)
        {
            string? blamed = explicitModId;
            var modsList = new List<string>();

            if (loadedMods != null)
            {
                modsList.AddRange(loadedMods);
                if (string.IsNullOrWhiteSpace(blamed))
                {
                    blamed = BlameMod(stackTrace, modsList);
                }
            }

            var report = new CrashReport(exceptionType, message, stackTrace, blamed, hookName);
            report.LoadedMods.AddRange(modsList);

            report.Save(customReportsDirectory);
            ActiveCrashReport = report;
            if (requestOverlay) IsOverlayRequested = true;

            OnCrashReportCreated?.Invoke(report);
            return report;
        }

        /// <summary>
        /// Dismisses the active crash overlay if the user decides to continue running.
        /// </summary>
        public static void DismissActiveCrash()
        {
            IsOverlayRequested = false;
        }

        /// <summary>
        /// Safely terminates the game process.
        /// </summary>
        public static void TerminateGame()
        {
            try
            {
                Process.GetCurrentProcess().Kill();
            }
            catch
            {
                Environment.Exit(1);
            }
        }
    }
}
