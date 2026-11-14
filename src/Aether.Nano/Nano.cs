using System;
using BepInEx.Logging;
using com.mindblocks.i18n;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aether.Nano
{
    /// <summary>
    /// Shared services for mods: language, logging, the camera, the active world and whether the player is underground.
    /// Everything is computed here once per frame, so mods never depend on one another for it (Dusk no longer needs
    /// Wayfinder to know you are in a cave, and no mod needs Aether).
    /// </summary>
    public static class Nano
    {
        public const string Guid = "dev.ultimaibrahim.aether.nano";
        public const string Version = "1.0.1";

        private static ManualLogSource _log;
        public static ManualLogSource Log => _log ??= BepInEx.Logging.Logger.CreateLogSource("Aether Nano");

        public static bool IsSpanish()
        {
            try { return T.currentLanguage == LanguageCode.ES; }
            catch { return false; }
        }

        public static string Tr(string en, string es) => IsSpanish() ? es : en;

        private static Camera _camera;
        /// <summary>The main camera, cached; null in scenes without one.</summary>
        public static Camera CachedCamera
        {
            get
            {
                if (_camera == null) _camera = Camera.main;
                return _camera;
            }
        }

        private static Digger _digger;
        private static float _nextDigger;
        /// <summary>The player's Digger (reach, held tool), looked up at most twice a second until found.</summary>
        public static Digger CachedDigger
        {
            get
            {
                if (_digger == null && InGame && Time.unscaledTime >= _nextDigger)
                {
                    _nextDigger = Time.unscaledTime + 0.5f;
                    try { _digger = UnityEngine.Object.FindAnyObjectByType<Digger>(); } catch { }
                }
                return _digger;
            }
        }

        private static Inventory _inventory;
        private static float _nextInventory;
        /// <summary>The game's Inventory, or null in menus.</summary>
        public static Inventory Inventory
        {
            get
            {
                if (_inventory == null) _inventory = Inventory.Instance;
                if (_inventory == null && Time.unscaledTime >= _nextInventory)
                {
                    _nextInventory = Time.unscaledTime + 0.5f;
                    try { _inventory = UnityEngine.Object.FindAnyObjectByType<Inventory>(); } catch { }
                }
                return _inventory;
            }
        }

        /// <summary>An inventory, chest, crafting or other game window is open.</summary>
        public static bool NativeWindowOpen
        {
            get
            {
                Inventory inv = Inventory;
                if (inv == null) return false;
                try { return inv.isWindowOpen(); } catch { return false; }
            }
        }

        /// <summary>The in-game pause/settings screen is open.</summary>
        public static bool PauseMenuOpen
        {
            get
            {
                if (!InGame) return false;
                Inventory inv = Inventory;
                if (inv == null) return false;
                return (inv.mainSettingsButton != null && inv.mainSettingsButton.activeInHierarchy)
                    || (inv.settingsCamera != null && inv.settingsCamera.gameObject.activeInHierarchy);
            }
        }

        /// <summary>True when there is solid, opaque ground above the camera (refreshed about three times a second).</summary>
        public static bool IsUnderground { get; private set; }

        /// <summary>The active scene's name ("Menu", "StartScene", "PlayingMobile"...).</summary>
        public static string Scene { get; private set; } = string.Empty;

        public static bool InMainMenu => Scene == "Menu" || Scene == "StartScene";
        public static bool InGame => Scene == "PlayingMobile";

        private static bool? _aether;
        /// <summary>Aether's menu (Aether.UI) is running in this game: it lists Nano pages itself.</summary>
        public static bool AetherPresent => _aether ??= NanoHosts.AssemblyLoaded("Aether.UI");

        private static float _nextUnderground;
        private static int _frame = -1;

        /// <summary>Called by <see cref="NanoRuntime"/> every frame; idempotent within a frame.</summary>
        public static void Tick()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            NanoPerf.Tick();
            string scene = SceneManager.GetActiveScene().name;
            if (scene != Scene) { NanoInput.ReleaseAll(); Scene = scene; _camera = null; _digger = null; _inventory = null; IsUnderground = false; }

            if (Time.unscaledTime >= _nextUnderground)
            {
                _nextUnderground = Time.unscaledTime + 0.35f;
                try
                {
                    Camera cam = CachedCamera;
                    var world = InGame && cam != null ? NanoWorld.Active() : null;
                    IsUnderground = world != null && NanoWorld.IsUnderground(world, cam.transform.position);
                }
                catch (Exception) { IsUnderground = false; }
            }
            NanoInput.KeepHeld();
        }
    }
}
