using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Aether.UI.Modules
{
    /// <summary>
    /// Registration point for per-mod UI runtimes. Mod entrypoints create their module and register
    /// it here; the UI shell attaches to the registry and forwards lifecycle callbacks. Registration
    /// order is irrelevant: a module registered before the shell is initialized immediately when the
    /// shell attaches, and a module registered later is initialized by the attach callback.
    /// </summary>
    public static class AetherUiModuleRegistry
    {
        private static readonly List<IAetherUiModule> Modules = new List<IAetherUiModule>();
        private static Action<IAetherUiModule> attach;

        /// <summary>All registered modules, in registration order.</summary>
        public static IReadOnlyList<IAetherUiModule> All => Modules;

        /// <summary>Returns the first registered module implementing <typeparamref name="T"/>.</summary>
        public static T Get<T>() where T : class, IAetherUiModule
        {
            for (int i = 0; i < Modules.Count; i++)
            {
                if (Modules[i] is T typed) return typed;
            }
            return null;
        }

        /// <summary>Wayfinder module, or null when its assembly is not installed.</summary>
        public static IWayfinderUiModule Wayfinder => Get<IWayfinderUiModule>();

        /// <summary>Blueprint module, or null when its assembly is not installed.</summary>
        public static IBlueprintUiModule Blueprint => Get<IBlueprintUiModule>();

        /// <summary>Dusk module, or null when its assembly is not installed.</summary>
        public static IDuskUiModule Dusk => Get<IDuskUiModule>();

        public static ILensUiModule Lens => Get<ILensUiModule>();
        public static ISodiumUiModule Sodium => Get<ISodiumUiModule>();
        public static ILithiumUiModule Lithium => Get<ILithiumUiModule>();
        public static IEntityCullingUiModule EntityCulling => Get<IEntityCullingUiModule>();
        public static IFerriteCoreUiModule FerriteCore => Get<IFerriteCoreUiModule>();

        /// <summary>
        /// Registers a module. Duplicate ids are ignored so a reloaded assembly cannot register two
        /// runtimes and reintroduce duplicate renderers/engines.
        /// </summary>
        public static void Register(IAetherUiModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (string.IsNullOrEmpty(module.Id))
                throw new ArgumentException("A UI module must declare a stable id.", nameof(module));

            for (int i = 0; i < Modules.Count; i++)
            {
                if (string.Equals(Modules[i].Id, module.Id, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            Modules.Add(module);
            attach?.Invoke(module);
        }

        /// <summary>
        /// Called once by the UI shell. Initializes every module already registered and installs the
        /// callback used by modules that register after the shell.
        /// </summary>
        public static void Attach(Action<IAetherUiModule> onAttach)
        {
            attach = onAttach;
            if (attach == null) return;

            for (int i = 0; i < Modules.Count; i++)
                attach(Modules[i]);
        }

        /// <summary>Test/shutdown hook: forget every module and the attach callback.</summary>
        public static void Clear()
        {
            Modules.Clear();
            attach = null;
        }
    }

    /// <summary>
    /// Wayfinder's public UI surface. The shell and the remaining shared modules depend on this
    /// contract, never on the Wayfinder assembly, so the map can be disabled or removed without
    /// breaking the suite.
    /// </summary>
    public interface IWayfinderUiModule : IAetherUiModule
    {
        // --- configuration (namespaced [Wayfinder] section; values migrate in place) ---
        ConfigEntry<bool> MinimapEnabled { get; }
        ConfigEntry<KeyCode> MinimapToggleKey { get; }
        ConfigEntry<KeyCode> WayfinderKey { get; }
        ConfigEntry<float> MinimapRadarRange { get; }
        ConfigEntry<bool> MinimapShowCoords { get; }
        ConfigEntry<string> MinimapShape { get; }
        ConfigEntry<string> MinimapAnchor { get; }
        ConfigEntry<float> MinimapOffsetX { get; }
        ConfigEntry<float> MinimapOffsetY { get; }
        ConfigEntry<string> MinimapTheme { get; }
        ConfigEntry<string> MapLayerMode { get; }
        ConfigEntry<string> MapRenderMode { get; }
        ConfigEntry<bool> ShowChunkGrid { get; }
        ConfigEntry<string> GridStyle { get; }
        ConfigEntry<bool> ShowMapInfo { get; }
        ConfigEntry<bool> Deathpoints { get; }
        ConfigEntry<int> DeathpointLimit { get; }
        ConfigEntry<int> MapSliceY { get; }
        ConfigEntry<bool> ShowWorldWaypoints { get; }
        ConfigEntry<bool> ShowWaypointBeacons { get; }
        ConfigEntry<float> MinimapSize { get; }
        ConfigEntry<float> MinimapScale { get; }
        ConfigEntry<float> FullscreenMapScale { get; }
        ConfigEntry<float> MinimapRefreshSeconds { get; }
        ConfigEntry<float> FullscreenRefreshSeconds { get; }
        ConfigEntry<float> EntityVerticalRange { get; }
        ConfigEntry<bool> ShowMobsOnMinimap { get; }
        ConfigEntry<bool> ShowWaypointLabelsOnMinimap { get; }
        ConfigEntry<bool> SaveExploredMap { get; }
        ConfigEntry<bool> ShowNativeRadarPois { get; }
        ConfigEntry<bool> ShowSurfaceAboveCaves { get; }
        ConfigEntry<bool> AlwaysMapSurface { get; }

        // --- state and actions used by the shell, HUD editor, keybinds and diagnostics ---
        bool IsOpen { get; }
        bool HasWaypoints { get; }
        int WaypointCount { get; }
        bool IsBlockPassable(ushort blockId);
        bool IsBlockSolidCeiling(ushort blockId);
        Color32 GetBlockColor(ushort blockId);
        Rect GetMinimapScreenRect();
        int GetStandingY(Blocksters.Terrain.chunk.IWorld world, Vector3 position);
        void SetOpen(bool open);
        void CycleLayerMode();
        void SetLayerY(int y);
        void CreateQuickWaypoint();
        void SaveWaypoints();
        void UpdateStorage();
        void FlushStorage();
        void CheckDeathpoint();
        void MarkCaveVisited(int x, int z, int y);
        bool HasSurfaceStore { get; }
        void SaveMapNow();
        void DrawCornerMinimap();
        void DrawWorldWaypoints3D();
        void DrawWaypointBeacons3D();
        void DrawFullscreen();
        void DrawSettings(Rect rightRect, float settingsBaseY);
    }
}
