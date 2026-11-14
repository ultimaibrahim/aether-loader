#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Linq;
using Aether.Nano;
using BepInEx.Configuration;
using UnityEngine;
using static Aether.Nano.Nano;
using static Aether.Nano.NanoUi;
using UnityEngine.SceneManagement;
using com.mindblocks.i18n;

namespace Aether.Mods.Blueprint
{
    /// <summary>
    /// Roughly Enough Items (Blueprint) Recipe & Item Catalog Module.
    /// Provides in-game recipe browser, item grid, categories, search, bookmarks, and recipe explorer (R / U hotkeys).
    /// </summary>
    public sealed partial class BlueprintModule 
    {
        // =====================================================================
        //                         CONFIG ENTRIES
        // =====================================================================
        public static ConfigEntry<bool> BlueprintEnabled { get; private set; }
        public static ConfigEntry<bool> BlueprintAutoOpenWithInventory { get; private set; }
        public static ConfigEntry<string> BlueprintSavedBookmarks { get; private set; }
        public static ConfigEntry<KeyCode> BlueprintToggleKey { get; private set; }
        public static ConfigEntry<KeyCode> BlueprintRecipeKey { get; private set; }
        public static ConfigEntry<KeyCode> BlueprintUsageKey { get; private set; }
        public static ConfigEntry<KeyCode> BlueprintBookmarkKey { get; private set; }

        // =====================================================================
        //                           Blueprint STATE & DATA
        // =====================================================================
        public enum BlueprintCategory
        {
            All,
            Blocks,
            Weapons,
            Tools,
            Armor,
            Consumables,
            Materials
        }

        public class BlueprintRecipeEntry
        {
            public Recipe recipe;
            public string stationName = "Crafting Table";
            public string stationNameEs = "Mesa de Crafteo";
            public string categoryName = "";
        }

        private static bool blueprintInitialized = false;
        public static bool isBlueprintVisible { get; set; } = false;
        private static bool wasInventoryOpenLastFrame = false;
        private static string blueprintSearchQuery = "";
        private static BlueprintCategory blueprintSelectedCategory = BlueprintCategory.All;
        private static int blueprintCurrentPage = 0;
        private const int BlueprintCols = 6;
        private const int BlueprintRows = 7;
        private const int BlueprintItemsPerPage = BlueprintCols * BlueprintRows; // 42 items per page

        private static readonly List<InvBaseItem> blueprintAllItems = new List<InvBaseItem>();
        private static readonly List<InvBaseItem> blueprintFilteredItems = new List<InvBaseItem>();
        private static readonly HashSet<string> blueprintBookmarkedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly BlueprintCategory[] BlueprintCategoriesAll = (BlueprintCategory[])Enum.GetValues(typeof(BlueprintCategory));

        // Recipe index
        private static readonly Dictionary<string, List<BlueprintRecipeEntry>> recipesByResult = new Dictionary<string, List<BlueprintRecipeEntry>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<BlueprintRecipeEntry>> recipesByIngredient = new Dictionary<string, List<BlueprintRecipeEntry>>(StringComparer.OrdinalIgnoreCase);
        private static bool recipesIndexed = false;
        private static string recipesIndexedScene = null;
        private static float lastRecipeIndexAttemptTime = 0f;
        private static readonly List<string> allKnownPlankNames = new List<string>();

        // Recipe Explorer Card modal
        public static bool isRecipeCardOpen { get; set; } = false;
        private static InvBaseItem activeRecipeItem = null;
        private static int activeRecipeTab = 0; // 0 = Crafting (R), 1 = Used In (U)
        private static int activeRecipeIndex = 0;
        private static Vector2 usedInScrollPos = Vector2.zero;
        private static readonly Stack<Tuple<InvBaseItem, int>> recipeHistory = new Stack<Tuple<InvBaseItem, int>>();

        // Topmost Tooltip Rendering
        private static InvBaseItem hoveredTopmostItem = null;
        private static Rect hoveredTopmostRect = default;
        private static string hoveredTopmostText = null;

        // =====================================================================
        //                      INITIALIZATION & CONFIG
        // =====================================================================
        public void InitBlueprintModule()
        {
            if (IsInitialized) return;
            try
            {
                // Blueprint owns its [Blueprint] section. The canonical key names match the ones the keybind
                // manager used before the split (ToggleKey/RecipeLookupKey/UsageLookupKey/BookmarkKey),
                // so existing configs keep their values.
                BlueprintEnabled = Config.Bind("Blueprint", "Enabled", true, "Enable Roughly Enough Items (Blueprint) recipe catalog and item browser.");
                BlueprintToggleKey = Config.Bind("Blueprint", "ToggleKey", KeyCode.O, "Hotkey to toggle Blueprint item browser overlay.");
                BlueprintRecipeKey = Config.Bind("Blueprint", "RecipeLookupKey", KeyCode.R, "Hotkey to view Crafting Recipe (R) for hovered item.");
                BlueprintUsageKey = Config.Bind("Blueprint", "UsageLookupKey", KeyCode.U, "Hotkey to view Uses (U) for hovered item.");
                BlueprintBookmarkKey = Config.Bind("Blueprint", "BookmarkKey", KeyCode.A, "Hotkey to toggle Bookmarking (A) for hovered item.");
                BlueprintAutoOpenWithInventory = Config.Bind("Blueprint", "AutoOpenWithInventory", true, "Automatically open Blueprint whenever the player opens Inventory, Crafting Table, Chests, etc.");
                BlueprintSavedBookmarks = Config.Bind("Blueprint", "SavedBookmarks", "", "Semicolon-separated list of pinned bookmarked item names.");

                LoadBlueprintBookmarks();
                Log.LogInfo("[Blueprint] Module initialized (own assembly, per-mod config section).");
            }
            catch (Exception ex)
            {
                Log.LogError($"[Blueprint] Error initializing: {ex}");
            }
        }

        private static void LoadBlueprintBookmarks()
        {
            blueprintBookmarkedNames.Clear();
            string raw = BlueprintSavedBookmarks?.Value;
            if (string.IsNullOrEmpty(raw)) return;

            string[] items = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < items.Length; i++)
            {
                string trim = items[i].Trim();
                if (!string.IsNullOrEmpty(trim))
                {
                    blueprintBookmarkedNames.Add(trim);
                }
            }
        }

        private static void SaveBlueprintBookmarks()
        {
            if (BlueprintSavedBookmarks != null)
            {
                BlueprintSavedBookmarks.Value = string.Join(";", blueprintBookmarkedNames.ToArray());
                Config.Save();
            }
        }

        public static bool IsMouseOverBlueprintPanel()
        {
            if (BlueprintEnabled == null || !BlueprintEnabled.Value) return false;

            Vector2 mPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

            if (isRecipeCardOpen)
            {
                float cardW = 440f;
                float cardH = 390f;
                float cardX = (Screen.width - cardW) * 0.5f;
                float cardY = (Screen.height - cardH) * 0.5f;
                if (new Rect(cardX, cardY, cardW, cardH).Contains(mPos)) return true;
            }

            if (isBlueprintVisible)
            {
                float blueprintWidth = 312f;
                float blueprintHeight = Mathf.Min(680f, Screen.height - 48f);
                float blueprintX = Screen.width - blueprintWidth - 14f;
                float blueprintY = (Screen.height - blueprintHeight) * 0.5f;
                if (new Rect(blueprintX, blueprintY, blueprintWidth, blueprintHeight).Contains(mPos)) return true;
            }

            return false;
        }

        // =====================================================================
        //                 PART A: Blueprint DATABASE & RECIPE INDEXING
        // =====================================================================
        private static void EnsureBlueprintItemsIndexed()
        {
            if (blueprintInitialized && blueprintAllItems.Count > 0) return;

            InvDatabase[] dbs = InvDatabase.list;
            if (dbs == null || dbs.Length == 0) return;

            blueprintAllItems.Clear();
            allKnownPlankNames.Clear();
            HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < dbs.Length; i++)
            {
                InvDatabase db = dbs[i];
                if (db == null || db.items == null) continue;

                for (int j = 0; j < db.items.Count; j++)
                {
                    InvBaseItem item = db.items[j];
                    if (item == null || string.IsNullOrEmpty(item.name)) continue;
                    if (item.category == InvBaseItem.CreativeCategory.Hide) continue;

                    if (!seenNames.Contains(item.name))
                    {
                        seenNames.Add(item.name);
                        blueprintAllItems.Add(item);

                        if (item.name.EndsWith("Plank", StringComparison.OrdinalIgnoreCase))
                        {
                            allKnownPlankNames.Add(item.name);
                        }
                    }
                }
            }

            // Sort alphabetically by localized name
            blueprintAllItems.Sort((a, b) =>
            {
                string na = T.ItemNames(a.name) ?? a.name;
                string nb = T.ItemNames(b.name) ?? b.name;
                return string.Compare(na, nb, StringComparison.CurrentCultureIgnoreCase);
            });

            blueprintInitialized = true;
            ApplyBlueprintFilter();
        }

        private static void AddResultEntry(string key, BlueprintRecipeEntry entry)
        {
            if (string.IsNullOrEmpty(key)) return;
            string trimmed = key.Trim();
            if (string.IsNullOrEmpty(trimmed)) return;

            if (!recipesByResult.TryGetValue(trimmed, out var list))
            {
                list = new List<BlueprintRecipeEntry>();
                recipesByResult[trimmed] = list;
            }
            if (!list.Exists(e => e.recipe == entry.recipe))
            {
                list.Add(entry);
            }
        }

        private static void AddIngredientEntry(string key, BlueprintRecipeEntry entry)
        {
            if (string.IsNullOrEmpty(key)) return;
            string trimmed = key.Trim();
            if (string.IsNullOrEmpty(trimmed)) return;

            if (!recipesByIngredient.TryGetValue(trimmed, out var list))
            {
                list = new List<BlueprintRecipeEntry>();
                recipesByIngredient[trimmed] = list;
            }
            if (!list.Exists(e => e.recipe == entry.recipe))
            {
                list.Add(entry);
            }
        }

        private static void IndexRecipe(Recipe r, string stationEn, string stationEs, string catName)
        {
            if (r == null) return;

            // Never mutate the game's live Recipe objects. The previous "fix up result" code
            // replaced r.result with a synthesised item, permanently rewriting crafting output.

            // Determine if timed smelting (Furnace)
            if (r.minutes > 0.01 && stationEn == "Crafting Table")
            {
                stationEn = "Furnace";
                stationEs = "Horno";
            }

            BlueprintRecipeEntry entry = new BlueprintRecipeEntry
            {
                recipe = r,
                stationName = stationEn,
                stationNameEs = stationEs,
                categoryName = catName
            };

            // 1. Index Result. Only the actual product is a result key: Block Story recipe names
            // are verbs ("Dig", "Chop"), so indexing r.name made recipesByResult["Dig"] return the
            // pickaxe, shovel and axe, and the QA card printed Result='Dig' for the wrong item.
            string resBaseName = r.result?.baseItem?.name;
            string resName = r.result?.name;
            string transRes = !string.IsNullOrEmpty(resBaseName) ? T.ItemNames(resBaseName) : null;

            AddResultEntry(resBaseName, entry);
            AddResultEntry(resName, entry);
            AddResultEntry(transRes, entry);

            if (!string.IsNullOrEmpty(resBaseName)) AddResultEntry(resBaseName.Replace(" ", ""), entry);
            if (!string.IsNullOrEmpty(transRes)) AddResultEntry(transRes.Replace(" ", ""), entry);

            // 2. Index Ingredients
            if (r.ingredients != null)
            {
                for (int row = 0; row < r.ingredients.GetLength(0); row++)
                {
                    for (int col = 0; col < r.ingredients.GetLength(1); col++)
                    {
                        InvGameItem ing = r.ingredients[row, col];
                        if (ing == null) continue;

                        InvBaseItem resolvedBase = ing.baseItem != null ? ing.baseItem : (!string.IsNullOrEmpty(ing.name) ? InvDatabase.FindByName(ing.name) : null);
                        string ingBase = resolvedBase?.name;
                        string ingRaw = ing.name;
                        string ingTrans = !string.IsNullOrEmpty(ingBase) ? T.ItemNames(ingBase) : (!string.IsNullOrEmpty(ingRaw) ? T.ItemNames(ingRaw) : null);

                        AddIngredientEntry(ingBase, entry);
                        AddIngredientEntry(ingRaw, entry);
                        AddIngredientEntry(ingTrans, entry);

                        if (!string.IsNullOrEmpty(ingBase)) AddIngredientEntry(ingBase.Replace(" ", ""), entry);
                        if (!string.IsNullOrEmpty(ingTrans)) AddIngredientEntry(ingTrans.Replace(" ", ""), entry);

                        // Wood Planks: Any plank works for wood plank recipes
                        if ((ingBase != null && ingBase.EndsWith("Plank", StringComparison.OrdinalIgnoreCase)) ||
                            (ingRaw != null && ingRaw.EndsWith("Plank", StringComparison.OrdinalIgnoreCase)) ||
                            string.Equals(ingBase, "Wood", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(ingRaw, "Wood", StringComparison.OrdinalIgnoreCase))
                        {
                            for (int p = 0; p < allKnownPlankNames.Count; p++)
                            {
                                AddIngredientEntry(allKnownPlankNames[p], entry);
                            }
                        }
                    }
                }
            }
        }

        private static void EnsureRecipesIndexed()
        {
            // Re-index per scene/world: recipe managers are rebuilt when a world loads, and latching
            // the first (possibly empty) index left Blueprint showing "no recipe" until the process restarted.
            if (recipesIndexed && recipesIndexedScene == SceneManager.GetActiveScene().name) return;
            if (Time.unscaledTime - lastRecipeIndexAttemptTime < 1.5f) return;
            lastRecipeIndexAttemptTime = Time.unscaledTime;

            EnsureBlueprintItemsIndexed();

            // Collect all RecipeManager instances across the game
            List<RecipeManager> managers = new List<RecipeManager>();
            try
            {
                RecipeManager[] all = Resources.FindObjectsOfTypeAll<RecipeManager>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] != null && !managers.Contains(all[i])) managers.Add(all[i]);
                    }
                }
            }
            catch { }

            // Check Inventory.recipeBookManager
            try
            {
                Inventory inv = Inventory.Instance;
                if (inv != null && inv.recipeBookManager != null && inv.recipeBookManager.storage != null)
                {
                    for (int s = 0; s < inv.recipeBookManager.storage.Count; s++)
                    {
                        var storage = inv.recipeBookManager.storage[s];
                        if (storage == null) continue;

                        RecipeManager rmTable = storage.table?.GetComponentInChildren<RecipeManager>(true);
                        if (rmTable != null && !managers.Contains(rmTable)) managers.Add(rmTable);

                        RecipeBookList rbl = storage.categories?.GetComponentInChildren<RecipeBookList>(true);
                        if (rbl != null && rbl.recipeManager != null && !managers.Contains(rbl.recipeManager)) managers.Add(rbl.recipeManager);
                    }
                }
            }
            catch { }

            // Check MasterDatabase timed crafting managers
            try
            {
                if (MasterDatabase.instance != null)
                {
                    if (MasterDatabase.instance.furnaceExperiment != null && !managers.Contains(MasterDatabase.instance.furnaceExperiment))
                        managers.Add(MasterDatabase.instance.furnaceExperiment);
                    if (MasterDatabase.instance.cauldronExperiment != null && !managers.Contains(MasterDatabase.instance.cauldronExperiment))
                        managers.Add(MasterDatabase.instance.cauldronExperiment);
                    if (MasterDatabase.instance.anvilExperiment != null && !managers.Contains(MasterDatabase.instance.anvilExperiment))
                        managers.Add(MasterDatabase.instance.anvilExperiment);
                }
            }
            catch { }

            // Check CraftingOutputSlot components
            try
            {
                CraftingOutputSlot[] slots = Resources.FindObjectsOfTypeAll<CraftingOutputSlot>();
                if (slots != null)
                {
                    for (int i = 0; i < slots.Length; i++)
                    {
                        if (slots[i] != null && slots[i].recipeManager != null && !managers.Contains(slots[i].recipeManager))
                        {
                            managers.Add(slots[i].recipeManager);
                        }
                    }
                }
            }
            catch { }

            int totalRecipesFound = 0;
            RecipeManager parserRm = null;

            // Ensure built-in recipes are populated on each RecipeManager
            for (int m = 0; m < managers.Count; m++)
            {
                RecipeManager rm = managers[m];
                if (rm == null) continue;
                if (parserRm == null) parserRm = rm;

                if ((rm.recipeList == null || rm.recipeList.Count == 0) && rm.categorizedRecipes != null && rm.categorizedRecipes.Count > 0)
                {
                    try { rm.AddBuiltinRecipes(); } catch { }
                }

                if (rm.recipeList != null && rm.recipeList.Count > 0)
                {
                    totalRecipesFound += rm.recipeList.Count;
                }
            }

            if (totalRecipesFound == 0 && parserRm == null)
            {
                return; // Not ready yet, retry in 1.5s
            }

            recipesByResult.Clear();
            recipesByIngredient.Clear();

            // 1. Index all RecipeManager recipe lists
            for (int m = 0; m < managers.Count; m++)
            {
                RecipeManager rm = managers[m];
                if (rm == null || rm.recipeList == null) continue;

                string stEn = "Crafting Table";
                string stEs = "Mesa de Crafteo";
                string objName = rm.gameObject != null ? rm.gameObject.name : rm.name;

                if (rm is ColoringRecipeManager || objName.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    stEn = "Coloring Table";
                    stEs = "Mesa de Tinte";
                }
                else if (objName.IndexOf("Furnace", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    stEn = "Furnace";
                    stEs = "Horno";
                }
                else if (objName.IndexOf("Cauldron", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    stEn = "Cauldron";
                    stEs = "Caldero";
                }
                else if (objName.IndexOf("Anvil", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    stEn = "Anvil";
                    stEs = "Yunque";
                }

                for (int i = 0; i < rm.recipeList.Count; i++)
                {
                    Recipe r = rm.recipeList[i];
                    IndexRecipe(r, stEn, stEs, "");
                }
            }

            // 2. Scan TextAsset recipes directly as comprehensive fallback
            try
            {
                if (parserRm != null)
                {
                    TextAsset[] textAssets = Resources.FindObjectsOfTypeAll<TextAsset>();
                    if (textAssets != null)
                    {
                        for (int t = 0; t < textAssets.Length; t++)
                        {
                            TextAsset ta = textAssets[t];
                            if (ta == null || string.IsNullOrEmpty(ta.text)) continue;

                            string raw = ta.text;
                            if (raw.IndexOf("<recipe", StringComparison.OrdinalIgnoreCase) >= 0 && raw.IndexOf("<result", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                try
                                {
                                    Recipe parsed = parserRm.parse(raw);
                                    if (parsed != null && !string.IsNullOrEmpty(parsed.name))
                                    {
                                        string stEn = "Crafting Table";
                                        string stEs = "Mesa de Crafteo";

                                        if (raw.IndexOf("type=\"Furnace\"", StringComparison.OrdinalIgnoreCase) >= 0 || parsed.minutes > 0.01)
                                        {
                                            stEn = "Furnace";
                                            stEs = "Horno";
                                        }
                                        else if (raw.IndexOf("type=\"Cauldron\"", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            stEn = "Cauldron";
                                            stEs = "Caldero";
                                        }
                                        else if (raw.IndexOf("type=\"Anvil\"", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            stEn = "Anvil";
                                            stEs = "Yunque";
                                        }
                                        else if (raw.IndexOf("type=\"Player\"", StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            stEn = "Hand Crafting";
                                            stEs = "Crafteo Manual";
                                        }

                                        IndexRecipe(parsed, stEn, stEs, "");
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            if (recipesByResult.Count == 0)
            {
                // Nothing indexed yet (world still loading): do NOT latch, retry in 1.5s.
                return;
            }

            recipesIndexed = true;
            recipesIndexedScene = SceneManager.GetActiveScene().name;
            Log.LogInfo($"[Blueprint] Comprehensive Recipe Index complete: {recipesByResult.Count} result entries and {recipesByIngredient.Count} ingredient usages indexed across all crafting stations.");
        }

        private static List<BlueprintRecipeEntry> GetRecipesForResult(InvBaseItem item)
        {
            if (item == null) return null;
            List<BlueprintRecipeEntry> results = new List<BlueprintRecipeEntry>();
            HashSet<Recipe> seen = new HashSet<Recipe>();

            void TryAdd(string key)
            {
                if (string.IsNullOrEmpty(key)) return;
                if (recipesByResult.TryGetValue(key, out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        if (e != null && e.recipe != null && !seen.Contains(e.recipe))
                        {
                            seen.Add(e.recipe);
                            results.Add(e);
                        }
                    }
                }
            }

            TryAdd(item.name);
            TryAdd(T.ItemNames(item.name));
            if (!string.IsNullOrEmpty(item.name)) TryAdd(item.name.Replace(" ", ""));
            string trans = T.ItemNames(item.name);
            if (!string.IsNullOrEmpty(trans)) TryAdd(trans.Replace(" ", ""));

            return results;
        }

        private static List<BlueprintRecipeEntry> GetRecipesForIngredient(InvBaseItem item)
        {
            if (item == null) return null;
            List<BlueprintRecipeEntry> results = new List<BlueprintRecipeEntry>();
            HashSet<Recipe> seen = new HashSet<Recipe>();

            void TryAdd(string key)
            {
                if (string.IsNullOrEmpty(key)) return;
                if (recipesByIngredient.TryGetValue(key, out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i];
                        if (e != null && e.recipe != null && !seen.Contains(e.recipe))
                        {
                            seen.Add(e.recipe);
                            results.Add(e);
                        }
                    }
                }
            }

            TryAdd(item.name);
            TryAdd(T.ItemNames(item.name));
            if (!string.IsNullOrEmpty(item.name)) TryAdd(item.name.Replace(" ", ""));
            string trans = T.ItemNames(item.name);
            if (!string.IsNullOrEmpty(trans)) TryAdd(trans.Replace(" ", ""));

            return results;
        }

        private static BlueprintCategory GetItemBlueprintCategory(InvBaseItem item)
        {
            if (item == null) return BlueprintCategory.Materials;
            if (item.category == InvBaseItem.CreativeCategory.Blocks) return BlueprintCategory.Blocks;
            if (item.category == InvBaseItem.CreativeCategory.Weapons || item.slot == InvBaseItem.Slot.Weapon) return BlueprintCategory.Weapons;
            if (item.category == InvBaseItem.CreativeCategory.Tools) return BlueprintCategory.Tools;
            if (item.category == InvBaseItem.CreativeCategory.Armor ||
                item.slot == InvBaseItem.Slot.Hat || item.slot == InvBaseItem.Slot.Shirt ||
                item.slot == InvBaseItem.Slot.Pants || item.slot == InvBaseItem.Slot.Boots ||
                item.slot == InvBaseItem.Slot.Neck || item.slot == InvBaseItem.Slot.Ring)
                return BlueprintCategory.Armor;
            if (item.category == InvBaseItem.CreativeCategory.Foods || item.consumable) return BlueprintCategory.Consumables;
            return BlueprintCategory.Materials;
        }

        private static void ApplyBlueprintFilter()
        {
            blueprintFilteredItems.Clear();
            string query = blueprintSearchQuery != null ? blueprintSearchQuery.Trim() : "";

            for (int i = 0; i < blueprintAllItems.Count; i++)
            {
                InvBaseItem item = blueprintAllItems[i];
                if (item == null) continue;

                if (blueprintSelectedCategory != BlueprintCategory.All)
                {
                    BlueprintCategory cat = GetItemBlueprintCategory(item);
                    if (cat != blueprintSelectedCategory) continue;
                }

                if (!string.IsNullOrEmpty(query))
                {
                    string rawName = item.name ?? "";
                    string transName = T.ItemNames(item.name) ?? "";

                    bool matchRaw = rawName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchTrans = transName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!matchRaw && !matchTrans) continue;
                }

                blueprintFilteredItems.Add(item);
            }

            blueprintCurrentPage = 0;
        }

        // =====================================================================
        //                      MAIN LIFECYCLE HOOKS
        // =====================================================================
        public void UpdateBlueprintModule()
        {
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == "Menu" || currentScene == "StartScene")
            {
                isBlueprintVisible = false;
                isRecipeCardOpen = false;
                return;
            }

            // Blueprint Auto-Open with Player Inventory / Crafting Table / Chest
            Inventory inv = GetInventory();
            bool isInvOpenNow = inv != null && inv.inventoryGameObject != null && inv.inventoryGameObject.activeInHierarchy;
            bool isContainerOpen = inv != null && (
                (inv.invCrafting != null && inv.invCrafting.activeInHierarchy) ||
                (inv.invCraftTable != null && inv.invCraftTable.activeInHierarchy) ||
                (inv.invChest != null && inv.invChest.activeInHierarchy) ||
                (inv.invFurnace != null && inv.invFurnace.activeInHierarchy) ||
                (inv.invCauldron != null && inv.invCauldron.activeInHierarchy));

            bool anyInvScreenActive = isInvOpenNow || isContainerOpen;

            if (BlueprintAutoOpenWithInventory.Value)
            {
                if (anyInvScreenActive && !wasInventoryOpenLastFrame)
                {
                    isBlueprintVisible = true;
                }
                else if (!anyInvScreenActive && wasInventoryOpenLastFrame && !isRecipeCardOpen)
                {
                    isBlueprintVisible = false;
                }
            }
            wasInventoryOpenLastFrame = anyInvScreenActive;

            // Toggle Blueprint manually (O key)
            if (!KeybindCaptureActive && Input.GetKeyDown(BlueprintToggleKey.Value))
            {
                isBlueprintVisible = !isBlueprintVisible;
            }

            // Hotkeys: Recipe (R), Uses (U), Bookmark (A) on Hovered Inventory Slot
            if (BlueprintEnabled.Value && (isBlueprintVisible || anyInvScreenActive) && !KeybindCaptureActive)
            {
                InvGameItem hoveredItem = GetHoveredSlotItem();
                if (hoveredItem != null && hoveredItem.baseItem != null)
                {
                    if (Input.GetKeyDown(BlueprintRecipeKey.Value))
                    {
                        OpenRecipeCard(hoveredItem.baseItem, 0);
                    }
                    else if (Input.GetKeyDown(BlueprintUsageKey.Value))
                    {
                        OpenRecipeCard(hoveredItem.baseItem, 1);
                    }
                    else if (Input.GetKeyDown(BlueprintBookmarkKey.Value))
                    {
                        ToggleBookmark(hoveredItem.baseItem.name);
                    }
                }
            }

            // Escape key closes Recipe Card or Blueprint
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (isRecipeCardOpen)
                {
                    isRecipeCardOpen = false;
                    ModalClosedTimestamp = Time.unscaledTime;
                }
                else if (isBlueprintVisible && !anyInvScreenActive)
                {
                    isBlueprintVisible = false;
                }
            }
        }

        public void DrawBlueprintGUI()
        {
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == "Menu" || currentScene == "StartScene") return;

            hoveredTopmostItem = null;
            hoveredTopmostRect = default;
            hoveredTopmostText = null;

            // Draw Blueprint Overlay & Recipe Card
            if (BlueprintEnabled.Value && !IsModMenuOpen && !IsWayfinderOpen && !IsHudDragOpen)
            {
                EnsureBlueprintItemsIndexed();
                EnsureRecipesIndexed();

                if (isBlueprintVisible)
                {
                    DrawBlueprintSidebarGUI();
                }

                if (isRecipeCardOpen && activeRecipeItem != null)
                {
                    DrawRecipeExplorerCardGUI();
                }

                // TASK B FIX: Draw rich tooltip card ON TOP OF EVERYTHING at the very end
                if (hoveredTopmostText != null)
                {
                    DrawTopmostTooltipCard(Event.current.mousePosition, hoveredTopmostText, hoveredTopmostItem);
                }
            }
        }

        // =====================================================================
        //                      Blueprint GUI RENDERING
        // =====================================================================
        private void DrawBlueprintSidebarGUI()
        {
            float blueprintWidth = 312f;
            float blueprintHeight = Mathf.Min(680f, Screen.height - 48f);
            float blueprintX = Screen.width - blueprintWidth - 14f;
            float blueprintY = (Screen.height - blueprintHeight) * 0.5f;

            Rect panelRect = new Rect(blueprintX, blueprintY, blueprintWidth, blueprintHeight);

            DrawWoodPanel(panelRect, 0.98f);
            GUI.DrawTexture(new Rect(blueprintX + 6, blueprintY + 6, blueprintWidth - 12, blueprintHeight - 12), GetColorTexture(new Color(0.08f, 0.05f, 0.03f, 0.65f)));

            float cy = blueprintY + 10f;
            string title = Tr("Blueprint  •  ITEM CATALOG", "Blueprint  •  CATÁLOGO DE ÍTEMS");
            DrawShadowLabel(new Rect(blueprintX + 12, cy, blueprintWidth - 56, 26), title, MenuHeaderStyle, new Color(0.65f, 1.0f, 0.30f), Color.black);

            Rect closeBtnRect = new Rect(blueprintX + blueprintWidth - 38, cy - 2, 28, 26);
            if (DrawNativeWoodButton(closeBtnRect, "X", false, new Color(1.0f, 0.55f, 0.55f, 1.0f)))
            {
                isBlueprintVisible = false;
                return;
            }

            cy += 30f;

            Rect searchRect = new Rect(blueprintX + 12, cy, blueprintWidth - 54, 26);
            string newQuery = GUI.TextField(searchRect, blueprintSearchQuery ?? "", GUI.skin.textField);
            if (newQuery != blueprintSearchQuery)
            {
                blueprintSearchQuery = newQuery;
                ApplyBlueprintFilter();
            }

            Rect clearSearchRect = new Rect(blueprintX + blueprintWidth - 38, cy, 26, 26);
            if (DrawNativeWoodButton(clearSearchRect, "×", false, new Color(0.9f, 0.7f, 0.7f, 1f)))
            {
                blueprintSearchQuery = "";
                ApplyBlueprintFilter();
            }

            cy += 32f;

            float tabW = (blueprintWidth - 24f) / 7f;
            BlueprintCategory[] categories = BlueprintCategoriesAll;
            string[] catLabelsEn = { "All", "Block", "Weap", "Tool", "Armor", "Food", "Item" };
            string[] catLabelsEs = { "Todo", "Bloq", "Arma", "Herr", "Armad", "Com", "Ítem" };

            for (int i = 0; i < categories.Length; i++)
            {
                BlueprintCategory cat = categories[i];
                bool isSelected = (cat == blueprintSelectedCategory);
                Rect tabRect = new Rect(blueprintX + 12 + i * tabW, cy, tabW - 2, 24);
                string label = IsSpanish() ? catLabelsEs[i] : catLabelsEn[i];

                Color tabCol = isSelected ? new Color(0.70f, 1.0f, 0.35f, 1.0f) : new Color(0.85f, 0.75f, 0.60f, 1.0f);
                if (DrawNativeWoodButton(tabRect, label, isSelected, tabCol))
                {
                    blueprintSelectedCategory = cat;
                    ApplyBlueprintFilter();
                }
            }

            cy += 28f;

            // Pinned Bookmarks Bar
            if (blueprintBookmarkedNames.Count > 0)
            {
                DrawShadowLabel(new Rect(blueprintX + 12, cy, 140, 18), Tr("★ PINNED BOOKMARKS", "★ MARCADORES"), DescStyle, new Color(1.0f, 0.85f, 0.25f), Color.black);
                cy += 20f;

                Rect bStripRect = new Rect(blueprintX + 12, cy, blueprintWidth - 24, 38);
                GUI.DrawTexture(bStripRect, GetColorTexture(new Color(0.04f, 0.03f, 0.02f, 0.50f)));

                int bSlot = 0;
                // Snapshot: right-clicking a pin calls ToggleBookmark(), which removes from the
                // HashSet currently being enumerated -> InvalidOperationException every unpin.
                List<string> pinnedSnapshot = new List<string>(blueprintBookmarkedNames);
                foreach (string bName in pinnedSnapshot)
                {
                    if (bSlot >= 8) break;
                    InvBaseItem bItem = SafeFindItemByName(bName);
                    if (bItem == null) continue;

                    Rect bRect = new Rect(blueprintX + 14 + bSlot * 34, cy + 2, 32, 32);
                    DrawInvSlot(bRect, new Color(1.0f, 0.90f, 0.40f, 0.85f));
                    DrawItemIcon(new Rect(bRect.x + 3, bRect.y + 3, 26, 26), bItem);

                    if (bRect.Contains(Event.current.mousePosition))
                    {
                        DrawItemHoverBorder(bRect);
                        hoveredTopmostItem = bItem;
                        hoveredTopmostRect = bRect;
                        string locName = T.ItemNames(bItem.name) ?? bItem.name;
                        hoveredTopmostText = $"{locName}\n[Left: Recipe | Right: Unpin]";

                        if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                        {
                            OpenRecipeCard(bItem, 0);
                            Event.current.Use();
                        }
                        else if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
                        {
                            ToggleBookmark(bName);
                            Event.current.Use();
                        }
                    }

                    bSlot++;
                }

                cy += 42f;
            }

            int totalItems = blueprintFilteredItems.Count;
            int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)totalItems / BlueprintItemsPerPage));
            blueprintCurrentPage = Mathf.Clamp(blueprintCurrentPage, 0, totalPages - 1);

            int startIdx = blueprintCurrentPage * BlueprintItemsPerPage;
            int endIdx = Mathf.Min(startIdx + BlueprintItemsPerPage, totalItems);

            float gridStartY = cy;
            float slotW = 44f;
            float slotH = 44f;
            float slotGapX = 4f;
            float slotGapY = 4f;
            float gridMarginLeft = (blueprintWidth - (BlueprintCols * slotW + (BlueprintCols - 1) * slotGapX)) * 0.5f;

            Rect gridArea = new Rect(blueprintX + 10, gridStartY, blueprintWidth - 20, BlueprintRows * (slotH + slotGapY));

            if (gridArea.Contains(Event.current.mousePosition) && Event.current.type == EventType.ScrollWheel)
            {
                if (Event.current.delta.y > 0.05f)
                {
                    blueprintCurrentPage = Mathf.Min(totalPages - 1, blueprintCurrentPage + 1);
                    Event.current.Use();
                }
                else if (Event.current.delta.y < -0.05f)
                {
                    blueprintCurrentPage = Mathf.Max(0, blueprintCurrentPage - 1);
                    Event.current.Use();
                }
            }

            for (int idx = startIdx; idx < endIdx; idx++)
            {
                int gridPos = idx - startIdx;
                int col = gridPos % BlueprintCols;
                int row = gridPos / BlueprintCols;

                float sx = blueprintX + gridMarginLeft + col * (slotW + slotGapX);
                float sy = gridStartY + row * (slotH + slotGapY);
                Rect slotRect = new Rect(sx, sy, slotW, slotH);

                InvBaseItem item = blueprintFilteredItems[idx];
                bool isPinned = blueprintBookmarkedNames.Contains(item.name);

                DrawInvSlot(slotRect, isPinned ? new Color(1f, 0.9f, 0.3f, 0.9f) : (Color?)null);

                Rect iconRect = new Rect(sx + 4, sy + 4, slotW - 8, slotH - 8);
                DrawItemIcon(iconRect, item);

                if (isPinned)
                {
                    DrawShadowLabel(new Rect(sx + 2, sy + 2, 14, 14), "★", DescStyle, new Color(1f, 0.85f, 0.2f), Color.black);
                }

                if (slotRect.Contains(Event.current.mousePosition))
                {
                    DrawItemHoverBorder(slotRect);

                    // TASK B: Record hover info for top-level drawing at end of frame
                    hoveredTopmostItem = item;
                    hoveredTopmostRect = slotRect;
                    string localizedName = T.ItemNames(item.name) ?? item.name;
                    hoveredTopmostText = $"{localizedName}\n[L-Click/R: Recipe | R-Click/U: Uses | A: Pin]";

                    if (Event.current.type == EventType.MouseDown)
                    {
                        if (Event.current.button == 0)
                        {
                            OpenRecipeCard(item, 0);
                            Event.current.Use();
                        }
                        else if (Event.current.button == 1)
                        {
                            OpenRecipeCard(item, 1);
                            Event.current.Use();
                        }
                    }
                    else if (Event.current.type == EventType.KeyDown)
                    {
                        if (Event.current.keyCode == BlueprintRecipeKey.Value)
                        {
                            OpenRecipeCard(item, 0);
                            Event.current.Use();
                        }
                        else if (Event.current.keyCode == BlueprintUsageKey.Value)
                        {
                            OpenRecipeCard(item, 1);
                            Event.current.Use();
                        }
                        else if (Event.current.keyCode == BlueprintBookmarkKey.Value)
                        {
                            ToggleBookmark(item.name);
                            Event.current.Use();
                        }
                    }
                }
            }

            float botY = blueprintY + blueprintHeight - 38f;
            Rect prevBtn = new Rect(blueprintX + 12, botY, 68, 28);
            if (DrawNativeWoodButton(prevBtn, "< Prev", blueprintCurrentPage > 0))
            {
                if (blueprintCurrentPage > 0) blueprintCurrentPage--;
            }

            string pageInfo = $"{blueprintCurrentPage + 1} / {totalPages} ({totalItems})";
            DrawShadowLabel(new Rect(blueprintX + 84, botY + 4, blueprintWidth - 168, 22), pageInfo, DescStyle, new Color(0.95f, 0.90f, 0.75f), Color.black);

            Rect nextBtn = new Rect(blueprintX + blueprintWidth - 80, botY, 68, 28);
            if (DrawNativeWoodButton(nextBtn, "Next >", blueprintCurrentPage < totalPages - 1))
            {
                if (blueprintCurrentPage < totalPages - 1) blueprintCurrentPage++;
            }
        }

        // =====================================================================
        //                 RECIPE EXPLORER CARD (R / U)
        // =====================================================================
        public static void OpenRecipeCard(InvBaseItem item, int tab, bool pushHistory = true)
        {
            if (item == null) return;

            if (pushHistory && activeRecipeItem != null && activeRecipeItem != item)
            {
                recipeHistory.Push(new Tuple<InvBaseItem, int>(activeRecipeItem, activeRecipeTab));
            }

            activeRecipeItem = item;
            activeRecipeTab = tab;
            activeRecipeIndex = 0;
            isRecipeCardOpen = true;
            usedInScrollPos = Vector2.zero;
        }

        private static void ToggleBookmark(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return;

            if (blueprintBookmarkedNames.Contains(itemName))
            {
                blueprintBookmarkedNames.Remove(itemName);
            }
            else
            {
                blueprintBookmarkedNames.Add(itemName);
            }
            SaveBlueprintBookmarks();
        }

        private void DrawRecipeExplorerCardGUI()
        {
            Rect fullScreen = new Rect(0, 0, Screen.width, Screen.height);
            GUI.DrawTexture(fullScreen, GetColorTexture(new Color(0f, 0f, 0f, 0.60f)));

            float cardW = 440f;
            float cardH = 390f;
            float cardX = (Screen.width - cardW) * 0.5f;
            float cardY = (Screen.height - cardH) * 0.5f;
            Rect cardRect = new Rect(cardX, cardY, cardW, cardH);

            DrawWoodPanel(cardRect, 1.0f);
            GUI.DrawTexture(new Rect(cardX + 8, cardY + 8, cardW - 16, cardH - 16), GetColorTexture(new Color(0.08f, 0.05f, 0.03f, 0.70f)));

            float curY = cardY + 12f;

            if (recipeHistory.Count > 0)
            {
                Rect backBtn = new Rect(cardX + 12, curY, 68, 26);
                if (DrawNativeWoodButton(backBtn, "< Back"))
                {
                    var prev = recipeHistory.Pop();
                    activeRecipeItem = prev.Item1;
                    activeRecipeTab = prev.Item2;
                    activeRecipeIndex = 0;
                    return;
                }
            }

            Rect tabRecipe = new Rect(cardX + (recipeHistory.Count > 0 ? 84 : 14), curY, 130, 26);
            string rTabTitle = Tr("Recipe (R)", "Receta (R)");
            if (DrawNativeWoodButton(tabRecipe, rTabTitle, activeRecipeTab == 0))
            {
                activeRecipeTab = 0;
                activeRecipeIndex = 0;
            }

            List<BlueprintRecipeEntry> usesList = GetRecipesForIngredient(activeRecipeItem);
            int usesCount = usesList != null ? usesList.Count : 0;

            Rect tabUses = new Rect(tabRecipe.x + 134, curY, 130, 26);
            string uTabTitle = $"{Tr("Used In", "Usos")} ({usesCount})";
            if (DrawNativeWoodButton(tabUses, uTabTitle, activeRecipeTab == 1))
            {
                activeRecipeTab = 1;
                activeRecipeIndex = 0;
            }

            bool isPinned = blueprintBookmarkedNames.Contains(activeRecipeItem.name);
            Rect pinBtn = new Rect(cardX + cardW - 74, curY, 32, 26);
            string pinLabel = isPinned ? "★" : "☆";
            Color pinCol = isPinned ? new Color(1.0f, 0.85f, 0.20f, 1.0f) : Color.white;
            if (DrawNativeWoodButton(pinBtn, pinLabel, isPinned, pinCol))
            {
                ToggleBookmark(activeRecipeItem.name);
            }

            Rect closeBtn = new Rect(cardX + cardW - 38, curY, 26, 26);
            if (DrawNativeWoodButton(closeBtn, "X", false, new Color(1f, 0.6f, 0.6f, 1f)))
            {
                isRecipeCardOpen = false;
                ModalClosedTimestamp = Time.unscaledTime;
                return;
            }

            curY += 34f;

            // Target Item Summary Box
            Rect summaryRect = new Rect(cardX + 14, curY, cardW - 28, 56);
            GUI.DrawTexture(summaryRect, GetColorTexture(new Color(0.12f, 0.08f, 0.05f, 0.55f)));

            Rect bigSlot = new Rect(summaryRect.x + 6, summaryRect.y + 6, 44, 44);
            DrawInvSlot(bigSlot);
            DrawItemIcon(new Rect(bigSlot.x + 4, bigSlot.y + 4, 36, 36), activeRecipeItem);

            string itemName = T.ItemNames(activeRecipeItem.name) ?? activeRecipeItem.name;
            DrawShadowLabel(new Rect(summaryRect.x + 58, summaryRect.y + 6, summaryRect.width - 64, 24), itemName, MenuTitleStyle, new Color(0.65f, 1.0f, 0.30f), Color.black);

            string meta = $"{Tr("Category", "Categoría")}: {activeRecipeItem.category}   |   {Tr("Price", "Precio")}: ${activeRecipeItem.price}";
            if (activeRecipeItem.durability > 1) meta += $"   |   {Tr("Max Durability", "Durabilidad")}: {activeRecipeItem.durability}";
            DrawShadowLabel(new Rect(summaryRect.x + 58, summaryRect.y + 30, summaryRect.width - 64, 18), meta, AuthorStyle, new Color(0.90f, 0.84f, 0.72f), Color.black);

            curY += 64f;

            // TAB 0: Crafting Recipe View (R)
            if (activeRecipeTab == 0)
            {
                List<BlueprintRecipeEntry> recipes = GetRecipesForResult(activeRecipeItem);

                if (recipes == null || recipes.Count == 0)
                {
                    Rect emptyBox = new Rect(cardX + 14, curY + 20, cardW - 28, 140);
                    GUI.DrawTexture(emptyBox, GetColorTexture(new Color(0.12f, 0.08f, 0.05f, 0.40f)));

                    string noRecipeTitle = Tr("No Crafting Recipe Found", "No se encontró receta de crafteo");
                    DrawShadowLabel(new Rect(emptyBox.x, emptyBox.y + 36, emptyBox.width, 24), noRecipeTitle, SectionTitleStyle, new Color(1.0f, 0.75f, 0.35f), Color.black);

                    string howToObtain = Tr("• Obtainable via World Drops, Mining, Quests, or Merchants", "• Obtenible por Drops del Mundo, Minería, Misiones o Comerciantes");
                    DrawShadowLabel(new Rect(emptyBox.x, emptyBox.y + 68, emptyBox.width, 20), howToObtain, DescStyle, new Color(0.85f, 0.82f, 0.75f), Color.black);
                }
                else
                {
                    activeRecipeIndex = Mathf.Clamp(activeRecipeIndex, 0, recipes.Count - 1);
                    BlueprintRecipeEntry currentEntry = recipes[activeRecipeIndex];
                    Recipe recipe = currentEntry.recipe;

                    // Header row: Recipe counter + Station badge
                    Rect navRect = new Rect(cardX + 14, curY, cardW - 28, 24);

                    string stName = IsSpanish() ? currentEntry.stationNameEs : currentEntry.stationName;
                    Color stColor = new Color(0.50f, 1.0f, 0.40f);
                    if (currentEntry.stationName == "Furnace") stColor = new Color(1.0f, 0.65f, 0.25f);
                    else if (currentEntry.stationName == "Cauldron") stColor = new Color(0.85f, 0.55f, 1.0f);
                    else if (currentEntry.stationName == "Anvil") stColor = new Color(0.55f, 0.85f, 1.0f);
                    else if (currentEntry.stationName == "Coloring Table") stColor = new Color(1.0f, 0.90f, 0.35f);

                    string stationBadge = $"[{stName}]";
                    DrawShadowLabel(new Rect(navRect.x, navRect.y + 2, 160, 20), stationBadge, ToolStyle, stColor, Color.black);

                    if (recipes.Count > 1)
                    {
                        if (DrawNativeWoodButton(new Rect(navRect.x + navRect.width - 150, navRect.y, 44, 22), "< Prev", activeRecipeIndex > 0))
                        {
                            activeRecipeIndex = Mathf.Max(0, activeRecipeIndex - 1);
                        }

                        string recCounter = $"{activeRecipeIndex + 1} / {recipes.Count}";
                        DrawShadowLabel(new Rect(navRect.x + navRect.width - 102, navRect.y + 2, 54, 20), recCounter, DescStyle, new Color(1.0f, 0.90f, 0.40f), Color.black);

                        if (DrawNativeWoodButton(new Rect(navRect.x + navRect.width - 44, navRect.y, 44, 22), "Next >", activeRecipeIndex < recipes.Count - 1))
                        {
                            activeRecipeIndex = Mathf.Min(recipes.Count - 1, activeRecipeIndex + 1);
                        }
                    }

                    curY += 28f;

                    // Crafting Matrix (up to 3x3)
                    float matrixX = cardX + 34f;
                    float matrixY = curY;
                    float cSlotSize = 42f;
                    float cSlotGap = 4f;

                    int maxRows = recipe.ingredients != null ? Mathf.Max(1, Mathf.Min(3, recipe.ingredients.GetLength(0))) : 3;
                    int maxCols = recipe.ingredients != null ? Mathf.Max(1, Mathf.Min(3, recipe.ingredients.GetLength(1))) : 3;

                    for (int r = 0; r < 3; r++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            Rect cSlotRect = new Rect(matrixX + c * (cSlotSize + cSlotGap), matrixY + r * (cSlotSize + cSlotGap), cSlotSize, cSlotSize);
                            DrawInvSlot(cSlotRect);

                            InvGameItem ing = null;
                            if (recipe.ingredients != null && r < recipe.ingredients.GetLength(0) && c < recipe.ingredients.GetLength(1))
                            {
                                ing = recipe.ingredients[r, c];
                            }

                            if (ing != null && ing.baseItem != null)
                            {
                                DrawItemIcon(new Rect(cSlotRect.x + 3, cSlotRect.y + 3, cSlotSize - 6, cSlotSize - 6), ing.baseItem);

                                if (ing.count > 1)
                                {
                                    DrawShadowLabel(new Rect(cSlotRect.x + 2, cSlotRect.y + cSlotSize - 16, cSlotSize - 4, 14), ing.count.ToString(), DescStyle, Color.white, Color.black);
                                }

                                if (cSlotRect.Contains(Event.current.mousePosition))
                                {
                                    DrawItemHoverBorder(cSlotRect);
                                    hoveredTopmostItem = ing.baseItem;
                                    hoveredTopmostRect = cSlotRect;
                                    string ingHover = $"{T.ItemNames(ing.baseItem.name) ?? ing.baseItem.name} (x{ing.count})\n[Left-Click: Recipe | Right-Click: Uses]";
                                    hoveredTopmostText = ingHover;

                                    if (Event.current.type == EventType.MouseDown)
                                    {
                                        if (Event.current.button == 0)
                                        {
                                            OpenRecipeCard(ing.baseItem, 0);
                                            Event.current.Use();
                                        }
                                        else if (Event.current.button == 1)
                                        {
                                            OpenRecipeCard(ing.baseItem, 1);
                                            Event.current.Use();
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Crafting Arrow ➔
                    float arrowX = matrixX + 3 * (cSlotSize + cSlotGap) + 16f;
                    float arrowY = matrixY + 44f;
                    DrawShadowLabel(new Rect(arrowX, arrowY, 40, 36), "➔", MenuHeaderStyle, new Color(0.70f, 1.0f, 0.35f), Color.black);

                    // Output Result Slot
                    float outX = arrowX + 46f;
                    float outY = matrixY + 36f;
                    Rect outSlotRect = new Rect(outX, outY, 54, 54);
                    DrawInvSlot(outSlotRect, new Color(0.40f, 1.0f, 0.40f, 0.90f));
                    DrawItemIcon(new Rect(outX + 5, outY + 5, 44, 44), activeRecipeItem);

                    int resultCount = recipe.result != null ? recipe.result.count : 1;
                    if (resultCount > 1)
                    {
                        DrawShadowLabel(new Rect(outX + 4, outY + 34, 46, 16), resultCount.ToString(), SectionTitleStyle, Color.yellow, Color.black);
                    }

                    if (outSlotRect.Contains(Event.current.mousePosition))
                    {
                        DrawItemHoverBorder(outSlotRect);
                        hoveredTopmostItem = activeRecipeItem;
                        hoveredTopmostRect = outSlotRect;
                        string outHover = $"{T.ItemNames(activeRecipeItem.name) ?? activeRecipeItem.name} (x{resultCount})\n[Right-Click: Uses]";
                        hoveredTopmostText = outHover;

                        if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
                        {
                            OpenRecipeCard(activeRecipeItem, 1);
                            Event.current.Use();
                        }
                    }

                    // Extra Recipe Info (Mana, Time, Diamonds Cost, Station)
                    float infoY = matrixY + 3 * (cSlotSize + cSlotGap) + 10f;
                    string extraInfo = "";
                    if (recipe.mana > 0.1f) extraInfo += $"Mana: {recipe.mana:F0}   ";
                    if (recipe.minutes > 0.01f) extraInfo += $"{Tr("Time", "Tiempo")}: {recipe.minutes:F1}m   ";
                    if (recipe.cost > 0.1f) extraInfo += $"{Tr("Cost", "Costo")}: {recipe.cost:F0}   ";
                    extraInfo += $"{Tr("Station", "Estación")}: {stName}";

                    DrawShadowLabel(new Rect(cardX + 14, infoY, cardW - 28, 20), extraInfo, AuthorStyle, new Color(0.88f, 0.88f, 0.74f), Color.black);
                }
            }
            // TAB 1: Used In View (U)
            else if (activeRecipeTab == 1)
            {
                if (usesList == null || usesList.Count == 0)
                {
                    Rect emptyBox = new Rect(cardX + 14, curY + 20, cardW - 28, 140);
                    GUI.DrawTexture(emptyBox, GetColorTexture(new Color(0.12f, 0.08f, 0.05f, 0.40f)));

                    string noUsesTitle = Tr("Not Used in Any Known Recipes", "No se usa en ninguna receta conocida");
                    DrawShadowLabel(new Rect(emptyBox.x, emptyBox.y + 45, emptyBox.width, 24), noUsesTitle, SectionTitleStyle, new Color(1.0f, 0.75f, 0.35f), Color.black);

                    string noUsesDesc = Tr("This item is not currently required as an ingredient.", "Este ítem no se requiere como ingrediente actualmente.");
                    DrawShadowLabel(new Rect(emptyBox.x, emptyBox.y + 75, emptyBox.width, 20), noUsesDesc, DescStyle, new Color(0.85f, 0.82f, 0.75f), Color.black);
                }
                else
                {
                    Rect scrollArea = new Rect(cardX + 14, curY, cardW - 28, cardH - (curY - cardY) - 16f);
                    float viewHeight = usesList.Count * 46f;
                    usedInScrollPos = GUI.BeginScrollView(scrollArea, usedInScrollPos, new Rect(0, 0, scrollArea.width - 20, viewHeight));

                    for (int u = 0; u < usesList.Count; u++)
                    {
                        BlueprintRecipeEntry recEntry = usesList[u];
                        Recipe rec = recEntry.recipe;
                        InvBaseItem resItem = rec.result?.baseItem;
                        if (resItem == null && !string.IsNullOrEmpty(rec.name))
                        {
                            resItem = SafeFindItemByName(rec.name);
                        }
                        if (resItem == null) continue;

                        Rect rowRect = new Rect(0, u * 46f, scrollArea.width - 24, 42f);
                        DrawDarkSlotBox(rowRect, false, rowRect.Contains(Event.current.mousePosition));

                        Rect uSlot = new Rect(rowRect.x + 4, rowRect.y + 3, 36, 36);
                        DrawInvSlot(uSlot);
                        DrawItemIcon(new Rect(uSlot.x + 3, uSlot.y + 3, 30, 30), resItem);

                        string resName = T.ItemNames(resItem.name) ?? resItem.name;
                        if (rec.result != null && rec.result.count > 1) resName += $"  (x{rec.result.count})";

                        string stBadge = $"[{ (IsSpanish() ? recEntry.stationNameEs : recEntry.stationName) }]";
                        DrawShadowLabel(new Rect(rowRect.x + 46, rowRect.y + 4, rowRect.width - 120, 18), resName, ModNameStyle, Color.white, Color.black);
                        DrawShadowLabel(new Rect(rowRect.x + 46, rowRect.y + 22, rowRect.width - 120, 16), stBadge, AuthorStyle, new Color(0.70f, 0.90f, 0.50f), Color.black);

                        Rect viewBtn = new Rect(rowRect.x + rowRect.width - 64, rowRect.y + 8, 56, 26);
                        if (DrawNativeWoodButton(viewBtn, Tr("View", "Ver")))
                        {
                            OpenRecipeCard(resItem, 0);
                        }

                        if (rowRect.Contains(Event.current.mousePosition))
                        {
                            hoveredTopmostItem = resItem;
                            hoveredTopmostRect = rowRect;
                            hoveredTopmostText = $"{T.ItemNames(resItem.name) ?? resItem.name}\n[Click View: Recipe]";
                        }
                    }

                    GUI.EndScrollView();
                }
            }
        }

        private static void DrawItemHoverBorder(Rect rect)
        {
            Color old = GUI.color;
            GUI.color = new Color(0.65f, 1.0f, 0.30f, 0.85f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - 2, rect.width, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 2, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x + rect.width - 2, rect.y, 2, rect.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // =====================================================================
        //        TASK B: TOPMOST RICH FLOATING TOOLTIP (DRAWN ON TOP OF ALL SLOTS)
        // =====================================================================
        private void DrawTopmostTooltipCard(Vector2 mousePos, string text, InvBaseItem item)
        {
            int oldDepth = GUI.depth;
            GUI.depth = -2000;
            try
            {
                GUIContent content = new GUIContent(text);
                Vector2 size = DescStyle.CalcSize(content);
                float cardW = Mathf.Max(160f, size.x + 22f);
                float cardH = size.y + 16f;

                if (item != null)
                {
                    cardW = Mathf.Max(cardW, 190f);
                    cardH += 20f;
                }

                // Clamp to screen bounds so tooltip is never clipped or drawn off-screen
                float px = Mathf.Clamp(mousePos.x + 16f, 10f, Screen.width - cardW - 12f);
                float py = Mathf.Clamp(mousePos.y - cardH - 8f, 10f, Screen.height - cardH - 12f);

                Rect cardRect = new Rect(px, py, cardW, cardH);

                DrawWoodPanel(cardRect, 1.0f);
                GUI.DrawTexture(new Rect(px + 4, py + 4, cardW - 8, cardH - 8), GetColorTexture(new Color(0.06f, 0.04f, 0.02f, 0.95f)));

                if (item != null)
                {
                    string catInfo = $"[{item.category}]";
                    if (item.price > 0) catInfo += $"  •  ${item.price}";
                    if (item.durability > 1) catInfo += $"  •  Dur: {item.durability}";
                    DrawShadowLabel(new Rect(px + 10, py + 6, cardW - 20, 16), catInfo, AuthorStyle, new Color(0.75f, 0.85f, 1.0f), Color.black);

                    DrawShadowLabel(new Rect(px + 10, py + 22, cardW - 20, size.y + 8), text, DescStyle, new Color(1.0f, 0.96f, 0.88f), Color.black);
                }
                else
                {
                    DrawShadowLabel(new Rect(px + 10, py + 8, cardW - 20, size.y + 8), text, DescStyle, new Color(1.0f, 0.96f, 0.88f), Color.black);
                }
            }
            finally
            {
                GUI.depth = oldDepth;
            }
        }

        // =====================================================================
        //                 MOD MENU INTERACTIVE SETTINGS DRAWING
        // =====================================================================
        public void DrawBlueprintModMenuSettings(Rect rightRect, float baseY = 230f)
        {
            float sy = baseY;
            DrawShadowLabel(new Rect(10, sy, rightRect.width - 24, 22), Tr("REAL-TIME Blueprint SETTINGS", "AJUSTES DE Blueprint EN TIEMPO REAL"), SectionTitleStyle, new Color(1.0f, 0.85f, 0.25f), Color.black);

            string blueprintToggleText = BlueprintEnabled.Value
                ? Tr("Blueprint Recipe Browser: ENABLED (O)", "Navegador Blueprint: ACTIVADO (O)")
                : Tr("Blueprint Recipe Browser: DISABLED (O)", "Navegador Blueprint: DESACTIVADO (O)");
            if (DrawNativeWoodButton(new Rect(12, sy + 26, 270, 32), blueprintToggleText, BlueprintEnabled.Value))
            {
                BlueprintEnabled.Value = !BlueprintEnabled.Value;
                Config.Save();
            }

            string autoOpenText = BlueprintAutoOpenWithInventory.Value
                ? Tr("Auto-Open with Inventory: YES", "Auto-abrir con Inventario: SÍ")
                : Tr("Auto-Open with Inventory: NO", "Auto-abrir con Inventario: NO");
            if (DrawNativeWoodButton(new Rect(290, sy + 26, 250, 32), autoOpenText, BlueprintAutoOpenWithInventory.Value))
            {
                BlueprintAutoOpenWithInventory.Value = !BlueprintAutoOpenWithInventory.Value;
                Config.Save();
            }

            sy += 70f;
            DrawShadowLabel(new Rect(10, sy, rightRect.width - 24, 22), Tr("HOTKEYS & SHORTCUTS", "ATAJOS DE TECLADO"), SectionTitleStyle, new Color(1.0f, 0.85f, 0.25f), Color.black);

            string hotkeysDesc = Tr(
                "• [R] Key / Left-Click: View Crafting Recipe on any item in catalog or hovered inventory slot.\n" +
                "• [U] Key / Right-Click: View Uses (every recipe where item is an ingredient).\n" +
                "• [A] Key: Bookmark item to pin it to quick access bar.\n" +
                "• [O] Key: Manually toggle Blueprint catalog visibility.\n" +
                "• Mouse Wheel: Smoothly scroll item pages in the grid.",
                "• Tecla [R] / Clic Izq: Ver receta de crafteo de cualquier ítem o ranura de inventario.\n" +
                "• Tecla [U] / Clic Der: Ver usos (todas las recetas que usan este ingrediente).\n" +
                "• Tecla [A]: Fijar marcador a la barra de acceso rápido.\n" +
                "• Tecla [O]: Alternar visibilidad de Blueprint manualmente.\n" +
                "• Rueda del Ratón: Paginación suave en la cuadrícula de ítems.");
            DrawShadowLabel(new Rect(12, sy + 26, rightRect.width - 24, 110), hotkeysDesc, DescStyle, new Color(0.90f, 0.88f, 0.80f), Color.black);
        }

    }
}
