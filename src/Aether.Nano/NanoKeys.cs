using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UAction = UnityEngine.InputSystem.InputAction;

namespace Aether.Nano
{
    /// <summary>
    /// Mod keys in the game's own keybinding window (Pause → Controls → Edit keybindings), like Fabric's key mappings:
    /// a mod registers its <c>ConfigEntry&lt;KeyCode&gt;</c> once and Nano adds a header per mod and a row per key after the
    /// game's rows. Rebinding there writes the mod's config, and "Reset All" resets them too. The mod keeps reading its
    /// config entry as before. With Aether, F7 replaces that window and lists the same keys with these labels.
    /// </summary>
    public static class NanoKeys
    {
        public sealed class Key
        {
            public string ModId;
            public string ModName;
            public string LabelEn;
            public string LabelEs;
            public ConfigEntry<KeyCode> Entry;
            public string Label => Nano.Tr(LabelEn, string.IsNullOrEmpty(LabelEs) ? LabelEn : LabelEs);
            internal UAction Action;
            internal InputActionReference Ref;
        }

        private static readonly List<Key> Keys = new List<Key>();
        public static IReadOnlyList<Key> All => Keys;

        /// <summary>Adds <paramref name="entry"/> to the game's keybinding window under <paramref name="modName"/>.</summary>
        public static void Register(string modId, string modName, ConfigEntry<KeyCode> entry, string labelEn, string labelEs = null)
        {
            if (entry == null) return;
            Keys.RemoveAll(k => k.Entry == entry);
            var key = new Key { ModId = modId ?? string.Empty, ModName = modName ?? modId, LabelEn = labelEn ?? entry.Definition.Key, LabelEs = labelEs, Entry = entry };
            Keys.Add(key);
            entry.SettingChanged += (_, __) => ApplyOverride(key);
            Patch();
        }

        /// <summary>The registration for this config entry, or null.</summary>
        public static Key Find(ConfigEntryBase entry)
        {
            foreach (Key k in Keys) if (ReferenceEquals(k.Entry, entry)) return k;
            return null;
        }

        // ---- Harmony ---------------------------------------------------------------------------

        private static bool _patched;

        private static void Patch()
        {
            if (_patched) return;
            _patched = true;
            try
            {
                var h = new Harmony(Nano.Guid + ".keys");
                h.Patch(AccessTools.Method(typeof(LoadKeybindings), "loadAllKeybinds"), postfix: new HarmonyMethod(typeof(NanoKeys), nameof(AfterLoadAll)));
                h.Patch(AccessTools.Method(typeof(LoadKeybindings), nameof(LoadKeybindings.ResetAllKeybinds)), postfix: new HarmonyMethod(typeof(NanoKeys), nameof(AfterResetAll)));
                h.Patch(AccessTools.Method(typeof(BindKey), "UpdateDisplay"), postfix: new HarmonyMethod(typeof(NanoKeys), nameof(AfterUpdateDisplay)));
            }
            catch (Exception ex) { Nano.Log.LogWarning("Mod keys will not show in the game's keybinding window: " + ex.Message); }
        }

        private static void AfterLoadAll(LoadKeybindings __instance)
        {
            try { AddRows(__instance); }
            catch (Exception ex) { Nano.Log.LogWarning("Could not add mod keys to the keybinding window: " + ex); }
        }

        private static void AfterResetAll(LoadKeybindings __instance)
        {
            try
            {
                foreach (Key k in Keys) k.Entry.Value = (KeyCode)k.Entry.DefaultValue;
                foreach (BindKey b in __instance.container.GetComponentsInChildren<BindKey>())
                    if (Owner(b) != null) b.LoadBinding();
            }
            catch (Exception ex) { Nano.Log.LogWarning("Could not reset mod keys: " + ex.Message); }
        }

        /// <summary>Runs after every load, rebind, cancel or reset of a slot: our slots copy the binding into the mod's config.</summary>
        private static void AfterUpdateDisplay(BindKey __instance)
        {
            Key key = Owner(__instance);
            if (key == null) return;
            UAction a = key.Action;
            string pref = $"Rebind_{a.name}_0";
            if (PlayerPrefs.HasKey(pref)) { PlayerPrefs.DeleteKey(pref); PlayerPrefs.Save(); } // the config is the source of truth
            string path = a.bindings[0].effectivePath;
            KeyCode code = ToKeyCode(path);
            if (code == KeyCode.None && !string.IsNullOrEmpty(path))
            {
                ApplyOverride(key); // a gamepad button or the like: legacy input cannot read it, keep the old key
                __instance.keyDisplay.text = a.GetBindingDisplayString(0);
                return;
            }
            if (code != key.Entry.Value) key.Entry.Value = code;
        }

        private static Key Owner(BindKey b)
        {
            if (b == null || b.actionReference == null) return null;
            foreach (Key k in Keys) if (k.Ref == b.actionReference) return k;
            return null;
        }

        // ---- Rows --------------------------------------------------------------------------------

        private static readonly Color HeaderColor = new Color(1f, 0.78f, 0.45f, 1f);
        private const float HeaderX = 290f; // between the name column (x -9) and the second key column (x 595)

        private static void AddRows(LoadKeybindings lk)
        {
            if (Keys.Count == 0 || lk.template == null || lk.container == null) return;
            int index = lk.actions?.Length ?? 0;
            var mods = new List<string>();
            foreach (Key k in Keys) if (!mods.Contains(k.ModName)) mods.Add(k.ModName);

            foreach (string mod in mods)
            {
                GameObject header = AddRow(lk, index++, "Nano header " + mod);
                KeybindingEntry he = header.GetComponent<KeybindingEntry>();
                he.actionLabel.text = mod;
                he.actionLabel.color = HeaderColor;
                he.actionLabel.lineWidth = 0; // one line across the row, centred over the key columns
                Vector3 hp = he.actionLabel.transform.localPosition;
                he.actionLabel.transform.localPosition = new Vector3(HeaderX, hp.y, hp.z);
                foreach (BindKey b in header.GetComponentsInChildren<BindKey>(true)) b.gameObject.SetActive(false);
                he.bindingLabel1.gameObject.SetActive(false);
                he.bindingLabel2.gameObject.SetActive(false);

                foreach (Key k in Keys)
                {
                    if (k.ModName != mod) continue;
                    EnsureAction(k);
                    GameObject row = AddRow(lk, index++, "Nano key " + k.Action.name);
                    KeybindingEntry e = row.GetComponent<KeybindingEntry>();
                    e.actionLabel.text = k.Label;
                    if (k.Label.Length > 7) e.actionLabel.transform.localScale *= 0.75f; // the game wraps long names mid-word
                    e.bindingLabel2.gameObject.SetActive(false);
                    foreach (BindKey b in row.GetComponentsInChildren<BindKey>(true))
                    {
                        if (b.ActionNumber != 0) { b.gameObject.SetActive(false); continue; } // one key per setting
                        b.actionReference = k.Ref;
                        PlayerPrefs.DeleteKey($"Rebind_{k.Action.name}_0"); // a stale game-side copy must not override the config
                        b.LoadBinding();
                    }
                }
            }
        }

        private static GameObject AddRow(LoadKeybindings lk, int index, string name)
        {
            GameObject row = NGUITools.AddChild(lk.container.gameObject, lk.template);
            row.name = name;
            row.SetActive(true);
            Vector3 p = row.transform.localPosition;
            row.transform.localPosition = new Vector3(p.x, -index * lk.yOffset, p.z);
            return row;
        }

        // ---- Input System <-> KeyCode ---------------------------------------------------------------

        private static void EnsureAction(Key k)
        {
            if (k.Action != null) return;
            ConfigDefinition d = k.Entry.Definition;
            // One asset and map per key: a reference needs an asset, and the game's rebind disables and enables the whole map.
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.hideFlags = HideFlags.HideAndDontSave;
            InputActionMap map = asset.AddActionMap("Nano " + k.ModId);
            k.Action = map.AddAction($"nano:{k.ModId}/{d.Section}/{d.Key}", InputActionType.Button);
            k.Action.AddBinding(ToPath((KeyCode)k.Entry.DefaultValue));
            k.Ref = InputActionReference.Create(k.Action);
            k.Ref.hideFlags = HideFlags.HideAndDontSave; // survives scene changes and asset unloads
            ApplyOverride(k);
        }

        private static void ApplyOverride(Key k)
        {
            if (k.Action == null) return;
            string path = ToPath(k.Entry.Value);
            if (path == k.Action.bindings[0].path) k.Action.RemoveBindingOverride(0);
            else k.Action.ApplyBindingOverride(0, path);
        }

        private static readonly Dictionary<KeyCode, string> Named = new Dictionary<KeyCode, string>
        {
            { KeyCode.Return, "enter" }, { KeyCode.Space, "space" }, { KeyCode.Tab, "tab" }, { KeyCode.Backspace, "backspace" },
            { KeyCode.Escape, "escape" }, { KeyCode.Delete, "delete" }, { KeyCode.Insert, "insert" }, { KeyCode.Home, "home" },
            { KeyCode.End, "end" }, { KeyCode.PageUp, "pageUp" }, { KeyCode.PageDown, "pageDown" }, { KeyCode.UpArrow, "upArrow" },
            { KeyCode.DownArrow, "downArrow" }, { KeyCode.LeftArrow, "leftArrow" }, { KeyCode.RightArrow, "rightArrow" },
            { KeyCode.LeftShift, "leftShift" }, { KeyCode.RightShift, "rightShift" }, { KeyCode.LeftControl, "leftCtrl" },
            { KeyCode.RightControl, "rightCtrl" }, { KeyCode.LeftAlt, "leftAlt" }, { KeyCode.RightAlt, "rightAlt" },
            { KeyCode.CapsLock, "capsLock" }, { KeyCode.Minus, "minus" }, { KeyCode.Equals, "equals" },
            { KeyCode.LeftBracket, "leftBracket" }, { KeyCode.RightBracket, "rightBracket" }, { KeyCode.Semicolon, "semicolon" },
            { KeyCode.Quote, "quote" }, { KeyCode.BackQuote, "backquote" }, { KeyCode.Comma, "comma" }, { KeyCode.Period, "period" },
            { KeyCode.Slash, "slash" }, { KeyCode.Backslash, "backslash" }, { KeyCode.Pause, "pause" }, { KeyCode.Print, "printScreen" },
            { KeyCode.ScrollLock, "scrollLock" }, { KeyCode.Numlock, "numLock" }, { KeyCode.KeypadEnter, "numpadEnter" },
            { KeyCode.KeypadPlus, "numpadPlus" }, { KeyCode.KeypadMinus, "numpadMinus" }, { KeyCode.KeypadMultiply, "numpadMultiply" },
            { KeyCode.KeypadDivide, "numpadDivide" }, { KeyCode.KeypadPeriod, "numpadPeriod" }, { KeyCode.KeypadEquals, "numpadEquals" },
        };

        private static readonly Dictionary<KeyCode, string> MouseButtons = new Dictionary<KeyCode, string>
        {
            { KeyCode.Mouse0, "leftButton" }, { KeyCode.Mouse1, "rightButton" }, { KeyCode.Mouse2, "middleButton" },
            { KeyCode.Mouse3, "backButton" }, { KeyCode.Mouse4, "forwardButton" },
        };

        /// <summary>The Input System path for a legacy key ("&lt;Keyboard&gt;/n"), or "" for None and keys it has no name for.</summary>
        public static string ToPath(KeyCode code)
        {
            if (code >= KeyCode.A && code <= KeyCode.Z) return "<Keyboard>/" + (char)('a' + (code - KeyCode.A));
            if (code >= KeyCode.Alpha0 && code <= KeyCode.Alpha9) return "<Keyboard>/" + (code - KeyCode.Alpha0);
            if (code >= KeyCode.Keypad0 && code <= KeyCode.Keypad9) return "<Keyboard>/numpad" + (code - KeyCode.Keypad0);
            if (code >= KeyCode.F1 && code <= KeyCode.F15) return "<Keyboard>/f" + (code - KeyCode.F1 + 1);
            if (Named.TryGetValue(code, out string k)) return "<Keyboard>/" + k;
            if (MouseButtons.TryGetValue(code, out string m)) return "<Mouse>/" + m;
            return string.Empty;
        }

        /// <summary>The legacy key for an Input System path, or None when legacy input cannot read it (gamepads, unknown keys).</summary>
        public static KeyCode ToKeyCode(string path)
        {
            if (string.IsNullOrEmpty(path)) return KeyCode.None;
            int slash = path.LastIndexOf('/');
            string device = slash > 0 ? path.Substring(0, slash) : string.Empty;
            string control = path.Substring(slash + 1);
            Dictionary<KeyCode, string> table;
            if (device.Equals("<Keyboard>", StringComparison.OrdinalIgnoreCase)) table = Named;
            else if (device.Equals("<Mouse>", StringComparison.OrdinalIgnoreCase)) table = MouseButtons;
            else return KeyCode.None;

            for (KeyCode c = KeyCode.A; c <= KeyCode.Z && table == Named; c++) if (Match(c, control)) return c;
            if (table == Named)
            {
                for (KeyCode c = KeyCode.Alpha0; c <= KeyCode.Alpha9; c++) if (Match(c, control)) return c;
                for (KeyCode c = KeyCode.Keypad0; c <= KeyCode.Keypad9; c++) if (Match(c, control)) return c;
                for (KeyCode c = KeyCode.F1; c <= KeyCode.F15; c++) if (Match(c, control)) return c;
            }
            foreach (var kv in table) if (kv.Value.Equals(control, StringComparison.OrdinalIgnoreCase)) return kv.Key;
            return KeyCode.None;
        }

        private static bool Match(KeyCode c, string control) => ToPath(c).EndsWith("/" + control, StringComparison.OrdinalIgnoreCase);
    }
}
