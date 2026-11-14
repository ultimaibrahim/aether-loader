using System;
using System.Text;
using Aether.Nano;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace Aether.Mods.Blueprint
{
    /// <summary>
    /// Blueprint lifecycle and what it takes from Aether Nano: its config and logger (set by <see cref="BlueprintPlugin"/>),
    /// styles, panels and the inventory. The browser, catalog and recipe card are owned here.
    /// </summary>
    public sealed partial class BlueprintModule
    {
        public static ConfigFile Config { get; internal set; }
        public static ManualLogSource Log { get; internal set; }

        private static GUIStyle AuthorStyle => NanoStyles.Author;
        private static GUIStyle DescStyle => NanoStyles.Desc;
        private static GUIStyle MenuHeaderStyle => NanoStyles.MenuHeader;
        private static GUIStyle MenuTitleStyle => NanoStyles.MenuTitle;
        private static GUIStyle ModNameStyle => NanoStyles.ModName;
        private static GUIStyle SectionTitleStyle => NanoStyles.SectionTitle;
        private static GUIStyle ToolStyle => NanoStyles.Tool;

        // A host menu (Aether's F10, its HUD editor, Wayfinder's map...) covers Blueprint.
        private static bool IsModMenuOpen => NanoHud.OverlayOpen;
        private static bool IsWayfinderOpen => false;
        private static bool IsHudDragOpen => false;
        private static bool KeybindCaptureActive => NanoHud.KeyboardCaptured;
        private static float ModalClosedTimestamp { get => NanoPanels.LastClosed; set => NanoPanels.LastClosed = value; }

        private static Inventory GetInventory() => Aether.Nano.Nano.Inventory;
        private static InvGameItem GetHoveredSlotItem() => NanoItems.HoveredSlotItem();
        private static InvBaseItem SafeFindItemByName(string name) => NanoItems.FindByName(name);
        private static bool DrawItemIcon(Rect rect, InvBaseItem item, Color? tint = null) => NanoItems.DrawIcon(rect, item, tint);
        public static BlueprintModule Current { get; private set; }

        public string Id => "aether_blueprint";
        public bool IsInitialized { get; private set; }

        public void Initialize()
        {
            if (IsInitialized) return;
            Current = this;
            InitBlueprintModule();
            IsInitialized = true;
        }

        public void OnUpdate() => UpdateBlueprintModule();

        public void OnLateUpdate()
        {
        }

        public void OnSceneChanged(string sceneName)
        {
            isBlueprintVisible = false;
            isRecipeCardOpen = false;
        }

        public bool IsModalOpen => isRecipeCardOpen;

        public void ForceClose()
        {
            isBlueprintVisible = false;
            isRecipeCardOpen = false;
        }

        public bool IsVisible => isBlueprintVisible;
        public bool IsRecipeCardOpen => isRecipeCardOpen;
        public bool MouseOverPanel() => IsMouseOverBlueprintPanel();
        public void DrawGui() => DrawBlueprintGUI();

        /// <summary>
        /// Self-test used by the shared QA runner: indexes the catalog/recipes and verifies that
        /// real crafting data is present (not just an empty UI shell).
        /// </summary>
        public bool RunQaDiagnostics()
        {
            try
            {
                EnsureBlueprintItemsIndexed();
                EnsureRecipesIndexed();

                int itemCount = blueprintAllItems != null ? blueprintAllItems.Count : 0;
                int resKeys = recipesByResult != null ? recipesByResult.Count : 0;
                int ingKeys = recipesByIngredient != null ? recipesByIngredient.Count : 0;
                Log.LogInfo($"[QA-DIAGNOSTIC] [1/7 Blueprint Module] Items indexed: {itemCount} | " +
                            $"Result keys: {resKeys} | Ingredient keys: {ingKeys}");

                string[] testItems = { "Dig", "Stone Pick Axe", "Stone Pickaxe", "Pickaxe Stone", "Pick Axe Stone", "Pickaxe" };
                int recipesFound = 0;

                foreach (string term in testItems)
                {
                    if (recipesByResult != null && recipesByResult.TryGetValue(term, out var list) && list.Count > 0)
                    {
                        foreach (var entry in list)
                        {
                            recipesFound++;
                            StringBuilder ingredients = new StringBuilder();
                            if (entry.recipe?.ingredients != null)
                            {
                                for (int r = 0; r < entry.recipe.ingredients.GetLength(0); r++)
                                {
                                    for (int c = 0; c < entry.recipe.ingredients.GetLength(1); c++)
                                    {
                                        var ing = entry.recipe.ingredients[r, c];
                                        if (ing != null && !string.IsNullOrEmpty(ing.name))
                                        {
                                            if (ingredients.Length > 0) ingredients.Append(", ");
                                            ingredients.Append($"{ing.name}x{ing.count}");
                                        }
                                    }
                                }
                            }
                            Log.LogInfo($"[QA-DIAGNOSTIC] [1/7 Blueprint Module] VERIFIED CRAFTING RECIPE for '{term}': " +
                                        $"Result='{entry.recipe?.name ?? "null"}', Station='{entry.stationName}', " +
                                        $"Ingredients=[{ingredients}]");
                        }
                    }

                    if (recipesByIngredient != null && recipesByIngredient.TryGetValue(term, out var uses) && uses.Count > 0)
                    {
                        Log.LogInfo($"[QA-DIAGNOSTIC] [1/7 Blueprint Module] VERIFIED USES for '{term}': " +
                                    $"Used as ingredient in {uses.Count} recipe(s)!");
                    }
                }

                if (recipesFound == 0 && blueprintAllItems != null)
                {
                    foreach (var item in blueprintAllItems)
                    {
                        if (item == null) continue;
                        if (item.name.IndexOf("Pick", StringComparison.OrdinalIgnoreCase) < 0 &&
                            item.name.IndexOf("Dig", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        if (recipesByResult != null && recipesByResult.TryGetValue(item.name, out var list) && list.Count > 0)
                        {
                            recipesFound++;
                            Log.LogInfo($"[QA-DIAGNOSTIC] [1/7 Blueprint Module] Matched Pick Tool '{item.name}' " +
                                        $"with {list.Count} crafting recipe(s)!");
                        }
                    }
                }

                bool pass = itemCount > 0 && resKeys > 0 && recipesFound > 0;
                Log.LogInfo($"[QA-DIAGNOSTIC] [1/7 Blueprint Module] {(pass ? "PASS" : "FAIL")}: " +
                            $"Catalog & Recipe Indexer operational (Matched recipes: {recipesFound}).");
                return pass;
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-DIAGNOSTIC] [1/7 Blueprint Module] FAIL: {ex}");
                return false;
            }
        }
    }
}
