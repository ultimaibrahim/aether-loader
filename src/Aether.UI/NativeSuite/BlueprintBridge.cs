using BepInEx.Configuration;
using UnityEngine;

namespace BlockStoryLens
{
    /// <summary>
    /// Aether Blueprint is a Nano mod since 1.1.0: it draws its browser and recipe card and owns its [Blueprint] settings.
    /// The shell reads those settings and its panel state through Nano, so its tooltips and modals respect it.
    /// Everything is null/false without Blueprint.
    /// </summary>
    public partial class BlockStorySuitePlugin
    {
        public const string BlueprintGuid = "dev.ultimaibrahim.aether.blueprint";

        private static ConfigEntry<T> BlueprintSetting<T>(string key) => Aether.Nano.NanoPages.Setting<T>(BlueprintGuid, "Blueprint", key);

        public static ConfigEntry<bool> BlueprintEnabled => BlueprintSetting<bool>("Enabled");
        public static ConfigEntry<bool> BlueprintAutoOpenWithInventory => BlueprintSetting<bool>("AutoOpenWithInventory");
        public static ConfigEntry<string> BlueprintSavedBookmarks => BlueprintSetting<string>("SavedBookmarks");
        public static ConfigEntry<KeyCode> BlueprintToggleKey => BlueprintSetting<KeyCode>("ToggleKey");
        public static ConfigEntry<KeyCode> BlueprintRecipeKey => BlueprintSetting<KeyCode>("RecipeLookupKey");
        public static ConfigEntry<KeyCode> BlueprintUsageKey => BlueprintSetting<KeyCode>("UsageLookupKey");
        public static ConfigEntry<KeyCode> BlueprintBookmarkKey => BlueprintSetting<KeyCode>("BookmarkKey");

        public static bool isBlueprintVisible
        {
            get => Aether.Nano.NanoPanels.IsOpen(BlueprintGuid);
            set { if (!value) Aether.Nano.NanoPanels.Find(BlueprintGuid)?.Close?.Invoke(); }
        }

        public static bool isRecipeCardOpen
        {
            get => Aether.Nano.NanoPanels.IsModalOpen(BlueprintGuid);
            set { if (!value && isRecipeCardOpen) Aether.Nano.NanoPanels.Find(BlueprintGuid)?.Close?.Invoke(); }
        }

        public static bool IsMouseOverBlueprintPanel() => Aether.Nano.NanoPanels.MouseOverAny;

        public void InitBlueprintAndSodium() => InitPerformanceSuite();
        public void UpdateBlueprintAndSodium() => UpdatePerformanceSuite();
        public void DrawBlueprintAndSodiumGUI() => DrawPerformanceSuiteGUI();
        public void DrawBlueprintModMenuSettings(Rect rightRect, float baseY = 230f) { }
        public bool RunBlueprintQaDiagnostics() => isBlueprintVisible || BlueprintEnabled != null; // Blueprint checks itself
    }
}
