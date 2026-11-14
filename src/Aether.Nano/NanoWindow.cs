using UnityEngine;

namespace Aether.Nano
{
    /// <summary>
    /// Nano's own settings window, in the game's wooden style. When Aether is not installed it opens from a "Mods" button
    /// Nano adds to the main menu.
    /// </summary>
    public static class NanoWindow
    {
        private static NanoPage _page;
        private static bool _open;
        private static Vector2 _scroll, _listScroll;

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

        internal static void OnGUI()
        {
            int depth = GUI.depth;
            GUI.depth = -4990;
            try
            {
                if (ShowOwnButton)
                {
                    var r = new Rect(14f, Screen.height - 14f - 34f, 150f, 34f);
                    if (NanoUi.DrawNativeWoodButton(r, Nano.Tr("Mods", "Mods") + " (" + NanoPages.All.Count + ")")) Open(null);
                }
                if (_open) DrawWindow();
            }
            finally { GUI.depth = depth; }
        }

        private static void DrawWindow()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) { Close(); Event.current.Use(); return; }
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            float w = Mathf.Min(920f, Screen.width - 40f), h = Mathf.Min(640f, Screen.height - 40f);
            var win = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            NanoUi.DrawWoodPanel(win);
            GUI.BeginGroup(win);
            try
            {
                NanoUi.DrawShadowLabel(new Rect(18, 10, w - 120, 30), (_page?.Name ?? "Mods").ToUpperInvariant(), NanoStyles.SectionTitle, new Color(0.65f, 1.0f, 0.30f), Color.black);
                if (NanoUi.DrawNativeWoodButton(new Rect(w - 52, 10, 38, 30), "X")) { Close(); return; }

                float listW = NanoPages.All.Count > 1 ? 220f : 0f;
                if (listW > 0)
                {
                    var listArea = new Rect(14, 50, listW, h - 64);
                    _listScroll = GUI.BeginScrollView(listArea, _listScroll, new Rect(0, 0, listW - 18, NanoPages.All.Count * 40f));
                    for (int i = 0; i < NanoPages.All.Count; i++)
                    {
                        NanoPage p = NanoPages.All[i];
                        if (NanoUi.DrawNativeWoodButton(new Rect(0, i * 40f, listW - 22, 34), p.Name, p == _page)) { _page = p; _scroll = Vector2.zero; }
                    }
                    GUI.EndScrollView();
                }
                if (_page != null)
                {
                    var area = new Rect(20 + listW, 50, w - 34 - listW, h - 64);
                    GUI.DrawTexture(area, NanoUi.ColorTexture(new Color(0.08f, 0.05f, 0.03f, 0.55f)));
                    _scroll = NanoPages.DrawScrolled(_page, area, _scroll);
                }
            }
            finally { GUI.EndGroup(); }
        }
    }
}
