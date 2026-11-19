using System;
using System.Collections.Generic;
using System.Reflection;
using Aether.Nano;
using BepInEx.Configuration;
using BepInEx.Logging;
using Blocksters.Terrain.chunk;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Aether.Nano.Nano;

namespace Aether.Mods.Canvas
{
    /// <summary>
    /// Aether Canvas runtime: non-destructive vanilla HUD positioning + standalone WYSIWYG HUD editor
    /// that auto-detects both Block Story's native HUD and every mod registered in <see cref="NanoHud.All"/>.
    /// </summary>
    public sealed partial class CanvasModule
    {
        public static ConfigFile Config { get; internal set; }
        public static ManualLogSource Log { get; internal set; }

        // Config entries
        public static ConfigEntry<KeyCode> OpenEditorKey { get; private set; }
        public static ConfigEntry<string> HudLayoutPreset { get; private set; }
        public static ConfigEntry<bool> MoveDiamondsUnderXP { get; private set; }
        public static ConfigEntry<bool> MoveTopRightButtonsToBottomRight { get; private set; }

        public static ConfigEntry<float> StatsOffsetX { get; private set; }
        public static ConfigEntry<float> StatsOffsetY { get; private set; }
        public static ConfigEntry<float> StatsScale { get; private set; }

        public static ConfigEntry<float> DiamondsOffsetX { get; private set; }
        public static ConfigEntry<float> DiamondsOffsetY { get; private set; }
        public static ConfigEntry<float> DiamondsScale { get; private set; }

        public static ConfigEntry<float> MenuButtonsOffsetX { get; private set; }
        public static ConfigEntry<float> MenuButtonsOffsetY { get; private set; }
        public static ConfigEntry<float> MenuButtonsScale { get; private set; }

        public static ConfigEntry<float> ActionsOffsetX { get; private set; }
        public static ConfigEntry<float> ActionsOffsetY { get; private set; }
        public static ConfigEntry<float> ActionsScale { get; private set; }

        // Native HUD object cache & original vanilla state for exact restoration
        private sealed class VanillaState
        {
            public UIAnchor Anchor;
            public bool HadAnchor, AnchorEnabled;
            public UIAnchor.Side Side;
            public Vector2 Offset;
            public Vector3 Scale, LocalPosition;
            public TweenPosition Tween;
            public bool TweenEnabled;
            public bool Moved;
        }

        private readonly Dictionary<GameObject, VanillaState> _vanilla = new Dictionary<GameObject, VanillaState>();
        private GameObject _statsObj, _barsAnchorObj, _diamondObj, _buttonsObj, _actionsObj;
        private UIAnchor _statsAnchor, _diamondAnchor, _buttonsAnchor, _actionsAnchor;
        private float _lastHudSearchTime;

        // Editor modal & pause state
        public bool IsEditorOpen { get; private set; }
        private GameObject _hiddenSettingsCam;
        private GameObject _hiddenMainSettingsBtn;
        private bool _hasSavedHudVisibility;
        private bool _savedGuiCamGoActive;
        private bool _savedGuiCamEnabled;
        private bool _savedHudActive;
        private bool _savedQuickSlotActive;
        private float _savedTimeScale = 1f;
        private bool _hasSavedTimeScale;

        // Selection & drag state
        private string _selectedId = string.Empty;
        private string _draggingId = string.Empty;
        private Vector2 _dragStartMouse;
        private float _dragStartX, _dragStartY;

        // Draggable floating inspector window
        private Vector2 _windowPos = new Vector2(-1f, -1f);
        private bool _windowCollapsed;
        private bool _draggingWindow;
        private Vector2 _windowDragOffset;
        private Vector2 _listScroll;

        // Cached styles (zero per-frame alloc)
        private static GUIStyle _winTitleStyle, _winMetaStyle, _pillStyle, _rowLabelStyle, _rowMetaStyle, _wireLabelStyle, _noticeStyle;
        private static readonly GUIContent Measure = new GUIContent();
        private static readonly MethodInfo UiAnchorUpdateMethod =
            typeof(UIAnchor).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static void SyncAnchorNow(UIAnchor anchor)
        {
            if (anchor == null || UiAnchorUpdateMethod == null) return;
            try { UiAnchorUpdateMethod.Invoke(anchor, null); }
            catch { }
        }

        private static readonly NanoSegmentOption<string>[] PresetSegmentsEn =
        {
            new NanoSegmentOption<string>("ModernModpack", "Modern"),
            new NanoSegmentOption<string>("CompactLeft", "Left"),
            new NanoSegmentOption<string>("BottomHUD", "Bottom"),
            new NanoSegmentOption<string>("VanillaClassic", "Vanilla")
        };
        private static readonly NanoSegmentOption<string>[] PresetSegmentsEs =
        {
            new NanoSegmentOption<string>("ModernModpack", "Modern"),
            new NanoSegmentOption<string>("CompactLeft", "Izq."),
            new NanoSegmentOption<string>("BottomHUD", "Abajo"),
            new NanoSegmentOption<string>("VanillaClassic", "Vanilla")
        };

        private static readonly NanoSegmentOption<string>[] CenterAnchorsEn =
        {
            new NanoSegmentOption<string>("TopCenter", "Top"),
            new NanoSegmentOption<string>("TopLeft", "Top-L"),
            new NanoSegmentOption<string>("BottomCenter", "Bot"),
            new NanoSegmentOption<string>("AboveCrosshair", "Aim")
        };
        private static readonly NanoSegmentOption<string>[] CenterAnchorsEs =
        {
            new NanoSegmentOption<string>("TopCenter", "Arriba"),
            new NanoSegmentOption<string>("TopLeft", "Izq."),
            new NanoSegmentOption<string>("BottomCenter", "Abajo"),
            new NanoSegmentOption<string>("AboveCrosshair", "Mira")
        };

        private static readonly NanoSegmentOption<string>[] CornerAnchorsEn =
        {
            new NanoSegmentOption<string>("TopRight", "Top-R"),
            new NanoSegmentOption<string>("TopLeft", "Top-L"),
            new NanoSegmentOption<string>("BottomRight", "Bot-R"),
            new NanoSegmentOption<string>("BottomLeft", "Bot-L")
        };
        private static readonly NanoSegmentOption<string>[] CornerAnchorsEs =
        {
            new NanoSegmentOption<string>("TopRight", "Arr-D"),
            new NanoSegmentOption<string>("TopLeft", "Arr-I"),
            new NanoSegmentOption<string>("BottomRight", "Aba-D"),
            new NanoSegmentOption<string>("BottomLeft", "Aba-I")
        };

        private const string NativeStatsId = "native:stats";
        private const string NativeDiamondsId = "native:diamonds";
        private const string NativeButtonsId = "native:buttons";
        private const string NativeActionsId = "native:actions";

        public void Initialize()
        {
            OpenEditorKey = Config.Bind("Canvas", "OpenEditorKey", KeyCode.F9, "Hotkey to open the live WYSIWYG HUD editor.");

            HudLayoutPreset = Config.Bind("HUDLayout", "Preset", "ModernModpack", "HUD layout preset: ModernModpack, CompactLeft, BottomHUD, VanillaClassic, Custom.");
            MoveDiamondsUnderXP = Config.Bind("HUDLayout", "MoveDiamondsUnderXP", true, "Move native Diamond counter under XP/Health/Mana when Lens uses TopCenter.");
            MoveTopRightButtonsToBottomRight = Config.Bind("HUDLayout", "MoveTopRightButtonsToBottomRight", true, "Move native top-right buttons to bottom-right when a minimap uses TopRight.");

            StatsOffsetX = Config.Bind("HUD_Stats", "OffsetX", 0.015f, new ConfigDescription("Horizontal normalized offset for Player Stats.", new AcceptableValueRange<float>(-0.2f, 1.2f)));
            StatsOffsetY = Config.Bind("HUD_Stats", "OffsetY", -0.015f, new ConfigDescription("Vertical normalized offset for Player Stats.", new AcceptableValueRange<float>(-1.2f, 0.2f)));
            StatsScale = Config.Bind("HUD_Stats", "Scale", 1.0f, new ConfigDescription("Scale multiplier for Player Stats.", new AcceptableValueRange<float>(0.5f, 1.5f)));

            DiamondsOffsetX = Config.Bind("HUD_Diamonds", "OffsetX", 0.055f, new ConfigDescription("Horizontal normalized offset for Diamonds.", new AcceptableValueRange<float>(-0.2f, 1.2f)));
            DiamondsOffsetY = Config.Bind("HUD_Diamonds", "OffsetY", -0.142f, new ConfigDescription("Vertical normalized offset for Diamonds.", new AcceptableValueRange<float>(-1.2f, 0.2f)));
            DiamondsScale = Config.Bind("HUD_Diamonds", "Scale", 0.85f, new ConfigDescription("Scale multiplier for Diamonds.", new AcceptableValueRange<float>(0.5f, 1.5f)));

            MenuButtonsOffsetX = Config.Bind("HUD_Buttons", "OffsetX", 1.0f, new ConfigDescription("Horizontal normalized offset for Menu Buttons.", new AcceptableValueRange<float>(-0.2f, 1.2f)));
            MenuButtonsOffsetY = Config.Bind("HUD_Buttons", "OffsetY", -0.75f, new ConfigDescription("Vertical normalized offset for Menu Buttons.", new AcceptableValueRange<float>(-1.2f, 0.2f)));
            MenuButtonsScale = Config.Bind("HUD_Buttons", "Scale", 0.90f, new ConfigDescription("Scale multiplier for Menu Buttons.", new AcceptableValueRange<float>(0.5f, 1.5f)));

            ActionsOffsetX = Config.Bind("HUD_Actions", "OffsetX", 1.0f, new ConfigDescription("Horizontal normalized offset for Action Buttons.", new AcceptableValueRange<float>(-0.2f, 1.2f)));
            ActionsOffsetY = Config.Bind("HUD_Actions", "OffsetY", -1.0f, new ConfigDescription("Vertical normalized offset for Action Buttons.", new AcceptableValueRange<float>(-1.2f, 0.2f)));
            ActionsScale = Config.Bind("HUD_Actions", "Scale", 1.0f, new ConfigDescription("Scale multiplier for Action Buttons.", new AcceptableValueRange<float>(0.5f, 1.5f)));
        }

        public bool AreDiamondsMoved()
        {
            string p = HudLayoutPreset != null ? HudLayoutPreset.Value : "ModernModpack";
            if (p == "VanillaClassic") return false;
            return MoveDiamondsUnderXP != null && MoveDiamondsUnderXP.Value && (p != "ModernModpack" || AnyModAtTopCenter());
        }

        public bool AreTopButtonsMoved()
        {
            string p = HudLayoutPreset != null ? HudLayoutPreset.Value : "ModernModpack";
            if (p == "VanillaClassic") return false;
            return MoveTopRightButtonsToBottomRight != null && MoveTopRightButtonsToBottomRight.Value && (p != "ModernModpack" || AnyModAtTopRight());
        }

        private static bool AnyModAtTopCenter()
        {
            var all = NanoHud.All;
            for (int i = 0; i < all.Count; i++)
            {
                var el = all[i];
                if (el?.Anchor != null && string.Equals(el.Anchor.Value, "TopCenter", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool AnyModAtTopRight()
        {
            var all = NanoHud.All;
            for (int i = 0; i < all.Count; i++)
            {
                var el = all[i];
                if (el?.Anchor != null && string.Equals(el.Anchor.Value, "TopRight", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // =====================================================================
        //                      EDITOR LIFECYCLE & PAUSE
        // =====================================================================

        public void OpenEditor()
        {
            if (IsEditorOpen) return;
            NanoHud.CloseHostMenus?.Invoke();
            NanoWindow.Close();

            IsEditorOpen = true;
            NanoHud.CanvasEditorOpen = true;
            NanoMotion.Snap("aether:canvas_editor", false);
            _draggingId = string.Empty;
            _draggingWindow = false;
            if (string.IsNullOrEmpty(_selectedId))
                _selectedId = NanoHud.All.Count > 0 ? NanoHud.All[0].Id : NativeStatsId;

            if (_windowPos.x < 0f)
                _windowPos = new Vector2(Mathf.Max(20f, (Screen.width - 292f) * 0.5f), Mathf.Max(84f, Screen.height - 440f));

            // Hide Block Story's pause menu AND wake up the live NGUI HUD camera/canvas so the player sees
            // the real game HUD while editing (opening Pause Menu calls KillGUI(), which hides guiCamera & hud).
            try
            {
                Inventory inv = Aether.Nano.Nano.Inventory;
                if (inv != null)
                {
                    if (inv.settingsCamera != null && inv.settingsCamera.gameObject.activeSelf)
                    {
                        _hiddenSettingsCam = inv.settingsCamera.gameObject;
                        _hiddenSettingsCam.SetActive(false);
                    }
                    if (inv.mainSettingsButton != null && inv.mainSettingsButton.activeSelf)
                    {
                        _hiddenMainSettingsBtn = inv.mainSettingsButton;
                        _hiddenMainSettingsBtn.SetActive(false);
                    }

                    if (!_hasSavedHudVisibility)
                    {
                        _savedGuiCamGoActive = inv.guiCamera != null && inv.guiCamera.gameObject.activeSelf;
                        _savedGuiCamEnabled = inv.guiCamera != null && inv.guiCamera.enabled;
                        _savedHudActive = inv.hud != null && inv.hud.activeSelf;
                        _savedQuickSlotActive = inv.quickSlotBar != null && inv.quickSlotBar.activeSelf;
                        _hasSavedHudVisibility = true;
                    }

                    if (inv.guiCamera != null)
                    {
                        if (!inv.guiCamera.gameObject.activeSelf) inv.guiCamera.gameObject.SetActive(true);
                        inv.guiCamera.enabled = true;
                    }
                    if (inv.hud != null && !inv.hud.activeSelf) inv.hud.SetActive(true);
                    if (inv.quickSlotBar != null && !inv.quickSlotBar.activeSelf) inv.quickSlotBar.SetActive(true);
                    inv.ToggleWindowBlur(false);
                }
            }
            catch { }

            // Freeze simulation cleanly while editing
            if (!_hasSavedTimeScale)
            {
                _savedTimeScale = Time.timeScale > 0.001f ? Time.timeScale : 1f;
                _hasSavedTimeScale = true;
            }
            Time.timeScale = 0f;

            // AcquireIsolation disables all active UICamera scripts (so NGUI HUD renders live without eating clicks)
            NanoInput.AcquireIsolation();
            ApplyVanillaHudLayout(true);
            _pendingOpenSnapshot = true;
        }

        public void CloseEditor()
        {
            if (!IsEditorOpen) return;

            if (InGame)
            {
                WorldMode mode = GetCurrentWorldMode();
                WriteLayoutSnapshot("close", mode,
                    BuildStatsVisual(mode), BuildDiamondsVisual(mode),
                    BuildButtonsVisual(mode), BuildActionsVisual(mode),
                    NanoHud.All);
            }

            IsEditorOpen = false;
            NanoHud.CanvasEditorOpen = false;
            _pendingOpenSnapshot = false;
            _draggingId = string.Empty;
            _draggingWindow = false;

            try { Config?.Save(); } catch { }

            try
            {
                Inventory inv = Aether.Nano.Nano.Inventory;
                if (inv != null && _hasSavedHudVisibility)
                {
                    if (inv.guiCamera != null)
                    {
                        inv.guiCamera.enabled = _savedGuiCamEnabled;
                        inv.guiCamera.gameObject.SetActive(_savedGuiCamGoActive);
                    }
                    if (inv.hud != null) inv.hud.SetActive(_savedHudActive);
                    if (inv.quickSlotBar != null) inv.quickSlotBar.SetActive(_savedQuickSlotActive);
                }
            }
            catch { }
            _hasSavedHudVisibility = false;

            if (_hiddenSettingsCam != null)
            {
                try { _hiddenSettingsCam.SetActive(true); } catch { }
                _hiddenSettingsCam = null;
            }
            if (_hiddenMainSettingsBtn != null)
            {
                try { _hiddenMainSettingsBtn.SetActive(true); } catch { }
                _hiddenMainSettingsBtn = null;
            }

            if (_hasSavedTimeScale)
            {
                Time.timeScale = _savedTimeScale;
                _hasSavedTimeScale = false;
            }

            NanoInput.ReleaseIsolation();
        }

        public void TickEditor()
        {
            // Keep simulation frozen, pause camera hidden, and live native HUD visible while editing
            if (Time.timeScale != 0f) Time.timeScale = 0f;
            if (_hiddenSettingsCam != null && _hiddenSettingsCam.activeSelf)
                _hiddenSettingsCam.SetActive(false);
            if (_hiddenMainSettingsBtn != null && _hiddenMainSettingsBtn.activeSelf)
                _hiddenMainSettingsBtn.SetActive(false);

            try
            {
                Inventory inv = Aether.Nano.Nano.Inventory;
                if (inv != null)
                {
                    if (inv.guiCamera != null)
                    {
                        if (!inv.guiCamera.gameObject.activeSelf) inv.guiCamera.gameObject.SetActive(true);
                        if (!inv.guiCamera.enabled) inv.guiCamera.enabled = true;
                    }
                    if (inv.guiCameraScript != null && inv.guiCameraScript.enabled)
                        inv.guiCameraScript.enabled = false;
                    if (inv.hud != null && !inv.hud.activeSelf) inv.hud.SetActive(true);
                    if (inv.quickSlotBar != null && !inv.quickSlotBar.activeSelf) inv.quickSlotBar.SetActive(true);
                }
            }
            catch { }

            // Continuous 1:1 smooth drag tracking in Update() so movement never stutters between IMGUI MouseDrag events
            if (!string.IsNullOrEmpty(_draggingId))
            {
                if (Input.GetMouseButton(0))
                {
                    Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                    UpdateActiveDrag(guiMouse);
                }
                else
                {
                    _draggingId = string.Empty;
                    try { Config?.Save(); } catch { }
                }
            }

            // Pixel-precise keyboard nudging for the selected element
            if (!string.IsNullOrEmpty(_selectedId))
            {
                float step = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? 10f : 1f;
                float dx = 0f, dy = 0f;
                if (Input.GetKeyDown(KeyCode.LeftArrow)) dx = -step;
                else if (Input.GetKeyDown(KeyCode.RightArrow)) dx = step;
                else if (Input.GetKeyDown(KeyCode.UpArrow)) dy = -step;
                else if (Input.GetKeyDown(KeyCode.DownArrow)) dy = step;

                if (dx != 0f || dy != 0f)
                    NudgeElement(_selectedId, dx, dy);
            }
        }

        public void OnUpdate()
        {
            ApplyVanillaHudLayout(IsEditorOpen);
        }

        public void OnSceneChanged(string sceneName)
        {
            if (IsEditorOpen) CloseEditor();
            _statsObj = _barsAnchorObj = _diamondObj = _buttonsObj = _actionsObj = null;
            _statsAnchor = _diamondAnchor = _buttonsAnchor = _actionsAnchor = null;
            _lastHudSearchTime = 0f;
            _vanilla.Clear();
        }

        // =====================================================================
        //                      SETTINGS PAGE (F10 / NANO)
        // =====================================================================

        public void DrawSettings(Rect area, float sy)
        {
            bool es = IsSpanish();
            float pad = 12f;
            float w = Mathf.Max(260f, area.width - pad * 2f);
            Rect col = new Rect(pad, sy, w, area.height);

            sy = NanoSurface.DrawSectionHeader(col, sy,
                es ? "Editor visual en vivo" : "Live WYSIWYG editor",
                $"{OpenEditorKey.Value}");

            if (NanoSurface.DrawActionButton(new Rect(pad, sy, Mathf.Min(280f, w), 34f),
                    es ? $"Abrir Editor de HUD ({OpenEditorKey.Value})" : $"Open Live HUD Editor ({OpenEditorKey.Value})", true))
            {
                OpenEditor();
                return;
            }
            sy += 44f;

            sy = NanoSurface.DrawSectionHeader(col, sy,
                es ? "Presets de diseño" : "Layout presets", HudLayoutPreset.Value);

            if (NanoSurface.DrawSegmented(new Rect(pad, sy, w, 30f),
                    es ? PresetSegmentsEs : PresetSegmentsEn, HudLayoutPreset.Value, out string nextPreset))
            {
                ApplyPreset(nextPreset);
            }
            sy += 38f;

            float halfW = (w - 8f) * 0.5f;
            bool nextDia = NanoSurface.DrawToggleRow(new Rect(pad, sy, halfW, 32f),
                es ? "Diamantes bajo XP" : "Diamonds Under XP", MoveDiamondsUnderXP.Value);
            if (nextDia != MoveDiamondsUnderXP.Value)
            {
                MoveDiamondsUnderXP.Value = nextDia;
                HudLayoutPreset.Value = "Custom";
            }

            bool nextBtns = NanoSurface.DrawToggleRow(new Rect(pad + halfW + 8f, sy, halfW, 32f),
                es ? "Botones arriba → abajo" : "Top Buttons → Bottom", MoveTopRightButtonsToBottomRight.Value);
            if (nextBtns != MoveTopRightButtonsToBottomRight.Value)
            {
                MoveTopRightButtonsToBottomRight.Value = nextBtns;
                HudLayoutPreset.Value = "Custom";
            }
            sy += 44f;

            sy = NanoSurface.DrawSectionHeader(col, sy,
                es ? "Elementos detectados" : "Detected elements",
                es ? $"4 nativos · {NanoHud.All.Count} mods" : $"4 native · {NanoHud.All.Count} mods");

            if (NanoSurface.DrawActionButton(new Rect(pad, sy, 190f, 30f),
                    es ? "Restablecer todo el HUD" : "Reset All HUD Positions", false))
            {
                ResetAll();
            }
        }

        // =====================================================================
        //                       WYSIWYG CANVAS RENDERER
        // =====================================================================

        private static void EnsureStyles()
        {
            if (_winTitleStyle != null) return;
            _winTitleStyle = NanoBrand.Style(NanoBrand.Display, 19, NanoBrand.Paper);
            _winMetaStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Mute);
            _pillStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.PaperLight, TextAnchor.MiddleCenter);
            _rowLabelStyle = NanoBrand.Style(NanoBrand.BodySemi, 12, NanoBrand.Paper);
            _rowMetaStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Mute, TextAnchor.MiddleRight);
            _wireLabelStyle = NanoBrand.Style(NanoBrand.BodySemi, 12, NanoBrand.Mute, TextAnchor.MiddleCenter);
            _noticeStyle = NanoBrand.Style(NanoBrand.Body, 11, NanoBrand.Mute, TextAnchor.MiddleCenter, true);
        }

        public void DrawEditorGUI()
        {
            EnsureStyles();
            int prevDepth = GUI.depth;
            GUI.depth = -4960;
            try
            {
                bool inGame = InGame;
                bool es = IsSpanish();
                Vector2 mousePos = Event.current.mousePosition;
                WorldMode worldMode = inGame ? GetCurrentWorldMode() : WorldMode.Story;

                float anim = NanoMotion.Sample("aether:canvas_editor", IsEditorOpen, 0.19f, 0.12f);

                // 1. Light translucent scrim over the paused world so native HUD & world remain clearly visible
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
                    NanoUi.ColorTexture(new Color(0.03f, 0.024f, 0.018f, 0.24f * anim)));

                // 2. Render live previews of ALL registered NanoHud mod elements (real Lens, real Wayfinder, etc.)
                var modElements = NanoHud.All;
                for (int i = 0; i < modElements.Count; i++)
                {
                    var el = modElements[i];
                    if (el == null || el.GetRect == null) continue;
                    if (el.DrawPreview != null)
                    {
                        try { el.DrawPreview(el.GetRect()); }
                        catch { }
                    }
                }

                // Smooth continuous drag update right before projecting NGUI bounds so brackets & real HUD stay 1:1 locked
                if (!string.IsNullOrEmpty(_draggingId))
                {
                    if (Input.GetMouseButton(0))
                    {
                        UpdateActiveDrag(mousePos);
                    }
                    else
                    {
                        _draggingId = string.Empty;
                        try { Config?.Save(); } catch { }
                    }
                }

                // 3. Project exact pixel-fitted silhouettes for Native HUD (live widgets + low-opacity mode-hidden ghosts)
                HudElementVisual statsVis = inGame ? BuildStatsVisual(worldMode) : default;
                HudElementVisual diaVis = inGame ? BuildDiamondsVisual(worldMode) : default;
                HudElementVisual btnVis = inGame ? BuildButtonsVisual(worldMode) : default;
                HudElementVisual actVis = inGame ? BuildActionsVisual(worldMode) : default;

                // One-shot zero-overhead telemetry snapshot on first rendered editor frame
                if (_pendingOpenSnapshot && Event.current.type == EventType.Repaint)
                {
                    _pendingOpenSnapshot = false;
                    WriteLayoutSnapshot("open", worldMode, statsVis, diaVis, btnVis, actVis, modElements);
                }

                // Render low-opacity ghost previews for native HUD elements hidden in the active game mode
                if (inGame)
                {
                    if (statsVis.HiddenByMode) DrawGhostNativePreview(NativeStatsId, statsVis.Bounds, es, es ? statsVis.ShortBadgeEs : statsVis.ShortBadgeEn);
                    if (diaVis.HiddenByMode) DrawGhostNativePreview(NativeDiamondsId, diaVis.Bounds, es, es ? diaVis.ShortBadgeEs : diaVis.ShortBadgeEn);
                    if (btnVis.HiddenByMode) DrawGhostNativePreview(NativeButtonsId, btnVis.Bounds, es, es ? btnVis.ShortBadgeEs : btnVis.ShortBadgeEn);
                    if (actVis.HiddenByMode) DrawGhostNativePreview(NativeActionsId, actVis.Bounds, es, es ? actVis.ShortBadgeEs : actVis.ShortBadgeEn);
                }

                // Compute floating inspector window bounds first so clicks on it never leak to handles behind it
                Rect winRect = ComputeWindowRect(inGame, modElements.Count);
                bool overWindow = winRect.Contains(mousePos) || _draggingWindow;

                // 4. Center alignment crosshairs (glow softly in Ember when actively dragged item aligns)
                Rect draggedRect = ResolveRectById(_draggingId, inGame, statsVis.Bounds, diaVis.Bounds, btnVis.Bounds, actVis.Bounds);
                float screenCx = Screen.width * 0.5f;
                float screenCy = Screen.height * 0.5f;
                bool alignedX = !string.IsNullOrEmpty(_draggingId) && draggedRect.width > 1f && Mathf.Abs(draggedRect.center.x - screenCx) <= 6f;
                bool alignedY = !string.IsNullOrEmpty(_draggingId) && draggedRect.height > 1f && Mathf.Abs(draggedRect.center.y - screenCy) <= 6f;
                Color guideIdle = new Color(0.92f, 0.88f, 0.78f, 0.07f);
                Color guideLit = new Color(NanoBrand.Ember.r, NanoBrand.Ember.g, NanoBrand.Ember.b, 0.60f);
                GUI.DrawTexture(new Rect(screenCx - 0.5f, 0f, 1f, Screen.height), NanoUi.ColorTexture(alignedX ? guideLit : guideIdle));
                GUI.DrawTexture(new Rect(0f, screenCy - 0.5f, Screen.width, 1f), NanoUi.ColorTexture(alignedY ? guideLit : guideIdle));

                // 5. Sculpted contour brackets tightly fitted around real UI (zero clunky opaque boxes)
                if (inGame)
                {
                    DrawSculptedActionField(NativeStatsId, statsVis.Bounds, statsVis.Parts,
                        "Stats", $"{StatsScale.Value:F2}x", NanoBrand.Ok,
                        !overWindow && statsVis.Bounds.Contains(mousePos), true,
                        statsVis.HiddenByMode, es ? statsVis.ShortBadgeEs : statsVis.ShortBadgeEn);
                    DrawSculptedActionField(NativeDiamondsId, diaVis.Bounds, diaVis.Parts,
                        es ? "Diamantes" : "Diamonds", $"{DiamondsScale.Value:F2}x", new Color(0.56f, 0.76f, 0.86f),
                        !overWindow && diaVis.Bounds.Contains(mousePos), true,
                        diaVis.HiddenByMode, es ? diaVis.ShortBadgeEs : diaVis.ShortBadgeEn);
                    DrawSculptedActionField(NativeButtonsId, btnVis.Bounds, btnVis.Parts,
                        es ? "Botones" : "Buttons", $"{MenuButtonsScale.Value:F2}x", NanoBrand.Warn,
                        !overWindow && btnVis.Bounds.Contains(mousePos), true,
                        btnVis.HiddenByMode, es ? btnVis.ShortBadgeEs : btnVis.ShortBadgeEn);
                    DrawSculptedActionField(NativeActionsId, actVis.Bounds, actVis.Parts,
                        es ? "Acciones" : "Actions", $"{ActionsScale.Value:F2}x", NanoBrand.Bad,
                        !overWindow && actVis.Bounds.Contains(mousePos), true,
                        actVis.HiddenByMode, es ? actVis.ShortBadgeEs : actVis.ShortBadgeEn);
                }

                for (int i = 0; i < modElements.Count; i++)
                {
                    var el = modElements[i];
                    if (el == null || el.GetRect == null) continue;
                    Rect r = el.GetRect();
                    if (r.width < 8f || r.height < 8f) continue;
                    string badge = FormatModReadout(el);
                    Color accent = PickModAccent(el.Id, i);
                    DrawSculptedActionField(el.Id, r, null, el.Name, badge, accent,
                        !overWindow && r.Contains(mousePos), el.DrawPreview != null, false, null);
                }

                // 6. Floating Draggable Inspector Window (smooth modal glide-in)
                using (NanoMotion.BeginModal(winRect, anim, 10f, 0.975f))
                {
                    DrawInspectorWindow(winRect, inGame, es, worldMode, statsVis, diaVis, btnVis, actVis, modElements);
                }

                // 7. Handle Dragging & Scroll-Wheel Scaling on HUD Action Fields (when not over the inspector window)
                if (!overWindow || !string.IsNullOrEmpty(_draggingId))
                    HandleCanvasPointer(mousePos, inGame, statsVis.Bounds, diaVis.Bounds, btnVis.Bounds, actVis.Bounds, modElements);
            }
            finally
            {
                GUI.depth = prevDepth;
            }
        }

        private Rect ComputeWindowRect(bool inGame, int modCount)
        {
            const float winW = 304f;
            float winH = _windowCollapsed ? 42f : Mathf.Min(532f, Screen.height - 32f);
            float x = Mathf.Clamp(_windowPos.x, 12f, Mathf.Max(12f, Screen.width - winW - 12f));
            float y = Mathf.Clamp(_windowPos.y, 12f, Mathf.Max(12f, Screen.height - winH - 12f));
            _windowPos = new Vector2(x, y);
            return new Rect(x, y, winW, winH);
        }

        private void DrawInspectorWindow(
            Rect winRect,
            bool inGame,
            bool es,
            WorldMode worldMode,
            HudElementVisual statsVis,
            HudElementVisual diaVis,
            HudElementVisual btnVis,
            HudElementVisual actVis,
            IReadOnlyList<NanoHudElement> mods)
        {
            // Handle dragging the floating inspector window by its title bar
            var headerDragRect = new Rect(winRect.x, winRect.y, winRect.width - 68f, 40f);
            Event ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0 && headerDragRect.Contains(ev.mousePosition))
            {
                _draggingWindow = true;
                _windowDragOffset = ev.mousePosition - _windowPos;
                ev.Use();
            }
            else if (ev.type == EventType.MouseDrag && _draggingWindow)
            {
                _windowPos = ev.mousePosition - _windowDragOffset;
                ev.Use();
            }
            else if (ev.type == EventType.MouseUp && _draggingWindow)
            {
                _draggingWindow = false;
                ev.Use();
            }

            // Window surface (solid Aether Ink squircle, zero decorative stripes)
            NanoSurface.DrawCardSurface(
                winRect,
                new Color(0.094f, 0.075f, 0.058f, 0.96f),
                new Color(0.047f, 0.039f, 0.031f, 0.97f),
                NanoBrand.Line,
                NanoBrand.Ember,
                new Color(0.98f, 0.93f, 0.85f, 0.12f),
                12f, 1f, 0f, 0f);

            // Title bar
            float cx = winRect.x + 12f;
            if (NanoBrand.Cube != null)
                cx += NanoBrand.DrawCube(cx, winRect.y + 11f, 18f) + 8f;
            NanoSurface.DrawCrispLabel(new Rect(cx, winRect.y + 5f, 80f, 30f), "canvas", _winTitleStyle, NanoBrand.Paper, 0.4f);

            string countMeta = inGame
                ? $"{GetWorldModeShort(worldMode, es)} · {mods.Count}m"
                : $"{mods.Count} mod";
            NanoSurface.DrawCrispLabel(new Rect(cx + 66f, winRect.y + 12f, 114f, 18f), countMeta, _winMetaStyle, NanoBrand.Mute, 0.25f);

            if (NanoSurface.DrawActionButton(new Rect(winRect.xMax - 64f, winRect.y + 8f, 26f, 24f), _windowCollapsed ? "+" : "−", false))
                _windowCollapsed = !_windowCollapsed;

            if (NanoSurface.DrawActionButton(new Rect(winRect.xMax - 34f, winRect.y + 8f, 26f, 24f), "×", false))
            {
                CloseEditor();
                return;
            }

            if (_windowCollapsed) return;

            float pad = 12f;
            float colW = winRect.width - pad * 2f;
            float y = winRect.y + 42f;
            NanoSurface.FillRounded(new Rect(winRect.x + pad, y, colW, 1f), NanoBrand.Line, 0.5f);
            y += 8f;

            // Scrollable auto-detected element list (Native + Mods, custom Aether scrollbar)
            float listH = 166f;
            var listBox = new Rect(winRect.x + pad, y, colW, listH);
            NanoSurface.DrawDebossedSlot(listBox, new Color(0.038f, 0.030f, 0.024f, 0.92f), NanoBrand.Line, 8f);

            int totalRows = (inGame ? 4 : 0) + mods.Count;
            var viewArea = new Rect(listBox.x + 5f, listBox.y + 5f, colW - 10f, listH - 10f);
            float contentH = totalRows * 28f;
            _listScroll = NanoSurface.BeginScrollView(viewArea, _listScroll, contentH, out float innerListW);

            float ry = 0f;
            if (inGame)
            {
                DrawListRow(new Rect(0f, ry, innerListW, 25f), NativeStatsId, es ? "Stats (HP / Mana / XP)" : "Player Stats",
                    statsVis.HiddenByMode ? (es ? "oculto" : "hidden") : $"{StatsScale.Value:F2}x", NanoBrand.Ok, statsVis.HiddenByMode); ry += 28f;
                DrawListRow(new Rect(0f, ry, innerListW, 25f), NativeDiamondsId, es ? "Diamantes" : "Diamonds",
                    diaVis.HiddenByMode ? (es ? "oculto" : "hidden") : $"{DiamondsScale.Value:F2}x", new Color(0.56f, 0.76f, 0.86f), diaVis.HiddenByMode); ry += 28f;
                DrawListRow(new Rect(0f, ry, innerListW, 25f), NativeButtonsId, es ? "Botones de menú" : "Menu Buttons",
                    btnVis.HiddenByMode ? (es ? "oculto" : "hidden") : $"{MenuButtonsScale.Value:F2}x", NanoBrand.Warn, btnVis.HiddenByMode); ry += 28f;
                DrawListRow(new Rect(0f, ry, innerListW, 25f), NativeActionsId, es ? "Botones de acción" : "Action Buttons",
                    actVis.HiddenByMode ? (es ? "oculto" : "hidden") : $"{ActionsScale.Value:F2}x", NanoBrand.Bad, actVis.HiddenByMode); ry += 28f;
            }
            for (int i = 0; i < mods.Count; i++)
            {
                var el = mods[i];
                if (el == null) continue;
                DrawListRow(new Rect(0f, ry, innerListW, 25f), el.Id, el.Name, FormatModReadout(el), PickModAccent(el.Id, i), false);
                ry += 28f;
            }

            NanoSurface.EndScrollView();
            y += listH + 10f;

            // Selected Element Controls (includes honest mode-visibility notice when hidden in current game mode)
            string hiddenNotice = ResolveSelectedHiddenNotice(inGame, es, statsVis, diaVis, btnVis, actVis);
            y = DrawSelectedInspector(winRect.x + pad, y, colW, es, hiddenNotice);
            y += 6f;

            // Quick Presets
            if (NanoSurface.DrawSegmented(new Rect(winRect.x + pad, y, colW, 28f),
                    es ? PresetSegmentsEs : PresetSegmentsEn, HudLayoutPreset.Value, out string preset))
            {
                ApplyPreset(preset);
            }
            y += 34f;

            // Subtle hint line
            NanoSurface.DrawCrispLabel(new Rect(winRect.x + pad, y, colW, 16f),
                es ? "Arrastra en pantalla · Rueda escala · Flechas ajustan" : "Drag on screen · Scroll scales · Arrows nudge",
                _noticeStyle, NanoBrand.Mute, 0.25f);

            // Footer buttons pinned near bottom
            float footerY = winRect.yMax - 40f;
            float halfBtn = (colW - 8f) * 0.5f;
            if (NanoSurface.DrawActionButton(new Rect(winRect.x + pad, footerY, halfBtn, 28f), es ? "Restablecer" : "Reset All", false))
                ResetAll();
            if (NanoSurface.DrawActionButton(new Rect(winRect.x + pad + halfBtn + 8f, footerY, halfBtn, 28f), es ? "Guardar y salir" : "Save & Done", true))
                CloseEditor();
        }

        private string ResolveSelectedHiddenNotice(
            bool inGame,
            bool es,
            HudElementVisual stats,
            HudElementVisual dia,
            HudElementVisual btns,
            HudElementVisual acts)
        {
            if (!inGame) return null;
            if (_selectedId == NativeStatsId && stats.HiddenByMode) return es ? stats.HiddenReasonEs : stats.HiddenReasonEn;
            if (_selectedId == NativeDiamondsId && dia.HiddenByMode) return es ? dia.HiddenReasonEs : dia.HiddenReasonEn;
            if (_selectedId == NativeButtonsId && btns.HiddenByMode) return es ? btns.HiddenReasonEs : btns.HiddenReasonEn;
            if (_selectedId == NativeActionsId && acts.HiddenByMode) return es ? acts.HiddenReasonEs : acts.HiddenReasonEn;
            return null;
        }

        private void DrawListRow(Rect r, string id, string label, string readout, Color accent, bool hiddenByMode)
        {
            bool selected = string.Equals(_selectedId, id, StringComparison.OrdinalIgnoreCase);
            bool hover = r.Contains(Event.current.mousePosition);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                _selectedId = id;

            if (Event.current.type != EventType.Repaint) return;

            Color rowAccent = hiddenByMode ? NanoBrand.Warn : accent;
            if (selected)
            {
                NanoSurface.FillRounded(r, new Color(0.16f, 0.125f, 0.095f, 0.96f), 6f);
                NanoSurface.StrokeRounded(r, new Color(rowAccent.r, rowAccent.g, rowAccent.b, hiddenByMode ? 0.45f : 0.58f), 1f, 6f);
            }
            else if (hover)
            {
                NanoSurface.FillRounded(r, new Color(0.11f, 0.088f, 0.070f, 0.72f), 6f);
            }

            // Visible = crisp accent rhombus (filled when selected); Hidden-by-mode = hollow muted rhombus
            Color rhCol = hiddenByMode ? new Color(NanoBrand.Warn.r, NanoBrand.Warn.g, NanoBrand.Warn.b, 0.55f) : accent;
            NanoSurface.DrawRhombus(new Rect(r.x + 8f, r.y + 9f, 7f, 7f), rhCol, selected && !hiddenByMode);

            Color labelCol = hiddenByMode
                ? (selected ? new Color(NanoBrand.Paper.r, NanoBrand.Paper.g, NanoBrand.Paper.b, 0.76f) : new Color(NanoBrand.Mute.r, NanoBrand.Mute.g, NanoBrand.Mute.b, 0.78f))
                : (selected ? NanoBrand.PaperLight : NanoBrand.Paper);
            NanoSurface.DrawCrispLabel(new Rect(r.x + 20f, r.y, r.width - 78f, r.height), label, _rowLabelStyle, labelCol, 0.3f);

            Color metaCol = hiddenByMode
                ? new Color(NanoBrand.Warn.r, NanoBrand.Warn.g, NanoBrand.Warn.b, 0.78f)
                : (selected ? accent : NanoBrand.Mute);
            NanoSurface.DrawCrispLabel(new Rect(r.xMax - 58f, r.y, 50f, r.height), readout, _rowMetaStyle, metaCol, 0.25f);
        }

        private float DrawSelectedInspector(float x, float y, float w, bool es, string hiddenNotice)
        {
            var col = new Rect(x, y, w, 140f);
            if (!string.IsNullOrEmpty(hiddenNotice))
            {
                EnsureVisualStyles();
                var noteRect = new Rect(x, y, w, 34f);
                NanoSurface.DrawDebossedSlot(noteRect, new Color(0.14f, 0.10f, 0.055f, 0.72f),
                    new Color(NanoBrand.Warn.r, NanoBrand.Warn.g, NanoBrand.Warn.b, 0.42f), 6f);
                NanoSurface.DrawCrispLabel(new Rect(noteRect.x + 8f, noteRect.y + 3f, noteRect.width - 16f, noteRect.height - 6f),
                    hiddenNotice, _noticeBoxStyle, new Color(NanoBrand.Warn.r, NanoBrand.Warn.g, NanoBrand.Warn.b, 0.92f), 0.25f);
                y += 40f;
                col = new Rect(x, y, w, 120f);
            }
            if (_selectedId == NativeStatsId)
            {
                y = NanoSurface.DrawSectionHeader(col, y, es ? "Stats del jugador" : "Player stats");
                float s = NanoSurface.DrawSlider(new Rect(x, y, w, 24f), es ? "Escala" : "Scale", StatsScale.Value, 0.5f, 1.5f, $"{StatsScale.Value:F2}x", 0.02f, 68f);
                if (Mathf.Abs(s - StatsScale.Value) > 0.005f) { StatsScale.Value = s; HudLayoutPreset.Value = "Custom"; ApplyStatsPosition(true); }
                y += 30f;
                if (NanoSurface.DrawActionButton(new Rect(x, y, w, 26f), es ? "Restablecer Stats" : "Reset Stats", false))
                {
                    StatsOffsetX.Value = 0.015f; StatsOffsetY.Value = -0.015f; StatsScale.Value = 1.0f; ApplyStatsPosition(true);
                }
                return y + 32f;
            }
            if (_selectedId == NativeDiamondsId)
            {
                y = NanoSurface.DrawSectionHeader(col, y, es ? "Diamantes" : "Diamonds");
                float s = NanoSurface.DrawSlider(new Rect(x, y, w, 24f), es ? "Escala" : "Scale", DiamondsScale.Value, 0.5f, 1.5f, $"{DiamondsScale.Value:F2}x", 0.02f, 68f);
                if (Mathf.Abs(s - DiamondsScale.Value) > 0.005f) { DiamondsScale.Value = s; HudLayoutPreset.Value = "Custom"; ApplyDiamondPosition(true); }
                y += 30f;
                if (NanoSurface.DrawActionButton(new Rect(x, y, w, 26f), es ? "Restablecer Diamantes" : "Reset Diamonds", false))
                {
                    DiamondsOffsetX.Value = 0.055f; DiamondsOffsetY.Value = -0.142f; DiamondsScale.Value = 0.85f; ApplyDiamondPosition(true);
                }
                return y + 32f;
            }
            if (_selectedId == NativeButtonsId)
            {
                y = NanoSurface.DrawSectionHeader(col, y, es ? "Botones de menú" : "Menu buttons");
                float s = NanoSurface.DrawSlider(new Rect(x, y, w, 24f), es ? "Escala" : "Scale", MenuButtonsScale.Value, 0.5f, 1.5f, $"{MenuButtonsScale.Value:F2}x", 0.02f, 68f);
                if (Mathf.Abs(s - MenuButtonsScale.Value) > 0.005f) { MenuButtonsScale.Value = s; HudLayoutPreset.Value = "Custom"; ApplyButtonsPosition(true); }
                y += 30f;
                if (NanoSurface.DrawActionButton(new Rect(x, y, w, 26f), es ? "Restablecer Botones" : "Reset Buttons", false))
                {
                    MenuButtonsOffsetX.Value = 1.0f; MenuButtonsOffsetY.Value = -0.75f; MenuButtonsScale.Value = 0.90f; ApplyButtonsPosition(true);
                }
                return y + 32f;
            }
            if (_selectedId == NativeActionsId)
            {
                y = NanoSurface.DrawSectionHeader(col, y, es ? "Botones de acción" : "Action buttons");
                float s = NanoSurface.DrawSlider(new Rect(x, y, w, 24f), es ? "Escala" : "Scale", ActionsScale.Value, 0.5f, 1.5f, $"{ActionsScale.Value:F2}x", 0.02f, 68f);
                if (Mathf.Abs(s - ActionsScale.Value) > 0.005f) { ActionsScale.Value = s; HudLayoutPreset.Value = "Custom"; ApplyActionsPosition(true); }
                y += 30f;
                if (NanoSurface.DrawActionButton(new Rect(x, y, w, 26f), es ? "Restablecer Acciones" : "Reset Actions", false))
                {
                    ActionsOffsetX.Value = 1.0f; ActionsOffsetY.Value = -1.0f; ActionsScale.Value = 1.0f; ApplyActionsPosition(true);
                }
                return y + 32f;
            }

            NanoHudElement modEl = NanoHud.Find(_selectedId);
            if (modEl != null)
            {
                y = NanoSurface.DrawSectionHeader(col, y, modEl.Name);
                if (modEl.Size != null)
                {
                    float sz = NanoSurface.DrawSlider(new Rect(x, y, w, 24f), es ? "Tamaño" : "Size",
                        modEl.Size.Value, modEl.SizeMin, modEl.SizeMax, $"{modEl.Size.Value:F0} px", 2f, 68f);
                    if (Mathf.Abs(sz - modEl.Size.Value) > 0.5f) { modEl.Size.Value = sz; HudLayoutPreset.Value = "Custom"; }
                    y += 28f;
                }
                else if (modEl.Scale != null)
                {
                    float sc = NanoSurface.DrawSlider(new Rect(x, y, w, 24f), es ? "Escala" : "Scale",
                        modEl.Scale.Value, 0.5f, 1.5f, $"{modEl.Scale.Value:F2}x", 0.02f, 68f);
                    if (Mathf.Abs(sc - modEl.Scale.Value) > 0.005f) { modEl.Scale.Value = sc; HudLayoutPreset.Value = "Custom"; }
                    y += 28f;
                }

                if (modEl.Anchor != null)
                {
                    bool cornerStyle = IsCornerAnchor(modEl.Anchor.Value);
                    var opts = cornerStyle ? (es ? CornerAnchorsEs : CornerAnchorsEn) : (es ? CenterAnchorsEs : CenterAnchorsEn);
                    if (NanoSurface.DrawSegmented(new Rect(x, y, w, 26f), opts, modEl.Anchor.Value, out string nextAnchor))
                    {
                        modEl.Anchor.Value = nextAnchor;
                        if (modEl.OffsetX != null) modEl.OffsetX.Value = 0f;
                        if (modEl.OffsetY != null) modEl.OffsetY.Value = 0f;
                        HudLayoutPreset.Value = "Custom";
                    }
                    y += 32f;
                }

                if (NanoSurface.DrawActionButton(new Rect(x, y, w, 26f),
                        es ? $"Restablecer {modEl.Name}" : $"Reset {modEl.Name}", false))
                {
                    ResetModElement(modEl);
                }
                return y + 32f;
            }

            return y + 20f;
        }

        private void HandleCanvasPointer(
            Vector2 mousePos,
            bool inGame,
            Rect statsRect,
            Rect diaRect,
            Rect btnRect,
            Rect actRect,
            IReadOnlyList<NanoHudElement> mods)
        {
            Event ev = Event.current;

            // Scroll wheel over any element scales/resizes it live
            if (ev.type == EventType.ScrollWheel)
            {
                float scroll = ev.delta.y;
                if (inGame && statsRect.Contains(mousePos))
                {
                    _selectedId = NativeStatsId;
                    StatsScale.Value = Mathf.Clamp(StatsScale.Value - scroll * 0.05f, 0.5f, 1.5f);
                    ApplyStatsPosition(true);
                    HudLayoutPreset.Value = "Custom";
                    ev.Use();
                    return;
                }
                if (inGame && diaRect.Contains(mousePos))
                {
                    _selectedId = NativeDiamondsId;
                    DiamondsScale.Value = Mathf.Clamp(DiamondsScale.Value - scroll * 0.05f, 0.5f, 1.5f);
                    ApplyDiamondPosition(true);
                    HudLayoutPreset.Value = "Custom";
                    ev.Use();
                    return;
                }
                if (inGame && btnRect.Contains(mousePos))
                {
                    _selectedId = NativeButtonsId;
                    MenuButtonsScale.Value = Mathf.Clamp(MenuButtonsScale.Value - scroll * 0.05f, 0.5f, 1.5f);
                    ApplyButtonsPosition(true);
                    HudLayoutPreset.Value = "Custom";
                    ev.Use();
                    return;
                }
                if (inGame && actRect.Contains(mousePos))
                {
                    _selectedId = NativeActionsId;
                    ActionsScale.Value = Mathf.Clamp(ActionsScale.Value - scroll * 0.05f, 0.5f, 1.5f);
                    ApplyActionsPosition(true);
                    HudLayoutPreset.Value = "Custom";
                    ev.Use();
                    return;
                }
                for (int i = 0; i < mods.Count; i++)
                {
                    var el = mods[i];
                    if (el == null || el.GetRect == null) continue;
                    if (!el.GetRect().Contains(mousePos)) continue;
                    _selectedId = el.Id;
                    if (el.Size != null)
                        el.Size.Value = Mathf.Clamp(el.Size.Value - scroll * 6f, el.SizeMin, el.SizeMax);
                    else if (el.Scale != null)
                        el.Scale.Value = Mathf.Clamp(el.Scale.Value - scroll * 0.05f, 0.5f, 1.5f);
                    HudLayoutPreset.Value = "Custom";
                    ev.Use();
                    return;
                }
            }

            if (ev.type == EventType.MouseDown && ev.button == 0)
            {
                // Prefer mod elements on top, then native HUD elements
                for (int i = mods.Count - 1; i >= 0; i--)
                {
                    var el = mods[i];
                    if (el == null || el.GetRect == null) continue;
                    if (el.GetRect().Contains(mousePos))
                    {
                        _selectedId = el.Id;
                        _draggingId = el.Id;
                        _dragStartMouse = mousePos;
                        _dragStartX = el.OffsetX != null ? el.OffsetX.Value : 0f;
                        _dragStartY = el.OffsetY != null ? el.OffsetY.Value : 0f;
                        ev.Use();
                        return;
                    }
                }
                if (inGame && statsRect.Contains(mousePos)) { BeginNativeDrag(NativeStatsId, mousePos, StatsOffsetX.Value, StatsOffsetY.Value); ev.Use(); return; }
                if (inGame && diaRect.Contains(mousePos)) { BeginNativeDrag(NativeDiamondsId, mousePos, DiamondsOffsetX.Value, DiamondsOffsetY.Value); ev.Use(); return; }
                if (inGame && btnRect.Contains(mousePos)) { BeginNativeDrag(NativeButtonsId, mousePos, MenuButtonsOffsetX.Value, MenuButtonsOffsetY.Value); ev.Use(); return; }
                if (inGame && actRect.Contains(mousePos)) { BeginNativeDrag(NativeActionsId, mousePos, ActionsOffsetX.Value, ActionsOffsetY.Value); ev.Use(); return; }
            }
            else if (ev.type == EventType.MouseDrag && ev.button == 0 && !string.IsNullOrEmpty(_draggingId))
            {
                UpdateActiveDrag(mousePos);
                ev.Use();
            }
            else if (ev.type == EventType.MouseUp && ev.button == 0 && !string.IsNullOrEmpty(_draggingId))
            {
                _draggingId = string.Empty;
                try { Config?.Save(); } catch { }
                ev.Use();
            }
        }

        private void UpdateActiveDrag(Vector2 mousePos)
        {
            if (string.IsNullOrEmpty(_draggingId)) return;

            float dx = mousePos.x - _dragStartMouse.x;
            float dy = mousePos.y - _dragStartMouse.y;
            float normDx = dx / Mathf.Max(1f, (float)Screen.width);
            float normDy = -dy / Mathf.Max(1f, (float)Screen.height);

            if (_draggingId == NativeStatsId)
            {
                StatsOffsetX.Value = Mathf.Clamp(_dragStartX + normDx, -0.2f, 1.2f);
                StatsOffsetY.Value = Mathf.Clamp(_dragStartY + normDy, -1.2f, 0.2f);
                ApplyStatsPosition(true);
            }
            else if (_draggingId == NativeDiamondsId)
            {
                DiamondsOffsetX.Value = Mathf.Clamp(_dragStartX + normDx, -0.2f, 1.2f);
                DiamondsOffsetY.Value = Mathf.Clamp(_dragStartY + normDy, -1.2f, 0.2f);
                ApplyDiamondPosition(true);
            }
            else if (_draggingId == NativeButtonsId)
            {
                MenuButtonsOffsetX.Value = Mathf.Clamp(_dragStartX + normDx, -0.2f, 1.2f);
                MenuButtonsOffsetY.Value = Mathf.Clamp(_dragStartY + normDy, -1.2f, 0.2f);
                ApplyButtonsPosition(true);
            }
            else if (_draggingId == NativeActionsId)
            {
                ActionsOffsetX.Value = Mathf.Clamp(_dragStartX + normDx, -0.2f, 1.2f);
                ActionsOffsetY.Value = Mathf.Clamp(_dragStartY + normDy, -1.2f, 0.2f);
                ApplyActionsPosition(true);
            }
            else
            {
                NanoHudElement el = NanoHud.Find(_draggingId);
                if (el != null)
                {
                    if (el.OffsetX != null)
                    {
                        float nextX = Mathf.Clamp(_dragStartX + dx, -600f, 600f);
                        bool centered = el.Anchor != null && (el.Anchor.Value == "TopCenter" || el.Anchor.Value == "BottomCenter" || el.Anchor.Value == "AboveCrosshair");
                        if (centered && Mathf.Abs(nextX) <= 6f) nextX = 0f;
                        el.OffsetX.Value = nextX;
                    }
                    if (el.OffsetY != null)
                        el.OffsetY.Value = Mathf.Clamp(_dragStartY + dy, -600f, 600f);
                }
            }

            HudLayoutPreset.Value = "Custom";
        }

        private void BeginNativeDrag(string id, Vector2 mousePos, float ox, float oy)
        {
            _selectedId = id;
            _draggingId = id;
            _dragStartMouse = mousePos;
            _dragStartX = ox;
            _dragStartY = oy;
        }

        private void NudgeElement(string id, float dx, float dy)
        {
            float normDx = dx / Mathf.Max(1f, (float)Screen.width);
            float normDy = -dy / Mathf.Max(1f, (float)Screen.height);

            if (id == NativeStatsId) { StatsOffsetX.Value = Mathf.Clamp(StatsOffsetX.Value + normDx, -0.2f, 1.2f); StatsOffsetY.Value = Mathf.Clamp(StatsOffsetY.Value + normDy, -1.2f, 0.2f); ApplyStatsPosition(true); }
            else if (id == NativeDiamondsId) { DiamondsOffsetX.Value = Mathf.Clamp(DiamondsOffsetX.Value + normDx, -0.2f, 1.2f); DiamondsOffsetY.Value = Mathf.Clamp(DiamondsOffsetY.Value + normDy, -1.2f, 0.2f); ApplyDiamondPosition(true); }
            else if (id == NativeButtonsId) { MenuButtonsOffsetX.Value = Mathf.Clamp(MenuButtonsOffsetX.Value + normDx, -0.2f, 1.2f); MenuButtonsOffsetY.Value = Mathf.Clamp(MenuButtonsOffsetY.Value + normDy, -1.2f, 0.2f); ApplyButtonsPosition(true); }
            else if (id == NativeActionsId) { ActionsOffsetX.Value = Mathf.Clamp(ActionsOffsetX.Value + normDx, -0.2f, 1.2f); ActionsOffsetY.Value = Mathf.Clamp(ActionsOffsetY.Value + normDy, -1.2f, 0.2f); ApplyActionsPosition(true); }
            else
            {
                NanoHudElement el = NanoHud.Find(id);
                if (el != null)
                {
                    if (el.OffsetX != null) el.OffsetX.Value = Mathf.Clamp(el.OffsetX.Value + dx, -600f, 600f);
                    if (el.OffsetY != null) el.OffsetY.Value = Mathf.Clamp(el.OffsetY.Value + dy, -600f, 600f);
                }
            }
            HudLayoutPreset.Value = "Custom";
        }

        private static Rect ResolveRectById(string id, bool inGame, Rect stats, Rect dia, Rect btn, Rect act)
        {
            if (string.IsNullOrEmpty(id)) return Rect.zero;
            if (inGame)
            {
                if (id == NativeStatsId) return stats;
                if (id == NativeDiamondsId) return dia;
                if (id == NativeButtonsId) return btn;
                if (id == NativeActionsId) return act;
            }
            NanoHudElement el = NanoHud.Find(id);
            return el?.GetRect != null ? el.GetRect() : Rect.zero;
        }

        private static string FormatModReadout(NanoHudElement el)
        {
            if (el.Size != null) return $"{el.Size.Value:F0}px";
            if (el.Scale != null) return $"{el.Scale.Value:F2}x";
            return "MOD";
        }

        private static Color PickModAccent(string id, int index)
        {
            if (id != null && id.IndexOf("lens", StringComparison.OrdinalIgnoreCase) >= 0) return NanoBrand.Ember;
            if (id != null && id.IndexOf("wayfinder", StringComparison.OrdinalIgnoreCase) >= 0) return new Color(0.62f, 0.51f, 0.77f);
            Color[] gems = { NanoBrand.Ember, new Color(0.62f, 0.51f, 0.77f), NanoBrand.Ok, NanoBrand.Warn };
            return gems[index % gems.Length];
        }

        private static bool IsCornerAnchor(string anchor) =>
            string.Equals(anchor, "TopRight", StringComparison.OrdinalIgnoreCase)
            || string.Equals(anchor, "BottomRight", StringComparison.OrdinalIgnoreCase)
            || string.Equals(anchor, "BottomLeft", StringComparison.OrdinalIgnoreCase);

        private static void ResetModElement(NanoHudElement el)
        {
            if (el == null) return;
            if (el.OnReset != null) { el.OnReset(); return; }
            if (el.OffsetX != null) el.OffsetX.Value = 0f;
            if (el.OffsetY != null) el.OffsetY.Value = 0f;
            if (el.Scale != null) el.Scale.Value = 1.0f;
        }

        // =====================================================================
        //                      PRESETS & VANILLA LAYOUT
        // =====================================================================

        public void ApplyPreset(string preset)
        {
            HudLayoutPreset.Value = preset;
            switch (preset)
            {
                case "CompactLeft":
                    StatsOffsetX.Value = 0.015f; StatsOffsetY.Value = -0.015f; StatsScale.Value = 1.0f;
                    DiamondsOffsetX.Value = 0.055f; DiamondsOffsetY.Value = -0.142f; DiamondsScale.Value = 0.85f;
                    MenuButtonsOffsetX.Value = 1.0f; MenuButtonsOffsetY.Value = 0.0f; MenuButtonsScale.Value = 1.0f;
                    ActionsOffsetX.Value = 1.0f; ActionsOffsetY.Value = -1.0f; ActionsScale.Value = 1.0f;
                    MoveDiamondsUnderXP.Value = true;
                    MoveTopRightButtonsToBottomRight.Value = false;
                    ApplyModPreset("dev.ultimaibrahim.aether.wayfinder", "BottomLeft", 0f, 0f, 160f);
                    ApplyModPreset("dev.ultimaibrahim.aether.lens", "TopCenter", 0f, 0f, -1f, 1f);
                    break;

                case "BottomHUD":
                    StatsOffsetX.Value = 0.015f; StatsOffsetY.Value = -0.85f; StatsScale.Value = 1.0f;
                    DiamondsOffsetX.Value = 0.015f; DiamondsOffsetY.Value = -0.75f; DiamondsScale.Value = 0.85f;
                    MenuButtonsOffsetX.Value = 1.0f; MenuButtonsOffsetY.Value = 0.0f; MenuButtonsScale.Value = 1.0f;
                    ActionsOffsetX.Value = 1.0f; ActionsOffsetY.Value = -1.0f; ActionsScale.Value = 1.0f;
                    MoveDiamondsUnderXP.Value = true;
                    MoveTopRightButtonsToBottomRight.Value = false;
                    ApplyModPreset("dev.ultimaibrahim.aether.wayfinder", "BottomRight", -180f, 0f, 160f);
                    ApplyModPreset("dev.ultimaibrahim.aether.lens", "BottomCenter", 0f, -90f, -1f, 1f);
                    break;

                case "VanillaClassic":
                    StatsOffsetX.Value = 0.015f; StatsOffsetY.Value = -0.015f; StatsScale.Value = 1.0f;
                    DiamondsOffsetX.Value = 0.50f; DiamondsOffsetY.Value = -0.02f; DiamondsScale.Value = 1.0f;
                    MenuButtonsOffsetX.Value = 1.0f; MenuButtonsOffsetY.Value = 0.0f; MenuButtonsScale.Value = 1.0f;
                    ActionsOffsetX.Value = 1.0f; ActionsOffsetY.Value = -1.0f; ActionsScale.Value = 1.0f;
                    MoveDiamondsUnderXP.Value = false;
                    MoveTopRightButtonsToBottomRight.Value = false;
                    RestoreVanillaHud();
                    break;

                case "ModernModpack":
                default:
                    StatsOffsetX.Value = 0.015f; StatsOffsetY.Value = -0.015f; StatsScale.Value = 1.0f;
                    DiamondsOffsetX.Value = 0.055f; DiamondsOffsetY.Value = -0.142f; DiamondsScale.Value = 0.85f;
                    MenuButtonsOffsetX.Value = 1.0f; MenuButtonsOffsetY.Value = -0.75f; MenuButtonsScale.Value = 0.90f;
                    ActionsOffsetX.Value = 1.0f; ActionsOffsetY.Value = -1.0f; ActionsScale.Value = 1.0f;
                    MoveDiamondsUnderXP.Value = true;
                    MoveTopRightButtonsToBottomRight.Value = true;
                    ApplyModPreset("dev.ultimaibrahim.aether.wayfinder", "TopRight", 0f, 0f, 180f);
                    ApplyModPreset("dev.ultimaibrahim.aether.lens", "TopCenter", 0f, 0f, -1f, 1f);
                    break;
            }

            if (preset != "VanillaClassic") ApplyVanillaHudLayout(true);
            try { Config?.Save(); } catch { }
        }

        private static void ApplyModPreset(string id, string anchor, float ox, float oy, float size = -1f, float scale = -1f)
        {
            NanoHudElement el = NanoHud.Find(id);
            if (el == null) return;
            if (el.Anchor != null) el.Anchor.Value = anchor;
            if (el.OffsetX != null) el.OffsetX.Value = ox;
            if (el.OffsetY != null) el.OffsetY.Value = oy;
            if (size > 0f && el.Size != null) el.Size.Value = size;
            if (scale > 0f && el.Scale != null) el.Scale.Value = scale;
        }

        public void ResetAll()
        {
            ApplyPreset("ModernModpack");
            var all = NanoHud.All;
            for (int i = 0; i < all.Count; i++)
                ResetModElement(all[i]);
            try { Config?.Save(); } catch { }
        }

        private void Remember(GameObject go, UIAnchor anchor)
        {
            if (go == null) return;
            if (!_vanilla.TryGetValue(go, out VanillaState st))
            {
                TweenPosition tween = go.GetComponent<TweenPosition>();
                st = new VanillaState
                {
                    Anchor = anchor, HadAnchor = anchor != null, AnchorEnabled = anchor != null && anchor.enabled,
                    Side = anchor != null ? anchor.side : UIAnchor.Side.Center,
                    Offset = anchor != null ? anchor.relativeOffset : Vector2.zero,
                    Scale = go.transform.localScale, LocalPosition = go.transform.localPosition,
                    Tween = tween, TweenEnabled = tween != null && tween.enabled
                };
                _vanilla[go] = st;
            }
            st.Moved = true;
        }

        private void PutBack(GameObject go)
        {
            if (go == null || !_vanilla.TryGetValue(go, out VanillaState st) || !st.Moved) return;
            st.Moved = false;
            try
            {
                UIAnchor anchor = go.GetComponent<UIAnchor>();
                if (anchor != null)
                {
                    if (st.HadAnchor)
                    {
                        anchor.side = st.Side;
                        anchor.relativeOffset = st.Offset;
                        anchor.enabled = st.AnchorEnabled;
                    }
                    else anchor.enabled = false;
                }
                go.transform.localScale = st.Scale;
                go.transform.localPosition = st.LocalPosition;
                if (st.Tween != null) st.Tween.enabled = st.TweenEnabled;
            }
            catch { }
        }

        public void RestoreVanillaHud()
        {
            if (SceneManager.GetActiveScene().name != "PlayingMobile") return;
            PutBack(_barsAnchorObj);
            PutBack(_statsObj);
            PutBack(_diamondObj);
            PutBack(_buttonsObj);
            PutBack(_actionsObj);
        }

        public void ApplyVanillaHudLayout(bool force = false)
        {
            if (SceneManager.GetActiveScene().name != "PlayingMobile") return;

            if (_statsAnchor == null || _diamondAnchor == null || _buttonsAnchor == null || _actionsAnchor == null)
            {
                if (!force && Time.unscaledTime - _lastHudSearchTime < 0.5f) return;
                _lastHudSearchTime = Time.unscaledTime;
            }

            string preset = HudLayoutPreset != null ? HudLayoutPreset.Value : "ModernModpack";
            bool vanilla = preset == "VanillaClassic";
            bool chosen = !vanilla && preset != "ModernModpack";
            bool moveDia = !vanilla && (chosen || AreDiamondsMoved());
            bool moveBtns = !vanilla && (chosen || AreTopButtonsMoved());

            ApplyStatsPosition(chosen || moveDia);
            ApplyDiamondPosition(moveDia);
            ApplyButtonsPosition(moveBtns);
            ApplyActionsPosition(chosen || moveBtns);
        }

        private void ApplyStatsPosition(bool move)
        {
            if (_statsObj == null)
            {
                _statsObj = GameObject.Find("HUD/Stats");
                if (_statsObj != null) _statsAnchor = _statsObj.GetComponent<UIAnchor>();
            }
            if (!move) { PutBack(_barsAnchorObj); PutBack(_statsObj); return; }
            Remember(_statsObj, _statsAnchor);

            if (_statsAnchor != null)
            {
                var off = new Vector2(StatsOffsetX.Value, StatsOffsetY.Value);
                if (!_statsAnchor.enabled || _statsAnchor.side != UIAnchor.Side.TopLeft || _statsAnchor.relativeOffset != off)
                {
                    _statsAnchor.side = UIAnchor.Side.TopLeft;
                    _statsAnchor.relativeOffset = off;
                    _statsAnchor.enabled = true;
                    if (IsEditorOpen) SyncAnchorNow(_statsAnchor);
                }
            }
            if (_statsObj != null)
            {
                var scale = new Vector3(StatsScale.Value, StatsScale.Value, 1f);
                if (_statsObj.transform.localScale != scale) _statsObj.transform.localScale = scale;

                if (_barsAnchorObj == null)
                {
                    Transform t = _statsObj.transform.Find("BarsAnchor");
                    if (t != null) _barsAnchorObj = t.gameObject;
                }
                if (_barsAnchorObj != null)
                {
                    UIAnchor ba = _barsAnchorObj.GetComponent<UIAnchor>();
                    Remember(_barsAnchorObj, ba);
                    if (ba != null && ba.enabled) ba.enabled = false;
                    // Snug native spacing between Level numeral and food/heart/mana cluster (46f left a huge gap)
                    _barsAnchorObj.transform.localPosition = new Vector3(22f, -3.5f, 0f);
                    _barsAnchorObj.transform.localScale = Vector3.one;
                }
            }
        }

        private void ApplyDiamondPosition(bool move)
        {
            if (_diamondObj == null)
            {
                _diamondObj = GameObject.Find("HUD/Diamond");
                if (_diamondObj != null) _diamondAnchor = _diamondObj.GetComponent<UIAnchor>();
            }
            if (!move) { PutBack(_diamondObj); return; }
            Remember(_diamondObj, _diamondAnchor);

            if (_diamondAnchor != null)
            {
                var off = new Vector2(DiamondsOffsetX.Value, DiamondsOffsetY.Value);
                if (!_diamondAnchor.enabled || _diamondAnchor.side != UIAnchor.Side.TopLeft || _diamondAnchor.relativeOffset != off)
                {
                    _diamondAnchor.side = UIAnchor.Side.TopLeft;
                    _diamondAnchor.relativeOffset = off;
                    _diamondAnchor.enabled = true;
                    if (IsEditorOpen) SyncAnchorNow(_diamondAnchor);
                }
            }
            if (_diamondObj != null)
            {
                var scale = new Vector3(DiamondsScale.Value, DiamondsScale.Value, 1f);
                if (_diamondObj.transform.localScale != scale) _diamondObj.transform.localScale = scale;
            }
        }

        private void ApplyButtonsPosition(bool move)
        {
            if (_buttonsObj == null)
            {
                _buttonsObj = GameObject.Find("HUD/Buttons");
                if (_buttonsObj != null) _buttonsAnchor = _buttonsObj.GetComponent<UIAnchor>();
            }
            if (!move) { PutBack(_buttonsObj); return; }
            Remember(_buttonsObj, _buttonsAnchor);

            if (_buttonsAnchor != null)
            {
                var off = new Vector2(MenuButtonsOffsetX.Value, MenuButtonsOffsetY.Value);
                if (!_buttonsAnchor.enabled || _buttonsAnchor.side != UIAnchor.Side.TopLeft || _buttonsAnchor.relativeOffset != off)
                {
                    _buttonsAnchor.side = UIAnchor.Side.TopLeft;
                    _buttonsAnchor.relativeOffset = off;
                    _buttonsAnchor.enabled = true;
                    if (IsEditorOpen) SyncAnchorNow(_buttonsAnchor);
                }
            }
            if (_buttonsObj != null)
            {
                var scale = new Vector3(MenuButtonsScale.Value, MenuButtonsScale.Value, 1f);
                if (_buttonsObj.transform.localScale != scale) _buttonsObj.transform.localScale = scale;
                TweenPosition tp = _buttonsObj.GetComponent<TweenPosition>();
                if (tp != null && tp.enabled) tp.enabled = false;
            }
        }

        private void ApplyActionsPosition(bool move)
        {
            if (_actionsObj == null)
            {
                _actionsObj = GameObject.Find("HUD/Actions");
                if (_actionsObj != null) _actionsAnchor = _actionsObj.GetComponent<UIAnchor>();
            }
            if (!move) { PutBack(_actionsObj); return; }
            Remember(_actionsObj, _actionsObj != null ? _actionsObj.GetComponent<UIAnchor>() : null);

            if (_actionsObj != null && _actionsAnchor == null)
            {
                _actionsAnchor = _actionsObj.GetComponent<UIAnchor>() ?? _actionsObj.AddComponent<UIAnchor>();
                if (_actionsAnchor.uiCamera == null) _actionsAnchor.uiCamera = NGUITools.FindCameraForLayer(_actionsObj.layer);
            }
            if (_actionsAnchor != null)
            {
                var off = new Vector2(ActionsOffsetX.Value, ActionsOffsetY.Value);
                if (!_actionsAnchor.enabled || _actionsAnchor.side != UIAnchor.Side.TopLeft || _actionsAnchor.relativeOffset != off)
                {
                    _actionsAnchor.side = UIAnchor.Side.TopLeft;
                    _actionsAnchor.relativeOffset = off;
                    _actionsAnchor.enabled = true;
                    if (IsEditorOpen) SyncAnchorNow(_actionsAnchor);
                }
            }
            if (_actionsObj != null)
            {
                var scale = new Vector3(ActionsScale.Value, ActionsScale.Value, 1f);
                if (_actionsObj.transform.localScale != scale) _actionsObj.transform.localScale = scale;
            }
        }
    }
}
