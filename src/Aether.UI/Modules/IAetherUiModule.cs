using UnityEngine;

namespace Aether.UI.Modules
{
    /// <summary>
    /// Lifecycle contract for a gameplay module hosted by the Aether UI shell.
    /// A module is a plain class that owns its own state and configuration; it must not be a
    /// MonoBehaviour and must not be added as a component. The shell is the single renderer and
    /// forwards Unity callbacks to the registered modules.
    /// </summary>
    public interface IAetherUiModule
    {
        /// <summary>Stable mod id (for example <c>aether_wayfinder</c>).</summary>
        string Id { get; }

        /// <summary>True once <see cref="Initialize"/> completed successfully.</summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Binds this module's configuration and prepares runtime state. Called once by the shell
        /// after the ConfigFile exists, or immediately on registration if the shell already exists.
        /// </summary>
        void Initialize();

        /// <summary>Per-frame update, forwarded from the shell's Update.</summary>
        void OnUpdate();

        /// <summary>Per-frame late update, forwarded from the shell's LateUpdate.</summary>
        void OnLateUpdate();

        /// <summary>Called when the active Unity scene changes.</summary>
        void OnSceneChanged(string sceneName);

        /// <summary>True while the module owns a modal window and the suite must pause input.</summary>
        bool IsModalOpen { get; }

        /// <summary>Closes any modal owned by this module (scene change, quit, fatal reset).</summary>
        void ForceClose();
    }
}
