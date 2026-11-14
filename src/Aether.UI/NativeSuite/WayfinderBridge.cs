using BepInEx.Configuration;
using UnityEngine;

namespace BlockStoryLens
{
    /// <summary>
    /// Aether Wayfinder is a Nano mod since 1.1.0: it draws its minimap, beacons and fullscreen map, handles its keys and
    /// owns its [Wayfinder] settings. The shell reads those settings (HUD presets, HUD editor) and the map's open state
    /// through Nano. Everything is null/false without Wayfinder.
    /// </summary>
    public partial class BlockStorySuitePlugin
    {
        public const string WayfinderGuid = "dev.ultimaibrahim.aether.wayfinder";

        private static ConfigEntry<T> WayfinderSetting<T>(string key) => Aether.Nano.NanoPages.Setting<T>(WayfinderGuid, "Wayfinder", key);

        public static ConfigEntry<bool> MinimapEnabled => WayfinderSetting<bool>("Enabled");
        public static ConfigEntry<KeyCode> MinimapToggleKey => WayfinderSetting<KeyCode>("ToggleKey");
        public static ConfigEntry<KeyCode> WayfinderKey => WayfinderSetting<KeyCode>("FullscreenMapKey");
        public static ConfigEntry<float> MinimapRadarRange => WayfinderSetting<float>("RadarRange");
        public static ConfigEntry<bool> MinimapShowCoords => WayfinderSetting<bool>("ShowCoords");
        public static ConfigEntry<string> MinimapShape => WayfinderSetting<string>("Shape");
        public static ConfigEntry<string> MinimapAnchor => WayfinderSetting<string>("Anchor");
        public static ConfigEntry<float> MinimapOffsetX => WayfinderSetting<float>("OffsetX");
        public static ConfigEntry<float> MinimapOffsetY => WayfinderSetting<float>("OffsetY");
        public static ConfigEntry<string> MinimapTheme => WayfinderSetting<string>("Theme");
        public static ConfigEntry<string> MapLayerMode => WayfinderSetting<string>("LayerMode");
        public static ConfigEntry<string> MapRenderMode => WayfinderSetting<string>("RenderMode");
        public static ConfigEntry<bool> ShowChunkGrid => WayfinderSetting<bool>("ShowChunkGrid");
        public static ConfigEntry<string> GridStyle => WayfinderSetting<string>("GridStyle");
        public static ConfigEntry<bool> ShowMapInfo => WayfinderSetting<bool>("ShowMapInfo");
        public static ConfigEntry<bool> Deathpoints => WayfinderSetting<bool>("Deathpoints");
        public static ConfigEntry<int> DeathpointLimit => WayfinderSetting<int>("DeathpointLimit");
        public static ConfigEntry<int> MapSliceY => WayfinderSetting<int>("SliceY");
        public static ConfigEntry<bool> ShowWorldWaypoints => WayfinderSetting<bool>("ShowWorldWaypoints");
        public static ConfigEntry<bool> ShowWaypointBeacons => WayfinderSetting<bool>("ShowWaypointBeacons");
        public static ConfigEntry<float> MinimapSize => WayfinderSetting<float>("MinimapSize");
        public static ConfigEntry<float> MinimapScale => WayfinderSetting<float>("MinimapScale");
        public static ConfigEntry<float> FullscreenMapScale => WayfinderSetting<float>("FullscreenMapScale");
        public static ConfigEntry<float> MinimapRefreshSeconds => WayfinderSetting<float>("MinimapRefreshSeconds");
        public static ConfigEntry<float> FullscreenRefreshSeconds => WayfinderSetting<float>("FullscreenRefreshSeconds");
        public static ConfigEntry<float> EntityVerticalRange => WayfinderSetting<float>("EntityVerticalRange");
        public static ConfigEntry<bool> ShowMobsOnMinimap => WayfinderSetting<bool>("ShowMobsOnMinimap");
        public static ConfigEntry<bool> ShowWaypointLabelsOnMinimap => WayfinderSetting<bool>("ShowWaypointLabelsOnMinimap");
        public static ConfigEntry<bool> SaveExploredMap => WayfinderSetting<bool>("SaveExploredMap");
        public static ConfigEntry<bool> ShowNativeRadarPois => WayfinderSetting<bool>("ShowNativeRadarPois");
        public static ConfigEntry<bool> ShowSurfaceAboveCaves => WayfinderSetting<bool>("ShowSurfaceAboveCaves");
        public static ConfigEntry<bool> AlwaysMapSurface => WayfinderSetting<bool>("AlwaysMapSurface");

        public bool isWayfinderOpen
        {
            get => Aether.Nano.NanoPanels.IsOpen(WayfinderGuid);
            set { if (!value && isWayfinderOpen) Aether.Nano.NanoPanels.Find(WayfinderGuid)?.Close?.Invoke(); }
        }

        /// <summary>Only closing is supported from the shell; the map opens with its own key or its settings page.</summary>
        public void SetWayfinderOpen(bool open) { if (!open) isWayfinderOpen = false; }

        public Rect GetMinimapScreenRect() => Aether.Nano.NanoHud.Find(WayfinderGuid)?.GetRect() ?? default;
    }
}
