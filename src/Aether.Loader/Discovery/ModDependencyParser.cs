using System;
using System.Collections.Generic;

namespace Aether.Loader.Discovery
{
    /// <summary>
    /// Parses the compact dependency strings used by <see cref="AetherModAttribute"/>:
    ///
    ///   [assembly: AetherMod("example", "1.0.0",
    ///              Depends = "aether_ui@1.0.0, aether@>=1.0.0")]
    ///
    /// Entries are separated by comma or semicolon. <c>id@range</c> uses SemVer range syntax;
    /// a missing range means "*". Whitespace is ignored.
    /// </summary>
    public static class ModDependencyParser
    {
        /// <summary>Parses a comma/semicolon list of plain mod ids (LoadAfter, Breaks).</summary>
        public static void ParseIdListInto(string? raw, ICollection<string> destination)
        {
            if (string.IsNullOrWhiteSpace(raw) || destination == null) return;

            string[] parts = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string id = part.Trim();
                if (id.Length > 0 && !destination.Contains(id)) destination.Add(id);
            }
        }

        public static void ParseInto(string? raw, IDictionary<string, string> destination)
        {
            if (string.IsNullOrWhiteSpace(raw) || destination == null) return;

            string[] parts = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string item = part.Trim();
                if (item.Length == 0) continue;

                int at = item.IndexOf('@');
                string id = (at >= 0 ? item.Substring(0, at) : item).Trim();
                string range = at >= 0 ? item.Substring(at + 1).Trim() : "*";
                if (id.Length == 0) continue;
                destination[id] = string.IsNullOrEmpty(range) ? "*" : range;
            }
        }
    }
}
