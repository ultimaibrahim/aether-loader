using Aether.UI.Diagnostics;
using Aether.UI.Modules;
using BepInEx.Configuration;
using UnityEngine;

namespace BlockStoryLens
{
    /// <summary>
    /// Thin bridge to the split performance mods (Sodium, Lithium, EntityCulling, FerriteCore).
    /// The shell keeps the existing call sites; each engine owns its config, scans and settings.
    /// </summary>
    public partial class BlockStorySuitePlugin
    {
        private static ISodiumUiModule Sodium => AetherUiModuleRegistry.Sodium;
        private static ILithiumUiModule Lithium => AetherUiModuleRegistry.Lithium;
        private static IEntityCullingUiModule EntityCulling => AetherUiModuleRegistry.EntityCulling;
        private static IFerriteCoreUiModule FerriteCore => AetherUiModuleRegistry.FerriteCore;

        /// <summary>Legacy name; the F8 key is now owned by FerriteCore.</summary>
        public static ConfigEntry<KeyCode> SodiumOverlayKey => FerriteCore?.OverlayKey;

        public void InitPerformanceSuite()
        {
            Sodium?.Initialize();
            Lithium?.Initialize();
            EntityCulling?.Initialize();
            FerriteCore?.Initialize();
        }

        public void UpdatePerformanceSuite()
        {
            PerfMetrics.Tick();
            Sodium?.OnUpdate();
            Lithium?.OnUpdate();
            EntityCulling?.OnUpdate();
            FerriteCore?.OnUpdate();
        }

        public void DrawPerformanceSuiteGUI() => FerriteCore?.DrawOverlay();

        public void RestoreAllChunkRenderers() => Sodium?.RestoreAll();

        public void DrawSodiumSettings(Rect rightRect, float baseY = 230f)
            => Sodium?.DrawSettings(rightRect, baseY);

        public void DrawLithiumSettings(Rect rightRect, float baseY = 230f)
            => Lithium?.DrawSettings(rightRect, baseY);

        public void DrawEntityCullingSettings(Rect rightRect, float baseY = 230f)
            => EntityCulling?.DrawSettings(rightRect, baseY);

        public void DrawFerriteCoreSettings(Rect rightRect, float baseY = 230f)
            => FerriteCore?.DrawSettings(rightRect, baseY);

        public bool RunSodiumQaDiagnostics() => Sodium != null && Sodium.RunQaDiagnostics();
        public bool RunLithiumQaDiagnostics() => Lithium != null && Lithium.RunQaDiagnostics();
        public bool RunEntityCullingQaDiagnostics() => EntityCulling != null && EntityCulling.RunQaDiagnostics();
        public bool RunFerriteCoreQaDiagnostics() => FerriteCore != null && FerriteCore.RunQaDiagnostics();
    }
}
