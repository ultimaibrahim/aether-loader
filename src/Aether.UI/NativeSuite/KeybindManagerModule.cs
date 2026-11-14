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
                        case "Vanilla": return new Color(0.42f, 0.55f, 0.70f, 1.0f);
                        case "Wayfinder": return new Color(0.25f, 0.80f, 0.40f, 1.0f);
                        case "Blueprint": return new Color(0.95f, 0.45f, 0.20f, 1.0f);
                        case "HUD Layout": return new Color(0.30f, 0.75f, 0.95f, 1.0f);
                        case "Lens WAILA": return new Color(0.60f, 0.88f, 0.25f, 1.0f);
                        case "Sodium": return new Color(0.20f, 0.85f, 0.80f, 1.0f);
                        case "Hotkeys": return new Color(0.95f, 0.80f, 0.25f, 1.0f);
                        default: return new Color(0.70f, 0.70f, 0.70f, 1.0f);
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

        public void DrawKeybindManagerGUI()
        {
            SyncBepInExKeybinds();
            DrawKeybindsManagerGUI();
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
        private void DrawKeybindsManagerGUI()
        {
            ProcessInteractiveRebindingEvent();

            // Background Dimmer
            Rect fullScreen = new Rect(0, 0, Screen.width, Screen.height);
            GUI.DrawTexture(fullScreen, GetColorTexture(new Color(0f, 0f, 0f, 0.75f)));

            // Window Dimensions
            float winWidth = Mathf.Min(1020f, Screen.width - 24f);
            float winHeight = Mathf.Min(650f, Screen.height - 24f);
            float winX = (Screen.width - winWidth) * 0.5f;
            float winY = (Screen.height - winHeight) * 0.5f;
            Rect winRect = new Rect(winX, winY, winWidth, winHeight);

            // Native Wood Styled Panel
            DrawWoodPanel(winRect, 1.0f);

            // Header Title
            DrawAetherHeader(new Rect(winX + 22, winY + 10, winWidth - 250, 36), Tr("Keys & controls", "Teclas y controles"),
                Tr("game and mods", "juego y mods"));

            // Reset All Button
            Rect resetAllRect = new Rect(winX + winWidth - 215, winY + 12, 155, 32);
            if (DrawNativeWoodButton(resetAllRect, Tr("↺ Reset All Defaults", "↺ Restablecer Todo"), false, new Color(1.0f, 0.80f, 0.40f, 1.0f)))
            {
                ResetAllKeybindsToDefault();
            }

            // Close Button
            Rect closeBtnRect = new Rect(winX + winWidth - 52, winY + 12, 36, 32);
            if (DrawNativeWoodButton(closeBtnRect, "X", false, new Color(1.0f, 0.55f, 0.55f, 1.0f)))
            {
                SetKeybindsManagerOpen(false);
                return;
            }

            // -----------------------------------------------------------------
            //                     TOOLBAR: FILTERS & SEARCH BAR
            // -----------------------------------------------------------------
            float tbY = winY + 52f;
            float btnH = 32f;
            float curX = winX + 18f;

            int allCount = allKeybinds.Count;
            int vanillaCount = allKeybinds.Count(k => k.IsVanilla);
            int modsCount = allKeybinds.Count(k => !k.IsVanilla);

            // Filter: All
            string allTxt = Tr($"All ({allCount})", $"Todo ({allCount})");
            float allW = 100f;
            if (DrawNativeWoodButton(new Rect(curX, tbY, allW, btnH), allTxt, activeFilter == KeybindFilter.All))
            {
                activeFilter = KeybindFilter.All;
                ApplyFilterAndSearch();
            }
            curX += allW + 6f;

            // Filter: Vanilla
            string vanTxt = Tr($"Vanilla ({vanillaCount})", $"Vanilla ({vanillaCount})");
            float vanW = 120f;
            if (DrawNativeWoodButton(new Rect(curX, tbY, vanW, btnH), vanTxt, activeFilter == KeybindFilter.Vanilla))
            {
                activeFilter = KeybindFilter.Vanilla;
                ApplyFilterAndSearch();
            }
            curX += vanW + 6f;

            // Filter: Mods
            string modTxt = Tr($"Mods ({modsCount})", $"Mods ({modsCount})");
            float modW = 110f;
            if (DrawNativeWoodButton(new Rect(curX, tbY, modW, btnH), modTxt, activeFilter == KeybindFilter.Mods))
            {
                activeFilter = KeybindFilter.Mods;
                ApplyFilterAndSearch();
            }
            curX += modW + 6f;

            // Filter: Overlaps Only
            Color? overlapColor = totalOverlapCount > 0 ? new Color(1.0f, 0.45f, 0.20f, 1.0f) : (Color?)null;
            string ovlTxt = Tr($"⚠ Overlaps ({totalOverlapEntries})", $"⚠ Conflictos ({totalOverlapEntries})");
            float ovlW = 155f;
            if (DrawNativeWoodButton(new Rect(curX, tbY, ovlW, btnH), ovlTxt, activeFilter == KeybindFilter.OverlapsOnly, overlapColor))
            {
                activeFilter = KeybindFilter.OverlapsOnly;
                ApplyFilterAndSearch();
            }
            curX += ovlW + 16f;

            // Search Bar & Clear Button
            float searchRemainingW = (winX + winWidth - 18f) - curX;
            float searchInputW = Mathf.Max(120f, searchRemainingW - 42f);

            if (searchInputStyle == null)
            {
                searchInputStyle = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            Rect searchRect = new Rect(curX, tbY + 2f, searchInputW, 28f);
            GUI.DrawTexture(new Rect(searchRect.x - 1, searchRect.y - 1, searchRect.width + 2, searchRect.height + 2), GetColorTexture(new Color(0.35f, 0.25f, 0.15f, 0.85f)));
            string newSearch = GUI.TextField(searchRect, searchQuery ?? "", searchInputStyle);
            if (newSearch != searchQuery)
            {
                searchQuery = newSearch;
                ApplyFilterAndSearch();
            }

            // Placeholder hint if search empty
            if (string.IsNullOrEmpty(searchQuery))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.40f);
                GUI.Label(new Rect(searchRect.x + 8, searchRect.y + 4, searchRect.width - 16, 20), Tr("Search hotkeys...", "Buscar teclas..."), smallStyle);
                GUI.color = Color.white;
            }

            // Clear search button [X]
            Rect clearSearchRect = new Rect(curX + searchInputW + 4f, tbY + 2f, 32f, 28f);
            if (!string.IsNullOrEmpty(searchQuery))
            {
                if (DrawNativeWoodButton(clearSearchRect, "X", false, new Color(1f, 0.6f, 0.6f, 1f)))
                {
                    searchQuery = "";
                    ApplyFilterAndSearch();
                }
            }

            // -----------------------------------------------------------------
            //                     KEYBINDS SCROLLABLE LIST
            // -----------------------------------------------------------------
            float listY = tbY + 42f;
            float listH = winHeight - 146f;
            Rect listOuterRect = new Rect(winX + 16f, listY, winWidth - 32f, listH);

            // Dark inner background for contrast
            GUI.DrawTexture(listOuterRect, GetColorTexture(new Color(0.06f, 0.04f, 0.02f, 0.88f)));

            float rowH = 56f;
            float totalContentH = filteredKeybinds.Count * rowH + 12f;
            Rect viewRect = new Rect(0, 0, listOuterRect.width - 20f, Mathf.Max(listH, totalContentH));

            scrollKeybindsPos = GUI.BeginScrollView(listOuterRect, scrollKeybindsPos, viewRect);

            float rowY = 6f;
            for (int i = 0; i < filteredKeybinds.Count; i++)
            {
                var entry = filteredKeybinds[i];
                Rect rowRect = new Rect(6f, rowY, viewRect.width - 12f, rowH - 6f);

                // Alternating row background with conflict tint
                bool hasConflicts = entry.Conflicts.Count > 0;
                Color rowBgColor = hasConflicts
                    ? new Color(0.35f, 0.12f, 0.08f, 0.85f)
                    : (i % 2 == 0 ? new Color(0.12f, 0.08f, 0.05f, 0.75f) : new Color(0.16f, 0.11f, 0.07f, 0.75f));

                GUI.DrawTexture(rowRect, GetColorTexture(rowBgColor));

                // Left: Category Badge & Action Name
                float badgeW = 90f;
                Rect badgeRect = new Rect(rowRect.x + 8f, rowRect.y + 6f, badgeW, 20f);
                GUI.DrawTexture(badgeRect, GetColorTexture(entry.CategoryColor * 0.45f));
                GUI.DrawTexture(new Rect(badgeRect.x, badgeRect.y, 3f, badgeRect.height), GetColorTexture(entry.CategoryColor));
                DrawShadowLabel(badgeRect, entry.Category, smallStyle, entry.CategoryColor, Color.black);

                // Action Name
                string actionName = IsSpanish() ? entry.NameEs : entry.NameEn;
                Rect nameRect = new Rect(rowRect.x + badgeW + 18f, rowRect.y + 6f, 260f, 22f);
                DrawShadowLabel(nameRect, actionName, titleStyle ?? GUI.skin.label, Color.white, Color.black);

                // Overlap Warning Badge (if conflicting)
                if (hasConflicts)
                {
                    string conflictStr = Tr("⚠ Overlaps with: ", "⚠ Solapa con: ") + string.Join(", ", entry.Conflicts);
                    Rect conflictRect = new Rect(rowRect.x + 8f, rowRect.y + 28f, rowRect.width - 340f, 18f);
                    GUI.DrawTexture(conflictRect, GetColorTexture(new Color(0.60f, 0.15f, 0.10f, 0.90f)));
                    DrawShadowLabel(new Rect(conflictRect.x + 4f, conflictRect.y, conflictRect.width - 8f, 18f), conflictStr, smallStyle, new Color(1.0f, 0.85f, 0.30f, 1.0f), Color.black);
                }

                // Right: Primary Key Button
                float rightX = rowRect.x + rowRect.width - 325f;
                float keyBtnW = 125f;
                float keyBtnH = 34f;
                float keyBtnY = rowRect.y + (rowRect.height - keyBtnH) * 0.5f;

                bool isRebindingPrimary = (rebindingEntry == entry && rebindingSlot == 0);
                string pKeyLabel = isRebindingPrimary
                    ? Tr("⌨ Press Key...", "⌨ Pulsa Tecla...")
                    : FormatKeyName(entry.PrimaryKey);

                Color? pBtnColor = isRebindingPrimary
                    ? (Color?)new Color(0.35f, 1.0f, 0.45f, 1.0f)
                    : (hasConflicts ? (Color?)new Color(1.0f, 0.65f, 0.30f, 1.0f) : null);

                if (DrawNativeWoodButton(new Rect(rightX, keyBtnY, keyBtnW, keyBtnH), pKeyLabel, isRebindingPrimary, pBtnColor))
                {
                    if (rebindingEntry == entry && rebindingSlot == 0)
                        rebindingEntry = null; // Toggle off
                    else
                    {
                        rebindingEntry = entry;
                        rebindingSlot = 0;
                    }
                }

                // Secondary Key Button (for Vanilla, or "-" for Mods)
                rightX += keyBtnW + 8f;
                bool isRebindingSecondary = (rebindingEntry == entry && rebindingSlot == 1);
                string sKeyLabel = entry.IsVanilla
                    ? (isRebindingSecondary ? Tr("⌨ Press...", "⌨ Pulsa...") : FormatKeyName(entry.SecondaryKey))
                    : "-";

                Color? sBtnColor = isRebindingSecondary
                    ? (Color?)new Color(0.35f, 1.0f, 0.45f, 1.0f)
                    : (entry.IsVanilla && hasConflicts && entry.SecondaryKey != KeyCode.None ? (Color?)new Color(1.0f, 0.65f, 0.30f, 1.0f) : null);

                if (DrawNativeWoodButton(new Rect(rightX, keyBtnY, keyBtnW, keyBtnH), sKeyLabel, isRebindingSecondary, sBtnColor))
                {
                    if (entry.IsVanilla)
                    {
                        if (rebindingEntry == entry && rebindingSlot == 1)
                            rebindingEntry = null;
                        else
                        {
                            rebindingEntry = entry;
                            rebindingSlot = 1;
                        }
                    }
                }

                // Reset Button [↺]
                rightX += keyBtnW + 8f;
                Rect resetBtnRect = new Rect(rightX, keyBtnY, 34f, keyBtnH);
                if (DrawNativeWoodButton(resetBtnRect, "↺", false, new Color(0.9f, 0.9f, 0.7f, 1.0f)))
                {
                    ResetEntryToDefault(entry);
                }

                rowY += rowH;
            }

            GUI.EndScrollView();

            // -----------------------------------------------------------------
            //                     FOOTER / STATUS BAR
            // -----------------------------------------------------------------
            float footerY = winY + winHeight - 44f;
            string tipText = Tr(
                "✦ Click any key to rebind. [Esc] Cancel  •  [Backspace/Del] Unbind. Real-time conflict detector active.",
                "✦ Haz clic en una tecla para reasignar. [Esc] Cancelar  •  [Supr] Desasignar. Detector de conflictos en tiempo real."
            );
            DrawShadowLabel(new Rect(winX + 22f, footerY + 2f, winWidth - 300f, 22f), tipText, smallStyle, new Color(0.85f, 0.85f, 0.85f, 0.90f), Color.black);

            // Conflict Status Summary Badge
            Rect statusBadgeRect = new Rect(winX + winWidth - 270f, footerY, 250f, 26f);
            if (totalOverlapCount == 0)
            {
                GUI.DrawTexture(statusBadgeRect, GetColorTexture(new Color(0.12f, 0.35f, 0.12f, 0.90f)));
                DrawShadowLabel(statusBadgeRect, Tr("✔ All 0 Overlaps / Clear", "✔ 0 Conflictos / Todo Correcto"), smallStyle, new Color(0.50f, 1.0f, 0.40f), Color.black);
            }
            else
            {
                GUI.DrawTexture(statusBadgeRect, GetColorTexture(new Color(0.55f, 0.18f, 0.10f, 0.95f)));
                DrawShadowLabel(statusBadgeRect, Tr($"⚠ {totalOverlapEntries} actions share {totalOverlapCount} key(s)", $"⚠ {totalOverlapEntries} acciones comparten {totalOverlapCount} tecla(s)"), smallStyle, new Color(1.0f, 0.85f, 0.30f), Color.black);
            }

            // Click outside closes modal (unless in rebinding mode)
            if (rebindingEntry == null && Event.current.type == EventType.MouseDown && (Time.unscaledTime - modalOpenedTimestamp > 0.35f) && !winRect.Contains(Event.current.mousePosition))
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
