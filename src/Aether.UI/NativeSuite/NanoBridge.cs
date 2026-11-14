using System;
using Aether.Loader;
using Aether.Nano;
using Aether.Registry;
using UnityEngine;

namespace BlockStoryLens
{
    /// <summary>
    /// What Aether tells Aether Nano, so the mods built on it fit around Aether: where the game's top HUD ends, when Aether's
    /// menus cover the HUD or own the keyboard, which mod owns a block/item/creature, the player's UI theme, and how to open
    /// Aether's HUD editor. Without Aether, Nano uses its own defaults.
    /// </summary>
    public partial class BlockStorySuitePlugin
    {
        private bool nanoFontApplied;

        private void ConnectNano()
        {
            NanoHud.TopInset = () => CalculateSafeTopOffset();
            NanoHud.TopLeftInset = () => MoveDiamondsUnderXP != null && MoveDiamondsUnderXP.Value ? 160f : 24f;
            // Only Aether's own menus: a mod's panel (Blueprint's recipe card) must not hide that same mod.
            NanoHud.HostOverlayOpen = () => isModMenuOpen || isWayfinderOpen || isDraggingHudElements || isKeybindsManagerOpen || showKeybindsModal;
            NanoHud.HostKeyboardCaptured = () => isKeybindsManagerOpen || rebindingEntry != null;
            NanoHud.OpenHostEditor = () => { SetModMenuOpen(false); SetDraggingHudElements(true); };
            NanoHud.CloseHostMenus = () =>
            {
                if (isModMenuOpen) SetModMenuOpen(false);
                if (isDraggingHudElements) SetDraggingHudElements(false);
                if (isKeybindsManagerOpen) SetKeybindsManagerOpen(false);
            };
            NanoHud.DiamondsUnderXp = () => MoveDiamondsUnderXP != null && MoveDiamondsUnderXP.Value;
            NanoHud.TopRightButtonsMoved = () => MoveTopRightButtonsToBottomRight != null && MoveTopRightButtonsToBottomRight.Value;

            NanoOwners.ItemOwner = Aether.Loader.Hosting.ExternalModCatalog.ResolveItemOwner;
            NanoOwners.EntityOwner = Aether.Loader.Hosting.ExternalModCatalog.ResolveEntityOwner;
            NanoOwners.BlockOwner = BlockOwner;
        }

        /// <summary>The mod behind a custom block id: Aether's block registry namespace, shown by the mod's name when it is loaded.</summary>
        private static string BlockOwner(ushort id)
        {
            if (!DynamicBlockRegistry.Instance.TryGetIdentifier(id, out Identifier ident)) return null;
            string ns = ident.Namespace;
            foreach (var mod in AetherBootstrap.LoadedMods)
            {
                if (mod == null) continue;
                if (string.Equals(mod.Id, ns, StringComparison.OrdinalIgnoreCase) || string.Equals(mod.Id, "aether_" + ns, StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(mod.Name) ? ns : mod.Name;
            }
            return string.IsNullOrEmpty(ns) ? null : char.ToUpperInvariant(ns[0]) + ns.Substring(1);
        }

        /// <summary>The game's Zantroke font on Aether's own styles (Lens used to do this for the whole suite).</summary>
        private void ApplyNanoFont()
        {
            if (nanoFontApplied || titleStyle == null) return;
            Font font = NanoStyles.NativeFont;
            if (font == null) return;
            SharedApplyFont(font);
            nanoFontApplied = true;
        }
    }
}
