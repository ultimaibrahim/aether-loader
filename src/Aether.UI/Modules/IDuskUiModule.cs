using UnityEngine;

namespace Aether.UI.Modules
{
    /// <summary>Lighting/atmosphere engine surface. Runtime lives in Aether.Mods.Dusk.</summary>
    public interface IDuskUiModule : IAetherUiModule
    {
        void DrawSettings(Rect rightRect, float settingsBaseY);
        bool RunQaDiagnostics();
    }
}
