using BepInEx.Configuration;
using UnityEngine;

namespace Aether.UI.Modules
{
    /// <summary>
    /// Blueprint's public surface for the shell, keybind manager, performance modules and diagnostics.
    /// The browser/catalog/recipe card live in Aether.Mods.Blueprint; the shell never references its
    /// runtime types.
    /// </summary>
    public interface IBlueprintUiModule : IAetherUiModule
    {
        ConfigEntry<bool> Enabled { get; }
        ConfigEntry<bool> AutoOpenWithInventory { get; }
        ConfigEntry<string> SavedBookmarks { get; }
        ConfigEntry<KeyCode> ToggleKey { get; }
        ConfigEntry<KeyCode> RecipeKey { get; }
        ConfigEntry<KeyCode> UsageKey { get; }
        ConfigEntry<KeyCode> BookmarkKey { get; }

        bool IsVisible { get; }
        bool IsRecipeCardOpen { get; }

        bool IsMouseOverPanel();
        void CloseRecipeCard();
        void DrawGui();
        void DrawSettings(Rect rightRect, float settingsBaseY);
        bool RunQaDiagnostics();
    }
}
