using System;
using Aether.Loader;
using Aether.UI.Theme;
using BlockStoryLens;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aether.UI.Overlays
{
    /// <summary>
    /// Aether's seal in the menus (bottom-left): the open cube, "aether" in Gloock, version and mod count in mono and the
    /// key that opens its menu. Clicking it opens the menu too. Never drawn over gameplay.
    /// </summary>
    public static class TitleWatermarkOverlay
    {
        public static bool IsVisible { get; set; } = true;

        public static void DrawGUI()
        {
            if (!IsVisible) return;
            if (LoadingScreenState.IsLoadingOrMenuNotReady()) return;

            string sceneName = SceneManager.GetActiveScene().name;
            bool isGameplay = string.Equals(sceneName, "PlayingMobile", StringComparison.OrdinalIgnoreCase);

            // In PlayingMobile, never draw the watermark over gameplay.
            if (isGameplay)
            {
                return;
            }

            InitStyles();

            // Same count as the native menu's MODS button: every entry in LoadedMods
            // (Aether Core, the native suite and the flagship manifests).
            int modCount = AetherBootstrap.LoadedModCount;
            const bool showKey = true;

            string word = "aether";
            string meta = modCount == 0
                ? $"{AetherBootstrap.PluginVersion}  ·  {BlockStorySuitePlugin.Tr("ready", "listo")}"
                : $"{AetherBootstrap.PluginVersion}  ·  {modCount} mod{(modCount != 1 ? "s" : "")}";
            const string key = "F10";

            // A seal, not a pill: cube, the wordmark in Gloock, then version and count in mono, then the key.
            const float h = 34f, pad = 10f, cubeH = 20f;
            float wordW = _word!.CalcSize(new GUIContent(word)).x;
            float metaW = _meta!.CalcSize(new GUIContent(meta)).x;
            float keyW = showKey ? _key!.CalcSize(new GUIContent(key)).x + 12f : 0f;
            float cubeW = Aether.Nano.NanoBrand.Cube != null ? cubeH * 57f / 64f : 0f;
            float w = pad + cubeW + (cubeW > 0 ? 8f : 0f) + wordW + 12f + 1f + 12f + metaW + (showKey ? 10f + keyW : 0f) + pad;
            float x = 14f, y = Screen.height - h - 14f;
            var r = new Rect(x, y, w, h);

            int prevDepth = GUI.depth;
            GUI.depth = -4900; // Just below modal (-4980) and cursor (-5000)
            Color prevColor = GUI.color;
            bool hover = r.Contains(Event.current.mousePosition);

            Aether.Nano.NanoBrand.DrawCard(r, hover ? 1f : 0.7f);
            float cx = x + pad;
            if (cubeW > 0) { Aether.Nano.NanoBrand.DrawCube(cx, y + (h - cubeH) / 2f, cubeH); cx += cubeW + 8f; }
            GUI.Label(new Rect(cx, y + 1f, wordW + 2f, h - 2f), word, hover ? _wordHover : _word);
            cx += wordW + 12f;
            GUI.DrawTexture(new Rect(cx, y + 10f, 1f, h - 20f), Aether.Nano.NanoBrand.Solid(Aether.Nano.NanoBrand.Line));
            cx += 13f;
            GUI.Label(new Rect(cx, y, metaW + 2f, h), meta, _meta);
            cx += metaW + 10f;
            if (showKey)
            {
                var kr = new Rect(cx, y + 8f, keyW, h - 16f);
                GUI.DrawTexture(kr, Aether.Nano.NanoBrand.Solid(Aether.Nano.NanoBrand.InkSoft));
                GUI.DrawTexture(new Rect(kr.x, kr.yMax - 1f, kr.width, 1f), Aether.Nano.NanoBrand.Solid(Aether.Nano.NanoBrand.EmberDeep));
                GUI.Label(kr, key, _key);
            }

            // The whole seal opens Aether's menu.
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                BlockStorySuitePlugin.Instance?.SetModMenuOpenState(true);

            GUI.color = prevColor;
            GUI.depth = prevDepth;
        }

        private static GUIStyle? _word, _wordHover, _meta, _key;

        private static void InitStyles()
        {
            if (_word != null) return;
            _word = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Display, 20, Aether.Nano.NanoBrand.Paper);
            _wordHover = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Display, 20, Aether.Nano.NanoBrand.Ember);
            _meta = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 12, Aether.Nano.NanoBrand.Mute);
            _key = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 11, Aether.Nano.NanoBrand.Ember, TextAnchor.MiddleCenter);
        }
    }
}
