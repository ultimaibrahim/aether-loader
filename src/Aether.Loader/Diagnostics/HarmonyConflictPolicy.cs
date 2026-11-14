using System;
using System.Collections.Generic;
using System.Linq;

namespace Aether.Loader.Diagnostics
{
    /// <summary>What the loader does when it finds two Harmony owners on one method.</summary>
    public enum HarmonyConflictMode
    {
        /// <summary>Only report (default). Never changes the patch table.</summary>
        Warn,

        /// <summary>
        /// Report, and additionally unpatch every owner after the first on methods where more than
        /// one owner installs a transpiler (the only overlap that can corrupt IL). Opt-in.
        /// </summary>
        DisableConflictingTranspilers
    }

    public enum HarmonyPatchKind { Prefix, Postfix, Transpiler, Finalizer }

    public enum HarmonyConflictSeverity
    {
        /// <summary>Postfix/finalizer only: patches compose; informational.</summary>
        Info,

        /// <summary>Several prefixes: any of them can skip the original and the others.</summary>
        Warning,

        /// <summary>Several transpilers on one method: IL rewrites can be incompatible.</summary>
        Issue
    }

    /// <summary>One patch on a method, in the order Harmony will run it.</summary>
    public sealed class HarmonyPatchEntry
    {
        public string Owner { get; }
        public HarmonyPatchKind Kind { get; }

        /// <summary>
        /// For a prefix: true when it returns <c>bool</c> and can therefore skip the original (and later prefixes).
        /// A <c>void</c> prefix always lets everything run, so it is not a conflict by itself.
        /// </summary>
        public bool CanSkipOriginal { get; }

        public HarmonyPatchEntry(string owner, HarmonyPatchKind kind, bool canSkipOriginal = true)
        {
            Owner = owner;
            Kind = kind;
            CanSkipOriginal = canSkipOriginal;
        }
    }

    public sealed class HarmonyOverlap
    {
        public string Method { get; }
        public HarmonyConflictSeverity Severity { get; }
        public IReadOnlyList<string> Owners { get; }

        /// <summary>Owners the active mode wants unpatched (empty in <see cref="HarmonyConflictMode.Warn"/>).</summary>
        public IReadOnlyList<string> OwnersToDisable { get; }

        public string Message { get; }

        public HarmonyOverlap(string method, HarmonyConflictSeverity severity, IReadOnlyList<string> owners,
            IReadOnlyList<string> ownersToDisable, string message)
        {
            Method = method;
            Severity = severity;
            Owners = owners;
            OwnersToDisable = ownersToDisable;
            Message = message;
        }
    }

    /// <summary>
    /// Classifies Harmony overlaps by how dangerous they are and decides what, if anything, to
    /// disable. Pure logic (no HarmonyLib types) so it is unit-testable.
    /// </summary>
    public static class HarmonyConflictPolicy
    {
        /// <summary>
        /// Owners whose id starts with one of these are the host's own cooperating patches
        /// (Aether shell and flagship mods); their overlaps are expected and never disabled.
        /// </summary>
        public static readonly string[] DefaultTrustedPrefixes =
        {
            "com.ultimaibrahim.",
            "dev.ultimaibrahim.aether",
        };

        public static HarmonyConflictMode ParseMode(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                Enum.TryParse(value.Trim(), ignoreCase: true, out HarmonyConflictMode mode))
                return mode;
            return HarmonyConflictMode.Warn;
        }

        public static HarmonyOverlap? Analyze(
            string method,
            IReadOnlyList<HarmonyPatchEntry> patches,
            HarmonyConflictMode mode,
            IEnumerable<string>? trustedPrefixes = null)
        {
            string[] trusted = (trustedPrefixes ?? DefaultTrustedPrefixes).ToArray();

            var owners = patches.Select(p => p.Owner)
                .Where(o => !string.IsNullOrEmpty(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (owners.Count <= 1) return null;

            var transpilerOwners = DistinctOwners(patches, HarmonyPatchKind.Transpiler);
            var prefixOwners = patches
                .Where(p => p.Kind == HarmonyPatchKind.Prefix && p.CanSkipOriginal && !string.IsNullOrEmpty(p.Owner))
                .Select(p => p.Owner)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            HarmonyConflictSeverity severity;
            string why;
            if (transpilerOwners.Count > 1)
            {
                severity = HarmonyConflictSeverity.Issue;
                why = "multiple transpilers rewrite the same IL";
            }
            else if (prefixOwners.Count > 1)
            {
                severity = HarmonyConflictSeverity.Warning;
                why = "multiple prefixes that can skip the original; any can skip the original and the rest";
            }
            else
            {
                severity = HarmonyConflictSeverity.Info;
                why = "patches compose (postfix/finalizer, void prefix, or a single skipping prefix)";
            }

            var toDisable = new List<string>();
            if (mode == HarmonyConflictMode.DisableConflictingTranspilers &&
                severity == HarmonyConflictSeverity.Issue)
            {
                // Keep the first transpiler owner (applied first); drop the later ones unless
                // they are trusted host patches.
                foreach (string owner in transpilerOwners.Skip(1))
                {
                    if (!IsTrusted(owner, trusted)) toDisable.Add(owner);
                }
            }

            string msg = $"Harmony overlap on {method}: {string.Join(", ", owners)} - {why}";
            if (toDisable.Count > 0) msg += $" [policy: unpatching {string.Join(", ", toDisable)}]";
            return new HarmonyOverlap(method, severity, owners, toDisable, msg);
        }

        private static List<string> DistinctOwners(IReadOnlyList<HarmonyPatchEntry> patches, HarmonyPatchKind kind) =>
            patches.Where(p => p.Kind == kind && !string.IsNullOrEmpty(p.Owner))
                .Select(p => p.Owner)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static bool IsTrusted(string owner, string[] trusted) =>
            trusted.Any(t => owner.StartsWith(t, StringComparison.OrdinalIgnoreCase));
    }
}
