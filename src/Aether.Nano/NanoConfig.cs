using System;
using System.IO;
using System.Text;
using BepInEx.Configuration;

namespace Aether.Nano
{
    /// <summary>Config helpers for mods that move to Nano and must keep the player's settings.</summary>
    public static class NanoConfig
    {
        /// <summary>
        /// Copies a section from an older config file into <paramref name="config"/>'s file, once: only while that file has
        /// no such section yet. Keys keep their names, so the next <c>Bind</c> calls read the player's old values.
        /// </summary>
        public static bool AdoptSection(ConfigFile config, string legacyFile, string section)
        {
            try
            {
                if (config == null || !File.Exists(legacyFile)) return false;
                string header = "[" + section + "]";
                string own = File.Exists(config.ConfigFilePath) ? File.ReadAllText(config.ConfigFilePath) : string.Empty;
                if (own.IndexOf(header, StringComparison.OrdinalIgnoreCase) >= 0) return false;

                var copied = new StringBuilder();
                bool inside = false;
                foreach (string line in File.ReadAllLines(legacyFile))
                {
                    string t = line.Trim();
                    if (t.StartsWith("[")) { if (inside) break; inside = t.Equals(header, StringComparison.OrdinalIgnoreCase); }
                    if (inside) copied.AppendLine(line);
                }
                if (copied.Length == 0) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(config.ConfigFilePath));
                File.WriteAllText(config.ConfigFilePath, (own.Length > 0 ? own.TrimEnd() + Environment.NewLine + Environment.NewLine : string.Empty) + copied);
                config.Reload();
                return true;
            }
            catch (Exception ex) { Nano.Log.LogWarning($"Could not carry over [{section}] from {Path.GetFileName(legacyFile)}: {ex.Message}"); return false; }
        }
    }
}
