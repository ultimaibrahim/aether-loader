using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Aether.Nano
{
    /// <summary>A HUD card a host or HUD editor (like Aether Canvas) can inspect, preview and move.</summary>
    public sealed class NanoHudElement
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public Func<Rect> GetRect;
        public ConfigEntry<float> OffsetX;
        public ConfigEntry<float> OffsetY;
        /// <summary>Optional: scale of the card, changed with the mouse wheel in the HUD editor.</summary>
        public ConfigEntry<float> Scale;
        /// <summary>Optional: pixel size of the card (e.g. a square minimap), changed with the mouse wheel when set.</summary>
        public ConfigEntry<float> Size;
        public float SizeMin = 100f;
        public float SizeMax = 360f;
        /// <summary>Optional: where the card is anchored (TopCenter, TopLeft...), editable in the HUD editor.</summary>
        public ConfigEntry<string> Anchor;
        /// <summary>Optional: draws the real mod HUD live inside the WYSIWYG HUD editor while the game is paused.</summary>
        public Action<Rect> DrawPreview;
        /// <summary>Optional: restores this element's position/scale/anchor to its defaults.</summary>
        public Action OnReset;
    }

    /// <summary>
    /// What a HUD mod needs to know about the screen it shares. Without a host, sensible defaults; with Aether, Aether sets
    /// them every frame (room for the game's top HUD, its own menus being open...).
    /// </summary>
    public static class NanoHud
    {
        private static readonly List<NanoHudElement> Elements = new List<NanoHudElement>();
        public static IReadOnlyList<NanoHudElement> All => Elements;

        /// <summary>Space a top-centered card leaves above it. Default: below the game's top HUD at this resolution.</summary>
        public static Func<float> TopInset { get; set; } = () => 48f * (Screen.height / 480f) + 6f;

        /// <summary>Space a top-left card leaves above it.</summary>
        public static Func<float> TopLeftInset { get; set; } = () => 24f;

        /// <summary>A host's menu or editor covers the HUD (set by the host). The Nano window counts too.</summary>
        public static Func<bool> HostOverlayOpen { get; set; } = () => false;

        /// <summary>A host owns the keyboard right now (rebinding keys, typing in a search): mods ignore their hotkeys.</summary>
        public static Func<bool> HostKeyboardCaptured { get; set; } = () => false;

        /// <summary>Closes the host's own menus (Aether's F10, keybind manager, HUD editor) before a mod opens a full-screen window.</summary>
        public static Action CloseHostMenus { get; set; }

        /// <summary>The host moved the game's diamond counter under the XP bar (top-left cards then sit lower).</summary>
        public static Func<bool> DiamondsUnderXp { get; set; } = () => false;

        /// <summary>The host moved the game's top-right buttons to the bottom right (top-right cards can then go higher).</summary>
        public static Func<bool> TopRightButtonsMoved { get; set; } = () => false;

        /// <summary>Opens the host's HUD editor (or Aether Canvas when installed), or null when there is none.</summary>
        public static Action OpenHostEditor { get; set; }

        /// <summary>Dedicated WYSIWYG HUD editor registered by Aether Canvas (preferred over host fallback when present).</summary>
        public static Action OpenCanvasEditor { get; set; }

        /// <summary>True when a dedicated layout mod (Aether Canvas) owns vanilla HUD layout repositioning.</summary>
        public static bool ExternalLayoutActive { get; set; }

        /// <summary>True while Aether Canvas's live WYSIWYG editor is open (hides gameplay-only overlays like 3D waypoints).</summary>
        public static bool CanvasEditorOpen { get; set; }

        /// <summary>Opens the best available HUD editor (Aether Canvas first, else host fallback).</summary>
        public static void OpenEditor()
        {
            if (OpenCanvasEditor != null) { OpenCanvasEditor(); return; }
            OpenHostEditor?.Invoke();
        }

        public static bool OverlayOpen => NanoWindow.IsOpen || CanvasEditorOpen || Safe(HostOverlayOpen);
        public static bool KeyboardCaptured => NanoWindow.IsOpen || Safe(HostKeyboardCaptured);

        /// <summary>True in gameplay with no menu, inventory, pause screen or host overlay on top: the moment to draw a HUD card.</summary>
        public static bool CanDrawGameplayHud => Nano.InGame && !OverlayOpen && !Nano.NativeWindowOpen && !Nano.PauseMenuOpen;

        public static void Register(NanoHudElement element)
        {
            if (element == null || string.IsNullOrEmpty(element.Id) || element.GetRect == null) return;
            Elements.RemoveAll(e => string.Equals(e.Id, element.Id, StringComparison.OrdinalIgnoreCase));
            Elements.Add(element);
        }

        public static NanoHudElement Find(string id) =>
            Elements.Find(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

        private static bool Safe(Func<bool> f)
        {
            try { return f != null && f(); }
            catch { return false; }
        }
    }
}
