using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using SuitePlugin = BlockStoryLens.BlockStorySuitePlugin;

namespace BlockStoryLens
{
    public enum CustomizerDragTarget
    {
        None,
        Stats,
        Diamonds,
        Buttons,
        Actions,
        Lens,
        Minimap
    }

    public struct ThemePalette
    {
        public string Id;
        public string NameEn;
        public string NameEs;
        public string FrameSprite;
        public Color FrameTint;
        public Color BorderColor;
        public Color InnerBgColor;
        public Color ButtonNormalTint;
        public Color ButtonSelectedTint;
        public Color ButtonHoverTint;
        public Color TitleColor;
        public Color AccentColor;
        public Color TextColor;
        public Color ShadowColor;
    }

    public static class HudCustomizerModule
    {
        // =====================================================================
        //                 CONFIG ENTRIES FOR VANILLA & MOD HUD
        // =====================================================================
        // 1. Player Stats Bar (HUD/Stats — HP, Hunger, Mana, XP)
        public static ConfigEntry<float> StatsOffsetX { get; private set; }
        public static ConfigEntry<float> StatsOffsetY { get; private set; }
        public static ConfigEntry<float> StatsScale { get; private set; }

        // 2. Diamonds Counter (HUD/Diamond)
        public static ConfigEntry<float> DiamondsOffsetX { get; private set; }
        public static ConfigEntry<float> DiamondsOffsetY { get; private set; }
        public static ConfigEntry<float> DiamondsScale { get; private set; }

        // 3. Quick Menu Buttons (HUD/Buttons — Settings, Camera, Achievements, Quests, Skills)
        public static ConfigEntry<float> MenuButtonsOffsetX { get; private set; }
        public static ConfigEntry<float> MenuButtonsOffsetY { get; private set; }
        public static ConfigEntry<float> MenuButtonsScale { get; private set; }

        // 4. Action Buttons (HUD/Actions — Fly, Jump, Attack)
        public static ConfigEntry<float> ActionsOffsetX { get; private set; }
        public static ConfigEntry<float> ActionsOffsetY { get; private set; }
        public static ConfigEntry<float> ActionsScale { get; private set; }

        // 5. Lens WAILA HUD Scale
        /// <summary>Lens's card scale, from Aether Lens's own config (a Nano mod since 1.1.0); null when Lens is not installed.</summary>
        public static ConfigEntry<float> LensScale => BlockStorySuitePlugin.LensSetting<float>("Scale");

        // 6. Wayfinder Minimap Size & Scale
        public static ConfigEntry<float> MinimapSize => SuitePlugin.MinimapSize;
        public static ConfigEntry<float> MinimapScale => SuitePlugin.MinimapScale;

        // 7. Authentic Game UI Design Theme Preset
        public static ConfigEntry<string> UiDesignTheme { get; private set; }

        // =====================================================================
        //                 RUNTIME STATE & CACHED HUD OBJECTS
        // =====================================================================
        public static CustomizerDragTarget ActiveDragTarget = CustomizerDragTarget.None;
        private static Vector2 dragStartMousePos;
        private static float dragStartOffsetX;
        private static float dragStartOffsetY;

        private static GameObject cachedStatsObj;
        private static UIAnchor cachedStatsAnchor;
        private static GameObject cachedBarsAnchorObj;

        private static GameObject cachedDiamondObj;
        private static UIAnchor cachedDiamondAnchor;

        private static GameObject cachedButtonsObj;
        private static UIAnchor cachedButtonsAnchor;

        private static GameObject cachedActionsObj;
        private static UIAnchor cachedActionsAnchor;

        private static float lastHudSearchTime = 0f;

        // Styles
        private static GUIStyle handleStyle;
        private static GUIStyle bannerTitleStyle;

        // =====================================================================
        //                         INITIALIZATION
        // =====================================================================
        public static void InitHudCustomizer(ConfigFile config)
        {
            Initialize(config);
        }

        public static void InitHudCustomizer()
        {
            if (SuitePlugin.Instance != null && SuitePlugin.Instance.Config != null)
                Initialize(SuitePlugin.Instance.Config);
        }

        public static void Initialize(ConfigFile config)
        {
            // 1. Player Stats Bar Config
            StatsOffsetX = config.Bind("HUD_Stats", "OffsetX", 0.015f,
                new ConfigDescription("Horizontal normalized offset for Stats Bar (TopLeft anchor: 0=Left, 1=Right).",
                new AcceptableValueRange<float>(-0.2f, 1.2f)));
            StatsOffsetY = config.Bind("HUD_Stats", "OffsetY", -0.015f,
                new ConfigDescription("Vertical normalized offset for Stats Bar (TopLeft anchor: 0=Top, -1=Bottom).",
                new AcceptableValueRange<float>(-1.2f, 0.2f)));
            StatsScale = config.Bind("HUD_Stats", "Scale", 1.0f,
                new ConfigDescription("Scale multiplier for Player Stats Bar (HP, Mana, Hunger, XP).",
                new AcceptableValueRange<float>(0.5f, 1.5f)));

            // 2. Diamonds Counter Config
            DiamondsOffsetX = config.Bind("HUD_Diamonds", "OffsetX", 0.055f,
                new ConfigDescription("Horizontal normalized offset for Diamonds Counter.",
                new AcceptableValueRange<float>(-0.2f, 1.2f)));
            DiamondsOffsetY = config.Bind("HUD_Diamonds", "OffsetY", -0.142f,
                new ConfigDescription("Vertical normalized offset for Diamonds Counter.",
                new AcceptableValueRange<float>(-1.2f, 0.2f)));
            DiamondsScale = config.Bind("HUD_Diamonds", "Scale", 0.85f,
                new ConfigDescription("Scale multiplier for Diamonds Counter.",
                new AcceptableValueRange<float>(0.5f, 1.5f)));

            // 3. Quick Menu Buttons Config
            MenuButtonsOffsetX = config.Bind("HUD_MenuButtons", "OffsetX", 1.0f,
                new ConfigDescription("Horizontal normalized offset for Top Quick Menu Buttons.",
                new AcceptableValueRange<float>(-0.2f, 1.2f)));
            MenuButtonsOffsetY = config.Bind("HUD_MenuButtons", "OffsetY", -0.75f,
                new ConfigDescription("Vertical normalized offset for Top Quick Menu Buttons.",
                new AcceptableValueRange<float>(-1.2f, 0.2f)));
            MenuButtonsScale = config.Bind("HUD_MenuButtons", "Scale", 0.90f,
                new ConfigDescription("Scale multiplier for Top Quick Menu Buttons.",
                new AcceptableValueRange<float>(0.5f, 1.5f)));

            // 4. Action Buttons Config
            ActionsOffsetX = config.Bind("HUD_Actions", "OffsetX", 1.0f,
                new ConfigDescription("Horizontal normalized offset for Action Buttons (Fly, Jump, Attack).",
                new AcceptableValueRange<float>(-0.2f, 1.2f)));
            ActionsOffsetY = config.Bind("HUD_Actions", "OffsetY", -1.0f,
                new ConfigDescription("Vertical normalized offset for Action Buttons (Fly, Jump, Attack).",
                new AcceptableValueRange<float>(-1.2f, 0.2f)));
            ActionsScale = config.Bind("HUD_Actions", "Scale", 1.0f,
                new ConfigDescription("Scale multiplier for Action Buttons.",
                new AcceptableValueRange<float>(0.5f, 1.5f)));

            // 5. Lens WAILA Scale: bound by Aether Lens itself ([Lens] Scale in its own config).

            // 6. Wayfinder Minimap Size & Scale are owned and bound by Aether.Mods.Wayfinder
            //    ([Wayfinder] MinimapSize/MinimapScale). They stay accessible through the bridge.

            // 7. Authentic UI Design Theme
            UiDesignTheme = config.Bind("UI", "DesignTheme", "NativeWood",
                "Authentic Block Story UI Design Preset: NativeWood, AncientStone, DwarvenGold, ArcaneGlass.");

            ClearCache();
        }

        public static void ClearCache()
        {
            cachedStatsObj = null;
            cachedStatsAnchor = null;
            cachedBarsAnchorObj = null;
            cachedDiamondObj = null;
            cachedDiamondAnchor = null;
            cachedButtonsObj = null;
            cachedButtonsAnchor = null;
            cachedActionsObj = null;
            cachedActionsAnchor = null;
            lastHudSearchTime = 0f;
        }

        // =====================================================================
        //                 AUTHENTIC UI DESIGN PRESET PALETTES
        // =====================================================================
        public static ThemePalette GetThemePalette(string themeName = null)
        {
            // The table lives in Aether Nano (shared with the mods built on it); the player's theme is its default.
            if (UiDesignTheme != null) Aether.Nano.NanoThemes.Default = UiDesignTheme.Value;
            Aether.Nano.NanoTheme t = Aether.Nano.NanoThemes.Get(themeName);
            return new ThemePalette
            {
                Id = t.Id, NameEn = t.NameEn, NameEs = t.NameEs, FrameSprite = t.FrameSprite, FrameTint = t.FrameTint,
                BorderColor = t.BorderColor, InnerBgColor = t.InnerBgColor, ButtonNormalTint = t.ButtonNormalTint,
                ButtonSelectedTint = t.ButtonSelectedTint, ButtonHoverTint = t.ButtonHoverTint, TitleColor = t.TitleColor,
                AccentColor = t.AccentColor, TextColor = t.TextColor, ShadowColor = t.ShadowColor
            };
        }

        public static void DrawThemedFrame(Rect rect, string themeName = null, float opacity = 1.0f)
        {
            ThemePalette p = GetThemePalette(themeName);
            Color borderCol = new Color(p.BorderColor.r, p.BorderColor.g, p.BorderColor.b, p.BorderColor.a * opacity);
            GUI.DrawTexture(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), SuitePlugin.GetColorTexture(borderCol));

            Color frameTint = new Color(p.FrameTint.r, p.FrameTint.g, p.FrameTint.b, p.FrameTint.a * opacity);
            if (!SuitePlugin.DrawNativeSprite(rect, p.FrameSprite, frameTint))
            {
                GUI.DrawTexture(rect, SuitePlugin.GetColorTexture(new Color(p.InnerBgColor.r, p.InnerBgColor.g, p.InnerBgColor.b, opacity * 0.94f)));
            }
        }

        public static bool DrawThemedButton(Rect rect, string text, bool isSelected = false, Color? accentOverride = null, string themeName = null)
        {
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                ThemePalette p = GetThemePalette(themeName);
                bool hovered = rect.Contains(Event.current.mousePosition);

                Color btnTint = isSelected
                    ? (accentOverride ?? p.ButtonSelectedTint)
                    : (hovered ? p.ButtonHoverTint : p.ButtonNormalTint);

                if (!SuitePlugin.DrawNativeSprite(rect, "Button", btnTint))
                {
                    if (!SuitePlugin.DrawNativeSprite(rect, "SetButtonSqareSl", btnTint))
                    {
                        Color fbBorder = isSelected ? Color.white : (hovered ? Color.white : p.BorderColor);
                        Color fbBg = isSelected ? new Color(0.20f, 0.50f, 0.20f, 0.95f) : (hovered ? new Color(0.35f, 0.30f, 0.22f, 0.95f) : p.InnerBgColor);
                        GUI.DrawTexture(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), SuitePlugin.GetColorTexture(fbBorder));
                        GUI.DrawTexture(rect, SuitePlugin.GetColorTexture(fbBg));
                    }
                }

                Color textCol = isSelected ? Color.white : (hovered ? new Color(1.0f, 1.0f, 0.85f) : p.TextColor);
                SuitePlugin.DrawShadowLabel(rect, text, SuitePlugin.Instance != null ? SuitePlugin.Instance.GetWoodBtnTextStyle() : null, textCol, p.ShadowColor);
            }
            return clicked;
        }

        // =====================================================================
        //                 VANILLA HUD POSITIONING & LOCKING
        // =====================================================================
        /// <summary>
        /// Undoes every mutation this module applies to the vanilla HUD: re-enables the UIAnchor and
        /// TweenPosition components it disables and clears the offsets/scales it forced. Without this
        /// the "VanillaClassic" preset could never actually restore vanilla (M16).
        /// </summary>
        public static void RestoreVanillaHud()
        {
            if (SceneManager.GetActiveScene().name != "PlayingMobile") return;

            try
            {
                if (cachedBarsAnchorObj != null)
                {
                    UIAnchor barsAnchor = cachedBarsAnchorObj.GetComponent<UIAnchor>();
                    if (barsAnchor != null) barsAnchor.enabled = true;
                    cachedBarsAnchorObj.transform.localPosition = Vector3.zero;
                    cachedBarsAnchorObj.transform.localScale = Vector3.one;
                }

                if (cachedButtonsObj != null)
                {
                    TweenPosition tween = cachedButtonsObj.GetComponent<TweenPosition>();
                    if (tween != null) tween.enabled = true;
                }

                RestoreAnchorOffset(cachedStatsAnchor, cachedStatsObj);
                RestoreAnchorOffset(cachedDiamondAnchor, cachedDiamondObj);
                RestoreAnchorOffset(cachedButtonsAnchor, cachedButtonsObj);
                RestoreAnchorOffset(cachedActionsAnchor, cachedActionsObj);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[Aether.Suite] RestoreVanillaHud warning: " + ex.Message);
            }
        }

        private static void RestoreAnchorOffset(UIAnchor anchor, GameObject target)
        {
            if (target == null) return;

            if (anchor != null)
            {
                anchor.relativeOffset = Vector2.zero;
                anchor.enabled = true;
            }
            target.transform.localScale = Vector3.one;
        }

        public static void ApplyVanillaHudLayout(bool force = false)
        {
            if (SceneManager.GetActiveScene().name != "PlayingMobile") return;

            if (cachedStatsAnchor == null || cachedDiamondAnchor == null || cachedButtonsAnchor == null || cachedActionsAnchor == null)
            {
                if (!force && Time.unscaledTime - lastHudSearchTime < 0.5f) return;
                lastHudSearchTime = Time.unscaledTime;
            }

            ApplyStatsPosition();
            ApplyDiamondPosition();
            ApplyButtonsPosition();
            ApplyActionsPosition();
        }

        public static void ApplyStatsPosition()
        {
            if (cachedStatsObj == null)
            {
                cachedStatsObj = GameObject.Find("HUD/Stats");
                if (cachedStatsObj != null)
                    cachedStatsAnchor = cachedStatsObj.GetComponent<UIAnchor>();
            }

            if (cachedStatsAnchor != null)
            {
                if (cachedStatsAnchor.side != UIAnchor.Side.TopLeft || cachedStatsAnchor.relativeOffset != new Vector2(StatsOffsetX.Value, StatsOffsetY.Value))
                {
                    cachedStatsAnchor.side = UIAnchor.Side.TopLeft;
                    cachedStatsAnchor.relativeOffset = new Vector2(StatsOffsetX.Value, StatsOffsetY.Value);
                    cachedStatsAnchor.enabled = true;
                }
            }

            if (cachedStatsObj != null)
            {
                Vector3 targetScale = new Vector3(StatsScale.Value, StatsScale.Value, 1f);
                if (cachedStatsObj.transform.localScale != targetScale)
                    cachedStatsObj.transform.localScale = targetScale;

                // CRITICAL FIX: BarsAnchor has its own UIAnchor (Side.TopRight) in vanilla!
                // Disabling it locks BarsAnchor relative to HUD/Stats at (46, -3.5, 0),
                // so HP, Mana, Food, and XP bars move and scale in absolute lockstep with Stats!
                if (cachedBarsAnchorObj == null)
                {
                    Transform t = cachedStatsObj.transform.Find("BarsAnchor");
                    if (t != null) cachedBarsAnchorObj = t.gameObject;
                }
                if (cachedBarsAnchorObj != null)
                {
                    UIAnchor ba = cachedBarsAnchorObj.GetComponent<UIAnchor>();
                    if (ba != null && ba.enabled)
                    {
                        ba.enabled = false;
                    }
                    cachedBarsAnchorObj.transform.localPosition = new Vector3(46f, -3.5f, 0f);
                    cachedBarsAnchorObj.transform.localScale = Vector3.one;
                }
            }
        }

        public static void ApplyDiamondPosition()
        {
            if (cachedDiamondObj == null)
            {
                cachedDiamondObj = GameObject.Find("HUD/Diamond");
                if (cachedDiamondObj != null)
                    cachedDiamondAnchor = cachedDiamondObj.GetComponent<UIAnchor>();
            }

            if (cachedDiamondAnchor != null)
            {
                if (cachedDiamondAnchor.side != UIAnchor.Side.TopLeft || cachedDiamondAnchor.relativeOffset != new Vector2(DiamondsOffsetX.Value, DiamondsOffsetY.Value))
                {
                    cachedDiamondAnchor.side = UIAnchor.Side.TopLeft;
                    cachedDiamondAnchor.relativeOffset = new Vector2(DiamondsOffsetX.Value, DiamondsOffsetY.Value);
                    cachedDiamondAnchor.enabled = true;
                }
            }

            if (cachedDiamondObj != null)
            {
                Vector3 targetScale = new Vector3(DiamondsScale.Value, DiamondsScale.Value, 1f);
                if (cachedDiamondObj.transform.localScale != targetScale)
                    cachedDiamondObj.transform.localScale = targetScale;
            }
        }

        public static void ApplyButtonsPosition()
        {
            if (cachedButtonsObj == null)
            {
                cachedButtonsObj = GameObject.Find("HUD/Buttons");
                if (cachedButtonsObj != null)
                    cachedButtonsAnchor = cachedButtonsObj.GetComponent<UIAnchor>();
            }

            if (cachedButtonsAnchor != null)
            {
                if (cachedButtonsAnchor.side != UIAnchor.Side.TopLeft || cachedButtonsAnchor.relativeOffset != new Vector2(MenuButtonsOffsetX.Value, MenuButtonsOffsetY.Value))
                {
                    cachedButtonsAnchor.side = UIAnchor.Side.TopLeft;
                    cachedButtonsAnchor.relativeOffset = new Vector2(MenuButtonsOffsetX.Value, MenuButtonsOffsetY.Value);
                    cachedButtonsAnchor.enabled = true;
                }
            }

            if (cachedButtonsObj != null)
            {
                Vector3 targetScale = new Vector3(MenuButtonsScale.Value, MenuButtonsScale.Value, 1f);
                if (cachedButtonsObj.transform.localScale != targetScale)
                    cachedButtonsObj.transform.localScale = targetScale;

                // Stop any TweenPosition on HUD/Buttons that might fight with UIAnchor
                TweenPosition tp = cachedButtonsObj.GetComponent<TweenPosition>();
                if (tp != null && tp.enabled)
                {
                    tp.enabled = false;
                }
            }
        }

        public static void ApplyActionsPosition()
        {
            if (cachedActionsObj == null)
            {
                cachedActionsObj = GameObject.Find("HUD/Actions");
                if (cachedActionsObj != null)
                    cachedActionsAnchor = cachedActionsObj.GetComponent<UIAnchor>();
            }

            if (cachedActionsObj != null && cachedActionsAnchor == null)
            {
                cachedActionsAnchor = cachedActionsObj.GetComponent<UIAnchor>() ?? cachedActionsObj.AddComponent<UIAnchor>();
                if (cachedActionsAnchor.uiCamera == null)
                {
                    cachedActionsAnchor.uiCamera = NGUITools.FindCameraForLayer(cachedActionsObj.layer);
                }
            }

            if (cachedActionsAnchor != null)
            {
                if (cachedActionsAnchor.side != UIAnchor.Side.TopLeft || cachedActionsAnchor.relativeOffset != new Vector2(ActionsOffsetX.Value, ActionsOffsetY.Value))
                {
                    cachedActionsAnchor.side = UIAnchor.Side.TopLeft;
                    cachedActionsAnchor.relativeOffset = new Vector2(ActionsOffsetX.Value, ActionsOffsetY.Value);
                    cachedActionsAnchor.enabled = true;
                }
            }

            if (cachedActionsObj != null)
            {
                Vector3 targetScale = new Vector3(ActionsScale.Value, ActionsScale.Value, 1f);
                if (cachedActionsObj.transform.localScale != targetScale)
                    cachedActionsObj.transform.localScale = targetScale;
            }
        }

        // =====================================================================
        //                 SCREEN BOUNDING BOX CALCULATORS
        // =====================================================================
        public static Rect GetGameObjectScreenBounds(GameObject go, Rect fallbackRect)
        {
            if (go == null || !go.activeInHierarchy) return fallbackRect;
            try
            {
                Camera uiCam = NGUITools.FindCameraForLayer(go.layer);
                if (uiCam == null) return fallbackRect;

                Bounds b = NGUIMath.CalculateAbsoluteWidgetBounds(go.transform);
                if (b.size.x < 1f || b.size.y < 1f) return fallbackRect;

                Vector3 minScreen = uiCam.WorldToScreenPoint(b.min);
                Vector3 maxScreen = uiCam.WorldToScreenPoint(b.max);

                float x = Mathf.Min(minScreen.x, maxScreen.x);
                float y = Screen.height - Mathf.Max(minScreen.y, maxScreen.y);
                float w = Mathf.Abs(maxScreen.x - minScreen.x);
                float h = Mathf.Abs(maxScreen.y - minScreen.y);

                if (w >= 30f && h >= 20f && w < Screen.width && h < Screen.height)
                {
                    return new Rect(x - 4f, y - 4f, w + 8f, h + 8f);
                }
            }
            catch { }
            return fallbackRect;
        }

        public static Rect GetMinimapScreenRect()
        {
            if (SuitePlugin.Instance != null)
                return SuitePlugin.Instance.GetMinimapScreenRect();
            float baseSize = MinimapSize != null ? MinimapSize.Value : 180f;
            float scale = MinimapScale != null ? MinimapScale.Value : 1.0f;
            float s = Mathf.Clamp(baseSize * scale, 110f, 340f);
            return new Rect(Screen.width - s - 16f, 14f, s, s);
        }

        public static Rect GetStatsScreenRect()
        {
            float fx = StatsOffsetX.Value * Screen.width;
            float fy = -StatsOffsetY.Value * Screen.height;
            float fw = 265f * StatsScale.Value;
            float fh = 100f * StatsScale.Value;
            return GetGameObjectScreenBounds(cachedStatsObj, new Rect(fx, fy, fw, fh));
        }

        public static Rect GetDiamondsScreenRect()
        {
            float fx = DiamondsOffsetX.Value * Screen.width - (65f * DiamondsScale.Value);
            float fy = -DiamondsOffsetY.Value * Screen.height;
            float fw = 130f * DiamondsScale.Value;
            float fh = 42f * DiamondsScale.Value;
            return GetGameObjectScreenBounds(cachedDiamondObj, new Rect(fx, fy, fw, fh));
        }

        public static Rect GetButtonsScreenRect()
        {
            float fx = MenuButtonsOffsetX.Value * Screen.width - (250f * MenuButtonsScale.Value);
            float fy = -MenuButtonsOffsetY.Value * Screen.height;
            float fw = 250f * MenuButtonsScale.Value;
            float fh = 54f * MenuButtonsScale.Value;
            return GetGameObjectScreenBounds(cachedButtonsObj, new Rect(fx, fy, fw, fh));
        }

        public static Rect GetActionsScreenRect()
        {
            float fx = ActionsOffsetX.Value * Screen.width - (180f * ActionsScale.Value);
            // Fallback when HUD/Actions is not in the current scene (e.g. the main menu):
            // keep the handle visible in the bottom-right instead of y = -150.
            float fy = Screen.height - (150f * ActionsScale.Value) - 16f;
            float fw = 180f * ActionsScale.Value;
            float fh = 150f * ActionsScale.Value;
            return GetGameObjectScreenBounds(cachedActionsObj, new Rect(fx, fy, fw, fh));
        }

        // =====================================================================
        //                 INTERACTIVE HUD DRAG MODE (ALL 6 ELEMENTS)
        // =====================================================================
        public static void DrawHudDragModeGUI(BlockStorySuitePlugin plugin)
        {
            InitStyles();

            // Dark semi-transparent scrim
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), SuitePlugin.GetColorTexture(new Color(0f, 0f, 0f, 0.50f)));

            // Top Instructions Banner
            float bannerW = Mathf.Min(820f, Screen.width - 40f);
            float bannerH = 52f;
            float bannerX = (Screen.width - bannerW) * 0.5f;
            float bannerY = 86f; // below the TopCenter Lens handle
            Rect bannerRect = new Rect(bannerX, bannerY, bannerW, bannerH);
            DrawThemedFrame(bannerRect, null, 1.0f);

            string bannerText = SuitePlugin.Tr(
                "✥ HUD EDIT MODE  •  Left-Click & Drag to move. Scroll Wheel over handle to Scale (0.5x - 1.5x). [Esc/Enter] Done",
                "✥ MODO EDICIÓN HUD  •  Arrastra con Clic Izquierdo. Rueda sobre la caja para Escalar (0.5x - 1.5x). [Esc/Enter] Listo"
            );
            SuitePlugin.DrawShadowLabel(new Rect(bannerX + 16f, bannerY + 6f, bannerW - 130f, 38f), bannerText, bannerTitleStyle, new Color(0.65f, 1.0f, 0.30f), Color.black);

            Rect doneBtnRect = new Rect(bannerX + bannerW - 105f, bannerY + 10f, 92f, 32f);
            if (DrawThemedButton(doneBtnRect, SuitePlugin.Tr("Done", "Listo"), true))
            {
                plugin.SetDraggingHudElements(false);
                plugin.Config.Save();
                plugin.SetModMenuOpen(true);
                return;
            }

            // Retrieve current bounding boxes
            Rect statsBox = GetStatsScreenRect();
            Rect diaBox = GetDiamondsScreenRect();
            Rect btnBox = GetButtonsScreenRect();
            Rect actBox = GetActionsScreenRect();
            Aether.Nano.NanoHudElement lensCard = Aether.Nano.NanoHud.Find(BlockStorySuitePlugin.LensGuid);
            bool lensReady = lensCard != null && LensScale != null && SuitePlugin.LensAnchor != null && SuitePlugin.LensOffsetX != null;
            Rect lensBox = lensReady ? lensCard.GetRect() : Rect.zero;
            bool mapReady = MinimapSize != null && SuitePlugin.MinimapAnchor != null && SuitePlugin.MinimapOffsetX != null && SuitePlugin.MinimapOffsetY != null;
            Rect mapBox = mapReady ? plugin.GetMinimapScreenRect() : Rect.zero;

            Vector2 mousePos = Event.current.mousePosition;

            // Draw Handles
            DrawHandle(statsBox, SuitePlugin.Tr("★ Player Stats (HP/Mana/XP)", "★ Barra de Stats (HP/Mana/XP)"),
                $"{StatsScale.Value:F2}x  ({StatsOffsetX.Value:F2}, {StatsOffsetY.Value:F2})",
                new Color(0.25f, 0.95f, 0.40f), ActiveDragTarget == CustomizerDragTarget.Stats, statsBox.Contains(mousePos));

            DrawHandle(diaBox, SuitePlugin.Tr("◆ Diamonds", "◆ Diamantes"),
                $"{DiamondsScale.Value:F2}x  ({DiamondsOffsetX.Value:F2}, {DiamondsOffsetY.Value:F2})",
                new Color(0.25f, 0.85f, 1.0f), ActiveDragTarget == CustomizerDragTarget.Diamonds, diaBox.Contains(mousePos));

            DrawHandle(btnBox, SuitePlugin.Tr("≡ Menu Buttons", "≡ Botones de Menú"),
                $"{MenuButtonsScale.Value:F2}x  ({MenuButtonsOffsetX.Value:F2}, {MenuButtonsOffsetY.Value:F2})",
                new Color(1.0f, 0.75f, 0.20f), ActiveDragTarget == CustomizerDragTarget.Buttons, btnBox.Contains(mousePos));

            DrawHandle(actBox, SuitePlugin.Tr("✥ Action / Fly Buttons", "✥ Botones Acción / Vuelo"),
                $"{ActionsScale.Value:F2}x  ({ActionsOffsetX.Value:F2}, {ActionsOffsetY.Value:F2})",
                new Color(1.0f, 0.35f, 0.35f), ActiveDragTarget == CustomizerDragTarget.Actions, actBox.Contains(mousePos));

            if (lensReady) DrawHandle(lensBox, SuitePlugin.Tr("◉ Lens WAILA Tooltip", "◉ HUD Lens (WAILA)"),
                $"{LensScale.Value:F2}x  [{SuitePlugin.LensAnchor.Value}] ({SuitePlugin.LensOffsetX.Value:F0}, {SuitePlugin.LensOffsetY.Value:F0})",
                new Color(0.40f, 1.0f, 0.50f), ActiveDragTarget == CustomizerDragTarget.Lens, lensBox.Contains(mousePos));

            if (mapReady) DrawHandle(mapBox, SuitePlugin.Tr("▣ Wayfinder Minimap", "▣ Minimapa Wayfinder"),
                $"{MinimapSize.Value:F0}px [{(MinimapScale != null ? MinimapScale.Value : 1.0f):F2}x] [{SuitePlugin.MinimapAnchor.Value}] ({SuitePlugin.MinimapOffsetX.Value:F0}, {SuitePlugin.MinimapOffsetY.Value:F0})",
                new Color(0.35f, 0.80f, 1.0f), ActiveDragTarget == CustomizerDragTarget.Minimap, mapBox.Contains(mousePos));

            // Mouse Wheel Scaling (0.5x to 1.5x)
            if (Event.current.type == EventType.ScrollWheel)
            {
                float scroll = Event.current.delta.y;
                if (statsBox.Contains(mousePos))
                {
                    StatsScale.Value = Mathf.Clamp(StatsScale.Value - (scroll * 0.05f), 0.5f, 1.5f);
                    ApplyStatsPosition();
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
                else if (diaBox.Contains(mousePos))
                {
                    DiamondsScale.Value = Mathf.Clamp(DiamondsScale.Value - (scroll * 0.05f), 0.5f, 1.5f);
                    ApplyDiamondPosition();
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
                else if (btnBox.Contains(mousePos))
                {
                    MenuButtonsScale.Value = Mathf.Clamp(MenuButtonsScale.Value - (scroll * 0.05f), 0.5f, 1.5f);
                    ApplyButtonsPosition();
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
                else if (actBox.Contains(mousePos))
                {
                    ActionsScale.Value = Mathf.Clamp(ActionsScale.Value - (scroll * 0.05f), 0.5f, 1.5f);
                    ApplyActionsPosition();
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
                else if (lensBox.Contains(mousePos))
                {
                    LensScale.Value = Mathf.Clamp(LensScale.Value - (scroll * 0.05f), 0.5f, 1.5f);
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
                else if (mapBox.Contains(mousePos))
                {
                    MinimapSize.Value = Mathf.Clamp(MinimapSize.Value - (scroll * 6.0f), 110f, 340f);
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
            }

            // Mouse Drag & Movement Handling
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                if (statsBox.Contains(mousePos))
                {
                    ActiveDragTarget = CustomizerDragTarget.Stats;
                    dragStartMousePos = mousePos;
                    dragStartOffsetX = StatsOffsetX.Value;
                    dragStartOffsetY = StatsOffsetY.Value;
                    Event.current.Use();
                }
                else if (diaBox.Contains(mousePos))
                {
                    ActiveDragTarget = CustomizerDragTarget.Diamonds;
                    dragStartMousePos = mousePos;
                    dragStartOffsetX = DiamondsOffsetX.Value;
                    dragStartOffsetY = DiamondsOffsetY.Value;
                    Event.current.Use();
                }
                else if (btnBox.Contains(mousePos))
                {
                    ActiveDragTarget = CustomizerDragTarget.Buttons;
                    dragStartMousePos = mousePos;
                    dragStartOffsetX = MenuButtonsOffsetX.Value;
                    dragStartOffsetY = MenuButtonsOffsetY.Value;
                    Event.current.Use();
                }
                else if (actBox.Contains(mousePos))
                {
                    ActiveDragTarget = CustomizerDragTarget.Actions;
                    dragStartMousePos = mousePos;
                    dragStartOffsetX = ActionsOffsetX.Value;
                    dragStartOffsetY = ActionsOffsetY.Value;
                    Event.current.Use();
                }
                else if (lensBox.Contains(mousePos))
                {
                    ActiveDragTarget = CustomizerDragTarget.Lens;
                    dragStartMousePos = mousePos;
                    dragStartOffsetX = SuitePlugin.LensOffsetX.Value;
                    dragStartOffsetY = SuitePlugin.LensOffsetY.Value;
                    Event.current.Use();
                }
                else if (mapBox.Contains(mousePos))
                {
                    ActiveDragTarget = CustomizerDragTarget.Minimap;
                    dragStartMousePos = mousePos;
                    dragStartOffsetX = SuitePlugin.MinimapOffsetX.Value;
                    dragStartOffsetY = SuitePlugin.MinimapOffsetY.Value;
                    Event.current.Use();
                }
            }
            else if (Event.current.type == EventType.MouseDrag && Event.current.button == 0)
            {
                float dx = mousePos.x - dragStartMousePos.x;
                float dy = mousePos.y - dragStartMousePos.y;

                float normDx = dx / Mathf.Max(1f, (float)Screen.width);
                float normDy = -dy / Mathf.Max(1f, (float)Screen.height);

                if (ActiveDragTarget == CustomizerDragTarget.Stats)
                {
                    StatsOffsetX.Value = Mathf.Clamp(dragStartOffsetX + normDx, -0.2f, 1.2f);
                    StatsOffsetY.Value = Mathf.Clamp(dragStartOffsetY + normDy, -1.2f, 0.2f);
                    ApplyStatsPosition();
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
                else if (ActiveDragTarget == CustomizerDragTarget.Diamonds)
                {
                    DiamondsOffsetX.Value = Mathf.Clamp(dragStartOffsetX + normDx, -0.2f, 1.2f);
                    DiamondsOffsetY.Value = Mathf.Clamp(dragStartOffsetY + normDy, -1.2f, 0.2f);
                    ApplyDiamondPosition();
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
                else if (ActiveDragTarget == CustomizerDragTarget.Buttons)
                {
                    MenuButtonsOffsetX.Value = Mathf.Clamp(dragStartOffsetX + normDx, -0.2f, 1.2f);
                    MenuButtonsOffsetY.Value = Mathf.Clamp(dragStartOffsetY + normDy, -1.2f, 0.2f);
                    ApplyButtonsPosition();
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
                else if (ActiveDragTarget == CustomizerDragTarget.Actions)
                {
                    ActionsOffsetX.Value = Mathf.Clamp(dragStartOffsetX + normDx, -0.2f, 1.2f);
                    ActionsOffsetY.Value = Mathf.Clamp(dragStartOffsetY + normDy, -1.2f, 0.2f);
                    ApplyActionsPosition();
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
                else if (ActiveDragTarget == CustomizerDragTarget.Lens)
                {
                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensOffsetX.Value = Mathf.Clamp(dragStartOffsetX + dx, -600f, 600f);
                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensOffsetY.Value = Mathf.Clamp(dragStartOffsetY + dy, -600f, 600f);
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
                else if (ActiveDragTarget == CustomizerDragTarget.Minimap)
                {
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapOffsetX.Value = Mathf.Clamp(dragStartOffsetX + dx, -600f, 600f);
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapOffsetY.Value = Mathf.Clamp(dragStartOffsetY + dy, -600f, 600f);
                    SuitePlugin.HudLayoutPreset.Value = "Custom";
                    Event.current.Use();
                }
            }
            else if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                if (ActiveDragTarget != CustomizerDragTarget.None)
                {
                    ActiveDragTarget = CustomizerDragTarget.None;
                    plugin.Config.Save();
                    Event.current.Use();
                }
            }
        }

        private static void DrawHandle(Rect box, string title, string detail, Color accent, bool isSelected, bool isHovered)
        {
            Color borderCol = isSelected ? Color.white : (isHovered ? Color.Lerp(accent, Color.white, 0.5f) : accent);
            Color bgCol = isSelected
                ? new Color(accent.r * 0.35f, accent.g * 0.35f, accent.b * 0.35f, 0.88f)
                : (isHovered ? new Color(0.10f, 0.14f, 0.20f, 0.85f) : new Color(0.06f, 0.08f, 0.12f, 0.80f));

            GUI.DrawTexture(new Rect(box.x - 2, box.y - 2, box.width + 4, box.height + 4), SuitePlugin.GetColorTexture(borderCol));
            GUI.DrawTexture(box, SuitePlugin.GetColorTexture(bgCol));
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 2), SuitePlugin.GetColorTexture(new Color(accent.r, accent.g, accent.b, 0.90f)));

            string label = $"{title}\n{detail}";
            SuitePlugin.DrawShadowLabel(box, label, handleStyle, Color.white, Color.black);
        }

        private static void InitStyles()
        {
            if (handleStyle == null)
            {
                handleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
            }
            if (bannerTitleStyle == null)
            {
                bannerTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold
                };
            }
        }

        // =====================================================================
        //                 PRESET MANAGEMENT
        // =====================================================================
        public static void ApplyHudPreset(string preset)
        {
            SuitePlugin.HudLayoutPreset.Value = preset;

            switch (preset)
            {
                case "ModernModpack":
                    StatsOffsetX.Value = 0.015f;
                    StatsOffsetY.Value = -0.015f;
                    StatsScale.Value = 1.0f;

                    DiamondsOffsetX.Value = 0.055f;
                    DiamondsOffsetY.Value = -0.142f;
                    DiamondsScale.Value = 0.85f;

                    MenuButtonsOffsetX.Value = 1.0f;
                    MenuButtonsOffsetY.Value = -0.75f;
                    MenuButtonsScale.Value = 0.90f;

                    ActionsOffsetX.Value = 1.0f;
                    ActionsOffsetY.Value = -1.0f;
                    ActionsScale.Value = 1.0f;

                    SuitePlugin.MoveDiamondsUnderXP.Value = true;
                    SuitePlugin.MoveTopRightButtonsToBottomRight.Value = true;
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapAnchor.Value = "TopRight";
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapOffsetX.Value = 0f;
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapOffsetY.Value = 0f;
                    if (MinimapSize != null) MinimapSize.Value = 180f;

                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensAnchor.Value = "TopCenter";
                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensOffsetX.Value = 0f;
                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensOffsetY.Value = 0f;
                    if (LensScale != null) LensScale.Value = 1.0f;
                    break;

                case "CompactLeft":
                    StatsOffsetX.Value = 0.015f;
                    StatsOffsetY.Value = -0.015f;
                    StatsScale.Value = 1.0f;

                    DiamondsOffsetX.Value = 0.055f;
                    DiamondsOffsetY.Value = -0.142f;
                    DiamondsScale.Value = 0.85f;

                    MenuButtonsOffsetX.Value = 1.0f;
                    MenuButtonsOffsetY.Value = 0.0f;
                    MenuButtonsScale.Value = 1.0f;

                    ActionsOffsetX.Value = 1.0f;
                    ActionsOffsetY.Value = -1.0f;
                    ActionsScale.Value = 1.0f;

                    SuitePlugin.MoveDiamondsUnderXP.Value = true;
                    SuitePlugin.MoveTopRightButtonsToBottomRight.Value = false;
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapAnchor.Value = "BottomLeft";
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapOffsetX.Value = 0f;
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapOffsetY.Value = 0f;
                    if (MinimapSize != null) MinimapSize.Value = 160f;

                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensAnchor.Value = "TopCenter";
                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensOffsetX.Value = 0f;
                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensOffsetY.Value = 0f;
                    if (LensScale != null) LensScale.Value = 1.0f;
                    break;

                case "BottomHUD":
                    StatsOffsetX.Value = 0.015f;
                    StatsOffsetY.Value = -0.85f;
                    StatsScale.Value = 1.0f;

                    DiamondsOffsetX.Value = 0.015f;
                    DiamondsOffsetY.Value = -0.75f;
                    DiamondsScale.Value = 0.85f;

                    MenuButtonsOffsetX.Value = 1.0f;
                    MenuButtonsOffsetY.Value = 0.0f;
                    MenuButtonsScale.Value = 1.0f;

                    ActionsOffsetX.Value = 1.0f;
                    ActionsOffsetY.Value = -1.0f;
                    ActionsScale.Value = 1.0f;

                    SuitePlugin.MoveDiamondsUnderXP.Value = true;
                    SuitePlugin.MoveTopRightButtonsToBottomRight.Value = false;
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapAnchor.Value = "BottomRight";
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapOffsetX.Value = -180f;
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapOffsetY.Value = 0f;
                    if (MinimapSize != null) MinimapSize.Value = 160f;

                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensAnchor.Value = "BottomCenter";
                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensOffsetX.Value = 0f;
                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensOffsetY.Value = -90f;
                    if (LensScale != null) LensScale.Value = 1.0f;
                    break;

                case "VanillaClassic":
                default:
                    StatsOffsetX.Value = 0.015f;
                    StatsOffsetY.Value = -0.015f;
                    StatsScale.Value = 1.0f;

                    DiamondsOffsetX.Value = 0.50f;
                    DiamondsOffsetY.Value = -0.02f;
                    DiamondsScale.Value = 1.0f;

                    MenuButtonsOffsetX.Value = 1.0f;
                    MenuButtonsOffsetY.Value = 0.0f;
                    MenuButtonsScale.Value = 1.0f;

                    ActionsOffsetX.Value = 1.0f;
                    ActionsOffsetY.Value = -1.0f;
                    ActionsScale.Value = 1.0f;

                    SuitePlugin.MoveDiamondsUnderXP.Value = false;
                    SuitePlugin.MoveTopRightButtonsToBottomRight.Value = false;
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapAnchor.Value = "TopRight";
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapOffsetX.Value = 0f;
                    if (SuitePlugin.MinimapAnchor != null) SuitePlugin.MinimapOffsetY.Value = 0f;
                    if (MinimapSize != null) MinimapSize.Value = 160f;

                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensAnchor.Value = "TopCenter";
                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensOffsetX.Value = 0f;
                    if (SuitePlugin.LensAnchor != null) SuitePlugin.LensOffsetY.Value = 0f;
                    if (LensScale != null) LensScale.Value = 1.0f;
                    break;
            }

            if (preset == "VanillaClassic")
                RestoreVanillaHud();
            else
                ApplyVanillaHudLayout(true);
            SuitePlugin.Instance?.Config.Save();
        }

        public static void ResetAllHudToDefaults()
        {
            ApplyHudPreset("ModernModpack");
            UiDesignTheme.Value = "NativeWood";
            if (SuitePlugin.LensAnchor != null) SuitePlugin.LensTheme.Value = "NativeWood";
            SuitePlugin.MinimapTheme.Value = "NativeWood";
            SuitePlugin.Instance?.Config.Save();
        }
    }
}
