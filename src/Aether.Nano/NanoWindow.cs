using UnityEngine;

namespace Aether.Nano
{
    /// <summary>
    /// Nano's own settings window, in Aether's Ink squircle style. When Aether is not installed it opens
    /// from a "Mods" seal Nano adds to the main menu.
    /// </summary>
    public static class NanoWindow
    {
        private static NanoPage _page;
        private static bool _open;
        private static Vector2 _scroll, _listScroll;
        private static GUIStyle _titleStyle, _metaStyle, _rowStyle, _rowSubStyle;

        public static bool IsOpen => _open;

        public static void Open(NanoPage page)
        {
            _page = page ?? (NanoPages.All.Count > 0 ? NanoPages.All[0] : null);
            _open = _page != null;
            _scroll = Vector2.zero;
        }

        public static void Close() => _open = false;

        /// <summary>The standalone button: only in the main menu, only when Aether's menu is not there.</summary>
        private static bool ShowOwnButton => !_open && Nano.InMainMenu && NanoPages.All.Count > 0 && !Nano.AetherPresent;

        private static void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = NanoBrand.Style(NanoBrand.Display, 21, NanoBrand.Paper);
            _metaStyle = NanoBrand.Style(NanoBrand.Mono, 11, NanoBrand.Mute);
            _rowStyle = NanoBrand.Style(NanoBrand.BodySemi, 13, NanoBrand.Paper);
            _rowSubStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Mute);
        }

        internal static void OnGUI()
        {
            int depth = GUI.depth;
            GUI.depth = -4990;
            try
            {
                if (ShowOwnButton)
                {
                    var r = new Rect(14f, Screen.height - 14f - 34f, 146f, 34f);
                    if (NanoSurface.DrawActionButton(r, Nano.Tr("Mods", "Mods") + "  ·  " + NanoPages.All.Count, false))
                        Open(null);
                }
                float anim = NanoMotion.Sample("aether:nano_window", _open);
                if (anim > 0.005f) DrawWindow(anim);
            }
            finally { GUI.depth = depth; }
        }

        private static void DrawWindow(float anim)
        {
            if (_open && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) { Close(); Event.current.Use(); return; }
            if (_open)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            EnsureStyles();

            // Smooth fading scrim backdrop
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), NanoUi.ColorTexture(new Color(0.02f, 0.015f, 0.012f, 0.72f * anim)));

            float w = Mathf.Min(920f, Screen.width - 40f), h = Mathf.Min(640f, Screen.height - 40f);
            var win = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

            using (NanoMotion.BeginModal(win, anim))
            {
            NanoSurface.DrawCardSurface(
                win,
                new Color(0.090f, 0.072f, 0.056f, 0.97f),
                new Color(0.047f, 0.039f, 0.031f, 0.98f),
                NanoBrand.Line,
                NanoBrand.Ember,
                new Color(0.98f, 0.93f, 0.85f, 0.11f),
                14f,
                1f,
                0f,
                0f);

            GUI.BeginGroup(win);
            try
            {
                // Top bar
                float cx = 18f;
                if (_page != null && _page.Icon != null)
                {
                    NanoIcons.Draw(new Rect(cx, 12f, 26f, 26f), _page.Icon, 6f);
                    cx += 34f;
                }
                else if (NanoBrand.Cube != null)
                {
                    cx += NanoBrand.DrawCube(cx, 15f, 22f) + 10f;
                }
                NanoSurface.DrawCrispLabel(new Rect(cx, 11f, w - cx - 180f, 30f), _page?.Name ?? "Mods", _titleStyle, NanoBrand.Paper, 0.45f);
                if (_page != null && !string.IsNullOrEmpty(_page.Version))
                {
                    string ver = $"v{_page.Version}";
                    NanoSurface.DrawCrispLabel(new Rect(w - 170f, 17f, 112f, 18f), ver, _metaStyle, NanoBrand.Mute, 0.3f);
                }

                if (NanoSurface.DrawActionButton(new Rect(w - 48f, 11f, 32f, 30f), "×", false)) { Close(); return; }
                NanoSurface.FillRounded(new Rect(14f, 50f, w - 28f, 1f), NanoBrand.Line, 0.5f);

                float contentY = 60f;
                float contentH = h - 74f;
                float listW = NanoPages.All.Count > 1 ? 228f : 0f;

                if (listW > 0f)
                {
                    var listBg = new Rect(14f, contentY, listW, contentH);
                    NanoSurface.DrawDebossedSlot(listBg, new Color(0.040f, 0.032f, 0.026f, 0.90f), NanoBrand.Line, 10f);

                    var listArea = new Rect(listBg.x + 6f, listBg.y + 6f, listW - 12f, contentH - 12f);
                    _listScroll = NanoSurface.BeginScrollView(listArea, _listScroll, NanoPages.All.Count * 50f, out float itemW);
                    for (int i = 0; i < NanoPages.All.Count; i++)
                    {
                        NanoPage p = NanoPages.All[i];
                        var row = new Rect(0f, i * 50f, itemW, 44f);
                        bool active = p == _page;
                        bool hover = row.Contains(Event.current.mousePosition);
                        if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { _page = p; _scroll = Vector2.zero; }

                        if (Event.current.type == EventType.Repaint)
                        {
                            Color bgTop = active ? new Color(0.145f, 0.114f, 0.090f, 0.98f) : (hover ? new Color(0.11f, 0.088f, 0.070f, 0.92f) : new Color(0.075f, 0.060f, 0.048f, 0.78f));
                            Color bgBot = active ? new Color(0.095f, 0.075f, 0.058f, 0.98f) : bgTop;
                            NanoSurface.FillRoundedGradient(row, bgTop, bgBot, 8f);
                            NanoSurface.StrokeRounded(row, active ? new Color(NanoBrand.Ember.r, NanoBrand.Ember.g, NanoBrand.Ember.b, 0.55f) : NanoBrand.Line, 1f, 8f);

                            float tx = row.x + 8f;
                            if (p.Icon != null)
                            {
                                var iconSlot = new Rect(tx, row.y + 7f, 30f, 30f);
                                NanoSurface.DrawDebossedSlot(iconSlot, new Color(0.045f, 0.036f, 0.028f, 0.92f), NanoBrand.Line, 6f);
                                NanoIcons.Draw(new Rect(iconSlot.x + 3f, iconSlot.y + 3f, 24f, 24f), p.Icon, 4f);
                                tx += 36f;
                            }
                            else
                            {
                                tx += 3f;
                            }
                            float nameX = tx;
                            if (active)
                            {
                                NanoSurface.DrawRhombus(new Rect(nameX, row.y + 11f, 7f, 7f), NanoBrand.Ember, true);
                                nameX += 11f;
                            }
                            NanoSurface.DrawCrispLabel(new Rect(nameX, row.y + 5f, row.xMax - nameX - 8f, 18f), p.Name, _rowStyle, active ? NanoBrand.PaperLight : NanoBrand.Paper, 0.35f);
                            NanoSurface.DrawCrispLabel(new Rect(tx, row.y + 24f, row.xMax - tx - 8f, 14f),
                                string.IsNullOrEmpty(p.Version) ? "Nano" : $"v{p.Version}", _rowSubStyle, NanoBrand.Mute, 0.25f);
                        }
                    }
                    NanoSurface.EndScrollView();
                }

                if (_page != null)
                {
                    float areaX = listW > 0f ? (22f + listW) : 14f;
                    var areaBg = new Rect(areaX, contentY, w - areaX - 14f, contentH);
                    NanoSurface.DrawDebossedSlot(areaBg, new Color(0.042f, 0.034f, 0.028f, 0.90f), NanoBrand.Line, 10f);
                    var area = new Rect(areaBg.x + 4f, areaBg.y + 4f, areaBg.width - 8f, areaBg.height - 8f);
                    _scroll = NanoPages.DrawScrolled(_page, area, _scroll);
                }
            }
            finally { GUI.EndGroup(); }
            }
        }
    }
}
