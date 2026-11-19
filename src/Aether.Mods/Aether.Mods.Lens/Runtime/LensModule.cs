#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using Aether.Nano;
using BepInEx.Configuration;
using Blocksters.MathLib;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using com.mindblocks.i18n;
using UnityEngine;
using static Aether.Nano.Nano;
using static Aether.Nano.NanoUi;

namespace Aether.Mods.Lens
{
    /// <summary>
    /// Lens WAILA: block/creature inspection, real atlas cube, best-tool badge, native hearts and
    /// mob quest/level options. Owns its [Lens] config; the shell only forwards drawing/update.
    /// </summary>
    public sealed partial class LensModule
    {
        public static LensModule Current { get; private set; }
        /// <summary>First id of blocks added by mods (Aether's block registry starts there).</summary>
        private const ushort CustomBlockIdStart = 500;
        public string Id => "aether_lens";
        public bool IsInitialized { get; private set; }

        private static readonly RaycastHit[] lensHitBuffer = new RaycastHit[16];

        public static ConfigEntry<bool> LensEnabled { get; private set; }
        public static ConfigEntry<KeyCode> LensToggleKey { get; private set; }
        public static ConfigEntry<float> LensBgOpacity { get; private set; }
        public static ConfigEntry<int> LensCardWidth { get; private set; }
        public static ConfigEntry<string> LensAnchor { get; private set; } // "TopCenter", "TopLeft", "BottomCenter", "AboveCrosshair"
        public static ConfigEntry<float> LensOffsetX { get; private set; }
        public static ConfigEntry<float> LensOffsetY { get; private set; }
        public static ConfigEntry<string> LensTheme { get; private set; } // "Aether", "NativeWood", "DarkObsidian", "MinimalistGlass", "RoyalGold", "CompactBar"
        public static ConfigEntry<bool> LensShowCoords { get; private set; }
        public static ConfigEntry<bool> Lens3DBlockIcon { get; private set; }
        public static ConfigEntry<bool> LensReduceMotion { get; private set; }
        public static ConfigEntry<bool> LensHideCrosshairDigProgress { get; private set; }
        public static ConfigEntry<string> LensMobHealthStyle { get; private set; }
        public static ConfigEntry<bool> LensShowMobLevel { get; private set; }
        public static ConfigEntry<bool> LensShowMobQuests { get; private set; }
        public static ConfigEntry<bool> LensHideNativeMobHealth { get; private set; }
        private static bool lastHideNativeHp;
        private static float lastNativeHpScan;

        // Fluid motion, dynamic width, and block destruction progress state (zero per-frame allocations)
        private enum TargetSnapshotKind { None, Block, Entity }
        private static TargetSnapshotKind lastSnapshotKind;
        private static Vector3i lastSnapshotPos;
        private static Block lastSnapshotBlock;
        private static Digger lastSnapshotDigger;
        private static HealthBase lastSnapshotEntity;
        private static long lastTargetSignature;
        private static float lastTargetSeenTime = -10f;
        private static float cardVisibility;
        private static float activeDrawAlpha = 1f;
        private static float activeSettleY;
        private static float animatedCardWidth;
        private static float digProgressVisual;
        private static Cooldown suppressedDigIndicator;
        private static bool digIndicatorHidden;
        private static int lastMotionFrame = -1;
        private static readonly GUIContent MeasureContent = new GUIContent();

        private struct LensCardMaterial
        {
            public Color BgTop;
            public Color BgBottom;
            public Color Border;
            public Color TopRule;
            public Color Specular;
            public Color SlotBg;
            public Color SlotBorder;
            public Color TextPrimary;
            public Color TextMuted;
            public float Radius;
            public float WashAlpha;
            public bool UseNativeSerif;
        }

        private static GUIStyle lensNameBrand;
        private static GUIStyle lensCompactNameBrand;
        private static GUIStyle lensSubBrand;
        private static GUIStyle lensMonoSmall;
        private static GUIStyle lensMonoRight;
        private static GUIStyle lensChipStyle;
        private static GUIStyle lensBarTextStyle;

        public void Initialize()
        {
            if (IsInitialized) return;
            Current = this;
            LensEnabled = Config.Bind("Lens", "Enabled", true, "Enable or disable Lens HUD.");
            LensToggleKey = Config.Bind("Lens", "ToggleKey", KeyCode.F4, "Hotkey to toggle Lens HUD.");
            LensBgOpacity = Config.Bind("Lens", "BgOpacity", 0.94f, new ConfigDescription("HUD background opacity.", new AcceptableValueRange<float>(0.3f, 1f)));
            LensCardWidth = Config.Bind("Lens", "CardWidth", 248, new ConfigDescription("Minimum Lens card width in UI pixels; expands dynamically for long names.", new AcceptableValueRange<int>(200, 420)));
            LensAnchor = Config.Bind("Lens", "Anchor", "TopCenter", "Lens screen anchor: TopCenter, TopLeft, BottomCenter, AboveCrosshair.");
            LensOffsetX = Config.Bind("Lens", "OffsetX", 0f, new ConfigDescription("Horizontal pixel offset.", new AcceptableValueRange<float>(-500f, 500f)));
            LensOffsetY = Config.Bind("Lens", "OffsetY", 0f, new ConfigDescription("Vertical pixel offset.", new AcceptableValueRange<float>(-500f, 500f)));
            LensTheme = Config.Bind("Lens", "Theme", "Aether", "Visual design theme: Aether, NativeWood, DarkObsidian, MinimalistGlass, RoyalGold, CompactBar.");
            LensShowCoords = Config.Bind("Lens", "ShowCoordinates", false, "Show target block coordinates in Lens HUD.");
            Lens3DBlockIcon = Config.Bind("Lens", "3DBlockIcon", true, "Render placeable blocks as 3D isometric cubes in Lens WAILA HUD.");
            LensReduceMotion = Config.Bind("Lens", "ReduceMotion", false, "Disable reveal and target transition animations in Lens HUD.");
            LensHideCrosshairDigProgress = Config.Bind("Lens", "HideCrosshairDigProgress", false, "Hide Block Story's native crosshair mining indicator while Lens shows the break progress bar.");
            LensMobHealthStyle = Config.Bind("Lens", "MobHealthStyle", "Hearts", new ConfigDescription("Mob health display in Lens: Hearts, Bar, or None.", new AcceptableValueList<string>("Hearts", "Bar", "None")));
            LensShowMobLevel = Config.Bind("Lens", "ShowMobLevel", true, "Show the mob level in the Lens card.");
            LensShowMobQuests = Config.Bind("Lens", "ShowMobQuests", true, "Show whether a quest is available for quest NPCs in the Lens card.");
            LensHideNativeMobHealth = Config.Bind("Lens", "HideNativeMobHealth", false, "Hide Block Story's native overhead mob health bars while Lens is showing the mob card.");
            BindScale();
            IsInitialized = true;
            Log?.LogInfo("[Lens] WAILA module initialized (own assembly, per-mod config section).");
        }

        public void OnUpdate()
        {
            UpdateLensNativeMobHealthBars();
            UpdateCrosshairDigIndicator();
        }
        public void OnLateUpdate() { }
        public void OnSceneChanged(string sceneName)
        {
            RestoreCrosshairDigIndicator();
            nativeHeartProbed = false;
            lastSnapshotKind = TargetSnapshotKind.None;
            lastSnapshotEntity = null;
            lastHealthAnimTarget = null;
            cardVisibility = 0f;
            animatedCardWidth = 0f;
            digProgressVisual = 0f;
            lastTargetSignature = 0L;
        }
        public bool IsModalOpen => false;
        public void ForceClose() { }

        public void DrawHud() => DrawLensHUD();

        // GetName/GetDisplayName only exist on Health; pets and mod mobs may offer them under the same names.
        private static readonly Dictionary<Type, System.Reflection.MethodInfo[]> nameMethodCache =
            new Dictionary<Type, System.Reflection.MethodInfo[]>();

        private static string FromNameMethods(HealthBase health, params string[] methodNames)
        {
            Type t = health.GetType();
            if (!nameMethodCache.TryGetValue(t, out var methods))
            {
                methods = new System.Reflection.MethodInfo[methodNames.Length];
                for (int i = 0; i < methodNames.Length; i++)
                    methods[i] = t.GetMethod(methodNames[i], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null);
                nameMethodCache[t] = methods;
            }
            foreach (var m in methods)
            {
                if (m == null || m.ReturnType != typeof(string)) continue;
                try { string v = m.Invoke(health, null) as string; if (!string.IsNullOrEmpty(v)) return v; } catch { }
            }
            return null;
        }

        /// <summary>Mod mobs often expose their level as an int property "Level".</summary>
        private static int ModMobLevel(HealthBase health)
        {
            try
            {
                var p = health.GetType().GetProperty("Level", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(int)) return (int)p.GetValue(health);
            }
            catch { }
            return 0;
        }

        private static string RawMobName(HealthBase health) =>
            FromNameMethods(health, "GetName") ?? health.gameObject.name.Replace("(Clone)", "").Trim();

        private static string GetLocalizedMobName(HealthBase health)
        {
            if (health == null) return "Entity";
            try
            {
                string display = FromNameMethods(health, "GetDisplayName");
                if (!string.IsNullOrEmpty(display))
                    return display;

                string raw = RawMobName(health);
                string translated = T.Mobs(raw);
                if (!string.IsNullOrEmpty(translated))
                    return translated;

                return raw;
            }
            catch
            {
                return health.gameObject.name.Replace("(Clone)", "").Trim();
            }
        }


        private static void EnsureLensStyles()
        {
            if (lensNameBrand != null) return;
            lensNameBrand = NanoBrand.Style(NanoBrand.BodySemi, 15, NanoBrand.Paper, TextAnchor.MiddleLeft);
            lensCompactNameBrand = NanoBrand.Style(NanoBrand.BodySemi, 13, NanoBrand.Paper, TextAnchor.MiddleLeft);
            lensSubBrand = NanoBrand.Style(NanoBrand.Body, 12, NanoBrand.Paper, TextAnchor.MiddleLeft);
            lensMonoSmall = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Mute, TextAnchor.MiddleLeft);
            lensMonoRight = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Mute, TextAnchor.MiddleRight);
            lensChipStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Paper, TextAnchor.MiddleCenter);
            lensBarTextStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.PaperLight, TextAnchor.MiddleCenter);
        }

        private static LensCardMaterial ResolveCardMaterial(string theme)
        {
            switch (theme)
            {
                case "NativeWood":
                    return new LensCardMaterial
                    {
                        BgTop = new Color(0.145f, 0.095f, 0.060f, 0.94f),
                        BgBottom = new Color(0.082f, 0.052f, 0.032f, 0.96f),
                        Border = new Color(0.38f, 0.27f, 0.16f, 0.95f),
                        TopRule = new Color(0.89f, 0.66f, 0.29f, 1.0f),
                        Specular = new Color(0.98f, 0.91f, 0.78f, 0.13f),
                        SlotBg = new Color(0.060f, 0.038f, 0.022f, 0.88f),
                        SlotBorder = new Color(0.34f, 0.24f, 0.14f, 0.72f),
                        TextPrimary = new Color(0.95f, 0.91f, 0.84f, 1.0f),
                        TextMuted = new Color(0.73f, 0.65f, 0.55f, 1.0f),
                        Radius = 9f,
                        WashAlpha = 0.08f,
                        UseNativeSerif = true
                    };

                case "DarkObsidian":
                    return new LensCardMaterial
                    {
                        BgTop = new Color(0.088f, 0.072f, 0.125f, 0.94f),
                        BgBottom = new Color(0.044f, 0.036f, 0.068f, 0.96f),
                        Border = new Color(0.25f, 0.20f, 0.34f, 0.95f),
                        TopRule = new Color(0.62f, 0.51f, 0.77f, 1.0f),
                        Specular = new Color(0.86f, 0.82f, 0.96f, 0.11f),
                        SlotBg = new Color(0.035f, 0.028f, 0.055f, 0.90f),
                        SlotBorder = new Color(0.24f, 0.19f, 0.33f, 0.72f),
                        TextPrimary = new Color(0.93f, 0.91f, 0.96f, 1.0f),
                        TextMuted = new Color(0.64f, 0.60f, 0.72f, 1.0f),
                        Radius = 11f,
                        WashAlpha = 0.10f,
                        UseNativeSerif = false
                    };

                case "MinimalistGlass":
                    return new LensCardMaterial
                    {
                        BgTop = new Color(0.092f, 0.125f, 0.155f, 0.78f),
                        BgBottom = new Color(0.045f, 0.068f, 0.088f, 0.74f),
                        Border = new Color(0.50f, 0.61f, 0.69f, 0.52f),
                        TopRule = new Color(0.62f, 0.78f, 0.85f, 0.95f),
                        Specular = new Color(0.92f, 0.97f, 1.0f, 0.20f),
                        SlotBg = new Color(0.032f, 0.052f, 0.068f, 0.68f),
                        SlotBorder = new Color(0.46f, 0.57f, 0.65f, 0.45f),
                        TextPrimary = new Color(0.93f, 0.96f, 0.98f, 1.0f),
                        TextMuted = new Color(0.66f, 0.75f, 0.81f, 1.0f),
                        Radius = 12f,
                        WashAlpha = 0.08f,
                        UseNativeSerif = false
                    };

                case "RoyalGold":
                    return new LensCardMaterial
                    {
                        BgTop = new Color(0.115f, 0.084f, 0.042f, 0.94f),
                        BgBottom = new Color(0.058f, 0.042f, 0.020f, 0.96f),
                        Border = new Color(0.45f, 0.34f, 0.15f, 0.95f),
                        TopRule = new Color(0.84f, 0.61f, 0.23f, 1.0f),
                        Specular = new Color(1.0f, 0.90f, 0.66f, 0.17f),
                        SlotBg = new Color(0.044f, 0.032f, 0.015f, 0.90f),
                        SlotBorder = new Color(0.42f, 0.31f, 0.13f, 0.72f),
                        TextPrimary = new Color(0.96f, 0.92f, 0.84f, 1.0f),
                        TextMuted = new Color(0.75f, 0.66f, 0.50f, 1.0f),
                        Radius = 10f,
                        WashAlpha = 0.11f,
                        UseNativeSerif = false
                    };

                case "CompactBar":
                    return new LensCardMaterial
                    {
                        BgTop = new Color(0.090f, 0.072f, 0.056f, 0.94f),
                        BgBottom = new Color(0.047f, 0.039f, 0.031f, 0.96f),
                        Border = new Color(0.19f, 0.15f, 0.11f, 0.95f),
                        TopRule = NanoBrand.Ember,
                        Specular = new Color(0.96f, 0.90f, 0.82f, 0.11f),
                        SlotBg = new Color(0.035f, 0.028f, 0.022f, 0.90f),
                        SlotBorder = NanoBrand.Line,
                        TextPrimary = NanoBrand.Paper,
                        TextMuted = NanoBrand.Mute,
                        Radius = 9f,
                        WashAlpha = 0.09f,
                        UseNativeSerif = false
                    };

                case "Aether":
                default:
                    return new LensCardMaterial
                    {
                        BgTop = new Color(0.094f, 0.074f, 0.058f, 0.94f),
                        BgBottom = new Color(0.047f, 0.039f, 0.031f, 0.96f),
                        Border = new Color(0.19f, 0.15f, 0.11f, 0.96f),
                        TopRule = NanoBrand.Ember,
                        Specular = new Color(0.98f, 0.93f, 0.85f, 0.11f),
                        SlotBg = new Color(0.035f, 0.028f, 0.022f, 0.90f),
                        SlotBorder = NanoBrand.Line,
                        TextPrimary = NanoBrand.Paper,
                        TextMuted = NanoBrand.Mute,
                        Radius = 11f,
                        WashAlpha = 0.11f,
                        UseNativeSerif = false
                    };
            }
        }

        private static bool IsMotionReduced() => LensReduceMotion != null && LensReduceMotion.Value;

        private static float ComputeSettleShift(float eased)
        {
            if (IsMotionReduced()) return 0f;
            float remaining = 1f - Mathf.Clamp01(eased);
            bool fromBottom = string.Equals(LensAnchor?.Value, "BottomCenter", StringComparison.OrdinalIgnoreCase);
            return remaining * (fromBottom ? -4f : 4f);
        }

        private static void AdvanceMotion(bool hasTarget, long signature)
        {
            // Run at most once per Unity frame so multiple OnGUI Repaint passes never double-step or flicker.
            int frame = Time.frameCount;
            if (frame == lastMotionFrame) return;
            lastMotionFrame = frame;

            float now = Time.unscaledTime;
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.05f);
            bool reduced = IsMotionReduced();

            if (hasTarget)
            {
                if (signature != lastTargetSignature)
                {
                    digProgressVisual = 0f;
                    lastTargetSignature = signature;
                }
                lastTargetSeenTime = now;
                cardVisibility = reduced ? 1f : Mathf.MoveTowards(cardVisibility, 1f, dt / 0.15f);
            }
            else
            {
                bool inGrace = !reduced && (now - lastTargetSeenTime) < 0.14f;
                if (!inGrace)
                {
                    cardVisibility = reduced ? 0f : Mathf.MoveTowards(cardVisibility, 0f, dt / 0.12f);
                    digProgressVisual = 0f;
                }
            }

            if (cardVisibility <= 0.005f)
                animatedCardWidth = 0f;

            float inv = 1f - Mathf.Clamp01(cardVisibility);
            activeDrawAlpha = reduced ? (hasTarget ? 1f : 0f) : (1f - inv * inv * inv);
            activeSettleY = ComputeSettleShift(activeDrawAlpha);
        }

        private static float MeasureTextWidth(GUIStyle style, string text)
        {
            if (style == null || string.IsNullOrEmpty(text)) return 0f;
            MeasureContent.text = text;
            return style.CalcSize(MeasureContent).x;
        }

        private static float MeasureEntityCardWidth(
            string theme,
            string name,
            int mobLevel,
            float curHp,
            float maxHp,
            string healthStyle,
            string originName,
            bool isCustomMod,
            bool showQuestBadge,
            bool hasAvailableQuest)
        {
            EnsureLensStyles();
            LensCardMaterial mat = ResolveCardMaterial(theme);
            bool isCompact = theme == "CompactBar";
            string lvlPrefix = Tr("LV", "NV");
            bool hasLevel = mobLevel > 0;
            float chipW = hasLevel ? Mathf.Max(isCompact ? 36f : 40f, MeasureTextWidth(lensChipStyle, $"{lvlPrefix} {mobLevel}") + (isCompact ? 10f : 12f)) : 0f;

            if (isCompact)
            {
                GUIStyle compactSt = mat.UseNativeSerif ? TitleStyle : lensCompactNameBrand;
                float nameW = MeasureTextWidth(compactSt, name) + 6f;
                bool showBar = !string.Equals(healthStyle, "None", StringComparison.OrdinalIgnoreCase);
                float barPart = 0f;
                if (showBar)
                {
                    float hpW = Mathf.Clamp(MeasureTextWidth(lensMonoRight, $"{curHp:F0}/{maxHp:F0}") + 4f, 40f, 74f);
                    barPart = hpW + 8f + 70f + 10f;
                }
                return 22f + nameW + (hasLevel ? chipW + 8f : 0f) + barPart;
            }

            GUIStyle titleSt = mat.UseNativeSerif ? TitleStyle : lensNameBrand;
            float row1 = MeasureTextWidth(titleSt, name) + 6f + (hasLevel ? chipW + 10f : 0f);
            float row2 = string.Equals(healthStyle, "Hearts", StringComparison.OrdinalIgnoreCase)
                ? (10f * 14f + 10f + Mathf.Max(44f, MeasureTextWidth(lensMonoRight, $"{curHp:F0}/{maxHp:F0}") + 4f))
                : (string.Equals(healthStyle, "Bar", StringComparison.OrdinalIgnoreCase) ? 188f : 0f);

            float originW = (isCustomMod ? 11f : 0f) + MeasureTextWidth(lensMonoSmall, originName) + 4f;
            float questW = 0f;
            if (showQuestBadge)
            {
                string qt = hasAvailableQuest ? Tr("Quest available", "Misión disponible") : Tr("No quest now", "Sin misión");
                questW = 16f + 11f + MeasureTextWidth(lensMonoRight, qt) + 4f;
            }
            float row3 = originW + questW;
            return 28f + Mathf.Max(row1, Mathf.Max(row2, row3));
        }

        private static float MeasureBlockCardWidth(
            string theme,
            string locName,
            string toolLine,
            bool hasToolCapsule,
            string originName,
            bool isCustomMod,
            bool showCoords,
            int bx, int by, int bz)
        {
            EnsureLensStyles();
            LensCardMaterial mat = ResolveCardMaterial(theme);
            bool isCompact = theme == "CompactBar";

            if (isCompact)
            {
                GUIStyle compactSt = mat.UseNativeSerif ? TitleStyle : lensCompactNameBrand;
                float left = 8f + 28f + 9f;
                float right = hasToolCapsule ? (10f + 24f + 9f) : 11f;
                float nameW = MeasureTextWidth(compactSt, locName) + 6f;
                float coordW = showCoords ? (Mathf.Min(86f, MeasureTextWidth(lensMonoRight, $"{bx},{by},{bz}") + 6f) + 10f) : 0f;
                return left + nameW + coordW + right;
            }

            GUIStyle nameSt = mat.UseNativeSerif ? TitleStyle : lensNameBrand;
            float leftFixed = 11f + 52f + 10f;
            float rightFixed = hasToolCapsule ? (10f + 42f + 11f) : 12f;
            float row1 = MeasureTextWidth(nameSt, locName) + 6f;
            float row2 = 11f + MeasureTextWidth(lensSubBrand, toolLine) + 4f;
            float originW = (isCustomMod ? 11f : 0f) + MeasureTextWidth(lensMonoSmall, originName) + 4f;
            float coordPart = showCoords ? (Mathf.Min(96f, MeasureTextWidth(lensMonoRight, $"{bx}, {by}, {bz}") + 4f) + 12f) : 0f;
            float row3 = originW + coordPart;
            return leftFixed + Mathf.Max(row1, Mathf.Max(row2, row3)) + rightFixed;
        }

        private static string FormatToolLine(HarvestTool bestTool, string toolNeeded, bool showToolCheck, bool usingBestTool)
        {
            if (showToolCheck)
                return usingBestTool ? toolNeeded : Tr($"Needs {toolNeeded}", $"Requiere {toolNeeded}");
            return toolNeeded;
        }

        private static float ResolveAnimatedCardWidth(float desiredWidth, bool isCompact)
        {
            float baseMin = GetLensCardWidth(isCompact);
            float scale = GetLensScale();
            float maxOnScreen = Mathf.Max(baseMin, (Screen.width - 28f) / Mathf.Max(0.5f, scale));
            float maxCap = Mathf.Min(Mathf.Max(baseMin + 180f, 460f), maxOnScreen);
            float target = Mathf.Clamp(Mathf.Ceil(desiredWidth), baseMin, maxCap);

            if (IsMotionReduced() || animatedCardWidth <= 1f || cardVisibility < 0.08f)
            {
                animatedCardWidth = target;
            }
            else
            {
                float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.05f);
                animatedCardWidth = Mathf.Lerp(animatedCardWidth, target, 1f - Mathf.Exp(-dt * 18f));
                if (Mathf.Abs(animatedCardWidth - target) < 0.5f) animatedCardWidth = target;
            }

            return animatedCardWidth;
        }

        private static float GetActiveDigProgress(Digger heldDigger, out Cooldown activeIndicator)
        {
            activeIndicator = null;
            float best = 0f;

            if (heldDigger != null && heldDigger.isActiveAndEnabled && heldDigger.percentage > 0.001f)
            {
                best = heldDigger.percentage;
                activeIndicator = heldDigger.indicator;
            }

            Digger fallback = CachedDigger;
            if (fallback != null && fallback != heldDigger && fallback.isActiveAndEnabled && fallback.percentage > best)
            {
                best = fallback.percentage;
                if (fallback.indicator != null) activeIndicator = fallback.indicator;
            }

            return Mathf.Clamp01(best);
        }

        private static float SmoothDigProgress(float raw)
        {
            if (raw <= 0.002f)
            {
                digProgressVisual = 0f;
                return 0f;
            }
            if (IsMotionReduced() || raw < digProgressVisual)
            {
                digProgressVisual = raw;
                return raw;
            }
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.05f);
            digProgressVisual = Mathf.Lerp(digProgressVisual, raw, 1f - Mathf.Exp(-dt * 28f));
            return Mathf.Clamp01(digProgressVisual);
        }

        private static void UpdateCrosshairDigIndicator()
        {
            bool shouldHide = LensEnabled != null && LensEnabled.Value
                && LensHideCrosshairDigProgress != null && LensHideCrosshairDigProgress.Value;

            if (!shouldHide)
            {
                if (digIndicatorHidden) RestoreCrosshairDigIndicator();
                return;
            }

            Digger held = GetHeldDigger();
            float progress = GetActiveDigProgress(held, out Cooldown ind);
            if (progress > 0.001f && ind != null)
            {
                try
                {
                    if (suppressedDigIndicator != null && suppressedDigIndicator != ind)
                        RestoreCrosshairDigIndicator();

                    ind.StopAllCoroutines();
                    Color zero = ind.color;
                    zero.a = 0f;
                    if (ind.sprite != null)
                    {
                        ind.sprite.color = zero;
                        ind.sprite.fillAmount = 0f;
                        ind.sprite.enabled = false;
                    }
                    if (ind.icon != null)
                    {
                        ind.icon.color = zero;
                        ind.icon.enabled = false;
                    }
                    suppressedDigIndicator = ind;
                    digIndicatorHidden = true;
                }
                catch { }
            }
            else if (digIndicatorHidden)
            {
                RestoreCrosshairDigIndicator();
            }
        }

        private static void RestoreCrosshairDigIndicator()
        {
            if (suppressedDigIndicator != null)
            {
                try
                {
                    if (suppressedDigIndicator.sprite != null) suppressedDigIndicator.sprite.enabled = true;
                    if (suppressedDigIndicator.icon != null) suppressedDigIndicator.icon.enabled = true;
                }
                catch { }
            }
            suppressedDigIndicator = null;
            digIndicatorHidden = false;
        }

        private void DrawLensHUD()
        {
            // Pure drawing: skip the Layout pass so the physics + voxel raycasts run once per frame
            // instead of twice (Layout and Repaint).
            if (Event.current != null && Event.current.type != EventType.Repaint) return;

            Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
            if (cam == null) return;

            Vector3 origin = cam.transform.position;
            Vector3 direction = cam.transform.forward;

            // 1. Pointed Entity Raycast
            Ray ray = new Ray(origin, direction);
            // Collide (not Ignore): mod-made mobs often use trigger colliders as their hitbox. Oversized triggers
            // (aggro ranges) are filtered by EntityHitRules.
            int hitCount = Physics.RaycastNonAlloc(ray, lensHitBuffer, 18f, ~0, QueryTriggerInteraction.Collide);
            if (hitCount > 0)
            {
                float closestDist = float.MaxValue;
                HealthBase closestHealth = null;

                for (int i = 0; i < hitCount; i++)
                {
                    var hHit = lensHitBuffer[i];
                    if (hHit.collider == null) continue;
                    if (!NanoWorld.AcceptsEntityHit(hHit.collider))
                        continue;

                    // HealthBase is the common base of native mobs (Health), pets (PetHealth) and mod mobs
                    // alike; looking only for Health made every non-native mob invisible.
                    HealthBase health = hHit.collider.GetComponentInParent<HealthBase>();
                    if (health != null && !health.IsDead())
                    {
                        if (TerrainLoader.instance != null && TerrainLoader.instance.player != null &&
                            health.transform == TerrainLoader.instance.player)
                            continue;
                        if (health is PlayerHealth) continue;
                        if (hHit.distance < closestDist && hHit.distance > 0.4f)
                        {
                            closestDist = hHit.distance;
                            closestHealth = health;
                        }
                    }
                }

                if (closestHealth != null)
                {
                    long sig = (1L << 62) | (uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(closestHealth);
                    AdvanceMotion(true, sig);
                    lastSnapshotKind = TargetSnapshotKind.Entity;
                    lastSnapshotEntity = closestHealth;
                    DrawEntityHUD(closestHealth);
                    return;
                }
            }

            // 2. Deterministic Voxel Raycast using RayTracer.trace
            IWorld world = GetActiveWorld();
            if (world != null)
            {
                float reach = (CachedDigger != null && CachedDigger.reach > 0) ? CachedDigger.reach : 6.0f;
                float reachSq = reach * reach;

                foreach (RayTracer.Hit voxelHit in RayTracer.trace(origin, direction))
                {
                    float dx = (voxelHit.x + 0.5f) - origin.x;
                    float dy = (voxelHit.y + 0.5f) - origin.y;
                    float dz = (voxelHit.z + 0.5f) - origin.z;
                    float distSq = dx * dx + dy * dy + dz * dz;

                    if (distSq > reachSq)
                        break;

                    Block block;
                    try
                    {
                        block = NanoWorld.GetBlockSafe(world, voxelHit.x, voxelHit.y, voxelHit.z);
                    }
                    catch
                    {
                        break;
                    }

                    BlockType bType = block.type;
                    if (block.id == 0 || bType == null || bType.air)
                        continue;

                    if (bType.liquid || !bType.visible)
                        continue;

                    Vector3i targetPos = new Vector3i(voxelHit.x, voxelHit.y, voxelHit.z);
                    long sig = ((long)block.id << 40) ^ ((long)voxelHit.x * 73856093L) ^ ((long)voxelHit.y * 19349663L) ^ ((long)voxelHit.z * 83492791L);
                    AdvanceMotion(true, sig);
                    lastSnapshotKind = TargetSnapshotKind.Block;
                    lastSnapshotPos = targetPos;
                    lastSnapshotBlock = block;
                    lastSnapshotDigger = GetHeldDigger();
                    DrawBlockHUD(targetPos, block, GetHeldDigger());
                    return;
                }
            }

            // 3. No live target: gracefully hold during anti-flicker window and fade out smoothly.
            AdvanceMotion(false, 0L);
            if (activeDrawAlpha <= 0.01f)
            {
                lastSnapshotKind = TargetSnapshotKind.None;
                lastSnapshotEntity = null;
                return;
            }

            if (lastSnapshotKind == TargetSnapshotKind.Entity && lastSnapshotEntity != null && !lastSnapshotEntity.IsDead())
            {
                DrawEntityHUD(lastSnapshotEntity);
            }
            else if (lastSnapshotKind == TargetSnapshotKind.Block && lastSnapshotBlock.id != 0 && lastSnapshotBlock.type != null)
            {
                DrawBlockHUD(lastSnapshotPos, lastSnapshotBlock, lastSnapshotDigger);
            }
        }

        private static void DrawLensCardBackground(Rect rect, string theme, float opacity)
        {
            LensCardMaterial mat = ResolveCardMaterial(theme);

            // Clean squircle card with specular inner bevel and hairline border — zero static stripes.
            NanoSurface.DrawCardSurface(
                rect,
                mat.BgTop,
                mat.BgBottom,
                mat.Border,
                mat.TopRule,
                mat.Specular,
                mat.Radius,
                opacity,
                0f,
                0f);
        }

        private void UpdateLensNativeMobHealthBars()
        {
            bool hide = LensHideNativeMobHealth != null && LensHideNativeMobHealth.Value;
            bool stateChanged = hide != lastHideNativeHp;

            if (!stateChanged && Time.unscaledTime - lastNativeHpScan < 0.5f) return;
            lastNativeHpScan = Time.unscaledTime;
            lastHideNativeHp = hide;

            try
            {
                Health[] all = UnityEngine.Object.FindObjectsOfType<Health>();
                Transform player = TerrainLoader.instance != null ? TerrainLoader.instance.player : null;

                for (int i = 0; i < all.Length; i++)
                {
                    Health h = all[i];
                    if (h == null) continue;
                    if (player != null && h.transform == player) continue;

                    if (hide)
                    {
                        // Hide only the bar and numeric HP, never NameLabel/MobLvl.
                        if (h.barObject != null && h.barObject.activeSelf) h.barObject.SetActive(false);
                        if (h.HPLabel != null && h.HPLabel.gameObject.activeSelf) h.HPLabel.gameObject.SetActive(false);
                    }
                    else if (stateChanged)
                    {
                        h.RefreshHealthBarUI();
                    }
                }
            }
            catch { }
        }

        private static float DistanceToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax;
            float dy = by - ay;
            float lenSq = dx * dx + dy * dy;
            float t = lenSq > 1e-6f ? Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / lenSq) : 0f;
            float cx = ax + dx * t;
            float cy = ay + dy * t;
            float ex = px - cx;
            float ey = py - cy;
            return Mathf.Sqrt(ex * ex + ey * ey);
        }

        private static Texture2D GetToolStatusTexture(bool ok)
        {
            if (ok && toolCheckTexture != null) return toolCheckTexture;
            if (!ok && toolCrossTexture != null) return toolCrossTexture;

            const int size = 48;
            Color32[] pixels = new Color32[size * size];
            Color bg = ok ? new Color(0.08f, 0.17f, 0.11f, 0.98f) : new Color(0.19f, 0.08f, 0.08f, 0.98f);
            Color border = ok ? NanoBrand.Ok : NanoBrand.Bad;
            Color mark = ok ? NanoBrand.Ok : NanoBrand.Bad;

            float half = (size - 1) * 0.5f;
            float outerRadius = half - 1.5f;
            float strokeWidth = 3.1f;

            for (int y = 0; y < size; y++)
            {
                float ny = (y + 0.5f) / size;
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size;
                    float dx = x - half;
                    float dy = y - half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - outerRadius;
                    float discAlpha = Mathf.Clamp01(0.5f - dist);
                    if (discAlpha <= 0.002f) continue;

                    float ringMask = Mathf.Clamp01((dist + strokeWidth) / 1.4f);
                    Color c = Color.Lerp(bg, border, ringMask * 0.85f);

                    float glyphDist;
                    if (ok)
                    {
                        float d1 = DistanceToSegment(nx, ny, 0.27f, 0.53f, 0.44f, 0.70f);
                        float d2 = DistanceToSegment(nx, ny, 0.44f, 0.70f, 0.75f, 0.34f);
                        glyphDist = Mathf.Min(d1, d2) * size;
                    }
                    else
                    {
                        float d1 = DistanceToSegment(nx, ny, 0.32f, 0.32f, 0.68f, 0.68f);
                        float d2 = DistanceToSegment(nx, ny, 0.68f, 0.32f, 0.32f, 0.68f);
                        glyphDist = Mathf.Min(d1, d2) * size;
                    }

                    float glyphAlpha = Mathf.Clamp01(2.55f - glyphDist);
                    c = Color.Lerp(c, mark, glyphAlpha);
                    c.a = discAlpha * Mathf.Max(bg.a, glyphAlpha);
                    pixels[y * size + x] = c;
                }
            }

            // Flip vertically from top-down glyph coordinates to Unity's bottom-up Texture2D layout.
            Color32[] mirrored = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                int srcRow = (size - 1 - y) * size;
                int dstRow = y * size;
                for (int x = 0; x < size; x++)
                    mirrored[dstRow + x] = pixels[srcRow + x];
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.SetPixels32(mirrored);
            tex.Apply(false, true);

            if (ok) toolCheckTexture = tex;
            else toolCrossTexture = tex;
            return tex;
        }



        private static string lastLoggedHeldItem = "\0";

        private static readonly Dictionary<string, Digger.DiggerType> KnownDiggerItems =
            new Dictionary<string, Digger.DiggerType>(StringComparer.OrdinalIgnoreCase)
            {
                // Axe
                ["Axe"] = Digger.DiggerType.Axe,
                ["Iron Axe"] = Digger.DiggerType.Axe,

                // Pickaxe ("Dig" is the stone pickaxe item)
                ["Dig"] = Digger.DiggerType.Pickaxe,
                ["Iron Dig"] = Digger.DiggerType.Pickaxe,
                ["Goblin PickAxe"] = Digger.DiggerType.Pickaxe,

                // Shovel
                ["Shovel"] = Digger.DiggerType.Shovel,
                ["Iron Shovel"] = Digger.DiggerType.Shovel,

                // Hammer
                ["Hammer"] = Digger.DiggerType.Hammer,
                ["Iron Hammer"] = Digger.DiggerType.Hammer,

                // Special
                ["Multitool"] = Digger.DiggerType.Multitool
            };

        /// <summary>
        /// The weapon slot also holds buckets, cauldrons, controllers and sensors. Those prefabs
        /// can carry a default Digger component, so we only trust the small set of real digging
        /// tools; everything else is the bare hand.
        /// </summary>
        private static bool TryGetKnownDiggerType(string itemName, out Digger.DiggerType type)
        {
            type = Digger.DiggerType.Pickaxe;
            return !string.IsNullOrEmpty(itemName) && KnownDiggerItems.TryGetValue(itemName, out type);
        }

        /// <summary>
        /// Returns the Digger of the currently equipped weapon, or null for the bare hand.
        /// TerrainLoader.digger is a default hand Digger (it reports Shovel), so it must never
        /// be used when the equipment slot is empty.
        /// </summary>
        private static Digger GetHeldDigger()
        {
            try
            {
                InvEquipment equipment = PlayerHealth.Instance != null ? PlayerHealth.Instance.equipment : null;
                InvGameItem held = equipment != null ? equipment.GetItem(InvBaseItem.Slot.Weapon) : null;
                InvBaseItem baseItem = held != null ? held.baseItem : null;
                string itemName = baseItem != null ? baseItem.name : null;

                bool isKnownDigger = TryGetKnownDiggerType(itemName, out Digger.DiggerType knownType);
                GameObject attachment = isKnownDigger && baseItem.attachment != null ? baseItem.attachment : null;
                Digger heldDigger = attachment != null ? attachment.GetComponent<Digger>() : null;

                string heldState = heldDigger != null
                    ? $"tool:{itemName} ({heldDigger.diggerType})"
                    : baseItem != null
                        ? $"hand:{itemName} ({baseItem.category})"
                        : "hand:empty";

                if (!string.Equals(heldState, lastLoggedHeldItem, StringComparison.Ordinal))
                {
                    lastLoggedHeldItem = heldState;
                    Log?.LogInfo($"[Aether.Lens] Held tool: {heldState}");
                }

                // If a known digger item lacks its attachment Digger, treat it as Hand; do not
                // invent a MonoBehaviour from the table.
                return heldDigger;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUsingBestTool(Digger digger, HarvestTool bestTool)
        {
            if (digger == null || bestTool == HarvestTool.None || bestTool == HarvestTool.Hand)
                return false;

            if (digger.diggerType == Digger.DiggerType.Multitool)
                return true;

            switch (bestTool)
            {
                case HarvestTool.Pickaxe: return digger.diggerType == Digger.DiggerType.Pickaxe;
                case HarvestTool.Axe: return digger.diggerType == Digger.DiggerType.Axe;
                case HarvestTool.Shovel: return digger.diggerType == Digger.DiggerType.Shovel;
                case HarvestTool.Hammer: return digger.diggerType == Digger.DiggerType.Hammer;
                default: return false;
            }
        }

        private static string GetBlockOriginName(BlockType bType, string itemName)
        {
            if (bType == null) return "Block Story";

            // Who registered it: the host (Aether) knows mods' items and custom blocks; Nano alone does not.
            string owner = NanoOwners.OfItem(itemName) ?? NanoOwners.OfBlock(bType.id);
            if (owner != null) return owner;

            return bType.id >= CustomBlockIdStart ? Tr("Mod", "Mod") : "Block Story";
        }

        private static Texture2D heartFullTexture;
        private static Texture2D heartEmptyTexture;
        private static Texture2D toolCheckTexture;
        private static Texture2D toolCrossTexture;
        private static bool loggedFontFailure;
        private static bool nativeHeartProbed;
        private static UIAtlas nativeHeartAtlas;
        private static string nativeHeartFullSprite;
        private static string nativeHeartEmptySprite;
        private static HealthBase lastHealthAnimTarget;
        private static float lensHealthAnim = 1f;
        private static float lensHealthGhost = 1f;
        private static float lensHealthGhostHold;

        private static void ProbeNativeHearts()
        {
            if (nativeHeartProbed) return;
            nativeHeartProbed = true;

            try
            {
                UISprite[] sprites = Resources.FindObjectsOfTypeAll<UISprite>();
                int bestFullScore = -1;
                string candidates = "";

                // Select the real heart sprite, not the first "life" match (Life Magic is a mana icon).
                for (int i = 0; i < sprites.Length; i++)
                {
                    UISprite sprite = sprites[i];
                    if (sprite == null || sprite.atlas == null || sprite.atlas.texture == null) continue;
                    string spriteName = sprite.spriteName ?? "";
                    string label = spriteName.ToLowerInvariant();
                    if (!label.Contains("heart") && !label.Contains("life")) continue;
                    if (label.Contains("empty") || label.Contains("half") || label.Contains("background")) continue;
                    if (sprite.atlas.GetSprite(spriteName) == null) continue;

                    if (candidates.Length < 240)
                        candidates += $"{sprite.atlas.name}:{spriteName}; ";

                    int score = label == "heart" || label == "heartfull" || label == "heart full" ? 120
                        : label.Contains("heart") ? 80
                        : label == "life" ? 40
                        : 10;
                    if (score > bestFullScore)
                    {
                        bestFullScore = score;
                        nativeHeartAtlas = sprite.atlas;
                        nativeHeartFullSprite = spriteName;
                    }
                }

                // The empty sprite must belong to the same atlas as the full sprite.
                int bestEmptyScore = -1;
                if (nativeHeartAtlas != null)
                {
                    for (int i = 0; i < sprites.Length; i++)
                    {
                        UISprite sprite = sprites[i];
                        if (sprite == null || sprite.atlas != nativeHeartAtlas) continue;
                        string label = (sprite.spriteName ?? "").ToLowerInvariant();
                        if (!label.Contains("empty") && !label.Contains("background")) continue;
                        if (!label.Contains("heart") && !label.Contains("life")) continue;
                        if (nativeHeartAtlas.GetSprite(sprite.spriteName) == null) continue;

                        int score = label == "heartempty" || label == "heart empty" ? 100 : 50;
                        if (score > bestEmptyScore)
                        {
                            bestEmptyScore = score;
                            nativeHeartEmptySprite = sprite.spriteName;
                        }
                    }
                }

                if (nativeHeartAtlas != null && !string.IsNullOrEmpty(nativeHeartFullSprite))
                {
                    var fullSprite = nativeHeartAtlas.GetSprite(nativeHeartFullSprite);
                    Texture tex = nativeHeartAtlas.texture;
                    Log?.LogInfo($"[Aether.Lens] Native heart sprites: full='{nativeHeartFullSprite}', empty='{nativeHeartEmptySprite}', atlas='{nativeHeartAtlas.name}', coords={nativeHeartAtlas.coordinates}, outer={fullSprite.outer}, texture={tex.width}x{tex.height}; candidates={candidates}");
                }
                else
                {
                    Log?.LogWarning($"[Aether.Lens] Native heart sprite not found; using procedural fallback. candidates={candidates}");
                }
            }
            catch (Exception ex)
            {
                nativeHeartAtlas = null;
                nativeHeartFullSprite = null;
                nativeHeartEmptySprite = null;
                Log?.LogWarning($"[Aether.Lens] Native heart probe failed: {ex.Message}");
            }
        }

        private static bool DrawNativeHeart(Rect rect, bool full)
        {
            ProbeNativeHearts();
            if (nativeHeartAtlas == null) return false;

            string spriteName = full
                ? nativeHeartFullSprite
                : (!string.IsNullOrEmpty(nativeHeartEmptySprite) ? nativeHeartEmptySprite : nativeHeartFullSprite);
            if (string.IsNullOrEmpty(spriteName)) return false;

            Color old = GUI.color;
            try
            {
                var sprite = nativeHeartAtlas.GetSprite(spriteName);
                Texture atlasTex = nativeHeartAtlas.texture;
                if (sprite == null || atlasTex == null) return false;

                Rect uv = sprite.outer;
                if (nativeHeartAtlas.coordinates == UIAtlas.Coordinates.Pixels)
                    uv = NGUIMath.ConvertToTexCoords(uv, atlasTex.width, atlasTex.height);

                // HeartEmpty already has its final color; tinting it dark made it disappear.
                GUI.color = full || !string.IsNullOrEmpty(nativeHeartEmptySprite)
                    ? Color.white
                    : new Color(0.26f, 0.10f, 0.12f, 0.80f);
                GUI.DrawTextureWithTexCoords(rect, atlasTex, uv);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                GUI.color = old;
            }
        }

        private static float GetAnimatedHealthRatio(HealthBase health, float target)
        {
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.05f);
            bool reduced = IsMotionReduced();

            if (health != lastHealthAnimTarget)
            {
                lastHealthAnimTarget = health;
                lensHealthAnim = target;
                lensHealthGhost = target;
                lensHealthGhostHold = 0f;
            }
            else if (reduced)
            {
                lensHealthAnim = target;
                lensHealthGhost = target;
                lensHealthGhostHold = 0f;
            }
            else
            {
                float prev = lensHealthAnim;
                lensHealthAnim = Mathf.Lerp(lensHealthAnim, target, Mathf.Clamp01(dt * 12f));
                if (target < prev - 0.004f)
                {
                    lensHealthGhost = Mathf.Max(lensHealthGhost, prev);
                    lensHealthGhostHold = 0.24f;
                }
                else if (target > lensHealthGhost)
                {
                    lensHealthGhost = lensHealthAnim;
                }

                if (lensHealthGhostHold > 0f)
                    lensHealthGhostHold = Mathf.Max(0f, lensHealthGhostHold - dt);
                else if (lensHealthGhost > lensHealthAnim)
                    lensHealthGhost = Mathf.MoveTowards(lensHealthGhost, lensHealthAnim, dt * 0.65f);
            }
            return Mathf.Clamp01(lensHealthAnim);
        }

        private static Color GetHealthBarColor(float ratio)
        {
            if (ratio > 0.55f) return NanoBrand.Ok;
            if (ratio > 0.25f) return NanoBrand.Warn;
            return NanoBrand.Bad;
        }

        private static Texture2D GetHeartTexture(bool full)
        {
            if (full && heartFullTexture != null) return heartFullTexture;
            if (!full && heartEmptyTexture != null) return heartEmptyTexture;

            const int w = 9;
            const int h = 8;
            string[] mask =
            {
                "0110110",
                "1111111",
                "1111111",
                "0111110",
                "0011100",
                "0001000"
            };

            Color32[] pixels = new Color32[w * h];
            Color32 color = full
                ? new Color32(235, 55, 65, 255)
                : new Color32(85, 20, 25, 190);

            for (int y = 0; y < mask.Length; y++)
            {
                for (int x = 0; x < mask[y].Length; x++)
                {
                    int px = x + 1;
                    int py = (h - 1) - (y + 1);
                    if (px < w && py >= 0 && py < h && mask[y][x] == '1')
                        pixels[py * w + px] = color;
                }
            }

            Texture2D heart = new Texture2D(w, h, TextureFormat.ARGB32, false);
            heart.filterMode = FilterMode.Point;
            heart.wrapMode = TextureWrapMode.Clamp;
            heart.SetPixels32(pixels);
            heart.Apply();

            if (full) heartFullTexture = heart;
            else heartEmptyTexture = heart;
            return heart;
        }

        private static bool TryGetMobQuestState(HealthBase health, out bool isQuestNpc, out bool hasAvailableQuest)
        {
            isQuestNpc = false;
            hasAvailableQuest = false;

            try
            {
                QuestManager manager = QuestManager.instance;
                if (manager == null || manager.questsNPC == null || health == null) return false;

                string raw = RawMobName(health);

                if (!manager.questsNPC.TryGetValue(raw, out var quests) || quests == null) return false;

                isQuestNpc = true;
                for (int i = 0; i < quests.Count; i++)
                {
                    Quest quest = quests[i];
                    if (quest != null && quest.IsAvailable())
                    {
                        hasAvailableQuest = true;
                        break;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void DrawEntityHUD(HealthBase health)
        {
            string name = GetLocalizedMobName(health);

            MobLvl mobLvl = health.GetComponent<MobLvl>();
            bool showLevel = LensShowMobLevel == null || LensShowMobLevel.Value;
            int mobLevel = mobLvl != null && mobLvl.lvl > 0 ? mobLvl.lvl : ModMobLevel(health);

            float curHp = health.GetHealth();
            float maxHp = health.GetMaxHealth() > 0 ? health.GetMaxHealth() : curHp;
            float hpRatio = maxHp > 0f ? Mathf.Clamp01(curHp / maxHp) : 0f;
            float animatedRatio = GetAnimatedHealthRatio(health, hpRatio);
            float ghostRatio = Mathf.Clamp01(lensHealthGhost);

            string healthStyle = LensMobHealthStyle != null ? LensMobHealthStyle.Value : "Hearts";
            bool isCompact = LensTheme.Value == "CompactBar";
            bool hideHealth = string.Equals(healthStyle, "None", StringComparison.OrdinalIgnoreCase);

            string rawName = RawMobName(health);
            string originName = NanoOwners.OfEntity(rawName) ?? "Block Story";
            bool isCustomMod = !string.Equals(originName, "Block Story", StringComparison.OrdinalIgnoreCase);

            bool showQuests = LensShowMobQuests == null || LensShowMobQuests.Value;
            bool isQuestNpc = false;
            bool hasAvailableQuest = false;
            if (showQuests)
                isQuestNpc = TryGetMobQuestState(health, out bool questNpc, out hasAvailableQuest) && questNpc;

            float desiredWidth = MeasureEntityCardWidth(
                LensTheme.Value, name, showLevel ? mobLevel : 0, curHp, maxHp,
                healthStyle, originName, isCustomMod, showQuests && isQuestNpc, hasAvailableQuest);
            float boxWidth = ResolveAnimatedCardWidth(desiredWidth, isCompact);
            float boxHeight = isCompact ? 40f : (hideHealth ? 54f : 72f);

            Rect screenRect = GetLensScreenRect(boxWidth, boxHeight);
            float scale = GetLensScale();
            float alpha = activeDrawAlpha;
            float settleY = activeSettleY;

            Color prevGuiColor = GUI.color;
            Matrix4x4 prevMatrix = GUI.matrix;
            try
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                if (Mathf.Abs(scale - 1f) > 0.001f)
                    GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(screenRect.x, screenRect.y + settleY));

                var cardRect = new Rect(screenRect.x, screenRect.y + settleY, boxWidth, boxHeight);
                DrawEntityCardContents(
                    cardRect,
                    LensTheme.Value,
                    LensBgOpacity.Value,
                    name,
                    showLevel ? mobLevel : 0,
                    curHp,
                    maxHp,
                    hpRatio,
                    animatedRatio,
                    ghostRatio,
                    healthStyle,
                    originName,
                    isCustomMod,
                    showQuests && isQuestNpc,
                    hasAvailableQuest);
            }
            finally
            {
                GUI.matrix = prevMatrix;
                GUI.color = prevGuiColor;
            }
        }

        private static void DrawEntityCardContents(
            Rect hudRect,
            string theme,
            float opacity,
            string name,
            int mobLevel,
            float curHp,
            float maxHp,
            float hpRatio,
            float animatedRatio,
            float ghostRatio,
            string healthStyle,
            string originName,
            bool isCustomMod,
            bool showQuestBadge,
            bool hasAvailableQuest)
        {
            EnsureLensStyles();
            LensCardMaterial mat = ResolveCardMaterial(theme);
            bool isCompact = theme == "CompactBar";

            DrawLensCardBackground(hudRect, theme, opacity);

            float padX = isCompact ? 11f : 14f;
            float textX = hudRect.x + padX;
            float textW = hudRect.width - padX * 2f;
            string lvlPrefix = Tr("LV", "NV");
            bool hasLevel = mobLevel > 0;

            if (isCompact)
            {
                float rightEdge = hudRect.xMax - 11f;
                bool showBar = !string.Equals(healthStyle, "None", StringComparison.OrdinalIgnoreCase);

                if (showBar)
                {
                    string hpNum = $"{curHp:F0}/{maxHp:F0}";
                    float hpW = Mathf.Clamp(lensMonoRight.CalcSize(new GUIContent(hpNum)).x + 4f, 40f, 74f);
                    NanoSurface.DrawCrispLabel(new Rect(rightEdge - hpW, hudRect.y + 11f, hpW, 18f),
                        hpNum, lensMonoRight, hpRatio <= 0.25f ? NanoBrand.Bad : mat.TextMuted, 0.4f);
                    rightEdge -= hpW + 8f;

                    float miniW = 70f;
                    float barHeight = 8f;
                    NanoSurface.DrawProgressBar(
                        new Rect(rightEdge - miniW, hudRect.y + (hudRect.height - barHeight) * 0.5f, miniW, barHeight),
                        animatedRatio,
                        ghostRatio,
                        mat.SlotBg,
                        mat.SlotBorder,
                        GetHealthBarColor(hpRatio),
                        new Color(1f, 0.92f, 0.80f, 0.55f),
                        4f,
                        opacity);
                    rightEdge -= miniW + 10f;
                }

                if (hasLevel)
                {
                    string lvlChip = $"{lvlPrefix} {mobLevel}";
                    float chipW = Mathf.Max(36f, lensChipStyle.CalcSize(new GUIContent(lvlChip)).x + 10f);
                    NanoSurface.DrawChip(new Rect(rightEdge - chipW, hudRect.y + 11f, chipW, 18f),
                        lvlChip, lensChipStyle, mat.SlotBg, mat.SlotBorder, mat.TopRule, 4f, opacity);
                    rightEdge -= chipW + 8f;
                }

                GUIStyle nameStyle = mat.UseNativeSerif ? TitleStyle : lensCompactNameBrand;
                NanoSurface.DrawCrispLabel(new Rect(textX, hudRect.y + 9f, Mathf.Max(60f, rightEdge - textX), 22f),
                    name, nameStyle, mat.TextPrimary, 0.55f);
                return;
            }

            // Standard entity card — Row 1: Name + level chip
            float nameMaxW = textW;
            if (hasLevel)
            {
                string lvlChip = $"{lvlPrefix} {mobLevel}";
                float chipW = Mathf.Max(40f, lensChipStyle.CalcSize(new GUIContent(lvlChip)).x + 12f);
                var chipRect = new Rect(hudRect.xMax - padX - chipW, hudRect.y + 11f, chipW, 18f);
                NanoSurface.DrawChip(chipRect, lvlChip, lensChipStyle, mat.SlotBg, mat.SlotBorder, mat.TopRule, 5f, opacity);
                nameMaxW = Mathf.Max(80f, chipRect.x - textX - 8f);
            }

            GUIStyle titleSt = mat.UseNativeSerif ? TitleStyle : lensNameBrand;
            NanoSurface.DrawCrispLabel(new Rect(textX, hudRect.y + 9f, nameMaxW, 22f), name, titleSt, mat.TextPrimary, 0.55f);

            // Row 2: Hearts or beveled bar
            float statusY;
            if (string.Equals(healthStyle, "Hearts", StringComparison.OrdinalIgnoreCase))
            {
                int fullHearts = Mathf.Clamp(Mathf.RoundToInt(animatedRatio * 10f), 0, 10);
                const float heartSize = 12f;
                const float heartSpacing = 14f;
                float heartY = hudRect.y + 34f;

                for (int i = 0; i < 10; i++)
                {
                    Rect heartRect = new Rect(textX + i * heartSpacing, heartY, heartSize, heartSize);
                    if (!DrawNativeHeart(heartRect, i < fullHearts))
                        GUI.DrawTexture(heartRect, GetHeartTexture(i < fullHearts));
                }

                string hpReadout = $"{curHp:F0}/{maxHp:F0}";
                float readoutX = textX + 10f * heartSpacing + 4f;
                float readoutW = Mathf.Max(0f, hudRect.xMax - padX - readoutX);
                if (readoutW >= 36f)
                {
                    NanoSurface.DrawCrispLabel(new Rect(readoutX, heartY - 1f, readoutW, 14f),
                        hpReadout, lensMonoRight, hpRatio <= 0.25f ? NanoBrand.Bad : mat.TextMuted, 0.4f);
                }

                statusY = 52f;
            }
            else if (string.Equals(healthStyle, "Bar", StringComparison.OrdinalIgnoreCase))
            {
                float barY = hudRect.y + 33f;
                float barHeight = 14f;
                Rect barBg = new Rect(textX, barY, textW, barHeight);
                NanoSurface.DrawProgressBar(
                    barBg,
                    animatedRatio,
                    ghostRatio,
                    mat.SlotBg,
                    mat.SlotBorder,
                    GetHealthBarColor(hpRatio),
                    new Color(1f, 0.92f, 0.80f, 0.55f),
                    4.5f,
                    opacity);
                NanoSurface.DrawCrispLabel(barBg, $"{curHp:F0} / {maxHp:F0}  ({(hpRatio * 100f):F0}%)",
                    lensBarTextStyle, NanoBrand.PaperLight, 0.7f);
                statusY = 52f;
            }
            else
            {
                statusY = 34f;
            }

            // Row 3: Mod origin (left) + quest NPC indicator (right, only for actual quest NPCs)
            float footerY = hudRect.y + statusY;
            float rightLimit = hudRect.xMax - padX;

            if (showQuestBadge)
            {
                string questText = hasAvailableQuest
                    ? Tr("Quest available", "Misión disponible")
                    : Tr("No quest now", "Sin misión");
                Color questColor = hasAvailableQuest ? NanoBrand.Warn : mat.TextMuted;
                float qTextW = lensMonoRight.CalcSize(new GUIContent(questText)).x + 2f;
                var qTextRect = new Rect(rightLimit - qTextW, footerY, qTextW, 14f);
                NanoSurface.DrawCrispLabel(qTextRect, questText, lensMonoRight, questColor, 0.4f);
                NanoSurface.DrawRhombus(new Rect(qTextRect.x - 11f, footerY + 3.5f, 7f, 7f), questColor, hasAvailableQuest);
                rightLimit = qTextRect.x - 16f;
            }

            DrawOriginLine(textX, footerY, Mathf.Max(60f, rightLimit - textX), originName, isCustomMod, mat);
        }

        private static void DrawOriginLine(float x, float y, float width, string originName, bool isCustomMod, LensCardMaterial mat)
        {
            if (isCustomMod)
            {
                NanoSurface.DrawRhombus(new Rect(x, y + 3.5f, 7f, 7f), mat.TopRule, true);
                NanoSurface.DrawCrispLabel(new Rect(x + 11f, y, Mathf.Max(40f, width - 11f), 14f),
                    originName, lensMonoSmall, NanoBrand.EmberSoft, 0.4f);
            }
            else
            {
                NanoSurface.DrawCrispLabel(new Rect(x, y, width, 14f), originName, lensMonoSmall, mat.TextMuted, 0.35f);
            }
        }

        private void DrawBlockHUD(Vector3i pos, Block block, Digger digger)
        {
            BlockType bType = block.type;
            string rawName = bType != null ? bType.name : "Block";
            InvBaseItem baseItem = ResolveBlockItem(rawName);
            string locName = GetLocalizedBlockName(rawName, baseItem);
            HarvestTool bestTool = GetBestHarvestTool(bType);
            string toolNeeded = GetHarvestToolName(bestTool);
            InvBaseItem toolIcon = FindToolIcon(bestTool);
            string originName = GetBlockOriginName(bType, rawName);
            bool isCustomMod = IsModBlock(bType) || !string.Equals(originName, "Block Story", StringComparison.OrdinalIgnoreCase);
            bool showToolCheck = bestTool != HarvestTool.None && bestTool != HarvestTool.Hand;
            bool usingBestTool = IsUsingBestTool(digger, bestTool);

            bool isCompact = LensTheme.Value == "CompactBar";
            bool hasToolCapsule = toolIcon != null || showToolCheck;
            string toolLine = FormatToolLine(bestTool, toolNeeded, showToolCheck, usingBestTool);
            float desiredWidth = MeasureBlockCardWidth(
                LensTheme.Value, locName, toolLine, hasToolCapsule, originName, isCustomMod,
                LensShowCoords.Value, pos.X, pos.Y, pos.Z);
            float boxWidth = ResolveAnimatedCardWidth(desiredWidth, isCompact);
            float boxHeight = isCompact ? 40f : 72f;

            float digProgress = SmoothDigProgress(GetActiveDigProgress(digger, out _));

            Rect screenRect = GetLensScreenRect(boxWidth, boxHeight);
            float scale = GetLensScale();
            float alpha = activeDrawAlpha;
            float settleY = activeSettleY;

            Texture2D isoCube = null;
            bool canRenderCube = bType != null && bType.shape == BlockType.Shape.Cube && bType.visible && !bType.liquid;
            if (Lens3DBlockIcon.Value && canRenderCube)
                isoCube = IsometricBlockIconRenderer.GetOrCreateBlockCubeTexture(block, bType, baseItem);

            Color prevGuiColor = GUI.color;
            Matrix4x4 prevMatrix = GUI.matrix;
            try
            {
                GUI.color = new Color(1f, 1f, 1f, alpha);
                if (Mathf.Abs(scale - 1f) > 0.001f)
                    GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(screenRect.x, screenRect.y + settleY));

                var cardRect = new Rect(screenRect.x, screenRect.y + settleY, boxWidth, boxHeight);
                DrawBlockCardContents(
                    cardRect,
                    LensTheme.Value,
                    LensBgOpacity.Value,
                    locName,
                    bestTool,
                    toolNeeded,
                    toolIcon,
                    showToolCheck,
                    usingBestTool,
                    originName,
                    isCustomMod,
                    LensShowCoords.Value,
                    pos.X, pos.Y, pos.Z,
                    isoCube,
                    baseItem,
                    NanoPalette.GetBlockColor(block.id),
                    digProgress);
            }
            finally
            {
                GUI.matrix = prevMatrix;
                GUI.color = prevGuiColor;
            }
        }

        private static void DrawBlockCardContents(
            Rect hudRect,
            string theme,
            float opacity,
            string locName,
            HarvestTool bestTool,
            string toolNeeded,
            InvBaseItem toolIcon,
            bool showToolCheck,
            bool usingBestTool,
            string originName,
            bool isCustomMod,
            bool showCoords,
            int bx, int by, int bz,
            Texture2D isoCube,
            InvBaseItem baseItem,
            Color32 fallbackBlockColor,
            float digProgress = 0f)
        {
            EnsureLensStyles();
            LensCardMaterial mat = ResolveCardMaterial(theme);
            bool isCompact = theme == "CompactBar";

            DrawLensCardBackground(hudRect, theme, opacity);

            // 1. Block icon (no inventory frame; soft elliptical contact shadow grounds 3D cubes)
            float iconSize = isCompact ? 28f : 52f;
            float iconPadX = isCompact ? 8f : 11f;
            Rect iconSlot = new Rect(hudRect.x + iconPadX, hudRect.y + (hudRect.height - iconSize) * 0.5f, iconSize, iconSize);

            if (!isCompact)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.40f * opacity * prev.a);
                GUI.DrawTexture(new Rect(iconSlot.x + 6f, iconSlot.yMax - 6f, iconSize - 12f, 8f), NanoSurface.EllipticalShadow());
                GUI.color = prev;
            }

            if (isoCube != null)
            {
                GUI.DrawTexture(new Rect(iconSlot.x + 1f, iconSlot.y + 1f, iconSize - 2f, iconSize - 2f), isoCube);
            }
            else if (!DrawItemIcon(new Rect(iconSlot.x + 2f, iconSlot.y + 2f, iconSize - 4f, iconSize - 4f), baseItem))
            {
                // Fallback stylized isometric swatch when neither cube nor item sprite is available
                NanoSurface.FillRounded(new Rect(iconSlot.x + 6f, iconSlot.y + 6f, iconSize - 12f, iconSize - 12f), fallbackBlockColor, 6f);
            }

            // 2. Right-side debossed tool capsule (only occupies width when there is a harvest tool)
            bool hasToolCapsule = toolIcon != null || showToolCheck;
            float capsuleSize = isCompact ? 24f : 42f;
            float rightPad = isCompact ? 9f : 11f;
            float textX = iconSlot.xMax + (isCompact ? 9f : 10f);
            float textRight = hudRect.xMax - rightPad;

            if (hasToolCapsule)
            {
                Rect capsuleRect = new Rect(
                    hudRect.xMax - capsuleSize - rightPad,
                    hudRect.y + (hudRect.height - capsuleSize) * 0.5f,
                    capsuleSize,
                    capsuleSize);

                NanoSurface.DrawDebossedSlot(capsuleRect, mat.SlotBg, mat.SlotBorder, isCompact ? 6f : 9f, opacity);

                if (toolIcon != null)
                {
                    float pad = isCompact ? 3f : 6f;
                    DrawItemIcon(new Rect(capsuleRect.x + pad, capsuleRect.y + pad, capsuleSize - pad * 2f, capsuleSize - pad * 2f), toolIcon, Color.white);
                }
                else
                {
                    // Clean rhombus glyph fallback when tool icon atlas is unavailable (e.g. main-menu preview)
                    float rSize = isCompact ? 10f : 14f;
                    NanoSurface.DrawRhombus(
                        new Rect(capsuleRect.x + (capsuleSize - rSize) * 0.5f, capsuleRect.y + (capsuleSize - rSize) * 0.5f, rSize, rSize),
                        usingBestTool ? NanoBrand.Ok : NanoBrand.Bad,
                        true);
                }

                if (showToolCheck)
                {
                    float badgeSize = isCompact ? 12f : 17f;
                    Rect badgeRect = new Rect(
                        capsuleRect.xMax - badgeSize * 0.72f,
                        capsuleRect.yMax - badgeSize * 0.72f,
                        badgeSize,
                        badgeSize);
                    GUI.DrawTexture(badgeRect, GetToolStatusTexture(usingBestTool));
                }

                textRight = capsuleRect.x - 10f;
            }

            float textW = Mathf.Max(70f, textRight - textX);

            if (isCompact)
            {
                if (showCoords)
                {
                    string coordText = $"{bx},{by},{bz}";
                    float coordW = Mathf.Min(86f, lensMonoRight.CalcSize(new GUIContent(coordText)).x + 6f);
                    NanoSurface.DrawCrispLabel(new Rect(textRight - coordW, hudRect.y + 11f, coordW, 18f),
                        coordText, lensMonoRight, mat.TextMuted, 0.35f);
                    textW = Mathf.Max(50f, textRight - coordW - 8f - textX);
                }

                GUIStyle compactTitle = mat.UseNativeSerif ? TitleStyle : lensCompactNameBrand;
                NanoSurface.DrawCrispLabel(new Rect(textX, hudRect.y + 9f, textW, 22f), locName, compactTitle, mat.TextPrimary, 0.55f);
                NanoSurface.DrawBottomEdgeProgress(hudRect, digProgress, mat.TopRule, mat.Radius, opacity);
                return;
            }

            // 3. Standard block card text hierarchy
            // Row 1: Block name
            GUIStyle nameStyle = mat.UseNativeSerif ? TitleStyle : lensNameBrand;
            NanoSurface.DrawCrispLabel(new Rect(textX, hudRect.y + 9f, textW, 22f), locName, nameStyle, mat.TextPrimary, 0.55f);

            // Row 2: Semantic harvest tool line
            Color toolColor;
            string toolLine;
            bool filledDot;
            if (showToolCheck)
            {
                toolColor = usingBestTool ? NanoBrand.Ok : NanoBrand.Bad;
                toolLine = usingBestTool
                    ? toolNeeded
                    : Tr($"Needs {toolNeeded}", $"Requiere {toolNeeded}");
                filledDot = usingBestTool;
            }
            else if (bestTool == HarvestTool.None)
            {
                toolColor = NanoBrand.Warn;
                toolLine = toolNeeded;
                filledDot = true;
            }
            else
            {
                toolColor = mat.TextMuted;
                toolLine = toolNeeded;
                filledDot = false;
            }

            float toolRowY = hudRect.y + 32f;
            NanoSurface.DrawRhombus(new Rect(textX, toolRowY + 4.5f, 7f, 7f), toolColor, filledDot);
            NanoSurface.DrawCrispLabel(new Rect(textX + 11f, toolRowY, Mathf.Max(40f, textW - 11f), 17f),
                toolLine, lensSubBrand, toolColor, 0.45f);

            // Row 3: Mod origin (left) + tabular coordinates (right)
            float footerY = hudRect.y + 51f;
            float originRight = textRight;
            if (showCoords)
            {
                string coordText = $"{bx}, {by}, {bz}";
                float coordW = Mathf.Min(96f, lensMonoRight.CalcSize(new GUIContent(coordText)).x + 4f);
                NanoSurface.DrawCrispLabel(new Rect(originRight - coordW, footerY, coordW, 14f),
                    coordText, lensMonoRight, mat.TextMuted, 0.35f);
                originRight -= coordW + 8f;
            }

            DrawOriginLine(textX, footerY, Mathf.Max(50f, originRight - textX), originName, isCustomMod, mat);
            NanoSurface.DrawBottomEdgeProgress(hudRect, digProgress, mat.TopRule, mat.Radius, opacity);
        }

        internal float ComputePreviewCardWidth(bool mobMode)
        {
            bool isCompact = LensTheme.Value == "CompactBar";
            float minW = GetLensCardWidth(isCompact);
            float measured;
            if (mobMode)
            {
                string healthStyle = LensMobHealthStyle != null ? LensMobHealthStyle.Value : "Hearts";
                bool showLevel = LensShowMobLevel == null || LensShowMobLevel.Value;
                bool showQuests = LensShowMobQuests == null || LensShowMobQuests.Value;
                measured = MeasureEntityCardWidth(
                    LensTheme.Value, Tr("Wizard", "Mago"), showLevel ? 14 : 0, 74f, 100f,
                    healthStyle, "Block Story", false, showQuests, true);
            }
            else
            {
                string toolNeeded = GetHarvestToolName(HarvestTool.Pickaxe);
                measured = MeasureBlockCardWidth(
                    LensTheme.Value, Tr("Iron Ore", "Mena de Hierro"), toolNeeded, true,
                    "Block Story", false, LensShowCoords.Value, 142, 64, -28);
            }
            return Mathf.Clamp(Mathf.Ceil(measured), minW, Mathf.Max(minW + 180f, 440f));
        }

        /// <summary>Draws the real Lens card at its current screen rect inside Aether Canvas's WYSIWYG editor.</summary>
        internal void DrawCanvasPreview()
        {
            bool isCompact = LensTheme.Value == "CompactBar";
            float boxW = ComputePreviewCardWidth(false);
            float boxH = isCompact ? 40f : 72f;
            Rect screenRect = GetLensScreenRect(boxW, boxH);
            float scale = GetLensScale();

            Matrix4x4 prevMatrix = GUI.matrix;
            try
            {
                if (Mathf.Abs(scale - 1f) > 0.001f)
                    GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), new Vector2(screenRect.x, screenRect.y));
                DrawPreviewBlockCard(new Rect(screenRect.x, screenRect.y, boxW, boxH), 0f);
            }
            finally
            {
                GUI.matrix = prevMatrix;
            }
        }

        /// <summary>Live preview of a block card inside LensSettings (uses the exact gameplay renderer).</summary>
        internal void DrawPreviewBlockCard(Rect cardRect, float demoDigProgress = 0f)
        {
            InvBaseItem sampleBlock = SafeFindItemByName("Iron Ore") ?? SafeFindItemByName("Stone") ?? SafeFindItemByName("Dirt");
            InvBaseItem sampleTool = FindToolIcon(HarvestTool.Pickaxe);
            DrawBlockCardContents(
                cardRect,
                LensTheme.Value,
                LensBgOpacity.Value,
                Tr("Iron Ore", "Mena de Hierro"),
                HarvestTool.Pickaxe,
                GetHarvestToolName(HarvestTool.Pickaxe),
                sampleTool,
                true,
                true,
                "Block Story",
                false,
                LensShowCoords.Value,
                142, 64, -28,
                null,
                sampleBlock,
                new Color32(138, 128, 118, 255),
                demoDigProgress);
        }

        /// <summary>Live preview of a creature card inside LensSettings (uses the exact gameplay renderer).</summary>
        internal void DrawPreviewEntityCard(Rect cardRect)
        {
            string healthStyle = LensMobHealthStyle != null ? LensMobHealthStyle.Value : "Hearts";
            bool showLevel = LensShowMobLevel == null || LensShowMobLevel.Value;
            bool showQuests = LensShowMobQuests == null || LensShowMobQuests.Value;

            DrawEntityCardContents(
                cardRect,
                LensTheme.Value,
                LensBgOpacity.Value,
                Tr("Wizard", "Mago"),
                showLevel ? 14 : 0,
                74f,
                100f,
                0.74f,
                0.74f,
                0.86f,
                healthStyle,
                "Block Story",
                false,
                showQuests,
                true);
        }

        private enum HarvestTool
        {
            None,
            Hand,
            Pickaxe,
            Axe,
            Shovel,
            Hammer
        }

        /// <summary>
        /// The game's break rate is <c>block.type.*strength * diggerPower</c> and the break
        /// percentage is elapsed / breakrate, so the LOWEST strength is the FASTEST tool.
        /// This mirrors Digger.DiggerType exactly instead of guessing from the block name.
        /// </summary>
        private HarvestTool GetBestHarvestTool(BlockType bType)
        {
            if (bType == null) return HarvestTool.Hand;
            if (!bType.diggable || bType.air || bType.liquid) return HarvestTool.None;

            float pick = bType.strength;
            float shovel = bType.shovelstrength;
            float axe = bType.axestrength;
            float hammer = bType.hammerstrength;
            float best = Mathf.Min(Mathf.Min(pick, shovel), Mathf.Min(axe, hammer));

            if (best <= 0.0001f) return HarvestTool.Hand;

            // Tie order matches the hand fallback (shovelstrength) and the common tool choice.
            if (shovel <= best + 0.0001f) return HarvestTool.Shovel;
            if (pick <= best + 0.0001f) return HarvestTool.Pickaxe;
            if (axe <= best + 0.0001f) return HarvestTool.Axe;
            return HarvestTool.Hammer;
        }

        private string GetHarvestToolName(HarvestTool tool)
        {
            switch (tool)
            {
                case HarvestTool.Pickaxe: return Tr("Pickaxe", "Pico");
                case HarvestTool.Axe: return Tr("Axe", "Hacha");
                case HarvestTool.Shovel: return Tr("Shovel", "Pala");
                case HarvestTool.Hammer: return Tr("Hammer", "Martillo");
                case HarvestTool.None: return Tr("Unbreakable", "Irrompible");
                default: return Tr("Hand", "Mano");
            }
        }

        private static bool IsModBlock(BlockType bType)
        {
            if (bType == null) return false;

            return NanoOwners.OfBlock(bType.id) != null || bType.id >= CustomBlockIdStart;
        }

        private static readonly Dictionary<string, InvBaseItem> toolIconCache =
            new Dictionary<string, InvBaseItem>(StringComparer.OrdinalIgnoreCase);

        private InvBaseItem FindToolIcon(HarvestTool tool)
        {
            string toolName;
            switch (tool)
            {
                case HarvestTool.Pickaxe: toolName = "Pickaxe"; break;
                case HarvestTool.Axe: toolName = "Axe"; break;
                case HarvestTool.Shovel: toolName = "Shovel"; break;
                case HarvestTool.Hammer: toolName = "Hammer"; break;
                default: return null;
            }

            if (toolIconCache.TryGetValue(toolName, out InvBaseItem cached)) return cached;

            InvBaseItem exact = SafeFindItemByName(toolName);
            if (exact != null)
            {
                toolIconCache[toolName] = exact;
                return exact;
            }

            try
            {
                InvDatabase[] databases = InvDatabase.list;
                if (databases != null)
                {
                    for (int d = 0; d < databases.Length; d++)
                    {
                        InvDatabase db = databases[d];
                        if (db == null || db.items == null) continue;
                        for (int i = 0; i < db.items.Count; i++)
                        {
                            InvBaseItem item = db.items[i];
                            if (item == null || item.iconAtlas == null || string.IsNullOrEmpty(item.name)) continue;
                            if (item.name.IndexOf(toolName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                toolIconCache[toolName] = item;
                                return item;
                            }
                        }
                    }
                }
            }
            catch { }

            toolIconCache[toolName] = null;
            return null;
        }

    }
}
