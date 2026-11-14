using System;
using Aether.Core.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aether.UI.Layout
{
    /// <summary>
    /// Atelier NGUI HUD Layout Engine for Block Story.
    /// Manages non-destructive anchor realignment for native HUD elements (HUD/Diamond, HUD/Buttons, HUD/Stats/BarsAnchor),
    /// eliminating UI crowding and preventing drift across resolutions.
    /// </summary>
    public static class VanillaHudController
    {
        // -----------------------------------------------------------------
        // Configuration Bindings
        // -----------------------------------------------------------------
        public static ConfigEntry<string> Preset { get; }
        public static ConfigEntry<bool> MoveDiamondsUnderXP { get; }
        public static ConfigEntry<bool> MoveButtonsToBottomRight { get; }
        public static ConfigEntry<bool> FixBarsAnchor { get; }

        // Cached GameObject and Component references
        private static GameObject? _cachedStatsObj;
        private static GameObject? _cachedBarsAnchorObj;
        private static GameObject? _cachedDiamondObj;
        private static UIAnchor? _cachedDiamondAnchor;
        private static GameObject? _cachedButtonsObj;
        private static UIAnchor? _cachedButtonsAnchor;

        private static float _lastSearchTime = 0f;
        private static bool _initialized = false;

        static VanillaHudController()
        {
            Preset = AetherConfig.Bind(
                "aether_ui",
                "Hud",
                "Preset",
                "ModernModpack",
                "HUD Layout preset: 'ModernModpack' or 'VanillaClassic'.");

            MoveDiamondsUnderXP = AetherConfig.Bind(
                "aether_ui",
                "Hud",
                "MoveDiamondsUnderXP",
                true,
                "Moves HUD/Diamond to Top-Left under XP bar so Top-Center is free for Lens WAILA.");

            MoveButtonsToBottomRight = AetherConfig.Bind(
                "aether_ui",
                "Hud",
                "MoveButtonsToBottomRight",
                true,
                "Moves HUD/Buttons (C, J, Y, P) to Bottom-Right so Top-Right is free for Wayfinder.");

            FixBarsAnchor = AetherConfig.Bind(
                "aether_ui",
                "Hud",
                "FixBarsAnchor",
                true,
                "Disables UIAnchor on HUD/Stats/BarsAnchor and locks localPosition at (46, -3.5, 0) so HP/Mana/XP bars never drift.");

            Initialize();
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ClearCache();
            if (string.Equals(scene.name, "PlayingMobile", StringComparison.OrdinalIgnoreCase))
            {
                UpdateVanillaHud(true);
            }
        }

        public static void ClearCache()
        {
            _cachedStatsObj = null;
            _cachedBarsAnchorObj = null;
            _cachedDiamondObj = null;
            _cachedDiamondAnchor = null;
            _cachedButtonsObj = null;
            _cachedButtonsAnchor = null;
            _lastSearchTime = 0f;
        }

        public static void ApplyPreset(string presetName)
        {
            Preset.Value = presetName;

            if (string.Equals(presetName, "ModernModpack", StringComparison.OrdinalIgnoreCase))
            {
                MoveDiamondsUnderXP.Value = true;
                MoveButtonsToBottomRight.Value = true;
                FixBarsAnchor.Value = true;
            }
            else if (string.Equals(presetName, "VanillaClassic", StringComparison.OrdinalIgnoreCase))
            {
                MoveDiamondsUnderXP.Value = false;
                MoveButtonsToBottomRight.Value = false;
                FixBarsAnchor.Value = false;
            }

            UpdateVanillaHud(true);
            AetherConfig.SaveModConfigFile("aether_ui");
        }

        /// <summary>
        /// Periodically updates and enforces vanilla HUD transformations in PlayingMobile scene.
        /// Throttled to execute every 0.5 seconds unless forced.
        /// </summary>
        public static void UpdateVanillaHud(bool force = false)
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!string.Equals(activeScene.name, "PlayingMobile", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!force && (Time.unscaledTime - _lastSearchTime < 0.5f))
            {
                return;
            }
            _lastSearchTime = Time.unscaledTime;

            try
            {
                ApplyBarsAnchorFix();
                ApplyDiamondPosition();
                ApplyButtonsPosition();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Aether.UI] VanillaHudController encountered exception during HUD update: {ex.Message}");
            }
        }

        private static void ApplyBarsAnchorFix()
        {
            if (_cachedStatsObj == null)
            {
                _cachedStatsObj = GameObject.Find("HUD/Stats");
            }

            if (_cachedStatsObj != null)
            {
                if (_cachedBarsAnchorObj == null)
                {
                    var t = _cachedStatsObj.transform.Find("BarsAnchor");
                    if (t != null) _cachedBarsAnchorObj = t.gameObject;
                }

                if (_cachedBarsAnchorObj != null)
                {
                    var ba = _cachedBarsAnchorObj.GetComponent<UIAnchor>();
                    if (FixBarsAnchor.Value)
                    {
                        if (ba != null && ba.enabled)
                        {
                            ba.enabled = false;
                        }
                        _cachedBarsAnchorObj.transform.localPosition = new Vector3(46f, -3.5f, 0f);
                        _cachedBarsAnchorObj.transform.localScale = Vector3.one;
                    }
                    else
                    {
                        if (ba != null && !ba.enabled)
                        {
                            ba.enabled = true;
                        }
                    }
                }
            }
        }

        private static void ApplyDiamondPosition()
        {
            if (_cachedDiamondObj == null)
            {
                _cachedDiamondObj = GameObject.Find("HUD/Diamond");
                if (_cachedDiamondObj != null)
                {
                    _cachedDiamondAnchor = _cachedDiamondObj.GetComponent<UIAnchor>();
                }
            }

            if (_cachedDiamondAnchor != null && _cachedDiamondObj != null)
            {
                if (MoveDiamondsUnderXP.Value)
                {
                    var targetOffset = new Vector2(0.055f, -0.142f);
                    if (_cachedDiamondAnchor.side != UIAnchor.Side.TopLeft || _cachedDiamondAnchor.relativeOffset != targetOffset)
                    {
                        _cachedDiamondAnchor.side = UIAnchor.Side.TopLeft;
                        _cachedDiamondAnchor.relativeOffset = targetOffset;
                        _cachedDiamondAnchor.enabled = true;
                    }

                    var targetScale = new Vector3(0.85f, 0.85f, 1f);
                    if (_cachedDiamondObj.transform.localScale != targetScale)
                    {
                        _cachedDiamondObj.transform.localScale = targetScale;
                    }
                }
                else
                {
                    var targetOffset = new Vector2(0.50f, -0.02f);
                    if (_cachedDiamondAnchor.side != UIAnchor.Side.TopLeft || _cachedDiamondAnchor.relativeOffset != targetOffset)
                    {
                        _cachedDiamondAnchor.side = UIAnchor.Side.TopLeft;
                        _cachedDiamondAnchor.relativeOffset = targetOffset;
                        _cachedDiamondAnchor.enabled = true;
                    }

                    if (_cachedDiamondObj.transform.localScale != Vector3.one)
                    {
                        _cachedDiamondObj.transform.localScale = Vector3.one;
                    }
                }
            }
        }

        private static void ApplyButtonsPosition()
        {
            if (_cachedButtonsObj == null)
            {
                _cachedButtonsObj = GameObject.Find("HUD/Buttons");
                if (_cachedButtonsObj != null)
                {
                    _cachedButtonsAnchor = _cachedButtonsObj.GetComponent<UIAnchor>();
                }
            }

            if (_cachedButtonsAnchor != null && _cachedButtonsObj != null)
            {
                if (MoveButtonsToBottomRight.Value)
                {
                    var targetOffset = new Vector2(1.0f, -0.75f);
                    if (_cachedButtonsAnchor.side != UIAnchor.Side.TopLeft || _cachedButtonsAnchor.relativeOffset != targetOffset)
                    {
                        _cachedButtonsAnchor.side = UIAnchor.Side.TopLeft;
                        _cachedButtonsAnchor.relativeOffset = targetOffset;
                        _cachedButtonsAnchor.enabled = true;
                    }

                    var targetScale = new Vector3(0.90f, 0.90f, 1f);
                    if (_cachedButtonsObj.transform.localScale != targetScale)
                    {
                        _cachedButtonsObj.transform.localScale = targetScale;
                    }

                    var tp = _cachedButtonsObj.GetComponent<TweenPosition>();
                    if (tp != null && tp.enabled)
                    {
                        tp.enabled = false;
                    }
                }
                else
                {
                    var targetOffset = new Vector2(1.0f, 0.0f);
                    if (_cachedButtonsAnchor.side != UIAnchor.Side.TopLeft || _cachedButtonsAnchor.relativeOffset != targetOffset)
                    {
                        _cachedButtonsAnchor.side = UIAnchor.Side.TopLeft;
                        _cachedButtonsAnchor.relativeOffset = targetOffset;
                        _cachedButtonsAnchor.enabled = true;
                    }

                    if (_cachedButtonsObj.transform.localScale != Vector3.one)
                    {
                        _cachedButtonsObj.transform.localScale = Vector3.one;
                    }
                }
            }
        }
    }
}
