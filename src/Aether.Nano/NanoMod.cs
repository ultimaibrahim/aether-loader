using System;
using BepInEx;
using UnityEngine.SceneManagement;

namespace Aether.Nano
{
    /// <summary>
    /// Base class for a mod built on Nano. It is a normal BepInEx plugin: declare
    /// <c>[BepInPlugin]</c> and <c>[BepInDependency(Nano.Guid)]</c>, and put your code in <see cref="OnReady"/> and friends,
    /// which run from the first frame (never from Awake, see <see cref="NanoRuntime"/>). An exception disables only this mod.
    /// </summary>
    public abstract class NanoMod : BaseUnityPlugin
    {
        private bool _started;
        private string _scene = string.Empty;

        /// <summary>Runs once, on the first frame. Safe to touch Unity and apply Harmony patches here.</summary>
        protected abstract void OnReady();
        protected virtual void OnTick() { }
        protected virtual void OnLateTick() { }
        protected virtual void OnSceneChanged(string sceneName) { }
        /// <summary>Runs in OnGUI after <see cref="OnReady"/>: draw HUD cards here (check <see cref="NanoHud.CanDrawGameplayHud"/>).</summary>
        protected virtual void OnDraw() { }

        private void Update()
        {
            try
            {
                if (!_started) { _started = true; OnReady(); }
                string scene = SceneManager.GetActiveScene().name;
                if (scene != _scene) { _scene = scene; OnSceneChanged(scene); }
                OnTick();
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void LateUpdate()
        {
            if (!_started) return;
            try { OnLateTick(); }
            catch (Exception ex) { Fail(ex); }
        }

        private void OnGUI()
        {
            if (!_started) return;
            try { OnDraw(); }
            catch (Exception ex) { Fail(ex); }
        }

        private void Fail(Exception ex)
        {
            Logger.LogError("Disabled after an error: " + ex);
            enabled = false;
        }
    }
}
