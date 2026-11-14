using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aether.Nano
{
    /// <summary>
    /// For full-screen mod windows (a map, a manager): take the game's input while open and give it back after, and draw
    /// the game's own cursor. Counted, so Aether's menus and a mod's window can both hold it; the last release restores.
    /// </summary>
    public static class NanoInput
    {
        private static int _holders;
        private static readonly List<UICamera> DisabledCameras = new List<UICamera>();
        private static GameObject _hiddenPauseButtons;
        private static bool _fromPauseMenu;

        public static bool Isolated => _holders > 0;

        /// <summary>Stops the game's UI cameras and input under a modal window; shows the game cursor.</summary>
        public static void AcquireIsolation()
        {
            _holders++;
            if (_holders > 1) { EnforceGameCursor(); return; }
            try
            {
                RestoreCameras();
                Inventory inv = Nano.Inventory;
                _fromPauseMenu = Nano.PauseMenuOpen;
                if (_fromPauseMenu && inv != null && inv.mainSettingsButton != null && inv.mainSettingsButton.activeSelf)
                {
                    _hiddenPauseButtons = inv.mainSettingsButton;
                    _hiddenPauseButtons.SetActive(false);
                }
                UICamera.ClearHighlights();
                UICamera.ClearSelection();
                UIAutoButtonKey.currentDefault = null;
                foreach (UICamera cam in Resources.FindObjectsOfTypeAll<UICamera>())
                {
                    // Skip prefabs and cameras that are already off (they must not be turned on at release).
                    if (cam == null || !cam.enabled || !cam.gameObject.scene.IsValid() || !cam.gameObject.scene.isLoaded) continue;
                    if (!cam.gameObject.activeInHierarchy) continue;
                    cam.enabled = false;
                    DisabledCameras.Add(cam);
                }
                Inventory.allowInput = false;
                Inventory.isPaused = true;
                EnforceGameCursor();
            }
            catch (Exception ex) { Nano.Log.LogWarning("AcquireIsolation: " + ex.Message); }
        }

        /// <summary>Gives the input back once the last holder releases it (no-op when nothing is held).</summary>
        public static void ReleaseIsolation()
        {
            if (_holders == 0) return;
            if (--_holders > 0) return;
            try
            {
                if (_hiddenPauseButtons != null) { _hiddenPauseButtons.SetActive(true); _hiddenPauseButtons = null; }
                RestoreCameras();
                RestoreGameplayInput(_fromPauseMenu);
                _fromPauseMenu = false;
            }
            catch (Exception ex) { Nano.Log.LogWarning("ReleaseIsolation: " + ex.Message); }
        }

        /// <summary>
        /// Called every frame by Nano: while held, the game must not take input back on its own (NGUI re-enables its event
        /// camera, the inventory unpauses), so switch them off again.
        /// </summary>
        internal static void KeepHeld()
        {
            if (_holders == 0) return;
            try
            {
                EnforceGameCursor();
                Inventory.allowInput = false;
                Inventory.isPaused = true;
                UICamera ev = UICamera.eventHandler;
                if (ev != null && ev.enabled)
                {
                    ev.enabled = false;
                    if (!DisabledCameras.Contains(ev)) DisabledCameras.Add(ev);
                }
            }
            catch { }
        }

        /// <summary>Drops every hold (scene change): input and cameras come back now.</summary>
        public static void ReleaseAll()
        {
            if (_holders == 0) return;
            _holders = 1;
            ReleaseIsolation();
        }

        private static void RestoreCameras()
        {
            foreach (UICamera cam in DisabledCameras) if (cam != null) cam.enabled = true;
            DisabledCameras.Clear();
        }

        /// <summary>Input back to the game: the game cursor in menus and open windows, the locked gameplay cursor otherwise.</summary>
        public static void RestoreGameplayInput(bool wasPauseMenu)
        {
            bool windowOpen = wasPauseMenu || Nano.NativeWindowOpen;
            Inventory.allowInput = true;
            Inventory.isPaused = windowOpen;
            if (windowOpen || Nano.InMainMenu) { EnforceGameCursor(); return; }
            if (UICustomCursor.current != null) { UICustomCursor.current.Visible = false; UICustomCursor.current.Locked = true; }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>The game's own cursor, free to move (for menus and modal windows).</summary>
        public static void EnforceGameCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = false;
            if (UICustomCursor.current != null)
            {
                UICustomCursor.current.Locked = false;
                UICustomCursor.current.Visible = true;
                UICustomCursor.current.Position = UICustomCursor.GetScreenPosition();
            }
        }

        /// <summary>Draws the game's cursor over IMGUI windows (they are drawn above it otherwise). Call last in OnGUI.</summary>
        public static void DrawGameCursor()
        {
            if (UICustomCursor.current == null || UICustomCursor.current.Image == null) return;
            Cursor.visible = false;
            int depth = GUI.depth;
            GUI.depth = -5000;
            try
            {
                Texture tex = UICustomCursor.current.Image;
                Vector2 pos = UICustomCursor.GetScreenPosition() + UICustomCursor.current.hotSpot;
                float aspect = Screen.width / Mathf.Max(1f, Screen.height);
                float size = UICustomCursor.current.cursorSize <= 0.01f ? 1f : UICustomCursor.current.cursorSize;
                GUI.DrawTexture(new Rect(pos.x, Screen.height - pos.y, tex.width * aspect * size, tex.height * aspect * size), tex);
            }
            finally { GUI.depth = depth; }
        }
    }

    /// <summary>Live frame rate, measured over quarter-second windows.</summary>
    public static class NanoPerf
    {
        private static float _timer;
        private static int _frames;
        public static float LiveFps { get; private set; } = 60f;
        public static float FrameTimeMs { get; private set; } = 16.6f;

        internal static void Tick()
        {
            _timer += Time.unscaledDeltaTime;
            _frames++;
            if (_timer < 0.25f) return;
            LiveFps = _frames / _timer;
            FrameTimeMs = _timer / _frames * 1000f;
            _frames = 0;
            _timer = 0f;
        }
    }
}
