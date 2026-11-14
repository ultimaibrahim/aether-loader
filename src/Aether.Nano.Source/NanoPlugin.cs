using System;
using BepInEx;

namespace AetherNano
{
    /// <summary>
    /// Base class for a mod that must work with or without Aether. It is a normal BepInEx plugin, so any BepInEx install
    /// loads it; with Aether present the mod is hosted by Aether and gains its config and mod list.
    ///
    /// Your code goes in <see cref="OnReady"/> / <see cref="OnTick"/>, which run from the first Update, never from Awake:
    /// on this Unity build a plugin that touches Unity from Awake during BepInEx's early boot kills the game
    /// (docs/KNOWN_TRAPS.md, "Third-party BepInEx plugins crash at boot").
    /// </summary>
    internal abstract class NanoPlugin : BaseUnityPlugin
    {
        private bool _started;

        /// <summary>True when Aether is loaded in this game.</summary>
        protected bool AetherPresent => AetherLink.IsPresent;

        /// <summary>Binds a setting in Aether's config when Aether is present, in BepInEx's otherwise.</summary>
        protected NanoSetting<T> Bind<T>(string section, string key, T defaultValue, string description = "") =>
            NanoSetting<T>.Bind(Config, Info.Metadata.GUID, section, key, defaultValue, description);

        /// <summary>Runs once, on the first frame. Safe to touch Unity here.</summary>
        protected abstract void OnReady();

        /// <summary>Runs every frame after <see cref="OnReady"/>.</summary>
        protected virtual void OnTick() { }

        /// <summary>
        /// Adds this mod to the "Modded" mark on the main menu. Skipped when Aether is loaded: it has its own mod menu.
        /// One setting per mod, <c>[Nano] ShowModdedBadge</c>; the mark is hidden only if every Nano mod turns it off.
        /// </summary>
        private void RegisterBadge()
        {
            if (AetherPresent) return;
            NanoSetting<bool> show = Bind("Nano", "ShowModdedBadge", true,
                "Show a small 'Modded' mark on the main menu so it is clear this game runs mods.");
            NanoBadge.Register(Info.Metadata.Name, show.Value);
        }

        // Awake is deliberately absent: nothing may run at boot.

        private void Update()
        {
            try
            {
                if (!_started)
                {
                    _started = true;
                    OnReady();
                    RegisterBadge();
                }
                OnTick();
            }
            catch (Exception ex)
            {
                Logger.LogError("Disabled after an error: " + ex);
                enabled = false;
            }
        }
    }
}
