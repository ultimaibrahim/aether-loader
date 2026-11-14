using System.IO;
using Aether.Nano;
using BepInEx;
using UnityEngine;

namespace Aether.Mods.Wayfinder
{
    /// <summary>Aether Wayfinder as a BepInEx plugin on Aether Nano: no Aether needed.</summary>
    [BepInPlugin(Guid, "Aether Wayfinder", "1.1.1")]
    [BepInDependency(Aether.Nano.Nano.Guid)]
    public sealed class WayfinderPlugin : NanoMod
    {
        public const string Guid = "dev.ultimaibrahim.aether.wayfinder";
        internal static WayfinderPlugin Instance { get; private set; }

        private readonly WayfinderModule _module = new WayfinderModule();

        protected override void OnReady()
        {
            Instance = this;
            // Settings and waypoints used to live in the UI suite's file ([Wayfinder]); carry them over once.
            NanoConfig.AdoptSection(Config, Path.Combine(Paths.ConfigPath, "com.ultimaibrahim.blockstory.suite.cfg"), "Wayfinder");
            WayfinderModule.Config = Config;
            WayfinderModule.Log = Logger;
            _module.Initialize();
            const string wf = "Aether Wayfinder";
            NanoKeys.Register(Guid, wf, WayfinderModule.MinimapToggleKey, "Minimap", "Minimapa");
            NanoKeys.Register(Guid, wf, WayfinderModule.WayfinderKey, "Full map", "Mapa completo");
            NanoKeys.Register(Guid, wf, WayfinderModule.MapLayerModeKey, "Map layer", "Capa del mapa");
            NanoKeys.Register(Guid, wf, WayfinderModule.QuickWaypointKey, "Quick waypoint", "Waypoint rápido");
            Logger.LogInfo($"Aether Wayfinder {Info.Metadata.Version} ready on Aether Nano.");

            NanoPages.Register(new NanoPage
            {
                Id = Guid,
                Name = "Aether Wayfinder",
                Author = "Ibrahim García",
                Version = Info.Metadata.Version.ToString(),
                Description = Aether.Nano.Nano.Tr("Corner map and fullscreen map, cave and slice layers, waypoints, death points, PNG export.",
                                                  "Minimapa y mapa completo, capas de cueva y por altura, waypoints, puntos de muerte y exportar a PNG."),
                Draw = (rect, y) => _module.DrawSettings(rect, y),
                Config = Config,
                ContentHeight = 900f,
            });
            NanoPanels.Register(new NanoPanel
            {
                Id = Guid,
                IsOpen = () => _module.IsOpen,
                IsModal = () => _module.IsOpen,
                MouseOver = () => _module.IsOpen,
                Close = () => _module.SetWayfinderOpen(false),
            });
            NanoHud.Register(new NanoHudElement
            {
                Id = Guid,
                Name = "Wayfinder",
                GetRect = _module.GetMinimapScreenRect,
                OffsetX = WayfinderModule.MinimapOffsetX,
                OffsetY = WayfinderModule.MinimapOffsetY,
                Scale = WayfinderModule.MinimapScale,
                Anchor = WayfinderModule.MinimapAnchor,
            });
        }

        protected override void OnTick()
        {
            WayfinderCaves.Tick(_module);
            _module.UpdateWayfinderStorage();
            _module.CheckDeathpoint();
            _module.HandleKeys();
        }

        protected override void OnSceneChanged(string sceneName)
        {
            if (_module.IsOpen) _module.SetWayfinderOpen(false);
            try { _module.FlushWayfinderStorage(); }
            catch (System.Exception ex) { Logger.LogWarning("Scene flush failed: " + ex.Message); }
        }

        protected override void OnDraw()
        {
            if (_module.IsOpen)
            {
                _module.DrawFullscreenWayfinderGUI();
                NanoInput.DrawGameCursor();
                return;
            }
            if (!NanoHud.CanDrawGameplayHud) return;
            if (_module.HasWaypoints && WayfinderModule.ShowWaypointBeacons != null && WayfinderModule.ShowWaypointBeacons.Value) _module.DrawWaypointBeacons3D();
            if (_module.HasWaypoints && WayfinderModule.ShowWorldWaypoints != null && WayfinderModule.ShowWorldWaypoints.Value) _module.DrawWorldWaypoints3D();
            if (WayfinderModule.MinimapEnabled != null && WayfinderModule.MinimapEnabled.Value) _module.DrawCornerMinimap();
        }

        private void OnApplicationQuit()
        {
            try { _module.FlushWayfinderStorage(); } catch { }
        }
    }
}
