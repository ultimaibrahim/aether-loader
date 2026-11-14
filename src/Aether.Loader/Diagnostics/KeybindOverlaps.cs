using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Aether.Loader.Diagnostics
{
    /// <summary>One key setting found in a BepInEx config file.</summary>
    public sealed class KeybindBinding
    {
        public string Owner { get; }
        public string Section { get; }
        public string Setting { get; }
        public string Key { get; }

        public KeybindBinding(string owner, string section, string setting, string key)
        {
            Owner = owner;
            Section = section;
            Setting = setting;
            Key = key;
        }
    }

    /// <summary>
    /// Finds hotkeys that two different mods bind to the same key. It reads the BepInEx config files, which say what type each
    /// setting has ("# Setting type: KeyCode" / "KeyboardShortcut"), so it needs no cooperation from the mods and also covers
    /// plugins that are not hosted by Aether. A shared key is a warning, not an error: the two actions may only apply in
    /// different situations (a menu versus gameplay), which a config file cannot tell.
    /// </summary>
    public static class KeybindOverlaps
    {
        private const string TypeMarker = "# Setting type:";

        /// <summary>Reads the key settings of one config file (BepInEx format). Unbound keys ("None") are ignored.</summary>
        public static IReadOnlyList<KeybindBinding> ParseConfig(string owner, IEnumerable<string> lines)
        {
            var result = new List<KeybindBinding>();
            string section = string.Empty;
            bool keyTyped = false;

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith(TypeMarker, StringComparison.Ordinal))
                {
                    string type = line.Substring(TypeMarker.Length).Trim();
                    keyTyped = type.EndsWith("KeyCode", StringComparison.Ordinal) || type.EndsWith("KeyboardShortcut", StringComparison.Ordinal);
                    continue;
                }
                if (line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']') { section = line.Substring(1, line.Length - 2); keyTyped = false; continue; }

                int eq = line.IndexOf('=');
                if (eq > 0)
                {
                    string setting = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    // Some plugins write their file without the "# Setting type" comments; then a setting called ...Key whose
                    // value is plainly a key name counts too.
                    bool looksLikeKey = !keyTyped && setting.EndsWith("Key", StringComparison.OrdinalIgnoreCase) && IsPlainKeyName(value);
                    if ((keyTyped || looksLikeKey) && value.Length > 0 && !value.Equals("None", StringComparison.OrdinalIgnoreCase))
                        result.Add(new KeybindBinding(owner, section, setting, value));
                }
                keyTyped = false;
            }
            return result;
        }

        private static readonly HashSet<string> NamedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Tab", "Backspace", "Return", "Space", "Escape", "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
            "UpArrow", "DownArrow", "LeftArrow", "RightArrow", "BackQuote", "Minus", "Equals", "Comma", "Period", "Slash",
            "LeftBracket", "RightBracket", "Backslash", "Semicolon", "Quote"
        };

        private static bool IsPlainKeyName(string value)
        {
            if (value.Length == 1) return char.IsLetter(value[0]);
            if (NamedKeys.Contains(value)) return true;
            if (value.Length >= 2 && value.Length <= 3 && (value[0] == 'F' || value[0] == 'f') && int.TryParse(value.Substring(1), out int f)) return f >= 1 && f <= 15;
            if (value.StartsWith("Alpha", StringComparison.OrdinalIgnoreCase) && value.Length == 6 && char.IsDigit(value[5])) return true;
            if (value.StartsWith("Keypad", StringComparison.OrdinalIgnoreCase) && value.Length == 7 && char.IsDigit(value[6])) return true;
            return value.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase) && value.Length == 6 && char.IsDigit(value[5]);
        }

        /// <summary>The UI suite's old file. Since 1.1 each official mod copies its section into its own file once and reads only that.</summary>
        public const string LegacySuiteConfig = "com.ultimaibrahim.blockstory.suite";

        /// <summary>Reads every *.cfg in a config folder. <paramref name="ownerName"/> maps a file name (without extension) to a display name.</summary>
        public static IReadOnlyList<KeybindBinding> ScanDirectory(string configDirectory, Func<string, string>? ownerName = null)
        {
            var all = new List<(string Id, KeybindBinding Binding)>();
            if (string.IsNullOrEmpty(configDirectory) || !Directory.Exists(configDirectory)) return new List<KeybindBinding>();

            foreach (string file in Directory.GetFiles(configDirectory, "*.cfg").OrderBy(f => f, StringComparer.Ordinal))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                try { all.AddRange(ParseConfig(ownerName?.Invoke(id) ?? id, File.ReadAllLines(file)).Select(b => (id, b))); }
                catch (IOException) { /* being written by the game: skip this file this time */ }
            }
            return WithoutAdoptedLegacy(all);
        }

        /// <summary>
        /// Drops the old suite file's copy of a section that a mod already adopted into its own file (same section and setting
        /// elsewhere): nothing reads that copy any more, so it would only report the mod clashing with itself.
        /// </summary>
        public static IReadOnlyList<KeybindBinding> WithoutAdoptedLegacy(IEnumerable<(string FileId, KeybindBinding Binding)> bindings)
        {
            var list = bindings.ToList();
            var adopted = new HashSet<string>(list.Where(x => !IsLegacy(x.FileId)).Select(x => x.Binding.Section + "\n" + x.Binding.Setting),
                                              StringComparer.OrdinalIgnoreCase);
            return list.Where(x => !(IsLegacy(x.FileId) && adopted.Contains(x.Binding.Section + "\n" + x.Binding.Setting)))
                       .Select(x => x.Binding).ToList();
        }

        private static bool IsLegacy(string fileId) => string.Equals(fileId, LegacySuiteConfig, StringComparison.OrdinalIgnoreCase);

        /// <summary>One line per key that two or more different owners bind.</summary>
        public static IReadOnlyList<string> Describe(IEnumerable<KeybindBinding> bindings)
        {
            var lines = new List<string>();
            foreach (var group in bindings.GroupBy(b => b.Key, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (group.Select(b => b.Owner).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2) continue;
                string users = string.Join("; ", group.Select(b => $"{b.Owner} ({b.Section}.{b.Setting})"));
                lines.Add($"Key {group.Key} is bound by more than one mod: {users}. Both may react when it is pressed; " +
                          "rebind one of them in its config file if they clash.");
            }
            return lines;
        }
    }
}
