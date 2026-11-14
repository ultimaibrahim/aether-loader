using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using UnityEngine;

namespace BlockStoryLens
{
    /// <summary>
    /// Public surface of the UI shell used by the per-mod runtimes. Keeping these wrappers in a
    /// partial file means the shell's private members stay private while a module still gets a
    /// stable, documented service contract through <see cref="Aether.UI.SuiteShared"/>.
    /// </summary>
    public partial class BlockStorySuitePlugin
    {
        public static BlockStorySuitePlugin SharedShell => Instance;

        public ConfigFile SharedConfig => Config;
        public Camera SharedCamera => cachedCamera;
        public bool SharedMapCave => false;      // owned by Aether Wayfinder (WayfinderCaves)
        public bool SharedIsIndoors => false;
        public bool SharedIsUnderground => cachedIsUnderground;
        public List<Health> SharedMobCache => new List<Health>();
        public Digger SharedCachedDigger => cachedDigger;

        public void SharedApplyFont(Font font)
        {
            if (font == null) return;
            if (titleStyle != null) titleStyle.font = font;
            if (toolStyle != null) toolStyle.font = font;
            if (authorStyle != null) authorStyle.font = font;
            if (richTooltipStyle != null) richTooltipStyle.font = font;
        }

        public float SharedModalOpened
        {
            get => modalOpenedTimestamp;
            set => modalOpenedTimestamp = value;
        }

        public float SharedModalClosed
        {
            get => modalClosedTimestamp;
            set => modalClosedTimestamp = value;
        }

        public bool SharedModMenuOpen => isModMenuOpen;
        public bool SharedHudDragOpen => isDraggingHudElements;
        public bool SharedKeybindManagerOpen => isKeybindsManagerOpen;

        public GUIStyle SharedTitleStyle => titleStyle;
        public GUIStyle SharedMenuTitleStyle => menuTitleStyle;
        public GUIStyle SharedToolStyle => toolStyle;
        public GUIStyle SharedSmallStyle => smallStyle;
        public GUIStyle SharedMenuHeaderStyle => menuHeaderStyle;
        public GUIStyle SharedModNameStyle => modNameStyle;
        public GUIStyle SharedAuthorStyle => authorStyle;
        public GUIStyle SharedDescStyle => descStyle;
        public GUIStyle SharedSectionTitleStyle => sectionTitleStyle;
        public GUIStyle SharedBadgeStyle => badgeStyle;
        public GUIStyle SharedWoodBtnTextStyle => woodBtnTextStyle;
        public GUIStyle SharedMinimapLabelStyle => minimapLabelStyle;
        public GUIStyle SharedTextFieldStyle => textFieldStyle;
        public GUIStyle SharedSourceStyle => sourceStyle;

        public static bool SharedMoveDiamondsUnderXP => MoveDiamondsUnderXP != null && MoveDiamondsUnderXP.Value;
        public static bool SharedMoveTopRightButtonsToBottomRight
            => MoveTopRightButtonsToBottomRight != null && MoveTopRightButtonsToBottomRight.Value;

        public bool SharedDrawNativeWoodButton(Rect rect, string text, bool isSelected = false, Color? baseTint = null)
            => DrawNativeWoodButton(rect, text, isSelected, baseTint);

        public void SharedDrawWoodPanel(Rect rect, float opacity = 1.0f, Color? tintOverride = null)
            => DrawWoodPanel(rect, opacity, tintOverride);

        public void SharedDrawThemedPanel(Rect rect, string theme, float opacity = 1.0f)
            => DrawThemedPanel(rect, theme, opacity);

        public void SharedDrawDarkSlotBox(Rect rect, bool selected = false, bool hovered = false)
            => DrawDarkSlotBox(rect, selected, hovered);

        public void SharedDrawInvSlot(Rect rect, Color? tint = null)
            => DrawInvSlot(rect, tint);

        public static bool SharedDrawNativeSprite(Rect rect, string spriteName, Color? tint = null)
            => DrawNativeSprite(rect, spriteName, tint);

        public static bool SharedDrawItemIcon(Rect iconRect, InvBaseItem baseItem, Color? overrideColor = null)
            => DrawItemIcon(iconRect, baseItem, overrideColor);

        public void SharedAcquireModalIsolation() => AcquireModalIsolation();
        public void SharedReleaseModalIsolation() => ReleaseModalIsolation();

        public Inventory SharedGetInventory() => GetInventory();
        public InvGameItem SharedGetHoveredSlotItem() => GetHoveredSlotItem();

        public static InvBaseItem SharedResolveBlockItem(string blockTypeName) => ResolveBlockItem(blockTypeName);

        public static string SharedGetLocalizedBlockName(string rawBlockName, InvBaseItem baseItem)
            => GetLocalizedBlockName(rawBlockName, baseItem);

        public IWorld SharedGetActiveWorld() => GetActiveWorld();

        public void SharedRefreshMobCache(Vector3 playerPos, float maxDist) { }

        public Coroutine SharedStartCoroutine(IEnumerator routine) => StartCoroutine(routine);

        public void SharedCloseModMenu() => SetModMenuOpen(false);
        public void SharedCloseHudDrag() => SetDraggingHudElements(false);
        public void SharedCloseKeybinds() => SetKeybindsManagerOpen(false);
    }
}
