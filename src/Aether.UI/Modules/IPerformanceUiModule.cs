using UnityEngine;

namespace Aether.UI.Modules
{
    /// <summary>Common surface for a performance mod with a settings panel and a live self-test.</summary>
    public interface IPerformanceUiModule : IAetherUiModule
    {
        void DrawSettings(Rect rightRect, float settingsBaseY);
        bool RunQaDiagnostics();
    }

    /// <summary>Sodium chunk-render engine surface.</summary>
    public interface ISodiumUiModule : IPerformanceUiModule
    {
        bool Enabled { get; }
        void SetEnabled(bool enabled);
        void RestoreAll();
    }

    /// <summary>Lithium physics engine surface.</summary>
    public interface ILithiumUiModule : IPerformanceUiModule
    {
        bool Enabled { get; }
        void SetEnabled(bool enabled);
        float CurrentFixedDelta { get; }
    }

    /// <summary>EntityCulling surface.</summary>
    public interface IEntityCullingUiModule : IPerformanceUiModule
    {
        bool Enabled { get; }
        void SetEnabled(bool enabled);
    }

    /// <summary>FerriteCore memory/overlay surface.</summary>
    public interface IFerriteCoreUiModule : IPerformanceUiModule
    {
        bool Enabled { get; }
        bool OverlayEnabled { get; }
        void SetEnabled(bool enabled);
        void SetOverlay(bool enabled);
        BepInEx.Configuration.ConfigEntry<KeyCode> OverlayKey { get; }
        void DrawOverlay();
    }
}
