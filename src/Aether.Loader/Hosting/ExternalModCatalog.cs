using System;
using System.Collections.Generic;
using System.Linq;

namespace Aether.Loader.Hosting
{
    /// <summary>One third-party mod Aether did not load through <c>[AetherMod]</c>.</summary>
    public sealed class ExternalMod
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Version = string.Empty;
        public string Author = string.Empty;
        public string Description = string.Empty;
        /// <summary>Where the mod comes from; today always "BepInEx (hosted)".</summary>
        public string Source = string.Empty;
        public string AssemblyName = string.Empty;
        public string Location = string.Empty;
        public Func<bool>? GetEnabled;
        public Action<bool>? SetEnabled;
        public Action? OpenConfig;
    }

    /// <summary>
    /// One place that answers "which third-party mods are running and what do they own?": the BepInEx
    /// plugins hosted from Aether/Mods and the item→mod origin map used by tooltips.
    /// </summary>
    public static class ExternalModCatalog
    {
        private static readonly object Gate = new object();
        private static readonly List<ExternalMod> Hosted = new List<ExternalMod>();
        private static readonly Dictionary<string, string> EntityOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> ItemOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void AddHosted(string guid, string name, string version, string assemblyName, string location = "")
        {
            lock (Gate)
            {
                Hosted.RemoveAll(m => string.Equals(m.Id, guid, StringComparison.OrdinalIgnoreCase));
                Hosted.Add(new ExternalMod
                {
                    Id = guid, Name = name, Version = version, AssemblyName = assemblyName, Location = location,
                    Source = "BepInEx (hosted)"
                });
            }
        }

        private static long _countStamp;
        private static int _countCache;

        /// <summary>Number of third-party mods, cached for one second (UI draws it several times per frame).</summary>
        public static int CachedCount()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp() / (System.Diagnostics.Stopwatch.Frequency / 1000);
            if (now - _countStamp > 1000)
            {
                _countStamp = now;
                try { _countCache = Snapshot().Count; } catch { }
            }
            return _countCache;
        }

        public static void RemoveHosted(string guid)
        {
            lock (Gate) Hosted.RemoveAll(m => string.Equals(m.Id, guid, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The hosted plugins (one row per mod).</summary>
        public static IReadOnlyList<ExternalMod> Snapshot()
        {
            lock (Gate) return Hosted.Select(Clone).ToList();
        }

        // ------------------------------------------------------------------ item / block origin

        /// <summary>Remember that <paramref name="itemName"/> was registered by <paramref name="modName"/>.</summary>
        public static void RecordItemOwner(string itemName, string modName)
        {
            if (string.IsNullOrEmpty(itemName) || string.IsNullOrEmpty(modName)) return;
            lock (Gate) ItemOwners[itemName] = modName;
        }

        /// <summary>Remember that the mob/pet called <paramref name="entityName"/> was defined by <paramref name="modName"/>.</summary>
        public static void RecordEntityOwner(string entityName, string modName)
        {
            if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(modName)) return;
            lock (Gate) EntityOwners[entityName] = modName;
        }

        /// <summary>The third-party mod that defined the mob/pet, or null for vanilla / unknown entities.</summary>
        public static string? ResolveEntityOwner(string entityName)
        {
            if (string.IsNullOrEmpty(entityName)) return null;
            lock (Gate) return EntityOwners.TryGetValue(entityName, out string? mod) ? mod : null;
        }

        /// <summary>The third-party mod that registered the item, or null for vanilla / unknown items.</summary>
        public static string? ResolveItemOwner(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return null;
            lock (Gate) return ItemOwners.TryGetValue(itemName, out string? mod) ? mod : null;
        }

        /// <summary>
        /// Plugin names often start with the game's name ("Block Story - Minimap"). In a tooltip that reads as if the
        /// item were vanilla, so the prefix is dropped: "Minimap".
        /// </summary>
        public static string DisplayName(string pluginName)
        {
            if (string.IsNullOrWhiteSpace(pluginName)) return pluginName;
            string[] prefixes = { "Block Story - ", "Block Story: ", "Block Story – ", "BlockStory - " };
            foreach (string prefix in prefixes)
            {
                if (pluginName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && pluginName.Length > prefix.Length)
                    return pluginName.Substring(prefix.Length).Trim();
            }
            return pluginName;
        }

        private static ExternalMod Clone(ExternalMod m) => new ExternalMod
        {
            Id = m.Id, Name = m.Name, Version = m.Version, Author = m.Author, Description = m.Description,
            Source = m.Source, AssemblyName = m.AssemblyName, Location = m.Location, GetEnabled = m.GetEnabled,
            SetEnabled = m.SetEnabled, OpenConfig = m.OpenConfig
        };
    }
}
