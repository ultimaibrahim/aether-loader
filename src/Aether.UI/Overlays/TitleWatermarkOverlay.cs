using System;
using Aether.Loader;
using Aether.Nano;
using BlockStoryLens;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aether.UI.Overlays
{
    /// <summary>
    /// Aether's floating seal in the menus (bottom-left): smoked-crystal squircle with the open cube,
    /// "aether" in Gloock, version and mod count in mono, and a live keycap chip for the Mod Menu hotkey.
    /// Clicking it opens the menu too. Never drawn over gameplay or loading screens.
    /// </summary>
    public static class TitleWatermarkOverlay
    {
        public static bool IsVisible { get; set; } = true;

        private static GUIStyle? _word, _meta, _key;
        private static readonly GUIContent _measure = new GUIContent();

        public static void DrawGUI()
        {
            if (!IsVisible) return;

            string sceneName = SceneManager.GetActiveScene().name;
            bool isGameplay = string.Equals(sceneName, "PlayingMobile", StringComparison.OrdinalIgnoreCase);
            bool modalOpen = BlockStorySuitePlugin.Instance != null && BlockStorySuitePlugin.Instance.IsModalOpen;
            bool blocked = isGameplay || modalOpen || LoadingScreenState.IsLoadingOrMenuNotReady();

            float enter = NanoMotion.Sample("aether:watermark", !blocked, 0.28f, 0.14f);
            if (enter <= 0.005f) return;

            InitStyles();

            // Same count as the native menu's MODS button: every entry in LoadedMods
            // (Aether Core, the native suite and the flagship manifests).
            int modCount = AetherBootstrap.LoadedModCount;

            string word = "aether";
            string meta = modCount == 0
                ? $"{AetherBootstrap.PluginVersion}  ·  {BlockStorySuitePlugin.Tr("ready", "listo")}"
                : $"{AetherBootstrap.PluginVersion}  ·  {modCount} mod{(modCount != 1 ? "s" : "")}";

            KeyCode menuKeyCode = BlockStorySuitePlugin.MenuKey != null ? BlockStorySuitePlugin.MenuKey.Value : KeyCode.F10;
            string key = BlockStorySuitePlugin.Instance != null
                ? BlockStorySuitePlugin.Instance.FormatKeyName(menuKeyCode)
                : menuKeyCode.ToString();
            bool showKey = !string.IsNullOrEmpty(key) && key != "-";

            const float h = 38f, pad = 12f, cubeH = 20f, radius = 10f;
            _measure.text = word;
            float wordW = _word!.CalcSize(_measure).x;
            _measure.text = meta;
            float metaW = _meta!.CalcSize(_measure).x;
            _measure.text = key;
            float keyW = showKey ? Mathf.Max(34f, _key!.CalcSize(_measure).x + 14f) : 0f;
            float cubeW = NanoBrand.Cube != null ? cubeH * 57f / 64f : 0f;

            float w = pad + cubeW + (cubeW > 0f ? 9f : 0f) + wordW + 12f + 1f + 12f + metaW + (showKey ? 12f + keyW : 0f) + pad;
            float x = 16f;
            float y = Screen.height - h - 16f;
            var r = new Rect(x, y, w, h);

            int prevDepth = GUI.depth;
            GUI.depth = -4900; // Just below modal (-4980) and cursor (-5000)

            bool hover = !blocked && r.Contains(Event.current.mousePosition);
            float hoverT = NanoMotion.SpringTrack("aether:watermark_hover", hover ? 1f : 0f, 16f);

            using (NanoMotion.BeginSlide(enter, 0f, 8f))
            {
                if (Event.current.type == EventType.Repaint)
                {
                    // Floating smoked-crystal squircle card (glass only on floating layers)
                    NanoSurface.DrawDropShadow(r, radius, 0.72f * enter);
                    Color topFill = Color.Lerp(new Color(0.088f, 0.072f, 0.058f, 0.86f), new Color(0.125f, 0.098f, 0.076f, 0.94f), hoverT);
                    Color botFill = Color.Lerp(new Color(0.052f, 0.042f, 0.034f, 0.90f), new Color(0.078f, 0.062f, 0.048f, 0.96f), hoverT);
                    NanoSurface.FillRoundedGradient(r, topFill, botFill, radius);

                    // Subtle top specular catchlight
                    NanoSurface.FillRounded(new Rect(r.x + 10f, r.y + 1f, Mathf.Max(8f, r.width - 20f), 1f),
                        new Color(1f, 0.96f, 0.90f, Mathf.Lerp(0.09f, 0.18f, hoverT)), 0.5f);

                    Color border = Color.Lerp(NanoBrand.Line, NanoBrand.Ember, hoverT * 0.85f);
                    NanoSurface.StrokeRounded(r, border, 1f, radius);

                    float cx = x + pad;
                    if (cubeW > 0f)
                    {
                        NanoBrand.DrawCube(cx, y + (h - cubeH) * 0.5f, cubeH);
                        cx += cubeW + 9f;
                    }

                    Color wordColor = Color.Lerp(NanoBrand.Paper, NanoBrand.EmberSoft, hoverT);
                    NanoSurface.DrawCrispLabel(new Rect(cx, y + 1f, wordW + 4f, h - 2f), word, _word!, wordColor, 0.35f);
                    cx += wordW + 12f;

                    NanoSurface.FillRounded(new Rect(cx, y + 11f, 1f, h - 22f), NanoBrand.Line, 0.5f);
                    cx += 13f;

                    Color metaColor = Color.Lerp(NanoBrand.Mute, NanoBrand.Paper, hoverT * 0.5f);
                    NanoSurface.DrawCrispLabel(new Rect(cx, y, metaW + 4f, h), meta, _meta!, metaColor, 0.25f);
                    cx += metaW + 12f;

                    if (showKey)
                    {
                        var kr = new Rect(cx, y + 8f, keyW, h - 16f);
                        Color kBg = Color.Lerp(new Color(0.135f, 0.106f, 0.082f, 0.94f), new Color(0.22f, 0.145f, 0.095f, 0.98f), hoverT);
                        Color kBorder = Color.Lerp(NanoBrand.EmberDeep, NanoBrand.Ember, hoverT);
                        Color kText = Color.Lerp(NanoBrand.EmberSoft, NanoBrand.PaperLight, hoverT);
                        NanoSurface.DrawChip(kr, key, _key!, kBg, kBorder, kText, 6f);
                    }
                }

                // The whole seal opens Aether's menu.
                if (!blocked && GUI.Button(r, GUIContent.none, GUIStyle.none))
                    BlockStorySuitePlugin.Instance?.SetModMenuOpenState(true);
            }

            GUI.depth = prevDepth;
        }

        private static void InitStyles()
        {
            if (_word != null) return;
            _word = NanoBrand.Style(NanoBrand.Display, 20, NanoBrand.Paper);
            _meta = NanoBrand.Style(NanoBrand.Mono, 11, NanoBrand.Mute);
            _key = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.EmberSoft, TextAnchor.MiddleCenter);
        }
    }
}
