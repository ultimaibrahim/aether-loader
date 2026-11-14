using System;
using System.Collections.Generic;
using System.Linq;

namespace Aether.Loader.Diagnostics
{
    /// <summary>
    /// Folds per-method Harmony overlaps into one line per (severity, set of owners), so a install with
    /// dozens of mods reads as "A and B overlap on 9 methods" instead of hundreds of lines. Pure logic.
    /// </summary>
    public static class HarmonyOverlapSummary
    {
        public sealed class Group
        {
            public HarmonyConflictSeverity Severity { get; }
            public IReadOnlyList<string> Owners { get; }
            public IReadOnlyList<string> Methods { get; }

            public Group(HarmonyConflictSeverity severity, IReadOnlyList<string> owners, IReadOnlyList<string> methods)
            {
                Severity = severity;
                Owners = owners;
                Methods = methods;
            }

            public string Describe(int maxMethods = 4)
            {
                string kind = Severity switch
                {
                    HarmonyConflictSeverity.Issue => "transpilers rewrite the same IL",
                    HarmonyConflictSeverity.Warning => "prefixes that can skip the original",
                    _ => "patches compose"
                };
                string shown = string.Join(", ", Methods.Take(maxMethods));
                string more = Methods.Count > maxMethods ? $", +{Methods.Count - maxMethods} more" : "";
                return $"Harmony overlap between {string.Join(" and ", Owners)} on {Methods.Count} method(s) ({kind}): {shown}{more}";
            }
        }

        public static IReadOnlyList<Group> Build(IEnumerable<HarmonyOverlap> overlaps) =>
            overlaps
                .GroupBy(o => (o.Severity, Key: string.Join("|", o.Owners.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))))
                .Select(g => new Group(g.Key.Severity,
                    g.First().Owners.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    g.Select(o => o.Method).OrderBy(x => x, StringComparer.Ordinal).ToList()))
                .OrderByDescending(g => g.Severity).ThenByDescending(g => g.Methods.Count)
                .ToList();
    }

    /// <summary>
    /// User-chosen patch removals: <c>owner@Type.Method</c> entries separated by ';' or ','. Applied only on methods
    /// where Aether already found an overlap, so it can settle a conflict but never silently strip a lone patch.
    /// </summary>
    public static class HarmonyDisableRules
    {
        public static IReadOnlyList<(string Owner, string Method)> Parse(string? value)
        {
            var rules = new List<(string, string)>();
            if (string.IsNullOrWhiteSpace(value)) return rules;
            foreach (string piece in value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string raw = piece.Trim();
                int at = raw.IndexOf('@');
                if (at <= 0 || at == raw.Length - 1) continue;
                rules.Add((raw.Substring(0, at).Trim(), raw.Substring(at + 1).Trim()));
            }
            return rules;
        }

        public static IEnumerable<string> OwnersToDisable(IReadOnlyList<(string Owner, string Method)> rules,
            string method, IReadOnlyList<string> overlapOwners) =>
            rules.Where(r => string.Equals(r.Method, method, StringComparison.Ordinal) &&
                             overlapOwners.Contains(r.Owner, StringComparer.OrdinalIgnoreCase))
                 .Select(r => r.Owner).Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
