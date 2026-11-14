using System;
using System.Collections.Generic;
using System.Linq;

namespace AetherNano
{
    /// <summary>Pure text logic for the "this game is modded" mark, kept free of Unity types so it can be tested.</summary>
    internal static class NanoBadgeText
    {
        private const char Separator = '|';

        internal static string Encode(string modName, bool show) => (show ? "1" : "0") + Separator + modName;

        internal static bool TryDecode(string payload, out string modName, out bool show)
        {
            modName = string.Empty;
            show = true;
            if (string.IsNullOrEmpty(payload) || payload.Length < 3 || payload[1] != Separator) return false;
            show = payload[0] != '0';
            modName = payload.Substring(2).Trim();
            return modName.Length > 0;
        }

        /// <summary>"Modded · 3 mods: A, B, C" (long lists are cut so the mark stays small).</summary>
        internal static string Build(IReadOnlyCollection<string> names, int maxNames = 4)
        {
            if (names.Count == 0) return string.Empty;
            string noun = names.Count == 1 ? "mod" : "mods";
            string list = string.Join(", ", names.Take(maxNames));
            if (names.Count > maxNames) list += $" +{names.Count - maxNames}";
            return $"Modded · {names.Count} {noun}: {list}";
        }

        /// <summary>"Aether: installed" / "Aether: not installed" - its own line, above the versions.</summary>
        internal static string BuildAetherLine(bool aetherPresent) => aetherPresent ? "Aether: installed" : "Aether: not installed";

        /// <summary>"BepInEx 5.4.23.6 · Block Story 1.2 · Unity 6000.5.6f1". Empty parts are left out. With rich=true the
        /// names are tinted (Unity rich text) while the values stay in the line's own colour.</summary>
        internal static string BuildDetails(string bepInEx, string game, string unity, bool rich = false)
        {
            var parts = new List<string>();
            AddPart(parts, "BepInEx", bepInEx, "#7fd6ff", rich);
            AddPart(parts, "Block Story", game, "#ffd27f", rich);
            AddPart(parts, "Unity", unity, "#b6f08a", rich);
            return string.Join(" \u00b7 ", parts);
        }

        private static void AddPart(List<string> parts, string name, string value, string color, bool rich)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            parts.Add(rich ? $"<color={color}>{name}</color> {value.Trim()}" : $"{name} {value.Trim()}");
        }

        /// <summary>The main menu only: the boot/menu scenes, never a world.</summary>
        internal static bool IsMainMenuScene(string sceneName) =>
            string.Equals(sceneName, "Menu", StringComparison.Ordinal) || string.Equals(sceneName, "StartScene", StringComparison.Ordinal);
    }
}
