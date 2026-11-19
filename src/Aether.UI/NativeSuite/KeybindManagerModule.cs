#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Linq;
using Aether.UI.Modules;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BlockStoryLens
{
    /// <summary>
    /// Native Controls & Hotkeys Integration Engineer Module (Ponytail style: minimal, rock-solid).
    /// Provides full unified hotkey management for Vanilla actions and Mod Suite hotkeys,
    /// complete with real-time overlap detection, filters, instant search, and interactive rebinding.
    /// </summary>
    public partial class BlockStorySuitePlugin
    {
        // =====================================================================
        //                      MOD SUITE HOTKEY CONFIGS
        // =====================================================================
        // Blueprint's key entries are owned by Aether.Mods.Blueprint; see BlueprintBridge.cs.
        public static ConfigEntry<KeyCode> HudDragKey { get; private set; }
        public static ConfigEntry<KeyCode> KeybindsManagerKey { get; private set; }

        // =====================================================================
        //                      DATA STRUCTURES & REGISTRY
        // =====================================================================
        public enum KeybindFilter
        {
            All,
            Vanilla,
            Mods,
            OverlapsOnly
        }

        public class KeybindEntry
        {
            public string Id;
            public string NameEn;
            public string NameEs;
            public string Category;               // "Vanilla", "Wayfinder", "Blueprint", "HUD Layout", "Lens WAILA", "Sodium", "Hotkeys"
            public bool IsVanilla;
            public KeyCode PrimaryKey;
            public KeyCode DefaultPrimaryKey;
            public KeyCode SecondaryKey;          // Vanilla secondary key
            public KeyCode DefaultSecondaryKey;
            public ConfigEntry<KeyCode> ModConfigEntry;
            public string VanillaActionName;

            /// <summary>
            /// When the binding is live: "Any", "Gameplay", "Inventory" or "Menu". Two bindings only
            /// conflict if their contexts can be active at the same time (M11).
            /// </summary>
            public string Context = "Any";

            public readonly List<string> Conflicts = new List<string>();

            /// <summary>A key setting read from another BepInEx plugin's own config (not Aether's).</summary>
            public bool IsBepInExMod;

            public Color CategoryColor
            {
                get
                {
                    switch (Category)
                    {
                        case "Vanilla": return new Color(0.68f, 0.76f, 0.86f, 1.0f);
                        case "Wayfinder": return new Color(0.45f, 0.82f, 0.60f, 1.0f);
                        case "Blueprint": return new Color(0.95f, 0.56f, 0.34f, 1.0f);
                        case "HUD Layout":
                        case "Canvas": return new Color(0.48f, 0.78f, 0.94f, 1.0f);
                        case "Lens":
                        case "Lens WAILA": return new Color(0.70f, 0.86f, 0.44f, 1.0f);
                        case "Dusk": return new Color(0.84f, 0.66f, 0.92f, 1.0f);
                        case "Sodium":
                        case "FerriteCore": return new Color(0.42f, 0.84f, 0.80f, 1.0f);
                        case "Hotkeys": return new Color(0.94f, 0.76f, 0.42f, 1.0f);
                        default: return new Color(0.76f, 0.72f, 0.66f, 1.0f);
                    }
                }
            }
        }

        // State variables
        public bool isKeybindsManagerOpen { get; private set; } = false;
        public bool showKeybindsModal { get => isKeybindsManagerOpen; set => SetKeybindsManagerOpen(value); }

        public void InitKeybindManager()
        {
            InitKeybindManagerConfigs(Config);
            InitKeybindRegistry();
        }

        public void DrawKeybindManagerGUI(float anim = 1f)
        {
            if (isKeybindsManagerOpen)
                SyncBepInExKeybinds();
            DrawKeybindsManagerGUI(anim);
        }

        // ---- Keys of other BepInEx plugins (PotatoMode and the like) ------------------------
        private float nextPluginKeySync;

        /// <summary>
        /// Lists every <c>ConfigEntry&lt;KeyCode&gt;</c> of the other BepInEx plugins next to Aether's own keys, so their clashes show up
        /// here too and they can be rebound from this window. Settings typed <c>KeyboardShortcut</c> are not listed: they hold key combinations,
        /// not a single key; <c>aether doctor</c> reports those from the config files.
        /// </summary>
        private void SyncBepInExKeybinds()
        {
            if (rebindingEntry != null || Time.unscaledTime < nextPluginKeySync) return;
            nextPluginKeySync = Time.unscaledTime + 1f;

            try
            {
                var owned = new HashSet<ConfigEntry<KeyCode>>(allKeybinds.Where(k => k.ModConfigEntry != null && !k.IsBepInExMod).Select(k => k.ModConfigEntry));
                var found = new List<(string Plugin, string Setting, ConfigEntry<KeyCode> Entry)>();
                foreach (var info in Chainloader.PluginInfos.Values.ToList())
                {
                    string guid = info.Metadata?.GUID ?? string.Empty;
                    if (info.Instance == null || guid == "dev.ultimaibrahim.aether") continue;

                    ConfigFile cfg = info.Instance.Config;
                    foreach (ConfigDefinition def in cfg.Keys.ToList())
                    {
                        if (cfg[def] is ConfigEntry<KeyCode> entry && !owned.Contains(entry))
                            found.Add((info.Metadata.Name, def.Key, entry));
                    }
                }

                var current = allKeybinds.Where(k => k.IsBepInExMod).ToList();
                bool sameSet = current.Count == found.Count &&
                               current.Select(k => k.ModConfigEntry).SequenceEqual(found.Select(f => f.Entry));
                if (sameSet)
                {
                    bool changed = false;
                    foreach (var k in current)
                        if (k.PrimaryKey != k.ModConfigEntry.Value) { k.PrimaryKey = k.ModConfigEntry.Value; changed = true; }
                    if (changed) { RecalculateConflicts(); ApplyFilterAndSearch(); }
                    return;
                }

                allKeybinds.RemoveAll(k => k.IsBepInExMod);
                foreach (var f in found)
                {
                    var nano = Aether.Nano.NanoKeys.Find(f.Entry); // a Nano mod's own label for the key, as in the game's window
                    allKeybinds.Add(new KeybindEntry
                    {
                        Id = $"plugin_{f.Plugin}_{f.Entry.Definition.Section}_{f.Setting}",
                        NameEn = nano?.LabelEn ?? f.Setting,
                        NameEs = nano != null ? (nano.LabelEs ?? nano.LabelEn) : f.Setting,
                        Category = nano != null ? ShortModName(nano.ModName) : f.Plugin,
                        IsVanilla = false,
                        IsBepInExMod = true,
                        ModConfigEntry = f.Entry,
                        PrimaryKey = f.Entry.Value,
                        DefaultPrimaryKey = (KeyCode)f.Entry.DefaultValue,
                        SecondaryKey = KeyCode.None,
                        DefaultSecondaryKey = KeyCode.None
                    });
                }
                Logger.LogInfo($"[Aether.Keybinds] Keys of other BepInEx plugins listed: {found.Count}");
                RecalculateConflicts();
                ApplyFilterAndSearch();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("[Aether.Keybinds] Could not read other plugins' keys: " + ex.Message);
            }
        }

        /// <summary>"Aether Blueprint" → "Blueprint": fits the category chip and picks up the mod's colour.</summary>
        private static string ShortModName(string name) =>
            name != null && name.StartsWith("Aether ", StringComparison.Ordinal) ? name.Substring(7) : name;

        private bool openedFromNativeKeybinds = false;
        private readonly List<KeybindEntry> allKeybinds = new List<KeybindEntry>();
        private readonly List<KeybindEntry> filteredKeybinds = new List<KeybindEntry>();
        private KeybindFilter activeFilter = KeybindFilter.All;
        private string searchQuery = "";
        private Vector2 scrollKeybindsPos;
        private KeybindEntry rebindingEntry = null;
        private int rebindingSlot = 0; // 0 = Primary, 1 = Secondary
        private int totalOverlapCount = 0;
        private int totalOverlapEntries = 0;
        private GUIStyle searchInputStyle;

        public int VanillaKeybindCount => allKeybinds.Count(k => k.IsVanilla);

        // =====================================================================
        //                      INITIALIZATION & REGISTRY SETUP
        // =====================================================================
        private void InitKeybindManagerConfigs(ConfigFile config)
        {
            // Blueprint owns [Blueprint] ToggleKey/RecipeLookupKey/UsageLookupKey/BookmarkKey (Aether.Mods.Blueprint).
            HudDragKey = config.Bind("HUDLayout", "HudDragKey", KeyCode.F9, "Open interactive HUD element positioning & drag mode.");
            KeybindsManagerKey = config.Bind("Keybinds", "ManagerKey", KeyCode.F7, "Open Unified Keybinds & Controls Manager modal.");
        }

        private void InitKeybindRegistry()
        {
            allKeybinds.Clear();

            // 1. VANILLA ACTIONS (Native Block Story Inputs)
            RegisterVanillaAction("vanilla_forward", "Move Forward", "Mover Adelante", "forward", KeyCode.W, KeyCode.UpArrow);
            RegisterVanillaAction("vanilla_backward", "Move Backward", "Mover Atrás", "backward", KeyCode.S, KeyCode.DownArrow);
            RegisterVanillaAction("vanilla_left", "Strafe Left", "Mover Izquierda", "Left", KeyCode.A, KeyCode.LeftArrow);
            RegisterVanillaAction("vanilla_right", "Strafe Right", "Mover Derecha", "Right", KeyCode.D, KeyCode.RightArrow);
            RegisterVanillaAction("vanilla_jump", "Jump / Ascend", "Saltar / Ascender", "Jump", KeyCode.Space, KeyCode.None);
            RegisterVanillaAction("vanilla_fly", "Fly / Descend", "Volar / Descender", "Fly", KeyCode.LeftShift, KeyCode.RightShift);
            RegisterVanillaAction("vanilla_attack", "Attack / Mine", "Atacar / Minar", "Attack", KeyCode.Mouse0, KeyCode.None);
            RegisterVanillaAction("vanilla_use", "Use / Place Block", "Usar / Colocar Bloque", "Use", KeyCode.Mouse1, KeyCode.None);
            RegisterVanillaAction("vanilla_inventory", "Open Inventory", "Abrir Inventario", "Inventory", KeyCode.E, KeyCode.I);
            RegisterVanillaAction("vanilla_settings", "Settings / Pause", "Ajustes / Pausa", "Settings", KeyCode.Escape, KeyCode.None);
            RegisterVanillaAction("vanilla_slot1", "Quick Slot 1", "Acceso Rápido 1", "Quick Slot 1", KeyCode.Alpha1, KeyCode.Keypad1);
            RegisterVanillaAction("vanilla_slot2", "Quick Slot 2", "Acceso Rápido 2", "Quick Slot 2", KeyCode.Alpha2, KeyCode.Keypad2);
            RegisterVanillaAction("vanilla_slot3", "Quick Slot 3", "Acceso Rápido 3", "Quick Slot 3", KeyCode.Alpha3, KeyCode.Keypad3);
            RegisterVanillaAction("vanilla_slot4", "Quick Slot 4", "Acceso Rápido 4", "Quick Slot 4", KeyCode.Alpha4, KeyCode.Keypad4);
            RegisterVanillaAction("vanilla_slot5", "Quick Slot 5", "Acceso Rápido 5", "Quick Slot 5", KeyCode.Alpha5, KeyCode.Keypad5);
            RegisterVanillaAction("vanilla_slot6", "Quick Slot 6", "Acceso Rápido 6", "Quick Slot 6", KeyCode.Alpha6, KeyCode.Keypad6);
            RegisterVanillaAction("vanilla_slot7", "Quick Slot 7", "Acceso Rápido 7", "Quick Slot 7", KeyCode.Alpha7, KeyCode.Keypad7);
            RegisterVanillaAction("vanilla_slot8", "Quick Slot 8", "Acceso Rápido 8", "Quick Slot 8", KeyCode.Alpha8, KeyCode.Keypad8);
            RegisterVanillaAction("vanilla_slot9", "Quick Slot 9", "Acceso Rápido 9", "Quick Slot 9", KeyCode.Alpha9, KeyCode.Keypad9);
            RegisterVanillaAction("vanilla_slot10", "Quick Slot 10", "Acceso Rápido 10", "Quick Slot 10", KeyCode.Alpha0, KeyCode.Keypad0);
            RegisterVanillaAction("vanilla_prev_slot", "Previous Slot", "Slot Anterior", "Previous Slot", KeyCode.Q, KeyCode.None);
            RegisterVanillaAction("vanilla_next_slot", "Next Slot", "Slot Siguiente", "Next Slot", KeyCode.R, KeyCode.None);
            RegisterVanillaAction("vanilla_drop", "Drop Item", "Soltar Objeto", "Drop", KeyCode.G, KeyCode.None);
            RegisterVanillaAction("vanilla_chat", "Chat Input", "Abrir Chat", "Chat", KeyCode.Return, KeyCode.KeypadEnter);

            // 2. MOD SUITE HOTKEYS
            RegisterModAction("mod_hud_drag", "HUD Drag Editor", "Editor de Arrastre del HUD", "HUD Layout", KeyCode.F9, HudDragKey);
            // F8 belongs to the (currently parked) FerriteCore mod: list it only while that mod is installed.
            if (SodiumOverlayKey != null)
                RegisterModAction("mod_sodium_perf", "Performance Overlay", "Overlay de Rendimiento", "FerriteCore", KeyCode.F8, SodiumOverlayKey);
            // Blueprint's and Lens's keys come from their own configs now (listed with the other plugins' keys).
            RegisterModAction("mod_keybinds_manager", "Hotkeys & Controls Manager", "Gestor de Teclas y Controles", "Hotkeys", KeyCode.F7, KeybindsManagerKey);
            RegisterModAction("mod_menu", "Suite Mod Menu", "Menú de Mods de la Suite", "Hotkeys", KeyCode.F10, MenuKey);

            // Read persisted vanilla bindings and recalculate conflicts
            LoadAllVanillaBindings();
            RecalculateConflicts();
            ApplyFilterAndSearch();
        }

        private void RegisterVanillaAction(string id, string nameEn, string nameEs, string actionName, KeyCode defPrimary, KeyCode defSecondary)
        {
            allKeybinds.Add(new KeybindEntry
            {
                Id = id,
                NameEn = nameEn,
                NameEs = nameEs,
                Category = "Vanilla",
                IsVanilla = true,
                VanillaActionName = actionName,
                PrimaryKey = defPrimary,
                DefaultPrimaryKey = defPrimary,
                SecondaryKey = defSecondary,
                DefaultSecondaryKey = defSecondary,
                Context = "Gameplay"
            });
        }

        private void RegisterModAction(string id, string nameEn, string nameEs, string category, KeyCode defKey, ConfigEntry<KeyCode> config, string context = "Any")
        {
            allKeybinds.Add(new KeybindEntry
            {
                Id = id,
                NameEn = nameEn,
                NameEs = nameEs,
                Category = category,
                IsVanilla = false,
                PrimaryKey = config != null ? config.Value : defKey,
                DefaultPrimaryKey = defKey,
                SecondaryKey = KeyCode.None,
                DefaultSecondaryKey = KeyCode.None,
                ModConfigEntry = config,
                Context = context
            });
        }

        // =====================================================================
        //                      PERSISTENCE & NATIVE BINDINGS
        // =====================================================================
        private void LoadAllVanillaBindings()
        {
            for (int i = 0; i < allKeybinds.Count; i++)
            {
                var entry = allKeybinds[i];
                if (!entry.IsVanilla) continue;

                // Slot 0 (Primary)
                string raw0 = PlayerPrefs.GetString($"Rebind_{entry.VanillaActionName}_0", string.Empty);
                if (!string.IsNullOrEmpty(raw0))
                {
                    KeyCode k0 = InputPathToKeyCode(raw0);
                    if (k0 != KeyCode.None) entry.PrimaryKey = k0;
                }
                else if (PlayerPrefs.HasKey($"Suite_Rebind_{entry.Id}_0"))
                {
                    entry.PrimaryKey = (KeyCode)PlayerPrefs.GetInt($"Suite_Rebind_{entry.Id}_0", (int)entry.DefaultPrimaryKey);
                }

                // Slot 1 (Secondary)
                string raw1 = PlayerPrefs.GetString($"Rebind_{entry.VanillaActionName}_1", string.Empty);
                if (!string.IsNullOrEmpty(raw1))
                {
                    KeyCode k1 = InputPathToKeyCode(raw1);
                    if (k1 != KeyCode.None) entry.SecondaryKey = k1;
                }
                else if (PlayerPrefs.HasKey($"Suite_Rebind_{entry.Id}_1"))
                {
                    entry.SecondaryKey = (KeyCode)PlayerPrefs.GetInt($"Suite_Rebind_{entry.Id}_1", (int)entry.DefaultSecondaryKey);
                }
            }
        }

        private bool pendingVanillaBindingApply = true;
        private float lastBindingApplyAttempt = -10f;

        /// <summary>
        /// Re-applies persisted key rebinds to Unity's InputSystem on startup. The panel used to
        /// only paint saved keys into its list, so after a restart the game kept the defaults while
        /// the UI claimed the new binding.
        /// </summary>
        public void ApplyPersistedVanillaBindings()
        {
            if (!pendingVanillaBindingApply) return;
            if (Time.unscaledTime - lastBindingApplyAttempt < 0.5f) return;
            lastBindingApplyAttempt = Time.unscaledTime;

            try
            {
                if (InputSystem.actions == null) return; // not ready yet, retry in 0.5 s
                pendingVanillaBindingApply = false;

                for (int i = 0; i < allKeybinds.Count; i++)
                {
                    KeybindEntry entry = allKeybinds[i];
                    if (entry == null || !entry.IsVanilla) continue;

                    ApplyBindingOverride(entry, 0, entry.PrimaryKey);
                    ApplyBindingOverride(entry, 1, entry.SecondaryKey);
                }
            }
            catch (Exception ex)
            {
                Log?.LogWarning("[Keybinds] Could not re-apply saved bindings: " + ex.Message);
            }
        }

        private void ApplyBindingOverride(KeybindEntry entry, int slot, KeyCode key)
        {
            try
            {
                if (InputSystem.actions == null) return;

                var act = InputSystem.actions.FindAction(entry.VanillaActionName);
                if (act == null) return;

                int bindingIndex = ResolveBindingIndex(act, slot);
                if (bindingIndex < 0) return; // composite/vector action: leave it alone

                act.ApplyBindingOverride(bindingIndex, key == KeyCode.None ? "" : KeyCodeToInputPath(key));
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[Keybinds] Unity InputSystem override warning for {entry.VanillaActionName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Maps logical slot 0/1 (primary/secondary) to a real binding index, skipping composites and
        /// their parts. Using the slot as the index replaced a whole 2D composite (breaking strafing).
        /// </summary>
        private static int ResolveBindingIndex(UnityEngine.InputSystem.InputAction act, int slot)
        {
            int seen = 0;
            for (int i = 0; i < act.bindings.Count; i++)
            {
                var binding = act.bindings[i];
                if (binding.isComposite || binding.isPartOfComposite) continue;
                if (seen == slot) return i;
                seen++;
            }
            return -1;
        }

        private void SaveVanillaBinding(KeybindEntry entry, int slot, KeyCode key)
        {
            string path = KeyCodeToInputPath(key);
            PlayerPrefs.SetString($"Rebind_{entry.VanillaActionName}_{slot}", path);
            PlayerPrefs.SetInt($"Suite_Rebind_{entry.Id}_{slot}", (int)key);
            PlayerPrefs.Save();

            ApplyBindingOverride(entry, slot, key);
        }

        // =====================================================================
        //                   REAL-TIME OVERLAP DETECTOR
        // =====================================================================
        public void RecalculateConflicts()
        {
            // Clear prior conflicts
            for (int i = 0; i < allKeybinds.Count; i++)
            {
                allKeybinds[i].Conflicts.Clear();
            }

            // Build dictionary of KeyCode -> List<(Entry, Slot)>
            var keyUsage = new Dictionary<KeyCode, List<(KeybindEntry entry, int slot)>>();

            for (int i = 0; i < allKeybinds.Count; i++)
            {
                var entry = allKeybinds[i];

                if (entry.PrimaryKey != KeyCode.None)
                {
                    if (!keyUsage.TryGetValue(entry.PrimaryKey, out var list))
                    {
                        list = new List<(KeybindEntry, int)>();
                        keyUsage[entry.PrimaryKey] = list;
                    }
                    list.Add((entry, 0));
                }

                if (entry.SecondaryKey != KeyCode.None)
                {
                    if (!keyUsage.TryGetValue(entry.SecondaryKey, out var list))
                    {
                        list = new List<(KeybindEntry, int)>();
                        keyUsage[entry.SecondaryKey] = list;
                    }
                    list.Add((entry, 1));
                }
            }

            // Identify all shared keys
            foreach (var kvp in keyUsage)
            {
                if (kvp.Value.Count > 1)
                {
                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
                        var curEntry = kvp.Value[i].entry;
                        for (int j = 0; j < kvp.Value.Count; j++)
                        {
                            if (i == j) continue;
                            var otherEntry = kvp.Value[j].entry;
                            // Same entry can appear twice (primary == secondary); never report it as
                            // a conflict with itself.
                            if (ReferenceEquals(curEntry, otherEntry)) continue;
                            // Inventory-only binds (Blueprint) cannot clash with gameplay binds.
                            if (!ContextsOverlap(curEntry.Context, otherEntry.Context)) continue;
                            string name = IsSpanish() ? otherEntry.NameEs : otherEntry.NameEn;
                            string conflictDesc = $"{name} ({otherEntry.Category})";
                            if (!curEntry.Conflicts.Contains(conflictDesc))
                            {
                                curEntry.Conflicts.Add(conflictDesc);
                            }
                        }
                    }
                }
            }

            // Count conflicting *keys*, not entries: two keys shared by two actions used to report 4.
            int overlappingKeys = 0;
            foreach (var kvp in keyUsage)
            {
                bool hasDistinctEntries = false;
                for (int i = 0; i < kvp.Value.Count && !hasDistinctEntries; i++)
                {
                    for (int j = i + 1; j < kvp.Value.Count; j++)
                    {
                        // Same rule as the per-entry list above: binds that cannot be active together
                        // (e.g. an Inventory-only Blueprint key vs a gameplay key) are not a conflict, and must
                        // not be counted either, or the badge says "2" while the filter shows nothing.
                        if (!ReferenceEquals(kvp.Value[i].entry, kvp.Value[j].entry) &&
                            ContextsOverlap(kvp.Value[i].entry.Context, kvp.Value[j].entry.Context))
                        {
                            hasDistinctEntries = true;
                            break;
                        }
                    }
                }
                if (hasDistinctEntries) overlappingKeys++;
            }
            totalOverlapCount = overlappingKeys;
            totalOverlapEntries = allKeybinds.Count(k => k.Conflicts.Count > 0);
        }

        private static bool ContextsOverlap(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return true;
            if (a == "Any" || b == "Any") return true;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================
        //                      FILTERING & SEARCH BAR
        // =====================================================================
        private void ApplyFilterAndSearch()
        {
            filteredKeybinds.Clear();
            string q = (searchQuery ?? "").Trim().ToLowerInvariant();

            for (int i = 0; i < allKeybinds.Count; i++)
            {
                var entry = allKeybinds[i];

                // Category Filter
                if (activeFilter == KeybindFilter.Vanilla && !entry.IsVanilla) continue;
                if (activeFilter == KeybindFilter.Mods && entry.IsVanilla) continue;
                if (activeFilter == KeybindFilter.OverlapsOnly && entry.Conflicts.Count == 0) continue;

                // Search query filter
                if (!string.IsNullOrEmpty(q))
                {
                    string primaryStr = FormatKeyName(entry.PrimaryKey).ToLowerInvariant();
                    string secStr = FormatKeyName(entry.SecondaryKey).ToLowerInvariant();
                    bool matches = entry.NameEn.ToLowerInvariant().Contains(q) ||
                                   entry.NameEs.ToLowerInvariant().Contains(q) ||
                                   entry.Category.ToLowerInvariant().Contains(q) ||
                                   primaryStr.Contains(q) ||
                                   secStr.Contains(q);
                    if (!matches) continue;
                }

                filteredKeybinds.Add(entry);
            }
        }

        // =====================================================================
        //                      MODAL & INPUT ISOLATION
        // =====================================================================
        public void SetKeybindsManagerOpen(bool open, bool openedFromNative = false)
        {
            if (isKeybindsManagerOpen == open) return;

            if (open)
            {
                if (isModMenuOpen) SetModMenuOpen(false);
                if (isWayfinderOpen) SetWayfinderOpen(false);
                if (isDraggingHudElements) SetDraggingHudElements(false);

                openedFromNativeKeybinds = openedFromNative;
                isKeybindsManagerOpen = true;
                modalOpenedTimestamp = Time.unscaledTime;
                rebindingEntry = null;

                RecalculateConflicts();
                ApplyFilterAndSearch();
                AcquireModalIsolation();
            }
            else
            {
                rebindingEntry = null;
                isKeybindsManagerOpen = false;
                modalClosedTimestamp = Time.unscaledTime;
                ReleaseModalIsolation();

                if (openedFromNativeKeybinds)
                {
                    openedFromNativeKeybinds = false;
                    Inventory inv = GetInventory();
                    if (inv != null && inv.settingsCamera != null)
                    {
                        inv.settingsCamera.gameObject.SetActive(true);
                        if (inv.mainSettingsButton != null)
                            inv.mainSettingsButton.SetActive(true);
                    }
                }
            }
        }

        // =====================================================================
        //                   UPDATE LOOP & NATIVE HOOKS
        // =====================================================================
        private void UpdateKeybindManager()
        {
            // Re-apply the persisted rebinds once Unity's InputSystem is available.
            ApplyPersistedVanillaBindings();

            // 1. Detect Native Controls / Keybinds screen opening in Block Story
            if (!isKeybindsManagerOpen)
            {
                Inventory inv = GetInventory();
                if (inv != null)
                {
                    if (inv.keybindingWindow != null && inv.keybindingWindow.activeInHierarchy)
                    {
                        inv.keybindingWindow.SetActive(false); // Seamlessly takeover native screen
                        SetKeybindsManagerOpen(true, openedFromNative: true);
                        return;
                    }
                }

                // Main Menu options screen interception
                LoadKeybindings lk = FindObjectOfType<LoadKeybindings>();
                if (lk != null && lk.gameObject.activeInHierarchy && lk.container != null && lk.container.gameObject.activeInHierarchy)
                {
                    lk.gameObject.SetActive(false);
                    SetKeybindsManagerOpen(true, openedFromNative: true);
                    return;
                }
            }

            // 2. Hotkey to open Keybinds Manager directly [K]
            if (!isModMenuOpen && !isWayfinderOpen && !isDraggingHudElements)
            {
                if (KeybindsManagerKey != null && Input.GetKeyDown(KeybindsManagerKey.Value))
                {
                    SetKeybindsManagerOpen(!isKeybindsManagerOpen);
                    return;
                }
            }

            // 3. In-Game Mod Hotkey Handlers
            if (!IsModalOpen)
            {
                // HUD Drag Editor Hotkey [F9]
                if (HudDragKey != null && Input.GetKeyDown(HudDragKey.Value))
                {
                    SetDraggingHudElements(!isDraggingHudElements);
                    return;
                }

            }
        }

        // Map layer (L) and quick waypoint (B) belong to Aether Wayfinder now: it binds and handles them itself.

        // =====================================================================
        //                   INTERACTIVE REBINDING CAPTURE
        // =====================================================================
        private void ProcessInteractiveRebindingEvent()
        {
            if (rebindingEntry == null) return;

            Event ev = Event.current;
            if (ev == null) return;

            // Keyboard key capture
            if (ev.isKey && ev.type == EventType.KeyDown)
            {
                KeyCode pressed = ev.keyCode;
                if (pressed == KeyCode.Escape)
                {
                    // Cancel rebinding
                    rebindingEntry = null;
                    ev.Use();
                }
                else if (pressed == KeyCode.Backspace || pressed == KeyCode.Delete)
                {
                    // Unbind
                    ApplyRebind(rebindingEntry, rebindingSlot, KeyCode.None);
                    rebindingEntry = null;
                    ev.Use();
                }
                else if (pressed != KeyCode.None)
                {
                    ApplyRebind(rebindingEntry, rebindingSlot, pressed);
                    rebindingEntry = null;
                    ev.Use();
                }
            }
            // Mouse button capture
            else if (ev.isMouse && ev.type == EventType.MouseDown)
            {
                KeyCode mouseKey = KeyCode.None;
                if (ev.button == 0) mouseKey = KeyCode.Mouse0;
                else if (ev.button == 1) mouseKey = KeyCode.Mouse1;
                else if (ev.button == 2) mouseKey = KeyCode.Mouse2;

                if (mouseKey != KeyCode.None)
                {
                    ApplyRebind(rebindingEntry, rebindingSlot, mouseKey);
                    rebindingEntry = null;
                    ev.Use();
                }
            }
        }

        private void ApplyRebind(KeybindEntry entry, int slot, KeyCode newKey)
        {
            if (entry.IsVanilla)
            {
                if (slot == 0) entry.PrimaryKey = newKey;
                else entry.SecondaryKey = newKey;

                SaveVanillaBinding(entry, slot, newKey);
            }
            else
            {
                entry.PrimaryKey = newKey;
                if (entry.ModConfigEntry != null)
                {
                    entry.ModConfigEntry.Value = newKey;
                    Config.Save();
                }
            }

            RecalculateConflicts();
            ApplyFilterAndSearch();
        }

        private void ResetEntryToDefault(KeybindEntry entry)
        {
            if (entry.IsVanilla)
            {
                entry.PrimaryKey = entry.DefaultPrimaryKey;
                entry.SecondaryKey = entry.DefaultSecondaryKey;
                PlayerPrefs.DeleteKey($"Rebind_{entry.VanillaActionName}_0");
                PlayerPrefs.DeleteKey($"Rebind_{entry.VanillaActionName}_1");
                PlayerPrefs.DeleteKey($"Suite_Rebind_{entry.Id}_0");
                PlayerPrefs.DeleteKey($"Suite_Rebind_{entry.Id}_1");
                PlayerPrefs.Save();
                SaveVanillaBinding(entry, 0, entry.DefaultPrimaryKey);
                SaveVanillaBinding(entry, 1, entry.DefaultSecondaryKey);
            }
            else
            {
                entry.PrimaryKey = entry.DefaultPrimaryKey;
                if (entry.ModConfigEntry != null)
                {
                    entry.ModConfigEntry.Value = entry.DefaultPrimaryKey;
                    Config.Save();
                }
            }

            RecalculateConflicts();
            ApplyFilterAndSearch();
        }

        public void ResetAllKeybindsToDefault()
        {
            for (int i = 0; i < allKeybinds.Count; i++)
            {
                var entry = allKeybinds[i];
                if (entry.IsVanilla)
                {
                    entry.PrimaryKey = entry.DefaultPrimaryKey;
                    entry.SecondaryKey = entry.DefaultSecondaryKey;
                    PlayerPrefs.DeleteKey($"Rebind_{entry.VanillaActionName}_0");
                    PlayerPrefs.DeleteKey($"Rebind_{entry.VanillaActionName}_1");
                    PlayerPrefs.DeleteKey($"Suite_Rebind_{entry.Id}_0");
                    PlayerPrefs.DeleteKey($"Suite_Rebind_{entry.Id}_1");
                    SaveVanillaBinding(entry, 0, entry.DefaultPrimaryKey);
                    SaveVanillaBinding(entry, 1, entry.DefaultSecondaryKey);
                }
                else
                {
                    entry.PrimaryKey = entry.DefaultPrimaryKey;
                    if (entry.ModConfigEntry != null)
                    {
                        entry.ModConfigEntry.Value = entry.DefaultPrimaryKey;
                    }
                }
            }

            PlayerPrefs.Save();
            Config.Save();
            RecalculateConflicts();
            ApplyFilterAndSearch();
        }

        // =====================================================================
        //                      cInput COMPATIBILITY SHIM
        // =====================================================================
        public void SetKeybindFromCInput(string actionName, int slot, KeyCode key)
        {
            var entry = allKeybinds.FirstOrDefault(k => string.Equals(k.VanillaActionName, actionName, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                ApplyRebind(entry, slot, key);
            }
        }

        public KeyCode GetKeybindFromCInput(string actionName, int slot)
        {
            var entry = allKeybinds.FirstOrDefault(k => string.Equals(k.VanillaActionName, actionName, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                return slot == 0 ? entry.PrimaryKey : entry.SecondaryKey;
            }
            return KeyCode.None;
        }

        // =====================================================================
        //                    UNIFIED GUI MODAL RENDERING
        // =====================================================================
        private static GUIStyle _kbTitleStyle, _kbMetaStyle, _kbColHeaderStyle, _kbColHeaderCenterStyle;
        private static GUIStyle _kbRowTitleStyle, _kbBadgeStyle, _kbConflictStyle, _kbKeycapStyle;
        private static GUIStyle _kbFilterStyle, _kbSearchPlaceholderStyle, _kbStatusStyle, _kbTipStyle;
        private static GUIStyle _kbEmptyTitleStyle, _kbEmptySubStyle;
        private static readonly GUIContent _kbMeasure = new GUIContent();

        private void EnsureKeybindStyles()
        {
            if (_kbTitleStyle == null)
            {
                _kbTitleStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Display, 21, Aether.Nano.NanoBrand.Paper);
                _kbMetaStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 11, Aether.Nano.NanoBrand.Mute);
                _kbColHeaderStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Mute);
                _kbColHeaderCenterStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Mute, TextAnchor.MiddleCenter);
                _kbRowTitleStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.BodySemi, 13, Aether.Nano.NanoBrand.Paper);
                _kbBadgeStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Paper, TextAnchor.MiddleCenter);
                _kbConflictStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Warn);
                _kbKeycapStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 11, Aether.Nano.NanoBrand.Paper, TextAnchor.MiddleCenter);
                _kbFilterStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.BodySemi, 12, Aether.Nano.NanoBrand.Paper, TextAnchor.MiddleCenter);
                _kbSearchPlaceholderStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Body, 12, Aether.Nano.NanoBrand.Mute, TextAnchor.MiddleLeft);
                _kbStatusStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Paper, TextAnchor.MiddleCenter);
                _kbTipStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Mute, TextAnchor.MiddleLeft);
                _kbEmptyTitleStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Display, 18, Aether.Nano.NanoBrand.Paper, TextAnchor.MiddleCenter);
                _kbEmptySubStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Body, 12, Aether.Nano.NanoBrand.Mute, TextAnchor.MiddleCenter);
            }

            if (searchInputStyle == null)
            {
                searchInputStyle = new GUIStyle(GUI.skin.textField)
                {
                    font = Aether.Nano.NanoBrand.Body,
                    fontSize = 12,
                    alignment = TextAnchor.MiddleLeft,
                    border = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(10, 28, 4, 4)
                };
                searchInputStyle.normal.background = null;
                searchInputStyle.hover.background = null;
                searchInputStyle.focused.background = null;
                searchInputStyle.active.background = null;
                searchInputStyle.normal.textColor = Aether.Nano.NanoBrand.PaperLight;
                searchInputStyle.hover.textColor = Aether.Nano.NanoBrand.PaperLight;
                searchInputStyle.focused.textColor = Aether.Nano.NanoBrand.PaperLight;
                searchInputStyle.active.textColor = Aether.Nano.NanoBrand.PaperLight;
            }
        }

        private bool DrawKeybindFilterPill(Rect rect, string label, bool selected, bool alertTint = false)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                if (selected)
                {
                    Color top = alertTint ? new Color(0.55f, 0.22f, 0.14f, 0.96f) : new Color(0.24f, 0.17f, 0.12f, 0.96f);
                    Color bot = alertTint ? new Color(0.38f, 0.14f, 0.09f, 0.96f) : new Color(0.16f, 0.12f, 0.08f, 0.96f);
                    Color border = alertTint ? Aether.Nano.NanoBrand.Warn : Aether.Nano.NanoBrand.Ember;
                    Color text = alertTint ? new Color(1f, 0.90f, 0.72f, 1f) : Aether.Nano.NanoBrand.EmberSoft;
                    Aether.Nano.NanoSurface.FillRoundedGradient(rect, top, bot, 7f);
                    Aether.Nano.NanoSurface.StrokeRounded(rect, border, 1f, 7f);
                    Aether.Nano.NanoSurface.DrawCrispLabel(rect, label, _kbFilterStyle, text, 0.35f);
                }
                else
                {
                    if (hover)
                    {
                        Aether.Nano.NanoSurface.FillRounded(rect, new Color(0.14f, 0.11f, 0.085f, 0.85f), 7f);
                    }
                    else if (alertTint)
                    {
                        Aether.Nano.NanoSurface.FillRounded(rect, new Color(0.24f, 0.10f, 0.07f, 0.55f), 7f);
                    }
                    Color text = alertTint
                        ? (hover ? new Color(1f, 0.86f, 0.58f, 1f) : Aether.Nano.NanoBrand.Warn)
                        : (hover ? Aether.Nano.NanoBrand.PaperLight : Aether.Nano.NanoBrand.Mute);
                    Aether.Nano.NanoSurface.DrawCrispLabel(rect, label, _kbFilterStyle, text, 0.25f);
                }
            }

            return clicked;
        }

        private bool DrawKeycapButton(Rect rect, string label, bool isRebinding, bool isConflict, bool isModified, bool interactive)
        {
            bool hover = interactive && rect.Contains(Event.current.mousePosition);
            bool clicked = interactive && GUI.Button(rect, GUIContent.none, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                if (!interactive)
                {
                    Aether.Nano.NanoSurface.FillRounded(rect, new Color(0.055f, 0.045f, 0.036f, 0.55f), 7f);
                    Aether.Nano.NanoSurface.StrokeRounded(rect, new Color(Aether.Nano.NanoBrand.Line.r, Aether.Nano.NanoBrand.Line.g, Aether.Nano.NanoBrand.Line.b, 0.45f), 1f, 7f);
                    Aether.Nano.NanoSurface.DrawCrispLabel(rect, label, _kbKeycapStyle, new Color(Aether.Nano.NanoBrand.Mute.r, Aether.Nano.NanoBrand.Mute.g, Aether.Nano.NanoBrand.Mute.b, 0.55f), 0f);
                }
                else if (isRebinding)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
                    Color top = Color.Lerp(new Color(0.32f, 0.17f, 0.08f, 0.96f), new Color(0.46f, 0.24f, 0.10f, 0.98f), pulse);
                    Color bot = new Color(0.18f, 0.10f, 0.05f, 0.96f);
                    Color border = Color.Lerp(Aether.Nano.NanoBrand.Ember, Aether.Nano.NanoBrand.PaperLight, pulse * 0.45f);
                    Aether.Nano.NanoSurface.FillRoundedGradient(rect, top, bot, 7f);
                    Aether.Nano.NanoSurface.StrokeRounded(rect, border, 1.4f, 7f);
                    Aether.Nano.NanoSurface.DrawCrispLabel(rect, label, _kbKeycapStyle, Aether.Nano.NanoBrand.PaperLight, 0.35f);
                }
                else
                {
                    bool unbound = label == "-";
                    Color top = isConflict
                        ? (hover ? new Color(0.34f, 0.14f, 0.10f, 0.96f) : new Color(0.25f, 0.11f, 0.08f, 0.94f))
                        : (hover ? new Color(0.20f, 0.155f, 0.118f, 0.96f) : new Color(0.135f, 0.108f, 0.084f, 0.94f));
                    Color bot = isConflict
                        ? new Color(0.17f, 0.07f, 0.05f, 0.96f)
                        : (hover ? new Color(0.13f, 0.10f, 0.078f, 0.96f) : new Color(0.085f, 0.068f, 0.054f, 0.96f));
                    Color border = hover
                        ? Aether.Nano.NanoBrand.Ember
                        : (isConflict
                            ? new Color(Aether.Nano.NanoBrand.Warn.r, Aether.Nano.NanoBrand.Warn.g, Aether.Nano.NanoBrand.Warn.b, 0.80f)
                            : (isModified
                                ? new Color(Aether.Nano.NanoBrand.Ember.r, Aether.Nano.NanoBrand.Ember.g, Aether.Nano.NanoBrand.Ember.b, 0.55f)
                                : Aether.Nano.NanoBrand.Line));

                    Aether.Nano.NanoSurface.FillRoundedGradient(rect, top, bot, 7f);
                    // Tactile keycap bottom lip & subtle top bevel
                    Aether.Nano.NanoSurface.FillRounded(new Rect(rect.x + 5f, rect.y + 1f, rect.width - 10f, 1f),
                        new Color(1f, 0.96f, 0.90f, hover ? 0.16f : 0.08f), 0.5f);
                    Aether.Nano.NanoSurface.FillRounded(new Rect(rect.x + 4f, rect.yMax - 2.5f, rect.width - 8f, 1.5f),
                        new Color(0f, 0f, 0f, 0.35f), 0.75f);
                    Aether.Nano.NanoSurface.StrokeRounded(rect, border, 1f, 7f);

                    Color textCol = isConflict
                        ? new Color(1f, 0.86f, 0.54f, 1f)
                        : (unbound
                            ? Aether.Nano.NanoBrand.Mute
                            : (hover ? Aether.Nano.NanoBrand.PaperLight : (isModified ? Aether.Nano.NanoBrand.EmberSoft : Aether.Nano.NanoBrand.Paper)));
                    Aether.Nano.NanoSurface.DrawCrispLabel(rect, label, _kbKeycapStyle, textCol, 0.35f);
                }
            }

            return clicked;
        }

        private void DrawKeybindsManagerGUI(float anim = 1f)
        {
            EnsureKeybindStyles();
            if (isKeybindsManagerOpen)
                ProcessInteractiveRebindingEvent();

            // Warm Aether Ink backdrop dimmer
            Rect fullScreen = new Rect(0, 0, Screen.width, Screen.height);
            GUI.DrawTexture(fullScreen, GetColorTexture(new Color(0.02f, 0.015f, 0.012f, 0.74f * anim)));

            // Window Dimensions (matches F10 ModMenu shell geometry)
            float winWidth = Mathf.Min(1020f, Screen.width - 40f);
            float winHeight = Mathf.Min(650f, Screen.height - 40f);
            float winX = (Screen.width - winWidth) * 0.5f;
            float winY = (Screen.height - winHeight) * 0.5f;
            Rect winRect = new Rect(winX, winY, winWidth, winHeight);

            using (Aether.Nano.NanoMotion.BeginModal(winRect, anim))
            {
                DrawModMenuShell(winRect);

                // -----------------------------------------------------------------
                //                       INTEGRATED TOP BAR
                // -----------------------------------------------------------------
                float topBarH = 54f;
                float hx = winX + 18f;
                const float cubeH = 22f;
                if (Aether.Nano.NanoBrand.Cube != null)
                    hx += Aether.Nano.NanoBrand.DrawCube(hx, winY + (topBarH - cubeH) * 0.5f, cubeH) + 10f;

                string titleWord = Tr("Keys & controls", "Teclas y controles");
                _kbMeasure.text = titleWord;
                float wordW = _kbTitleStyle.CalcSize(_kbMeasure).x;
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(hx, winY + 9f, wordW + 4f, 36f),
                    titleWord, _kbTitleStyle, Aether.Nano.NanoBrand.Paper, 0.4f);
                hx += wordW + 12f;

                Aether.Nano.NanoSurface.FillRounded(new Rect(hx, winY + 19f, 1f, 16f), Aether.Nano.NanoBrand.Line, 0.5f);
                hx += 12f;

                string subtitle = Tr($"{allKeybinds.Count} bindings  ·  game & mods", $"{allKeybinds.Count} atajos  ·  juego y mods");
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(hx, winY + 10f, Mathf.Max(80f, winX + winWidth - 230f - hx), 36f),
                    subtitle, _kbMetaStyle, Aether.Nano.NanoBrand.Mute, 0.3f);

                // Reset All Button
                Rect resetAllRect = new Rect(winX + winWidth - 204f, winY + 12f, 150f, 30f);
                if (Aether.Nano.NanoSurface.DrawActionButton(resetAllRect, Tr("↺ Reset all", "↺ Restablecer todo"), false))
                {
                    ResetAllKeybindsToDefault();
                }

                // Close Button
                Rect closeBtnRect = new Rect(winX + winWidth - 46f, winY + 12f, 32f, 30f);
                if (Aether.Nano.NanoSurface.DrawActionButton(closeBtnRect, "×", false))
                {
                    SetKeybindsManagerOpen(false);
                    return;
                }

                // Clean 1px hairline under header
                Aether.Nano.NanoSurface.FillRounded(new Rect(winX + 14f, winY + topBarH, winWidth - 28f, 1f), Aether.Nano.NanoBrand.Line, 0.5f);

                // -----------------------------------------------------------------
                //                  TOOLBAR: SEGMENTED FILTERS & SEARCH
                // -----------------------------------------------------------------
                float tbY = winY + topBarH + 10f;
                float tbH = 34f;
                float leftPad = winX + 16f;

                int allCount = allKeybinds.Count;
                int vanillaCount = allKeybinds.Count(k => k.IsVanilla);
                int modsCount = allKeybinds.Count(k => !k.IsVanilla);

                float segTrackW = Mathf.Min(504f, winWidth * 0.54f);
                Rect segTrack = new Rect(leftPad, tbY, segTrackW, tbH);
                Aether.Nano.NanoSurface.DrawDebossedSlot(segTrack, new Color(0.042f, 0.034f, 0.027f, 0.94f), Aether.Nano.NanoBrand.Line, 9f);

                float padIn = 3f;
                float segH = tbH - padIn * 2f;
                float innerTrackW = segTrackW - padIn * 2f;
                float wAll = innerTrackW * 0.21f;
                float wVan = innerTrackW * 0.25f;
                float wMod = innerTrackW * 0.22f;
                float wOvl = innerTrackW - wAll - wVan - wMod - 9f;

                float sx = segTrack.x + padIn;
                if (DrawKeybindFilterPill(new Rect(sx, segTrack.y + padIn, wAll, segH), Tr($"All ({allCount})", $"Todo ({allCount})"), activeFilter == KeybindFilter.All))
                {
                    activeFilter = KeybindFilter.All;
                    ApplyFilterAndSearch();
                }
                sx += wAll + 3f;

                if (DrawKeybindFilterPill(new Rect(sx, segTrack.y + padIn, wVan, segH), $"Vanilla ({vanillaCount})", activeFilter == KeybindFilter.Vanilla))
                {
                    activeFilter = KeybindFilter.Vanilla;
                    ApplyFilterAndSearch();
                }
                sx += wVan + 3f;

                if (DrawKeybindFilterPill(new Rect(sx, segTrack.y + padIn, wMod, segH), $"Mods ({modsCount})", activeFilter == KeybindFilter.Mods))
                {
                    activeFilter = KeybindFilter.Mods;
                    ApplyFilterAndSearch();
                }
                sx += wMod + 3f;

                string ovlTxt = totalOverlapEntries > 0
                    ? Tr($"Conflicts ({totalOverlapEntries})", $"Conflictos ({totalOverlapEntries})")
                    : Tr("Conflicts (0)", "Conflictos (0)");
                if (DrawKeybindFilterPill(new Rect(sx, segTrack.y + padIn, wOvl, segH), ovlTxt, activeFilter == KeybindFilter.OverlapsOnly, totalOverlapCount > 0))
                {
                    activeFilter = KeybindFilter.OverlapsOnly;
                    ApplyFilterAndSearch();
                }

                // Recessed search field
                float searchX = segTrack.xMax + 12f;
                float searchW = Mathf.Max(140f, (winX + winWidth - 16f) - searchX);
                Rect searchRect = new Rect(searchX, tbY, searchW, tbH);
                bool searchFocused = GUI.GetNameOfFocusedControl() == "AetherKeybindsSearch";
                Aether.Nano.NanoSurface.DrawDebossedSlot(searchRect,
                    new Color(0.042f, 0.034f, 0.027f, 0.96f),
                    searchFocused ? Aether.Nano.NanoBrand.Ember : Aether.Nano.NanoBrand.Line,
                    9f);

                GUI.SetNextControlName("AetherKeybindsSearch");
                string newSearch = GUI.TextField(new Rect(searchRect.x + 2f, searchRect.y + 1f, searchRect.width - 28f, searchRect.height - 2f), searchQuery ?? "", searchInputStyle);
                if (newSearch != searchQuery)
                {
                    searchQuery = newSearch;
                    ApplyFilterAndSearch();
                }

                if (string.IsNullOrEmpty(searchQuery))
                {
                    Aether.Nano.NanoSurface.DrawCrispLabel(
                        new Rect(searchRect.x + 12f, searchRect.y, searchRect.width - 24f, searchRect.height),
                        Tr("Search action, mod or key…", "Buscar acción, mod o tecla…"),
                        _kbSearchPlaceholderStyle, Aether.Nano.NanoBrand.Mute, 0f);
                }
                else
                {
                    Rect clearRect = new Rect(searchRect.xMax - 26f, searchRect.y + 5f, 22f, 24f);
                    bool clearHover = clearRect.Contains(Event.current.mousePosition);
                    if (Event.current.type == EventType.Repaint)
                    {
                        if (clearHover)
                            Aether.Nano.NanoSurface.FillRounded(clearRect, new Color(0.22f, 0.14f, 0.10f, 0.9f), 6f);
                        Aether.Nano.NanoSurface.DrawCrispLabel(clearRect, "×", _kbFilterStyle,
                            clearHover ? Aether.Nano.NanoBrand.PaperLight : Aether.Nano.NanoBrand.Mute, 0f);
                    }
                    if (GUI.Button(clearRect, GUIContent.none, GUIStyle.none))
                    {
                        searchQuery = "";
                        ApplyFilterAndSearch();
                    }
                }

                // -----------------------------------------------------------------
                //                     COLUMN HEADERS & LIST SHELL
                // -----------------------------------------------------------------
                float colHeaderY = tbY + tbH + 8f;
                float listOuterX = winX + 16f;
                float listOuterW = winWidth - 32f;
                float listY = colHeaderY + 18f;
                float footerH = 44f;
                float listH = Mathf.Max(140f, (winY + winHeight - footerH - 8f) - listY);
                Rect listOuterRect = new Rect(listOuterX, listY, listOuterW, listH);

                // Subtle aligned column labels
                const float keyBtnW = 122f;
                const float resetBtnW = 32f;
                const float colGap = 8f;
                float rightColsTotal = keyBtnW * 2f + resetBtnW + colGap * 2f + 12f;
                float hdrRightStart = listOuterRect.xMax - rightColsTotal - 16f;
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(listOuterX + 16f, colHeaderY, 180f, 16f),
                    Tr("MOD / ACTION", "MOD / ACCIÓN"), _kbColHeaderStyle, Aether.Nano.NanoBrand.Mute, 0.2f);
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(hdrRightStart, colHeaderY, keyBtnW, 16f),
                    Tr("PRIMARY", "PRIMARIA"), _kbColHeaderCenterStyle, Aether.Nano.NanoBrand.Mute, 0.2f);
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(hdrRightStart + keyBtnW + colGap, colHeaderY, keyBtnW, 16f),
                    Tr("SECONDARY", "SECUNDARIA"), _kbColHeaderCenterStyle, Aether.Nano.NanoBrand.Mute, 0.2f);
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(hdrRightStart + (keyBtnW + colGap) * 2f, colHeaderY, resetBtnW, 16f),
                    "↺", _kbColHeaderCenterStyle, Aether.Nano.NanoBrand.Mute, 0.2f);

                // Recessed list frame
                Aether.Nano.NanoSurface.DrawDebossedSlot(listOuterRect, new Color(0.038f, 0.030f, 0.024f, 0.90f), Aether.Nano.NanoBrand.Line, 10f);

                Rect scrollViewRect = new Rect(listOuterRect.x + 6f, listOuterRect.y + 6f, listOuterRect.width - 12f, listOuterRect.height - 12f);

                if (filteredKeybinds.Count == 0)
                {
                    float cy = scrollViewRect.y + scrollViewRect.height * 0.42f;
                    bool cleanConflicts = activeFilter == KeybindFilter.OverlapsOnly && string.IsNullOrEmpty(searchQuery);
                    Aether.Nano.NanoSurface.DrawCrispLabel(
                        new Rect(scrollViewRect.x + 20f, cy - 16f, scrollViewRect.width - 40f, 28f),
                        cleanConflicts ? Tr("All bindings are clear", "Sin conflictos activos") : Tr("No matching bindings", "Sin atajos coincidentes"),
                        _kbEmptyTitleStyle, cleanConflicts ? Aether.Nano.NanoBrand.Ok : Aether.Nano.NanoBrand.Paper, 0.35f);
                    Aether.Nano.NanoSurface.DrawCrispLabel(
                        new Rect(scrollViewRect.x + 20f, cy + 14f, scrollViewRect.width - 40f, 22f),
                        cleanConflicts
                            ? Tr("Every active key in your game and mods is conflict-free.", "Ninguna tecla de tus mods o del juego se solapa.")
                            : Tr("Try another keyword or clear the active filter.", "Prueba con otra búsqueda o cambia el filtro."),
                        _kbEmptySubStyle, Aether.Nano.NanoBrand.Mute, 0.25f);
                }
                else
                {
                    const float rowStep = 56f;
                    float totalContentH = filteredKeybinds.Count * rowStep + 4f;
                    scrollKeybindsPos = Aether.Nano.NanoSurface.BeginScrollView(scrollViewRect, scrollKeybindsPos, totalContentH, out float innerW);
                    try
                    {
                        float rowY = 2f;
                        for (int i = 0; i < filteredKeybinds.Count; i++)
                        {
                            var entry = filteredKeybinds[i];
                            Rect rowRect = new Rect(2f, rowY, innerW - 4f, rowStep - 6f);
                            bool hasConflicts = entry.Conflicts.Count > 0;
                            bool rowHover = rowRect.Contains(Event.current.mousePosition);
                            bool isEntryRebinding = rebindingEntry == entry;

                            if (Event.current.type == EventType.Repaint)
                            {
                                Color rowBg = isEntryRebinding
                                    ? new Color(0.18f, 0.115f, 0.068f, 0.94f)
                                    : (hasConflicts
                                        ? (rowHover ? new Color(0.24f, 0.105f, 0.075f, 0.94f) : new Color(0.19f, 0.085f, 0.062f, 0.90f))
                                        : (rowHover
                                            ? new Color(0.125f, 0.100f, 0.078f, 0.94f)
                                            : (i % 2 == 0 ? new Color(0.082f, 0.066f, 0.052f, 0.88f) : new Color(0.098f, 0.078f, 0.062f, 0.88f))));
                                Color rowBorder = isEntryRebinding
                                    ? Aether.Nano.NanoBrand.Ember
                                    : (hasConflicts
                                        ? new Color(Aether.Nano.NanoBrand.Warn.r, Aether.Nano.NanoBrand.Warn.g, Aether.Nano.NanoBrand.Warn.b, 0.55f)
                                        : (rowHover
                                            ? new Color(Aether.Nano.NanoBrand.Ember.r, Aether.Nano.NanoBrand.Ember.g, Aether.Nano.NanoBrand.Ember.b, 0.40f)
                                            : new Color(Aether.Nano.NanoBrand.Line.r, Aether.Nano.NanoBrand.Line.g, Aether.Nano.NanoBrand.Line.b, 0.65f)));

                                Aether.Nano.NanoSurface.FillRounded(rowRect, rowBg, 8f);
                                Aether.Nano.NanoSurface.StrokeRounded(rowRect, rowBorder, 1f, 8f);
                            }

                            // Category chip (squircle, zero decorative side stripe)
                            float badgeW = 94f;
                            float badgeY = hasConflicts ? rowRect.y + 6f : rowRect.y + (rowRect.height - 20f) * 0.5f;
                            Rect badgeRect = new Rect(rowRect.x + 10f, badgeY, badgeW, 20f);
                            Color catCol = entry.CategoryColor;
                            if (Event.current.type == EventType.Repaint)
                            {
                                Aether.Nano.NanoSurface.DrawChip(badgeRect, entry.Category, _kbBadgeStyle,
                                    new Color(catCol.r * 0.20f, catCol.g * 0.20f, catCol.b * 0.20f, 0.92f),
                                    new Color(catCol.r, catCol.g, catCol.b, 0.50f),
                                    catCol, 5f);
                            }

                            // Action name & inline conflict note
                            string actionName = IsSpanish() ? entry.NameEs : entry.NameEn;
                            float nameX = badgeRect.xMax + 12f;
                            float rightX = rowRect.xMax - rightColsTotal;
                            float nameW = Mathf.Max(120f, rightX - nameX - 12f);
                            float nameY = hasConflicts ? rowRect.y + 5f : rowRect.y + (rowRect.height - 22f) * 0.5f;
                            if (Event.current.type == EventType.Repaint)
                            {
                                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(nameX, nameY, nameW, 22f),
                                    actionName, _kbRowTitleStyle, Aether.Nano.NanoBrand.Paper, 0.35f);

                                if (hasConflicts)
                                {
                                    string conflictStr = Tr("Overlaps with: ", "Solapa con: ") + string.Join(", ", entry.Conflicts);
                                    Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(rowRect.x + 12f, rowRect.y + 28f, rightX - rowRect.x - 22f, 16f),
                                        conflictStr, _kbConflictStyle, Aether.Nano.NanoBrand.Warn, 0.25f);
                                }
                            }

                            // Primary Keycap
                            const float keyBtnH = 32f;
                            float keyBtnY = rowRect.y + (rowRect.height - keyBtnH) * 0.5f;
                            bool isRebindingPrimary = rebindingEntry == entry && rebindingSlot == 0;
                            bool primaryModified = entry.PrimaryKey != entry.DefaultPrimaryKey;
                            string pKeyLabel = isRebindingPrimary
                                ? Tr("Press key…", "Pulsa tecla…")
                                : FormatKeyName(entry.PrimaryKey);

                            if (DrawKeycapButton(new Rect(rightX, keyBtnY, keyBtnW, keyBtnH), pKeyLabel,
                                    isRebindingPrimary, hasConflicts && entry.PrimaryKey != KeyCode.None, primaryModified, true))
                            {
                                if (isRebindingPrimary)
                                    rebindingEntry = null;
                                else
                                {
                                    rebindingEntry = entry;
                                    rebindingSlot = 0;
                                }
                            }

                            // Secondary Keycap (Vanilla only)
                            float secX = rightX + keyBtnW + colGap;
                            bool isRebindingSecondary = rebindingEntry == entry && rebindingSlot == 1;
                            bool secondaryModified = entry.IsVanilla && entry.SecondaryKey != entry.DefaultSecondaryKey;
                            string sKeyLabel = entry.IsVanilla
                                ? (isRebindingSecondary ? Tr("Press…", "Pulsa…") : FormatKeyName(entry.SecondaryKey))
                                : "—";

                            if (DrawKeycapButton(new Rect(secX, keyBtnY, keyBtnW, keyBtnH), sKeyLabel,
                                    isRebindingSecondary, entry.IsVanilla && hasConflicts && entry.SecondaryKey != KeyCode.None,
                                    secondaryModified, entry.IsVanilla))
                            {
                                if (isRebindingSecondary)
                                    rebindingEntry = null;
                                else
                                {
                                    rebindingEntry = entry;
                                    rebindingSlot = 1;
                                }
                            }

                            // Reset Entry Button [↺]
                            float rstX = secX + keyBtnW + colGap;
                            Rect resetBtnRect = new Rect(rstX, keyBtnY, resetBtnW, keyBtnH);
                            bool anyModified = primaryModified || secondaryModified;
                            bool rstHover = resetBtnRect.Contains(Event.current.mousePosition);
                            if (Event.current.type == EventType.Repaint)
                            {
                                Color bg = rstHover
                                    ? new Color(0.20f, 0.15f, 0.11f, 0.96f)
                                    : (anyModified ? new Color(0.14f, 0.11f, 0.08f, 0.90f) : new Color(0.07f, 0.056f, 0.045f, 0.60f));
                                Color bd = rstHover
                                    ? Aether.Nano.NanoBrand.Ember
                                    : (anyModified ? new Color(Aether.Nano.NanoBrand.Ember.r, Aether.Nano.NanoBrand.Ember.g, Aether.Nano.NanoBrand.Ember.b, 0.45f) : Aether.Nano.NanoBrand.Line);
                                Color fg = rstHover
                                    ? Aether.Nano.NanoBrand.PaperLight
                                    : (anyModified ? Aether.Nano.NanoBrand.EmberSoft : new Color(Aether.Nano.NanoBrand.Mute.r, Aether.Nano.NanoBrand.Mute.g, Aether.Nano.NanoBrand.Mute.b, 0.55f));
                                Aether.Nano.NanoSurface.FillRounded(resetBtnRect, bg, 7f);
                                Aether.Nano.NanoSurface.StrokeRounded(resetBtnRect, bd, 1f, 7f);
                                Aether.Nano.NanoSurface.DrawCrispLabel(resetBtnRect, "↺", _kbKeycapStyle, fg, 0.25f);
                            }
                            if (GUI.Button(resetBtnRect, GUIContent.none, GUIStyle.none))
                            {
                                ResetEntryToDefault(entry);
                            }

                            rowY += rowStep;
                        }
                    }
                    finally
                    {
                        Aether.Nano.NanoSurface.EndScrollView();
                    }
                }

                // -----------------------------------------------------------------
                //                       FOOTER / STATUS BAR
                // -----------------------------------------------------------------
                float footerSepY = winY + winHeight - footerH;
                Aether.Nano.NanoSurface.FillRounded(new Rect(winX + 14f, footerSepY, winWidth - 28f, 1f), Aether.Nano.NanoBrand.Line, 0.5f);

                float footerY = footerSepY + 9f;
                string tipText = Tr(
                    "Click any keycap to rebind  ·  [Esc] Cancel  ·  [Backspace/Del] Unbind",
                    "Clic en una tecla para reasignar  ·  [Esc] Cancelar  ·  [Supr] Desasignar"
                );
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(winX + 18f, footerY + 2f, winWidth - 290f, 22f),
                    tipText, _kbTipStyle, Aether.Nano.NanoBrand.Mute, 0.25f);

                // Conflict Status Summary Chip (clicking jumps to Conflicts filter when > 0)
                Rect statusBadgeRect = new Rect(winX + winWidth - 266f, footerY, 250f, 26f);
                if (totalOverlapCount == 0)
                {
                    Aether.Nano.NanoSurface.DrawChip(statusBadgeRect,
                        Tr("0 conflicts  ·  all clear", "0 conflictos  ·  todo listo"),
                        _kbStatusStyle,
                        new Color(Aether.Nano.NanoBrand.Ok.r * 0.18f, Aether.Nano.NanoBrand.Ok.g * 0.18f, Aether.Nano.NanoBrand.Ok.b * 0.18f, 0.90f),
                        new Color(Aether.Nano.NanoBrand.Ok.r, Aether.Nano.NanoBrand.Ok.g, Aether.Nano.NanoBrand.Ok.b, 0.55f),
                        Aether.Nano.NanoBrand.Ok, 7f);
                }
                else
                {
                    bool statusHover = statusBadgeRect.Contains(Event.current.mousePosition);
                    Aether.Nano.NanoSurface.DrawChip(statusBadgeRect,
                        Tr($"{totalOverlapEntries} actions share {totalOverlapCount} key(s)", $"{totalOverlapEntries} acciones comparten {totalOverlapCount} tecla(s)"),
                        _kbStatusStyle,
                        statusHover ? new Color(0.42f, 0.16f, 0.09f, 0.96f) : new Color(0.32f, 0.12f, 0.07f, 0.92f),
                        statusHover ? Aether.Nano.NanoBrand.Ember : Aether.Nano.NanoBrand.Warn,
                        new Color(1f, 0.88f, 0.58f, 1f), 7f);
                    if (GUI.Button(statusBadgeRect, GUIContent.none, GUIStyle.none))
                    {
                        activeFilter = KeybindFilter.OverlapsOnly;
                        ApplyFilterAndSearch();
                    }
                }
            }

            // Click outside closes modal (unless in rebinding mode)
            if (isKeybindsManagerOpen && rebindingEntry == null && Event.current.type == EventType.MouseDown && (Time.unscaledTime - modalOpenedTimestamp > 0.35f) && !winRect.Contains(Event.current.mousePosition))
            {
                SetKeybindsManagerOpen(false);
                Event.current.Use();
            }
        }

        // =====================================================================
        //                      STRING & KEYCODE FORMATTERS
        // =====================================================================
        public string FormatKeyName(KeyCode code)
        {
            if (code == KeyCode.None) return "-";

            switch (code)
            {
                case KeyCode.Mouse0: return Tr("Left Click", "Clic Izq.");
                case KeyCode.Mouse1: return Tr("Right Click", "Clic Der.");
                case KeyCode.Mouse2: return Tr("Middle Click", "Clic Central");
                case KeyCode.Space: return "Space";
                case KeyCode.Return: return "Enter";
                case KeyCode.KeypadEnter: return "Num Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.LeftShift: return "LShift";
                case KeyCode.RightShift: return "RShift";
                case KeyCode.LeftControl: return "LCtrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt: return "LAlt";
                case KeyCode.RightAlt: return "RAlt";
                case KeyCode.Tab: return "Tab";
                case KeyCode.Backspace: return "Backspace";
                case KeyCode.Delete: return "Delete";
                case KeyCode.UpArrow: return "↑ Up";
                case KeyCode.DownArrow: return "↓ Down";
                case KeyCode.LeftArrow: return "← Left";
                case KeyCode.RightArrow: return "→ Right";
                case KeyCode.BackQuote: return "~";
                case KeyCode.Alpha0: return "0";
                case KeyCode.Alpha1: return "1";
                case KeyCode.Alpha2: return "2";
                case KeyCode.Alpha3: return "3";
                case KeyCode.Alpha4: return "4";
                case KeyCode.Alpha5: return "5";
                case KeyCode.Alpha6: return "6";
                case KeyCode.Alpha7: return "7";
                case KeyCode.Alpha8: return "8";
                case KeyCode.Alpha9: return "9";
                case KeyCode.Keypad0: return "Num 0";
                case KeyCode.Keypad1: return "Num 1";
                case KeyCode.Keypad2: return "Num 2";
                case KeyCode.Keypad3: return "Num 3";
                case KeyCode.Keypad4: return "Num 4";
                case KeyCode.Keypad5: return "Num 5";
                case KeyCode.Keypad6: return "Num 6";
                case KeyCode.Keypad7: return "Num 7";
                case KeyCode.Keypad8: return "Num 8";
                case KeyCode.Keypad9: return "Num 9";
                default: return code.ToString();
            }
        }

        public static string KeyCodeToInputPath(KeyCode key)
        {
            if (key == KeyCode.None) return "";
            if (key == KeyCode.Mouse0) return "<Mouse>/leftButton";
            if (key == KeyCode.Mouse1) return "<Mouse>/rightButton";
            if (key == KeyCode.Mouse2) return "<Mouse>/middleButton";
            if (key == KeyCode.Space) return "<Keyboard>/space";
            if (key == KeyCode.Return || key == KeyCode.KeypadEnter) return "<Keyboard>/enter";
            if (key == KeyCode.Escape) return "<Keyboard>/escape";
            if (key == KeyCode.LeftShift) return "<Keyboard>/leftShift";
            if (key == KeyCode.RightShift) return "<Keyboard>/rightShift";
            if (key == KeyCode.LeftControl) return "<Keyboard>/leftCtrl";
            if (key == KeyCode.RightControl) return "<Keyboard>/rightCtrl";
            if (key == KeyCode.LeftAlt) return "<Keyboard>/leftAlt";
            if (key == KeyCode.RightAlt) return "<Keyboard>/rightAlt";
            if (key == KeyCode.Tab) return "<Keyboard>/tab";
            if (key == KeyCode.Backspace) return "<Keyboard>/backspace";
            if (key == KeyCode.Delete) return "<Keyboard>/delete";
            if (key == KeyCode.UpArrow) return "<Keyboard>/upArrow";
            if (key == KeyCode.DownArrow) return "<Keyboard>/downArrow";
            if (key == KeyCode.LeftArrow) return "<Keyboard>/leftArrow";
            if (key == KeyCode.RightArrow) return "<Keyboard>/rightArrow";
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return $"<Keyboard>/{key - KeyCode.Alpha0}";
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return $"<Keyboard>/numpad{key - KeyCode.Keypad0}";
            if (key >= KeyCode.F1 && key <= KeyCode.F12) return $"<Keyboard>/f{(int)(key - KeyCode.F1 + 1)}";
            return $"<Keyboard>/{key.ToString().ToLowerInvariant()}";
        }

        public static KeyCode InputPathToKeyCode(string path)
        {
            if (string.IsNullOrEmpty(path)) return KeyCode.None;
            path = path.Trim().ToLowerInvariant();

            if (path.Contains("leftbutton")) return KeyCode.Mouse0;
            if (path.Contains("rightbutton")) return KeyCode.Mouse1;
            if (path.Contains("middlebutton")) return KeyCode.Mouse2;
            if (path.Contains("space")) return KeyCode.Space;
            if (path.Contains("enter")) return KeyCode.Return;
            if (path.Contains("escape")) return KeyCode.Escape;
            if (path.Contains("leftshift")) return KeyCode.LeftShift;
            if (path.Contains("rightshift")) return KeyCode.RightShift;
            if (path.Contains("leftctrl")) return KeyCode.LeftControl;
            if (path.Contains("rightctrl")) return KeyCode.RightControl;
            if (path.Contains("leftalt")) return KeyCode.LeftAlt;
            if (path.Contains("rightalt")) return KeyCode.RightAlt;
            if (path.Contains("tab")) return KeyCode.Tab;
            if (path.Contains("backspace")) return KeyCode.Backspace;
            if (path.Contains("delete")) return KeyCode.Delete;
            if (path.Contains("uparrow")) return KeyCode.UpArrow;
            if (path.Contains("downarrow")) return KeyCode.DownArrow;
            if (path.Contains("leftarrow")) return KeyCode.LeftArrow;
            if (path.Contains("rightarrow")) return KeyCode.RightArrow;

            for (int i = 0; i <= 9; i++)
            {
                if (path.EndsWith("/" + i)) return KeyCode.Alpha0 + i;
                if (path.EndsWith("/numpad" + i)) return KeyCode.Keypad0 + i;
            }

            for (char c = 'a'; c <= 'z'; c++)
            {
                if (path.EndsWith("/" + c))
                {
                    if (Enum.TryParse<KeyCode>(c.ToString().ToUpperInvariant(), out KeyCode kc))
                        return kc;
                }
            }

            for (int i = 1; i <= 12; i++)
            {
                if (path.EndsWith("/f" + i))
                {
                    if (Enum.TryParse<KeyCode>("F" + i, out KeyCode kc))
                        return kc;
                }
            }

            return KeyCode.None;
        }
    }

    // =========================================================================
    //                 NATIVE & LEGACY cInput CALL COMPATIBILITY
    // =========================================================================
    public static class cInput
    {
        public static void ChangeKey(string actionName, KeyCode key)
        {
            ChangeKey(actionName, 1, key);
        }

        public static void ChangeKey(string actionName, int slot, KeyCode key)
        {
            BlockStorySuitePlugin.Instance?.SetKeybindFromCInput(actionName, slot - 1, key);
        }

        public static KeyCode GetKeyCode(string actionName, int slot = 1)
        {
            return BlockStorySuitePlugin.Instance?.GetKeybindFromCInput(actionName, slot - 1) ?? KeyCode.None;
        }

        public static string GetText(string actionName, int slot = 1)
        {
            KeyCode code = GetKeyCode(actionName, slot);
            return BlockStorySuitePlugin.Instance?.FormatKeyName(code) ?? "-";
        }

        public static int length => BlockStorySuitePlugin.Instance?.VanillaKeybindCount ?? 0;
    }
}
