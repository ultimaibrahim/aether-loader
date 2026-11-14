using Aether.UI.Modules;
using UnityEngine;

namespace BlockStoryLens
{
    /// <summary>Bridge to the independent Lighting module; the shell only forwards lifecycle/drawing.</summary>
    public partial class BlockStorySuitePlugin
    {
        private static IDuskUiModule Dusk => AetherUiModuleRegistry.Dusk;

        public void InitDusk() => Dusk?.Initialize();
        public void UpdateDusk() => Dusk?.OnUpdate();
        public void LateUpdateDusk() => Dusk?.OnLateUpdate();

        public void DrawDuskModMenuSettings(Rect rightRect, float baseY = 230f)
            => Dusk?.DrawSettings(rightRect, baseY);

        public bool RunDuskQaDiagnostics() => Dusk != null && Dusk.RunQaDiagnostics();
    }
}
