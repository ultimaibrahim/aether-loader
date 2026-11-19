using Aether.Nano;
using BepInEx.Configuration;
using BepInEx.Logging;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using UnityEngine;

namespace Aether.Mods.Lens
{
    /// <summary>What Lens takes from Aether Nano: its config and logger (set by <see cref="LensPlugin"/>), styles, items and
    /// where its card goes on screen (Nano's HUD insets, which Aether adjusts around the game's own HUD).</summary>
    public sealed partial class LensModule
    {
        public static ConfigFile Config { get; internal set; }
        public static ManualLogSource Log { get; internal set; }
        public static ConfigEntry<float> LensScale { get; private set; }

        private static GUIStyle TitleStyle => NanoStyles.Title;
        private static GUIStyle ToolStyle => NanoStyles.Tool;
        private static GUIStyle SmallStyle => NanoStyles.Small;
        private static GUIStyle AuthorStyle => NanoStyles.Author;
        private static GUIStyle DescStyle => NanoStyles.Desc;
        private static GUIStyle SectionTitleStyle => NanoStyles.SectionTitle;

        private static Texture2D GetColorTexture(Color c) => NanoUi.ColorTexture(c);
        private static IWorld GetActiveWorld() => NanoWorld.Active();
        private static NanoTheme GetThemePalette(string theme) => NanoThemes.Get(theme);
        private static InvBaseItem SafeFindItemByName(string name) => NanoItems.FindByName(name);
        private static InvBaseItem ResolveBlockItem(string blockTypeName) => NanoItems.ResolveBlockItem(blockTypeName);
        private static string GetLocalizedBlockName(string raw, InvBaseItem item) => NanoItems.LocalizedName(raw, item);
        private static bool DrawItemIcon(Rect rect, InvBaseItem item, Color? tint = null) => NanoItems.DrawIcon(rect, item, tint);

        private static void BindScale() =>
            LensScale = Config.Bind("Lens", "Scale", 1.0f, new ConfigDescription("Lens card scale.", new AcceptableValueRange<float>(0.5f, 1.5f)));

        private static float _lastW = 248f, _lastH = 72f;

        /// <summary>The card's current screen rect (for hosts that move it, like Aether's HUD editor).</summary>
        public static Rect CurrentCardRect() => GetLensScreenRect(_lastW, _lastH);

        private static float GetLensScale() =>
            Mathf.Clamp(LensScale != null ? LensScale.Value : 1f, 0.5f, 1.5f);

        private static float GetLensCardWidth(bool isCompact)
        {
            float configured = LensCardWidth != null ? LensCardWidth.Value : 248f;
            return Mathf.Clamp(isCompact ? configured - 30f : configured, 196f, 420f);
        }

        private static Rect GetLensScreenRect(float boxWidth = 316f, float boxHeight = 72f)
        {
            _lastW = boxWidth; _lastH = boxHeight;
            float scale = GetLensScale();
            float scaledW = boxWidth * scale;
            float scaledH = boxHeight * scale;
            float x, y;
            switch (LensAnchor != null ? LensAnchor.Value : "TopCenter")
            {
                case "TopLeft": x = 18f; y = NanoHud.TopLeftInset(); break;
                case "BottomCenter": x = (Screen.width - scaledW) * 0.5f; y = Screen.height - scaledH - 110f; break;
                case "AboveCrosshair": x = (Screen.width - scaledW) * 0.5f; y = Screen.height * 0.5f - scaledH - 40f; break;
                default: x = (Screen.width - scaledW) * 0.5f; y = NanoHud.TopInset(); break;
            }
            float clampedX = Mathf.Clamp(x + (LensOffsetX?.Value ?? 0f), 6f, Mathf.Max(6f, Screen.width - scaledW - 6f));
            float clampedY = Mathf.Clamp(y + (LensOffsetY?.Value ?? 0f), 6f, Mathf.Max(6f, Screen.height - scaledH - 6f));
            return new Rect(clampedX, clampedY, scaledW, scaledH);
        }
    }
}
