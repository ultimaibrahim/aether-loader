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
        private static readonly NanoSegmentOption<int>[] RecipeTabOpts = new NanoSegmentOption<int>[2];

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

        // Cached Aether Ink craft styles (zero per-frame allocations)
        private static GUIStyle _bpTitleStyle, _bpMetaStyle, _bpMonoStyle, _bpMonoCenterStyle, _bpChipStyle;
        private static GUIStyle _bpItemTitleStyle, _bpBodyStyle, _bpSearchStyle, _bpPlaceholderStyle, _bpArrowStyle;
        private static readonly GUIContent _bpMeasure = new GUIContent();

        private static readonly NanoSegmentOption<BlueprintCategory>[] CatSegmentsEn =
        {
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.All, "All"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Blocks, "Blk"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Weapons, "Wpn"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Tools, "Tool"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Armor, "Arm"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Consumables, "Food"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Materials, "Mat")
        };
        private static readonly NanoSegmentOption<BlueprintCategory>[] CatSegmentsEs =
        {
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.All, "Todo"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Blocks, "Bloq"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Weapons, "Arma"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Tools, "Herr"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Armor, "Armd"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Consumables, "Com"),
            new NanoSegmentOption<BlueprintCategory>(BlueprintCategory.Materials, "Mat")
        };

        private static void EnsureBlueprintCraftStyles()
        {
            if (_bpTitleStyle != null) return;
            _bpTitleStyle = NanoBrand.Style(NanoBrand.Display, 19, NanoBrand.Paper);
            _bpMetaStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Mute);
            _bpMonoStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Paper);
            _bpMonoCenterStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Paper, TextAnchor.MiddleCenter);
            _bpChipStyle = NanoBrand.Style(NanoBrand.Mono, 9, NanoBrand.PaperLight, TextAnchor.MiddleCenter);
            _bpItemTitleStyle = NanoBrand.Style(NanoBrand.Display, 18, NanoBrand.PaperLight);
            _bpBodyStyle = NanoBrand.Style(NanoBrand.Body, 12, NanoBrand.Paper, TextAnchor.UpperLeft, true);
            _bpPlaceholderStyle = NanoBrand.Style(NanoBrand.Body, 12, new Color(NanoBrand.Mute.r, NanoBrand.Mute.g, NanoBrand.Mute.b, 0.58f));
            _bpArrowStyle = NanoBrand.Style(NanoBrand.Display, 24, NanoBrand.Ember, TextAnchor.MiddleCenter);
            _bpSearchStyle = new GUIStyle(GUI.skin.textField)
            {
                font = NanoBrand.Body,
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(9, 8, 2, 2)
            };
            _bpSearchStyle.normal.textColor = NanoBrand.PaperLight;
            _bpSearchStyle.focused.textColor = NanoBrand.PaperLight;
            Texture2D clear = GetColorTexture(new Color(0f, 0f, 0f, 0f));
            _bpSearchStyle.normal.background = clear;
            _bpSearchStyle.focused.background = clear;
            _bpSearchStyle.hover.background = clear;
        }

        private static void DrawBlueprintShell(Rect rect, float radius = 12f, float opacity = 0.97f)
        {
            NanoSurface.DrawCardSurface(
                rect,
                new Color(0.092f, 0.074f, 0.058f, opacity),
                new Color(0.047f, 0.039f, 0.031f, Mathf.Clamp01(opacity + 0.01f)),
                NanoBrand.Line,
                NanoBrand.Ember,
                new Color(0.98f, 0.93f, 0.85f, 0.11f),
                radius, 1f, 0f, 0f);
        }

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
            if (currentScene == "Menu" || currentScene == "StartScene")
            {
                NanoMotion.Snap("aether:blueprint_drawer", false);
                NanoMotion.Snap("aether:blueprint_card", false);
                return;
            }

            hoveredTopmostItem = null;
            hoveredTopmostRect = default;
            hoveredTopmostText = null;

            bool allowDraw = BlueprintEnabled.Value && !IsModMenuOpen && !IsWayfinderOpen && !IsHudDragOpen && !NanoHud.CanvasEditorOpen;
            float drawerAnim = NanoMotion.Sample("aether:blueprint_drawer", allowDraw && isBlueprintVisible, 0.21f, 0.14f);
            float cardAnim = NanoMotion.Sample("aether:blueprint_card", allowDraw && isRecipeCardOpen && activeRecipeItem != null, 0.20f, 0.13f);

            if (drawerAnim > 0.005f || cardAnim > 0.005f)
            {
                EnsureBlueprintItemsIndexed();
                EnsureRecipesIndexed();
                EnsureBlueprintCraftStyles();

                if (drawerAnim > 0.005f)
                    DrawBlueprintSidebarGUI(drawerAnim);

                if (cardAnim > 0.005f && activeRecipeItem != null)
                    DrawRecipeExplorerCardGUI(cardAnim);

                if (allowDraw && hoveredTopmostText != null)
                    DrawTopmostTooltipCard(Event.current.mousePosition, hoveredTopmostText, hoveredTopmostItem);
            }
        }

        // =====================================================================
        //                      Blueprint GUI RENDERING
        // =====================================================================
        private void DrawBlueprintSidebarGUI(float anim)
        {
            bool es = IsSpanish();
            float blueprintWidth = 316f;
            float blueprintHeight = Mathf.Min(680f, Screen.height - 44f);
            float blueprintX = Screen.width - blueprintWidth - 14f;
            float blueprintY = (Screen.height - blueprintHeight) * 0.5f;
            Rect panelRect = new Rect(blueprintX, blueprintY, blueprintWidth, blueprintHeight);

            using (NanoMotion.BeginSlide(anim, 32f, 0f))
            {
                DrawBlueprintShell(panelRect, 12f, 0.96f);

                // Integrated Top Bar (open cube + Gloock wordmark + count + close)
                float pad = 12f;
                float cy = blueprintY + 10f;
                float hx = blueprintX + pad;
                if (NanoBrand.Cube != null)
                    hx += NanoBrand.DrawCube(hx, cy + 3f, 18f) + 8f;
                NanoSurface.DrawCrispLabel(new Rect(hx, cy - 2f, 110f, 28f), "blueprint", _bpTitleStyle, NanoBrand.Paper, 0.4f);

                int totalItems = blueprintFilteredItems.Count;
                string countTag = $"{totalItems}";
                NanoSurface.DrawCrispLabel(new Rect(blueprintX + blueprintWidth - 88f, cy + 5f, 44f, 16f),
                    countTag, _bpMonoCenterStyle, NanoBrand.Mute, 0.25f);

                if (NanoSurface.DrawActionButton(new Rect(blueprintX + blueprintWidth - 38f, cy, 26f, 24f), "×", false))
                {
                    isBlueprintVisible = false;
                    return;
                }

                cy += 30f;
                NanoSurface.FillRounded(new Rect(blueprintX + pad, cy, blueprintWidth - pad * 2f, 1f), NanoBrand.Line, 0.5f);
                cy += 8f;

                // Debossed squircle search field + clear button
                float searchW = blueprintWidth - pad * 2f - 30f;
                Rect searchRect = new Rect(blueprintX + pad, cy, searchW, 28f);
                if (Event.current.type == EventType.Repaint)
                {
                    NanoSurface.DrawDebossedSlot(searchRect, new Color(0.036f, 0.028f, 0.022f, 0.96f), NanoBrand.Line, 7f);
                    if (string.IsNullOrEmpty(blueprintSearchQuery))
                    {
                        NanoSurface.DrawCrispLabel(new Rect(searchRect.x + 9f, searchRect.y, searchRect.width - 14f, searchRect.height),
                            es ? "Buscar ítem…" : "Search items…", _bpPlaceholderStyle, _bpPlaceholderStyle.normal.textColor, 0f);
                    }
                }
                string newQuery = GUI.TextField(searchRect, blueprintSearchQuery ?? "", _bpSearchStyle);
                if (newQuery != blueprintSearchQuery)
                {
                    blueprintSearchQuery = newQuery;
                    ApplyBlueprintFilter();
                }

                if (NanoSurface.DrawActionButton(new Rect(searchRect.xMax + 4f, cy, 26f, 28f), "×", false))
                {
                    blueprintSearchQuery = "";
                    ApplyBlueprintFilter();
                }

                cy += 34f;

                // Category Segmented Control
                if (NanoSurface.DrawSegmented(new Rect(blueprintX + pad, cy, blueprintWidth - pad * 2f, 26f),
                        es ? CatSegmentsEs : CatSegmentsEn, blueprintSelectedCategory, out BlueprintCategory nextCat))
                {
                    blueprintSelectedCategory = nextCat;
                    ApplyBlueprintFilter();
                }

                cy += 32f;

                // Pinned Bookmarks Tray
                if (blueprintBookmarkedNames.Count > 0)
                {
                    NanoSurface.DrawCrispLabel(new Rect(blueprintX + pad, cy, 180f, 16f),
                        es ? "★ MARCADORES" : "★ PINNED", _bpMetaStyle, NanoBrand.EmberSoft, 0.25f);
                    cy += 18f;

                    Rect bStripRect = new Rect(blueprintX + pad, cy, blueprintWidth - pad * 2f, 40f);
                    NanoSurface.DrawDebossedSlot(bStripRect, new Color(0.038f, 0.030f, 0.024f, 0.92f), NanoBrand.Line, 8f);

                    int bSlot = 0;
                    List<string> pinnedSnapshot = new List<string>(blueprintBookmarkedNames);
                    foreach (string bName in pinnedSnapshot)
                    {
                        if (bSlot >= 8) break;
                        InvBaseItem bItem = SafeFindItemByName(bName);
                        if (bItem == null) continue;

                        Rect bRect = new Rect(bStripRect.x + 4f + bSlot * 35f, bStripRect.y + 4f, 32f, 32f);
                        bool bHov = isBlueprintVisible && bRect.Contains(Event.current.mousePosition);
                        DrawBlueprintItemSlot(bRect, true, bHov);
                        DrawItemIcon(new Rect(bRect.x + 3f, bRect.y + 3f, 26f, 26f), bItem);

                        if (bHov)
                        {
                            hoveredTopmostItem = bItem;
                            hoveredTopmostRect = bRect;
                            string locName = T.ItemNames(bItem.name) ?? bItem.name;
                            hoveredTopmostText = es
                                ? $"{locName}\nClic izq.: Receta  ·  Clic der.: Quitar"
                                : $"{locName}\nLeft-Click: Recipe  ·  Right-Click: Unpin";

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

                    cy += 46f;
                }

                // Item Grid
                int totalPages = Mathf.Max(1, Mathf.CeilToInt((float)totalItems / BlueprintItemsPerPage));
                blueprintCurrentPage = Mathf.Clamp(blueprintCurrentPage, 0, totalPages - 1);

                int startIdx = blueprintCurrentPage * BlueprintItemsPerPage;
                int endIdx = Mathf.Min(startIdx + BlueprintItemsPerPage, totalItems);

                float gridStartY = cy;
                float slotW = 44f;
                float slotH = 44f;
                float slotGapX = 5f;
                float slotGapY = 5f;
                float gridW = BlueprintCols * slotW + (BlueprintCols - 1) * slotGapX;
                float gridMarginLeft = (blueprintWidth - gridW) * 0.5f;
                float gridAvailH = (blueprintY + blueprintHeight - 46f) - gridStartY;
                int visibleRows = Mathf.Clamp(Mathf.FloorToInt((gridAvailH + slotGapY) / (slotH + slotGapY)), 1, BlueprintRows);
                int maxIdx = Mathf.Min(endIdx, startIdx + visibleRows * BlueprintCols);

                Rect gridArea = new Rect(blueprintX + 10f, gridStartY, blueprintWidth - 20f, Mathf.Max(48f, gridAvailH));
                if (isBlueprintVisible && gridArea.Contains(Event.current.mousePosition) && Event.current.type == EventType.ScrollWheel)
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

                for (int idx = startIdx; idx < maxIdx; idx++)
                {
                    int gridPos = idx - startIdx;
                    int col = gridPos % BlueprintCols;
                    int row = gridPos / BlueprintCols;

                    float sx = blueprintX + gridMarginLeft + col * (slotW + slotGapX);
                    float sy = gridStartY + row * (slotH + slotGapY);
                    Rect slotRect = new Rect(sx, sy, slotW, slotH);

                    InvBaseItem item = blueprintFilteredItems[idx];
                    bool isPinned = blueprintBookmarkedNames.Contains(item.name);
                    bool hov = isBlueprintVisible && slotRect.Contains(Event.current.mousePosition);

                    DrawBlueprintItemSlot(slotRect, isPinned, hov);
                    DrawItemIcon(new Rect(sx + 5f, sy + 5f, slotW - 10f, slotH - 10f), item);

                    if (isPinned)
                    {
                        NanoSurface.DrawRhombus(new Rect(sx + slotW - 9f, sy + 4f, 5f, 5f), NanoBrand.Ember, true);
                    }

                    if (hov)
                    {
                        hoveredTopmostItem = item;
                        hoveredTopmostRect = slotRect;
                        string localizedName = T.ItemNames(item.name) ?? item.name;
                        hoveredTopmostText = es
                            ? $"{localizedName}\n[R / Izq.] Receta  ·  [U / Der.] Usos  ·  [A] Fijar"
                            : $"{localizedName}\n[R / L-Click] Recipe  ·  [U / R-Click] Uses  ·  [A] Pin";

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

                // Bottom pagination bar
                float botY = blueprintY + blueprintHeight - 38f;
                if (NanoSurface.DrawActionButton(new Rect(blueprintX + pad, botY, 72f, 26f), es ? "‹ Ant." : "‹ Prev", false))
                {
                    if (blueprintCurrentPage > 0) blueprintCurrentPage--;
                }

                string pageInfo = $"{blueprintCurrentPage + 1} / {totalPages}";
                NanoSurface.DrawCrispLabel(new Rect(blueprintX + 88f, botY, blueprintWidth - 176f, 26f),
                    pageInfo, _bpMonoCenterStyle, NanoBrand.Paper, 0.3f);

                if (NanoSurface.DrawActionButton(new Rect(blueprintX + blueprintWidth - pad - 72f, botY, 72f, 26f), es ? "Sig. ›" : "Next ›", false))
                {
                    if (blueprintCurrentPage < totalPages - 1) blueprintCurrentPage++;
                }
            }
        }

        private static void DrawBlueprintItemSlot(Rect rect, bool pinned, bool hovered)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color bg = hovered
                ? new Color(0.145f, 0.112f, 0.086f, 0.96f)
                : new Color(0.048f, 0.038f, 0.030f, 0.92f);
            Color bd = hovered
                ? NanoBrand.Ember
                : (pinned ? new Color(NanoBrand.Ember.r, NanoBrand.Ember.g, NanoBrand.Ember.b, 0.48f) : NanoBrand.Line);
            NanoSurface.DrawDebossedSlot(rect, bg, bd, 7f);
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

        private void DrawRecipeExplorerCardGUI(float anim)
        {
            bool es = IsSpanish();
            Rect fullScreen = new Rect(0, 0, Screen.width, Screen.height);
            GUI.DrawTexture(fullScreen, GetColorTexture(new Color(0.02f, 0.015f, 0.012f, 0.60f * anim)));

            float cardW = 456f;
            float cardH = 408f;
            float cardX = (Screen.width - cardW) * 0.5f;
            float cardY = (Screen.height - cardH) * 0.5f;
            Rect cardRect = new Rect(cardX, cardY, cardW, cardH);

            using (NanoMotion.BeginModal(cardRect, anim))
            {
                DrawBlueprintShell(cardRect, 14f, 0.98f);

                float pad = 14f;
                float curY = cardY + 12f;
                float tx = cardX + pad;

                if (recipeHistory.Count > 0)
                {
                    if (NanoSurface.DrawActionButton(new Rect(tx, curY, 64f, 26f), es ? "‹ Atrás" : "‹ Back", false))
                    {
                        var prev = recipeHistory.Pop();
                        activeRecipeItem = prev.Item1;
                        activeRecipeTab = prev.Item2;
                        activeRecipeIndex = 0;
                        return;
                    }
                    tx += 70f;
                }

                List<BlueprintRecipeEntry> usesList = GetRecipesForIngredient(activeRecipeItem);
                int usesCount = usesList != null ? usesList.Count : 0;

                float tabAreaW = (cardX + cardW - pad - 68f) - tx;
                RecipeTabOpts[0] = new NanoSegmentOption<int>(0, es ? "Receta (R)" : "Recipe (R)");
                RecipeTabOpts[1] = new NanoSegmentOption<int>(1, (es ? "Usos" : "Used In") + $" ({usesCount})");
                if (NanoSurface.DrawSegmented(new Rect(tx, curY, tabAreaW, 26f), RecipeTabOpts, activeRecipeTab, out int nextTab))
                {
                    activeRecipeTab = nextTab;
                    activeRecipeIndex = 0;
                }

                bool isPinned = blueprintBookmarkedNames.Contains(activeRecipeItem.name);
                if (NanoSurface.DrawActionButton(new Rect(cardX + cardW - pad - 62f, curY, 30f, 26f), isPinned ? "★" : "☆", isPinned))
                    ToggleBookmark(activeRecipeItem.name);

                if (NanoSurface.DrawActionButton(new Rect(cardX + cardW - pad - 28f, curY, 28f, 26f), "×", false))
                {
                    isRecipeCardOpen = false;
                    ModalClosedTimestamp = Time.unscaledTime;
                    return;
                }

                curY += 36f;

                // Hero Target Item Plate
                Rect summaryRect = new Rect(cardX + pad, curY, cardW - pad * 2f, 60f);
                NanoSurface.DrawDebossedSlot(summaryRect, new Color(0.040f, 0.032f, 0.026f, 0.94f), NanoBrand.Line, 9f);

                Rect bigSlot = new Rect(summaryRect.x + 8f, summaryRect.y + 8f, 44f, 44f);
                DrawBlueprintItemSlot(bigSlot, isPinned, false);
                DrawItemIcon(new Rect(bigSlot.x + 4f, bigSlot.y + 4f, 36f, 36f), activeRecipeItem);

                string itemName = T.ItemNames(activeRecipeItem.name) ?? activeRecipeItem.name;
                NanoSurface.DrawCrispLabel(new Rect(bigSlot.xMax + 12f, summaryRect.y + 7f, summaryRect.width - 72f, 24f),
                    itemName, _bpItemTitleStyle, NanoBrand.PaperLight, 0.4f);

                string meta = $"{activeRecipeItem.category}";
                if (activeRecipeItem.price > 0) meta += $"  ·  ${activeRecipeItem.price}";
                if (activeRecipeItem.durability > 1) meta += $"  ·  {(es ? "Dur." : "Dur")} {activeRecipeItem.durability}";
                NanoSurface.DrawCrispLabel(new Rect(bigSlot.xMax + 12f, summaryRect.y + 34f, summaryRect.width - 72f, 18f),
                    meta, _bpMetaStyle, NanoBrand.Mute, 0.25f);

                curY += 70f;

                // TAB 0: Crafting Recipe View (R)
                if (activeRecipeTab == 0)
                {
                    List<BlueprintRecipeEntry> recipes = GetRecipesForResult(activeRecipeItem);

                    if (recipes == null || recipes.Count == 0)
                    {
                        Rect emptyBox = new Rect(cardX + pad, curY + 12f, cardW - pad * 2f, 150f);
                        NanoSurface.DrawDebossedSlot(emptyBox, new Color(0.038f, 0.030f, 0.024f, 0.88f), NanoBrand.Line, 10f);

                        string noRecipeTitle = es ? "Sin receta de crafteo" : "No Crafting Recipe";
                        NanoSurface.DrawCrispLabel(new Rect(emptyBox.x + 16f, emptyBox.y + 38f, emptyBox.width - 32f, 26f),
                            noRecipeTitle, _bpItemTitleStyle, NanoBrand.Warn, 0.35f);

                        string howToObtain = es
                            ? "Se obtiene por drops del mundo, minería, misiones o comerciantes."
                            : "Obtainable via world drops, mining, quests, or merchants.";
                        NanoSurface.DrawCrispLabel(new Rect(emptyBox.x + 16f, emptyBox.y + 72f, emptyBox.width - 32f, 44f),
                            howToObtain, _bpBodyStyle, NanoBrand.Mute, 0.25f);
                    }
                    else
                    {
                        activeRecipeIndex = Mathf.Clamp(activeRecipeIndex, 0, recipes.Count - 1);
                        BlueprintRecipeEntry currentEntry = recipes[activeRecipeIndex];
                        Recipe recipe = currentEntry.recipe;

                        Rect navRect = new Rect(cardX + pad, curY, cardW - pad * 2f, 24f);
                        string stName = es ? currentEntry.stationNameEs : currentEntry.stationName;
                        Color stColor = NanoBrand.Ok;
                        if (currentEntry.stationName == "Furnace") stColor = NanoBrand.Ember;
                        else if (currentEntry.stationName == "Cauldron") stColor = new Color(0.72f, 0.58f, 0.92f);
                        else if (currentEntry.stationName == "Anvil") stColor = new Color(0.56f, 0.78f, 0.94f);
                        else if (currentEntry.stationName == "Coloring Table") stColor = NanoBrand.Warn;

                        _bpMeasure.text = stName;
                        float chipW = Mathf.Max(82f, _bpChipStyle.CalcSize(_bpMeasure).x + 16f);
                        NanoSurface.DrawChip(new Rect(navRect.x, navRect.y + 2f, chipW, 20f), stName, _bpChipStyle,
                            new Color(0.08f, 0.064f, 0.050f, 0.94f), new Color(stColor.r, stColor.g, stColor.b, 0.65f), stColor, 5f);

                        if (recipes.Count > 1)
                        {
                            if (NanoSurface.DrawActionButton(new Rect(navRect.xMax - 124f, navRect.y, 34f, 22f), "‹", false))
                                activeRecipeIndex = Mathf.Max(0, activeRecipeIndex - 1);

                            NanoSurface.DrawCrispLabel(new Rect(navRect.xMax - 88f, navRect.y, 52f, 22f),
                                $"{activeRecipeIndex + 1} / {recipes.Count}", _bpMonoCenterStyle, NanoBrand.Paper, 0.3f);

                            if (NanoSurface.DrawActionButton(new Rect(navRect.xMax - 34f, navRect.y, 34f, 22f), "›", false))
                                activeRecipeIndex = Mathf.Min(recipes.Count - 1, activeRecipeIndex + 1);
                        }

                        curY += 32f;

                        // Recessed Crafting Stage
                        float stageH = 166f;
                        Rect stageRect = new Rect(cardX + pad, curY, cardW - pad * 2f, stageH);
                        NanoSurface.DrawDebossedSlot(stageRect, new Color(0.036f, 0.028f, 0.022f, 0.90f), NanoBrand.Line, 10f);

                        float cSlotSize = 44f;
                        float cSlotGap = 5f;
                        float matrixX = stageRect.x + 30f;
                        float matrixY = stageRect.y + (stageH - (3 * cSlotSize + 2 * cSlotGap)) * 0.5f;

                        for (int r = 0; r < 3; r++)
                        {
                            for (int c = 0; c < 3; c++)
                            {
                                Rect cSlotRect = new Rect(matrixX + c * (cSlotSize + cSlotGap), matrixY + r * (cSlotSize + cSlotGap), cSlotSize, cSlotSize);
                                InvGameItem ing = null;
                                if (recipe.ingredients != null && r < recipe.ingredients.GetLength(0) && c < recipe.ingredients.GetLength(1))
                                    ing = recipe.ingredients[r, c];

                                bool ingHov = ing != null && ing.baseItem != null && isRecipeCardOpen && cSlotRect.Contains(Event.current.mousePosition);
                                DrawBlueprintItemSlot(cSlotRect, false, ingHov);

                                if (ing != null && ing.baseItem != null)
                                {
                                    DrawItemIcon(new Rect(cSlotRect.x + 5f, cSlotRect.y + 5f, cSlotSize - 10f, cSlotSize - 10f), ing.baseItem);

                                    if (ing.count > 1)
                                    {
                                        NanoSurface.DrawCrispLabel(new Rect(cSlotRect.x + 2f, cSlotRect.yMax - 15f, cSlotSize - 5f, 14f),
                                            ing.count.ToString(), _bpMonoStyle, NanoBrand.PaperLight, 0.55f);
                                    }

                                    if (ingHov)
                                    {
                                        hoveredTopmostItem = ing.baseItem;
                                        hoveredTopmostRect = cSlotRect;
                                        hoveredTopmostText = es
                                            ? $"{T.ItemNames(ing.baseItem.name) ?? ing.baseItem.name} (×{ing.count})\nClic izq.: Receta  ·  Clic der.: Usos"
                                            : $"{T.ItemNames(ing.baseItem.name) ?? ing.baseItem.name} (×{ing.count})\nLeft-Click: Recipe  ·  Right-Click: Uses";

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

                        // Crafting Arrow ->
                        float arrowX = matrixX + 3 * (cSlotSize + cSlotGap) + 22f;
                        float arrowY = stageRect.center.y - 16f;
                        NanoSurface.DrawCrispLabel(new Rect(arrowX, arrowY, 36f, 32f), "→", _bpArrowStyle, NanoBrand.Ember, 0.35f);

                        // Output Result Slot
                        float outX = arrowX + 48f;
                        float outY = stageRect.center.y - 30f;
                        Rect outSlotRect = new Rect(outX, outY, 60f, 60f);
                        bool outHov = isRecipeCardOpen && outSlotRect.Contains(Event.current.mousePosition);
                        NanoSurface.DrawDebossedSlot(outSlotRect, new Color(0.068f, 0.052f, 0.038f, 0.96f),
                            outHov ? NanoBrand.Ember : new Color(NanoBrand.Ember.r, NanoBrand.Ember.g, NanoBrand.Ember.b, 0.65f), 10f);
                        DrawItemIcon(new Rect(outX + 7f, outY + 7f, 46f, 46f), activeRecipeItem);

                        int resultCount = recipe.result != null ? recipe.result.count : 1;
                        if (resultCount > 1)
                        {
                            NanoSurface.DrawCrispLabel(new Rect(outX + 4f, outY + 41f, 50f, 16f),
                                $"×{resultCount}", _bpMonoStyle, NanoBrand.EmberSoft, 0.5f);
                        }

                        if (outHov)
                        {
                            hoveredTopmostItem = activeRecipeItem;
                            hoveredTopmostRect = outSlotRect;
                            hoveredTopmostText = es
                                ? $"{T.ItemNames(activeRecipeItem.name) ?? activeRecipeItem.name} (×{resultCount})\nClic der.: Ver usos"
                                : $"{T.ItemNames(activeRecipeItem.name) ?? activeRecipeItem.name} (×{resultCount})\nRight-Click: Uses";

                            if (Event.current.type == EventType.MouseDown && Event.current.button == 1)
                            {
                                OpenRecipeCard(activeRecipeItem, 1);
                                Event.current.Use();
                            }
                        }

                        // Extra Recipe Info Footer
                        float infoY = stageRect.yMax + 10f;
                        string extraInfo = "";
                        if (recipe.mana > 0.1f) extraInfo += $"Maná {recipe.mana:F0}  ·  ";
                        if (recipe.minutes > 0.01f) extraInfo += $"{(es ? "Tiempo" : "Time")} {recipe.minutes:F1}m  ·  ";
                        if (recipe.cost > 0.1f) extraInfo += $"{(es ? "Costo" : "Cost")} {recipe.cost:F0}  ·  ";
                        extraInfo += $"{(es ? "Estación" : "Station")}: {stName}";
                        NanoSurface.DrawCrispLabel(new Rect(cardX + pad, infoY, cardW - pad * 2f, 20f),
                            extraInfo, _bpMetaStyle, NanoBrand.Mute, 0.25f);
                    }
                }
                // TAB 1: Used In View (U) with Custom Aether ScrollView
                else if (activeRecipeTab == 1)
                {
                    if (usesList == null || usesList.Count == 0)
                    {
                        Rect emptyBox = new Rect(cardX + pad, curY + 12f, cardW - pad * 2f, 150f);
                        NanoSurface.DrawDebossedSlot(emptyBox, new Color(0.038f, 0.030f, 0.024f, 0.88f), NanoBrand.Line, 10f);

                        NanoSurface.DrawCrispLabel(new Rect(emptyBox.x + 16f, emptyBox.y + 42f, emptyBox.width - 32f, 26f),
                            es ? "Sin usos en recetas" : "Not Used in Any Recipe", _bpItemTitleStyle, NanoBrand.Warn, 0.35f);
                        NanoSurface.DrawCrispLabel(new Rect(emptyBox.x + 16f, emptyBox.y + 74f, emptyBox.width - 32f, 40f),
                            es ? "Este ítem no se requiere como ingrediente actualmente." : "This item is not required as an ingredient.",
                            _bpBodyStyle, NanoBrand.Mute, 0.25f);
                    }
                    else
                    {
                        Rect scrollArea = new Rect(cardX + pad, curY, cardW - pad * 2f, cardH - (curY - cardY) - 14f);
                        float viewHeight = usesList.Count * 48f;
                        usedInScrollPos = NanoSurface.BeginScrollView(scrollArea, usedInScrollPos, viewHeight, out float uRowW);
                        try
                        {
                            for (int u = 0; u < usesList.Count; u++)
                            {
                                BlueprintRecipeEntry recEntry = usesList[u];
                                Recipe rec = recEntry.recipe;
                                InvBaseItem resItem = rec.result?.baseItem;
                                if (resItem == null && !string.IsNullOrEmpty(rec.name))
                                    resItem = SafeFindItemByName(rec.name);
                                if (resItem == null) continue;

                                Rect rowRect = new Rect(0f, u * 48f, uRowW, 44f);
                                bool rHov = rowRect.Contains(Event.current.mousePosition);
                                if (Event.current.type == EventType.Repaint)
                                {
                                    NanoSurface.FillRounded(rowRect,
                                        rHov ? new Color(0.115f, 0.090f, 0.072f, 0.94f) : new Color(0.056f, 0.045f, 0.036f, 0.88f), 8f);
                                    NanoSurface.StrokeRounded(rowRect,
                                        rHov ? new Color(NanoBrand.Ember.r, NanoBrand.Ember.g, NanoBrand.Ember.b, 0.55f) : NanoBrand.Line, 1f, 8f);
                                }

                                Rect uSlot = new Rect(rowRect.x + 5f, rowRect.y + 4f, 36f, 36f);
                                DrawBlueprintItemSlot(uSlot, false, false);
                                DrawItemIcon(new Rect(uSlot.x + 3f, uSlot.y + 3f, 30f, 30f), resItem);

                                string resName = T.ItemNames(resItem.name) ?? resItem.name;
                                if (rec.result != null && rec.result.count > 1) resName += $"  ×{rec.result.count}";
                                string stBadge = es ? recEntry.stationNameEs : recEntry.stationName;

                                NanoSurface.DrawCrispLabel(new Rect(uSlot.xMax + 10f, rowRect.y + 5f, rowRect.width - 118f, 18f),
                                    resName, _bpBodyStyle, NanoBrand.PaperLight, 0.35f);
                                NanoSurface.DrawCrispLabel(new Rect(uSlot.xMax + 10f, rowRect.y + 24f, rowRect.width - 118f, 15f),
                                    stBadge, _bpMetaStyle, NanoBrand.Ok, 0.25f);

                                if (NanoSurface.DrawActionButton(new Rect(rowRect.xMax - 62f, rowRect.y + 9f, 54f, 26f), es ? "Ver" : "View", false))
                                {
                                    OpenRecipeCard(resItem, 0);
                                    return;
                                }

                                if (rHov)
                                {
                                    hoveredTopmostItem = resItem;
                                    hoveredTopmostRect = rowRect;
                                    hoveredTopmostText = es
                                        ? $"{T.ItemNames(resItem.name) ?? resItem.name}\nPulsa Ver para abrir la receta"
                                        : $"{T.ItemNames(resItem.name) ?? resItem.name}\nClick View to inspect recipe";
                                }
                            }
                        }
                        finally
                        {
                            NanoSurface.EndScrollView();
                        }
                    }
                }
            }
        }

        // =====================================================================
        //        TOPMOST FLOATING TOOLTIP (SMOKED CRYSTAL AETHER INK)
        // =====================================================================
        private void DrawTopmostTooltipCard(Vector2 mousePos, string text, InvBaseItem item)
        {
            int oldDepth = GUI.depth;
            GUI.depth = -2000;
            try
            {
                EnsureBlueprintCraftStyles();
                _bpMeasure.text = text;
                Vector2 size = _bpBodyStyle.CalcSize(_bpMeasure);
                float cardW = Mathf.Max(184f, Mathf.Min(310f, size.x + 24f));
                float bodyH = _bpBodyStyle.CalcHeight(_bpMeasure, cardW - 22f);
                float cardH = bodyH + (item != null ? 36f : 18f);

                float px = Mathf.Clamp(mousePos.x + 16f, 10f, Screen.width - cardW - 12f);
                float py = Mathf.Clamp(mousePos.y - cardH - 8f, 10f, Screen.height - cardH - 12f);
                Rect cardRect = new Rect(px, py, cardW, cardH);

                // Floating crystal-glass surface (glass reserved strictly for floating layers!)
                NanoSurface.DrawCardSurface(
                    cardRect,
                    new Color(0.088f, 0.070f, 0.056f, 0.92f),
                    new Color(0.044f, 0.036f, 0.028f, 0.94f),
                    new Color(NanoBrand.Ember.r, NanoBrand.Ember.g, NanoBrand.Ember.b, 0.42f),
                    NanoBrand.Ember,
                    new Color(1f, 0.96f, 0.90f, 0.14f),
                    9f, 1f, 0f, 0f);

                float ty = py + 8f;
                if (item != null)
                {
                    string catInfo = $"{item.category}";
                    if (item.price > 0) catInfo += $"  ·  ${item.price}";
                    if (item.durability > 1) catInfo += $"  ·  Dur {item.durability}";
                    NanoSurface.DrawCrispLabel(new Rect(px + 11f, ty, cardW - 22f, 15f), catInfo, _bpMetaStyle, NanoBrand.EmberSoft, 0.25f);
                    ty += 18f;
                }

                NanoSurface.DrawCrispLabel(new Rect(px + 11f, ty, cardW - 22f, bodyH + 4f), text, _bpBodyStyle, NanoBrand.PaperLight, 0.35f);
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
            EnsureBlueprintCraftStyles();
            bool es = IsSpanish();
            float pad = 12f;
            float colW = Mathf.Max(260f, rightRect.width - pad * 2f);
            float halfW = (colW - 8f) * 0.5f;
            var col = new Rect(pad, baseY, colW, rightRect.height);

            float sy = NanoSurface.DrawSectionHeader(col, baseY,
                es ? "Catálogo de recetas Blueprint" : "Blueprint recipe browser",
                $"{BlueprintToggleKey.Value}");

            bool nextEn = NanoSurface.DrawToggleRow(
                new Rect(pad, sy, halfW, 32f),
                es ? $"Navegador activo ({BlueprintToggleKey.Value})" : $"Browser enabled ({BlueprintToggleKey.Value})",
                BlueprintEnabled.Value);
            if (nextEn != BlueprintEnabled.Value) { BlueprintEnabled.Value = nextEn; Config.Save(); }

            bool nextAuto = NanoSurface.DrawToggleRow(
                new Rect(pad + halfW + 8f, sy, halfW, 32f),
                es ? "Auto-abrir con inventario" : "Auto-open with inventory",
                BlueprintAutoOpenWithInventory.Value);
            if (nextAuto != BlueprintAutoOpenWithInventory.Value) { BlueprintAutoOpenWithInventory.Value = nextAuto; Config.Save(); }

            sy += 44f;
            sy = NanoSurface.DrawSectionHeader(col, sy, es ? "Atajos de teclado" : "Shortcuts");

            DrawShortcutRow(pad, sy, colW, $"{BlueprintRecipeKey.Value}", es ? "Ver receta de crafteo (o clic izquierdo)" : "View crafting recipe (or left-click)");
            sy += 28f;
            DrawShortcutRow(pad, sy, colW, $"{BlueprintUsageKey.Value}", es ? "Ver usos del ingrediente (o clic derecho)" : "View recipe uses (or right-click)");
            sy += 28f;
            DrawShortcutRow(pad, sy, colW, $"{BlueprintBookmarkKey.Value}", es ? "Fijar o quitar de marcadores" : "Pin or unpin bookmark");
            sy += 28f;
            DrawShortcutRow(pad, sy, colW, $"{BlueprintToggleKey.Value}", es ? "Mostrar u ocultar el catálogo" : "Toggle catalog drawer");
        }

        private static void DrawShortcutRow(float x, float y, float w, string key, string desc)
        {
            var chip = new Rect(x, y + 2f, 34f, 20f);
            NanoSurface.DrawChip(chip, key, _bpChipStyle,
                new Color(0.11f, 0.088f, 0.070f, 0.94f), NanoBrand.Line, NanoBrand.EmberSoft, 5f);
            NanoSurface.DrawCrispLabel(new Rect(x + 44f, y + 2f, w - 44f, 20f), desc, _bpBodyStyle, NanoBrand.Paper, 0.25f);
        }

    }
}
