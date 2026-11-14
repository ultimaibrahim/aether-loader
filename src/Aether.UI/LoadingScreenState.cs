using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aether.UI
{
    /// <summary>
    /// Detects Block Story's native loading/fade overlay (Fade + progress bar) and the cold-boot
    /// window before <see cref="MenuActions"/> exists. The suite must not draw its menu buttons,
    /// HUD or watermark over either screen.
    /// </summary>
    public static class LoadingScreenState
    {
        private static float lastMenuProbeTime = -10f;
        private static bool menuActionsReady;

        public static bool IsLoading()
        {
            try
            {
                Fade fade = Fade.instance;
                if (fade == null) return false;

                // Every Block Story load (single-player, multiplayer, return to menu) uses
                // showProgressBar: true. The progress bar is the reliable signal; a logo/hint
                // tween may be active for a menu transition that is not a loading screen.
                if (fade.progressBarSlider != null)
                    return fade.progressBarSlider.gameObject.activeInHierarchy;

                return fade.loadingLogo != null && fade.loadingLogo.gameObject.activeInHierarchy;
            }
            catch
            {
                // Never let a loading-screen probe break the GUI.
                return false;
            }
        }

        /// <summary>
        /// True while the native fade/progress bar is running, or while a menu scene exists but
        /// its <see cref="MenuActions"/> component has not been created yet (cold boot / black screen).
        /// </summary>
        public static bool IsLoadingOrMenuNotReady()
        {
            if (IsLoading()) return true;

            string scene = SceneManager.GetActiveScene().name;
            if (scene != "Menu" && scene != "StartScene") return false;

            if (Time.unscaledTime - lastMenuProbeTime < 0.5f)
                return !menuActionsReady;

            lastMenuProbeTime = Time.unscaledTime;
            try
            {
                menuActionsReady = UnityEngine.Object.FindAnyObjectByType<MenuActions>() != null;
            }
            catch
            {
                menuActionsReady = false;
            }

            return !menuActionsReady;
        }
    }
}
