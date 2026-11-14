using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aether.Core.Config
{
    public interface IConfigEntry
    {
        string Key { get; }
        string Category { get; }
        string Description { get; }
        Type ValueType { get; }
        object? BoxedValue { get; set; }
        object? DefaultValue { get; }
    }

    public sealed class ConfigEntry<T> : IConfigEntry
    {
        private T _value;

        public string Key { get; }
        public string Category { get; }
        public string Description { get; }
        public T DefaultValue { get; }
        public Type ValueType => typeof(T);

        object? IConfigEntry.DefaultValue => DefaultValue;

        public T Value
        {
            get => _value;
            set
            {
                if (!EqualityComparer<T>.Default.Equals(_value, value))
                {
                    _value = value;
                    OnValueChanged?.Invoke(_value);
                }
            }
        }

        public object? BoxedValue
        {
            get => Value;
            set
            {
                if (value == null)
                {
                    Value = default!;
                }
                else if (value is T directVal)
                {
                    Value = directVal;
                }
                else if (typeof(T).IsEnum)
                {
                    if (value is string strVal)
                        Value = (T)Enum.Parse(typeof(T), strVal, true);
                    else
                        Value = (T)Enum.ToObject(typeof(T), value);
                }
                else
                {
                    Value = (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture)!;
                }
            }
        }

        public event Action<T>? OnValueChanged;

        public ConfigEntry(string category, string key, T defaultValue, string description)
        {
            Category = category;
            Key = key;
            DefaultValue = defaultValue;
            _value = defaultValue;
            Description = description;
        }

        public override string ToString() => $"{Category}.{Key} = {Value}";
    }

    /// <summary>
    /// Atelier declarative, type-safe configuration engine.
    /// Provides 1-line configuration binding with automatic JSON serialization and crash recovery.
    /// </summary>
    public static class AetherConfig
    {
        private static string _baseConfigDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BepInEx", "config", "aether");
        private static readonly ConcurrentDictionary<string, Dictionary<string, IConfigEntry>> _modConfigs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, IConfigEntry> _emptyModEntries = new(0);

        // A mod's JSON is parsed once (lazily) and written at most once per frame.
        private static readonly HashSet<string> _loadedMods = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _dirtyMods = new(StringComparer.OrdinalIgnoreCase);

        public static string BaseConfigDirectory
        {
            get => _baseConfigDir;
            set => _baseConfigDir = value;
        }

        public static IReadOnlyCollection<string> GetRegisteredMods()
        {
            return _modConfigs.Keys.ToArray();
        }

        public static IReadOnlyDictionary<string, IConfigEntry> GetModEntries(string modId)
        {
            if (string.IsNullOrWhiteSpace(modId)) return _emptyModEntries;
            if (_modConfigs.TryGetValue(modId, out var entries))
            {
                return entries;
            }
            return _emptyModEntries;
        }

        public static ConfigEntry<T> Bind<T>(string modId, string category, string key, T defaultValue, string description = "")
        {
            if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("Mod ID cannot be empty.", nameof(modId));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be empty.", nameof(key));

            var modEntries = _modConfigs.GetOrAdd(modId, _ => new Dictionary<string, IConfigEntry>(StringComparer.OrdinalIgnoreCase));
            string fullKey = $"{category}.{key}";

            if (modEntries.TryGetValue(fullKey, out var existing))
            {
                return (ConfigEntry<T>)existing;
            }

            var entry = new ConfigEntry<T>(category, key, defaultValue, description);
            modEntries[fullKey] = entry;

            // Parse the mod's JSON once, not once per bound key (this used to be O(N^2) file I/O).
            EnsureModConfigLoaded(modId);

            // Persist lazily: one write per mod per frame instead of one per Bind.
            _dirtyMods.Add(modId);

            return entry;
        }

        /// <summary>Writes any config file whose entries changed since the last flush.</summary>
        public static void FlushPendingSaves()
        {
            if (_dirtyMods.Count == 0) return;

            foreach (var modId in _dirtyMods)
            {
                SaveModConfigFile(modId);
            }
            _dirtyMods.Clear();
        }

        private static void EnsureModConfigLoaded(string modId)
        {
            if (!_loadedMods.Add(modId)) return;
            LoadModConfigFile(modId);
        }

        public static void SaveModConfigFile(string modId)
        {
            if (!_modConfigs.TryGetValue(modId, out var entries)) return;

            try
            {
                Directory.CreateDirectory(_baseConfigDir);
                string filePath = Path.Combine(_baseConfigDir, $"{modId}.json");

                var root = new Dictionary<string, Dictionary<string, object?>>();

                foreach (var entry in entries.Values)
                {
                    if (!root.TryGetValue(entry.Category, out var catDict))
                    {
                        catDict = new Dictionary<string, object?>();
                        root[entry.Category] = catDict;
                    }
                    catDict[entry.Key] = entry.BoxedValue;
                }

                string json = JsonConvert.SerializeObject(root, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch
            {
                // Never crash the engine if file is locked or disk error occurs
            }
        }

        public static void LoadModConfigFile(string modId)
        {
            string filePath = Path.Combine(_baseConfigDir, $"{modId}.json");
            if (!File.Exists(filePath)) return;

            if (!_modConfigs.TryGetValue(modId, out var entries)) return;

            try
            {
                string json = File.ReadAllText(filePath);
                var root = JObject.Parse(json);

                foreach (var catProp in root.Properties())
                {
                    string category = catProp.Name;
                    if (catProp.Value is JObject catObj)
                    {
                        foreach (var keyProp in catObj.Properties())
                        {
                            string key = keyProp.Name;
                            string fullKey = $"{category}.{key}";

                            if (entries.TryGetValue(fullKey, out var entry))
                            {
                                try
                                {
                                    entry.BoxedValue = keyProp.Value.ToObject(entry.ValueType);
                                }
                                catch
                                {
                                    // Fall back to default value without throwing
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Corrupt JSON: preserve current defaults in memory
            }
        }
    }
}
