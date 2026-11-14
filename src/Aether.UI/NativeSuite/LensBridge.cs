using BepInEx.Configuration;
using UnityEngine;

namespace BlockStoryLens
{
    /// <summary>
    /// Aether Lens is a Nano mod since 1.1.0: it draws itself and owns its [Lens] settings. The shell only reads and edits
    /// those settings (HUD layout presets, the HUD editor) through Nano, by key. Every property is null without Lens.
    /// </summary>
    public partial class BlockStorySuitePlugin
    {
        public const string LensGuid = "dev.ultimaibrahim.aether.lens";

        public static ConfigEntry<T> LensSetting<T>(string key) => Aether.Nano.NanoPages.Setting<T>(LensGuid, "Lens", key);

        public static ConfigEntry<bool> LensEnabled => LensSetting<bool>("Enabled");
        public static ConfigEntry<KeyCode> LensToggleKey => LensSetting<KeyCode>("ToggleKey");
        public static ConfigEntry<float> LensBgOpacity => LensSetting<float>("BgOpacity");
        public static ConfigEntry<int> LensCardWidth => LensSetting<int>("CardWidth");
        public static ConfigEntry<string> LensAnchor => LensSetting<string>("Anchor");
        public static ConfigEntry<float> LensOffsetX => LensSetting<float>("OffsetX");
        public static ConfigEntry<float> LensOffsetY => LensSetting<float>("OffsetY");
        public static ConfigEntry<string> LensTheme => LensSetting<string>("Theme");
    }
}
