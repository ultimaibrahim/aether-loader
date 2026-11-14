using Aether.Nano;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Aether.Mods.Dusk
{
    /// <summary>What Dusk takes from its host: its own config and logger (set by <see cref="DuskPlugin"/>) and Nano's shared styles.</summary>
    public sealed partial class DuskModule
    {
        public static DuskModule Current { get; private set; }
        public static ConfigFile Config { get; internal set; }
        public static ManualLogSource Log { get; internal set; }

        private static GUIStyle SectionTitleStyle => NanoStyles.SectionTitle;
        private static GUIStyle ToolStyle => NanoStyles.Tool;
        private static GUIStyle DescStyle => NanoStyles.Desc;

        public bool IsInitialized { get; private set; }

        public void Initialize()
        {
            if (IsInitialized) return;
            Current = this;
            InitDusk();
            IsInitialized = true;
        }
    }
}
