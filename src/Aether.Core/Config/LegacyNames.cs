using System;
using System.IO;
using System.Text;

namespace Aether.Core.Config
{
    /// <summary>
    /// One-time migration for the 2026-11-05 mod rename (Jade -> Lens, JourneyMap -> Wayfinder, REI -> Blueprint,
    /// Lighting -> Dusk), so existing settings, waypoints and cave maps are not lost.
    /// </summary>
    public static class LegacyNames
    {
        public const string OldWorldFolder = "aether_journeymap";
        public const string NewWorldFolder = "aether_wayfinder";

        private static readonly (string Old, string New)[] Sections =
        {
            ("Jade", "Lens"), ("JourneyMap", "Wayfinder"), ("REI", "Blueprint"), ("Lighting", "Dusk")
        };

        /// <summary>Renames old <c>[Section]</c> headers in a BepInEx .cfg text, unless the new header already exists.</summary>
        public static string MigrateConfigText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (var (oldName, newName) in Sections)
            {
                string oldHeader = "[" + oldName + "]", newHeader = "[" + newName + "]";
                bool hasNew = false;
                foreach (string l in lines) if (l.Trim() == newHeader) { hasNew = true; break; }
                if (hasNew) continue;
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].Trim() == oldHeader) lines[i] = newHeader;
            }
            return string.Join(nl, lines);
        }

        /// <summary>Migrates a .cfg file in place (no-op when missing or already migrated). Never throws.</summary>
        public static bool MigrateConfigFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                string text = File.ReadAllText(path, Encoding.UTF8);
                string migrated = MigrateConfigText(text);
                if (migrated == text) return false;
                File.WriteAllText(path, migrated, new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Returns <c>&lt;world&gt;/aether_wayfinder</c>, first moving the old <c>aether_journeymap</c> folder there when only
        /// the old one exists. If the move fails the old folder is used so nothing is lost.
        /// </summary>
        public static string ResolveWorldDataFolder(string worldDirectory)
        {
            string neu = Path.Combine(worldDirectory, NewWorldFolder);
            string old = Path.Combine(worldDirectory, OldWorldFolder);
            try
            {
                if (!Directory.Exists(neu) && Directory.Exists(old))
                {
                    Directory.Move(old, neu);
                }
            }
            catch
            {
                if (Directory.Exists(old) && !Directory.Exists(neu)) return old;
            }
            return neu;
        }
    }
}
