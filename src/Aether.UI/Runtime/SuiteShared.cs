using System.Collections;
using System.Collections.Generic;
using Aether.UI.Modules;
using BepInEx.Configuration;
using BepInEx.Logging;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using BlockStoryLens;
using UnityEngine;

namespace Aether.UI
{
    /// <summary>
    /// Shared services and drawing primitives available to every per-mod UI runtime.
    /// Gameplay modules reference this facade instead of reaching into the suite plugin, so the
    /// shell can keep its internals private and a mod can be removed without breaking the others.
    /// </summary>
    public static class SuiteShared
    {
        private static BlockStorySuitePlugin Shell => BlockStorySuitePlugin.SharedShell;

        /// <summary>
        /// True while the keybind manager (F7) is open: it owns the keyboard (rebinding, search), so mods must
        /// ignore their own hotkeys instead of reacting to the keys the user is assigning.
        /// </summary>
        public static bool KeybindCaptureActive => Shell != null && Shell.isKeybindsManagerOpen;

        public static ConfigFile Config => Shell != null ? Shell.SharedConfig : null;
        public static ManualLogSource Log => BlockStorySuitePlugin.Log;

        public static bool IsSpanish() => BlockStorySuitePlugin.IsSpanish();
        public static string Tr(string en, string es) => BlockStorySuitePlugin.Tr(en, es);

        public static Camera CachedCamera => Shell != null ? Shell.SharedCamera : null;
        public static Digger CachedDigger => Shell != null ? Shell.SharedCachedDigger : null;
        public static bool MapCave => Shell != null && Shell.SharedMapCave;
        public static bool IsIndoors => Shell != null && Shell.SharedIsIndoors;
        public static bool IsUnderground => Shell != null && Shell.SharedIsUnderground;
        public static List<Health> MobCache => Shell != null ? Shell.SharedMobCache : null;

        public static bool IsModMenuOpen => Shell != null && Shell.SharedModMenuOpen;
        public static bool IsHudDragOpen => Shell != null && Shell.SharedHudDragOpen;
        public static bool IsKeybindManagerOpen => Shell != null && Shell.SharedKeybindManagerOpen;
        public static bool IsWayfinderOpen
            => AetherUiModuleRegistry.Wayfinder != null && AetherUiModuleRegistry.Wayfinder.IsOpen;
        public static bool IsBlueprintVisible
            => AetherUiModuleRegistry.Blueprint != null && AetherUiModuleRegistry.Blueprint.IsVisible;

        public static float ModalOpenedTimestamp
        {
            get => Shell != null ? Shell.SharedModalOpened : 0f;
            set { if (Shell != null) Shell.SharedModalOpened = value; }
        }

        public static float ModalClosedTimestamp
        {
            get => Shell != null ? Shell.SharedModalClosed : 0f;
            set { if (Shell != null) Shell.SharedModalClosed = value; }
        }

        public static GUIStyle TitleStyle => Shell != null ? Shell.SharedTitleStyle : null;
        public static GUIStyle MenuTitleStyle => Shell != null ? Shell.SharedMenuTitleStyle : null;
        public static GUIStyle ToolStyle => Shell != null ? Shell.SharedToolStyle : null;
        public static GUIStyle SmallStyle => Shell != null ? Shell.SharedSmallStyle : null;
        public static GUIStyle MenuHeaderStyle => Shell != null ? Shell.SharedMenuHeaderStyle : null;
        public static GUIStyle ModNameStyle => Shell != null ? Shell.SharedModNameStyle : null;
        public static GUIStyle AuthorStyle => Shell != null ? Shell.SharedAuthorStyle : null;
        public static GUIStyle DescStyle => Shell != null ? Shell.SharedDescStyle : null;
        public static GUIStyle SectionTitleStyle => Shell != null ? Shell.SharedSectionTitleStyle : null;
        public static GUIStyle BadgeStyle => Shell != null ? Shell.SharedBadgeStyle : null;
        public static GUIStyle WoodBtnTextStyle => Shell != null ? Shell.SharedWoodBtnTextStyle : null;
        public static GUIStyle MinimapLabelStyle => Shell != null ? Shell.SharedMinimapLabelStyle : null;
        public static GUIStyle TextFieldStyle => Shell != null ? Shell.SharedTextFieldStyle : null;
        public static GUIStyle SourceStyle => Shell != null ? Shell.SharedSourceStyle : null;

        public static float LiveFps => Aether.UI.Diagnostics.PerfMetrics.LiveFps;
        public static float LiveFrameTimeMs => Aether.UI.Diagnostics.PerfMetrics.FrameTimeMs;

        public static bool MoveDiamondsUnderXP => BlockStorySuitePlugin.SharedMoveDiamondsUnderXP;
        public static bool MoveTopRightButtonsToBottomRight
            => BlockStorySuitePlugin.SharedMoveTopRightButtonsToBottomRight;

        public static Texture2D GetColorTexture(Color color) => BlockStorySuitePlugin.GetColorTexture(color);

        public static ThemePalette GetThemePalette(string theme) => HudCustomizerModule.GetThemePalette(theme);

        public static void ApplyFontToSharedStyles(Font font)
        {
            if (Shell != null) Shell.SharedApplyFont(font);
        }

        public static void DrawShadowLabel(Rect rect, string text, GUIStyle style, Color textColor, Color shadowColor)
            => BlockStorySuitePlugin.DrawShadowLabel(rect, text, style, textColor, shadowColor);

        public static bool DrawNativeWoodButton(Rect rect, string text, bool isSelected = false, Color? baseTint = null)
            => Shell != null && Shell.SharedDrawNativeWoodButton(rect, text, isSelected, baseTint);

        public static void DrawWoodPanel(Rect rect, float opacity = 1.0f, Color? tintOverride = null)
        {
            if (Shell != null) Shell.SharedDrawWoodPanel(rect, opacity, tintOverride);
        }

        public static void DrawThemedPanel(Rect rect, string theme, float opacity = 1.0f)
        {
            if (Shell != null) Shell.SharedDrawThemedPanel(rect, theme, opacity);
        }

        public static void DrawDarkSlotBox(Rect rect, bool selected = false, bool hovered = false)
        {
            if (Shell != null) Shell.SharedDrawDarkSlotBox(rect, selected, hovered);
        }

        public static void DrawInvSlot(Rect rect, Color? tint = null)
        {
            if (Shell != null) Shell.SharedDrawInvSlot(rect, tint);
        }

        public static bool DrawNativeSprite(Rect rect, string spriteName, Color? tint = null)
            => BlockStorySuitePlugin.SharedDrawNativeSprite(rect, spriteName, tint);

        public static bool DrawItemIcon(Rect iconRect, InvBaseItem baseItem, Color? overrideColor = null)
            => BlockStorySuitePlugin.SharedDrawItemIcon(iconRect, baseItem, overrideColor);

        public static void AcquireModalIsolation()
        {
            if (Shell != null) Shell.SharedAcquireModalIsolation();
        }

        public static void ReleaseModalIsolation()
        {
            if (Shell != null) Shell.SharedReleaseModalIsolation();
        }

        public static Inventory GetInventory() => Shell != null ? Shell.SharedGetInventory() : null;

        public static InvGameItem GetHoveredSlotItem()
            => Shell != null ? Shell.SharedGetHoveredSlotItem() : null;

        public static InvBaseItem SafeFindItemByName(string name)
            => BlockStorySuitePlugin.SafeFindItemByName(name);

        public static InvBaseItem ResolveBlockItem(string blockTypeName)
            => BlockStorySuitePlugin.SharedResolveBlockItem(blockTypeName);

        public static string GetLocalizedBlockName(string rawBlockName, InvBaseItem baseItem)
            => BlockStorySuitePlugin.SharedGetLocalizedBlockName(rawBlockName, baseItem);

        public static IWorld GetActiveWorld() => Shell != null ? Shell.SharedGetActiveWorld() : null;

        public static void RefreshMobCache(Vector3 playerPos, float maxDist)
        {
            if (Shell != null) Shell.SharedRefreshMobCache(playerPos, maxDist);
        }

        public static Coroutine StartCoroutine(IEnumerator routine)
            => Shell != null ? Shell.SharedStartCoroutine(routine) : null;

        public static void CloseModMenu()
        {
            if (Shell != null) Shell.SharedCloseModMenu();
        }

        public static void CloseHudDrag()
        {
            if (Shell != null) Shell.SharedCloseHudDrag();
        }

        public static void CloseKeybinds()
        {
            if (Shell != null) Shell.SharedCloseKeybinds();
        }
    }
}
