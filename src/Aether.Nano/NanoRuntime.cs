using BepInEx;

namespace Aether.Nano
{
    /// <summary>
    /// Nano's own BepInEx plugin: it refreshes the shared game state once per frame and draws Nano window.
    /// No Awake on purpose: on this Unity build a plugin that touches Unity during BepInEx's early boot kills the game
    /// (docs/KNOWN_TRAPS.md, R1), so everything starts from the first Update.
    /// </summary>
    [BepInPlugin(Nano.Guid, "Aether Nano", Nano.Version)]
    public sealed class NanoRuntime : BaseUnityPlugin
    {
        private void Update() => Nano.Tick();
        private void OnGUI() => NanoWindow.OnGUI();
    }
}
