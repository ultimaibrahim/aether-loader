using BepInEx.Configuration;
using UnityEngine;

namespace Aether.UI.Modules
{
    /// <summary>
    /// Lens WAILA surface. The block/creature card and its config live in Aether.Mods.Lens;
    /// the shell only forwards drawing/update and reads the settings for the menu/HUD editor.
    /// </summary>
    public interface ILensUiModule : IAetherUiModule
    {
        ConfigEntry<bool> Enabled { get; }
        ConfigEntry<KeyCode> ToggleKey { get; }
        ConfigEntry<float> BgOpacity { get; }
        ConfigEntry<int> CardWidth { get; }
        ConfigEntry<string> Anchor { get; }
        ConfigEntry<float> OffsetX { get; }
        ConfigEntry<float> OffsetY { get; }
        ConfigEntry<string> Theme { get; }
        ConfigEntry<bool> ShowCoords { get; }
        ConfigEntry<bool> Use3DBlockIcon { get; }
        ConfigEntry<string> MobHealthStyle { get; }
        ConfigEntry<bool> ShowMobLevel { get; }
        ConfigEntry<bool> ShowMobQuests { get; }
        ConfigEntry<bool> HideNativeMobHealth { get; }

        void DrawHud();
    }
}
