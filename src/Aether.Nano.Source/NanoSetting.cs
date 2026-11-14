using System;
using System.Reflection;
using BepInEx.Configuration;

namespace AetherNano
{
    /// <summary>One setting. Backed by Aether's JSON config when Aether is loaded, by BepInEx's config file otherwise.</summary>
    internal sealed class NanoSetting<T>
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;

        /// <summary>True when the value lives in Aether's config (and shows up in its mod list).</summary>
        internal bool BackedByAether { get; }

        internal NanoSetting(Func<T> get, Action<T> set, bool backedByAether)
        {
            _get = get;
            _set = set;
            BackedByAether = backedByAether;
        }

        internal T Value
        {
            get => _get();
            set => _set(value);
        }

        internal static NanoSetting<T> Bind(ConfigFile bepInEx, string aetherModId, string section, string key, T defaultValue, string description)
        {
            if (AetherLink.IsPresent)
            {
                try
                {
                    // Late-bound on purpose: this mod must load when Aether.Core is not there at all.
                    Type config = AetherLink.FindType("Aether.Core.Config.AetherConfig");
                    MethodInfo bind = config?.GetMethod("Bind");
                    if (bind != null)
                    {
                        object entry = bind.MakeGenericMethod(typeof(T)).Invoke(null, new object[] { aetherModId, section, key, defaultValue, description });
                        PropertyInfo value = entry.GetType().GetProperty("Value");
                        if (value != null)
                            return new NanoSetting<T>(() => (T)value.GetValue(entry), v => value.SetValue(entry, v), true);
                    }
                }
                catch { /* Aether changed shape or refused: fall back to the plain file below */ }
            }

            ConfigEntry<T> plain = bepInEx.Bind(section, key, defaultValue, description);
            return new NanoSetting<T>(() => plain.Value, v => plain.Value = v, false);
        }
    }
}
