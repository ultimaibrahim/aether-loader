using System.Collections.Generic;
using UnityEngine;

namespace Aether.Nano
{
    /// <summary>
    /// IMGUI drawing in the game's own look: sprites from its GUI atlas (wood panels and buttons), its Zantroke font,
    /// and the shared text styles. Call these from OnGUI only.
    /// </summary>
    public static class NanoUi
    {
        private static UIAtlas _atlas;
        private static readonly Dictionary<Color, Texture2D> ColorTextures = new Dictionary<Color, Texture2D>();

        /// <summary>The game's GUI atlas (the one with "WoodPanel"), or null before the menus exist.</summary>
        public static UIAtlas GuiAtlas
        {
            get
            {
                if (_atlas != null && _atlas.texture != null) return _atlas;
                try
                {
                    foreach (UIAtlas a in Resources.FindObjectsOfTypeAll<UIAtlas>())
                        if (a != null && a.texture != null && (a.name == "GUI" || a.GetSprite("WoodPanel") != null)) { _atlas = a; break; }
                }
                catch { }
                return _atlas;
            }
        }

        /// <summary>Draws a sprite of the game's GUI atlas ("WoodPanel", "Button", "InvSlot"...). False when it is missing.</summary>
        public static bool DrawNativeSprite(Rect rect, string spriteName, Color? tint = null)
        {
            UIAtlas atlas = GuiAtlas;
            if (atlas == null || atlas.texture == null) return false;
            UIAtlas.Sprite sprite = atlas.GetSprite(spriteName);
            if (sprite == null) return false;
            Texture tex = atlas.texture;
            Rect uv = sprite.outer;
            if (atlas.coordinates == UIAtlas.Coordinates.Pixels) uv = NGUIMath.ConvertToTexCoords(uv, tex.width, tex.height);
            Color old = GUI.color;
            try
            {
                if (tint.HasValue) GUI.color = tint.Value;
                GUI.DrawTextureWithTexCoords(rect, tex, uv);
            }
            finally { GUI.color = old; }
            return true;
        }

        public static Texture2D ColorTexture(Color c)
        {
            if (ColorTextures.TryGetValue(c, out Texture2D t) && t != null) return t;
            t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            ColorTextures[c] = t;
            return t;
        }

        /// <summary>Same as <see cref="ColorTexture"/>; the name Aether's own UI code uses.</summary>
        public static Texture2D GetColorTexture(Color c) => ColorTexture(c);

        public static void DrawFallbackBox(Rect rect, Color bg, Color border)
        {
            GUI.DrawTexture(rect, ColorTexture(border));
            GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), ColorTexture(bg));
        }

        public static void DrawShadowLabel(Rect rect, string text, GUIStyle style, Color textColor, Color shadowColor)
        {
            if (string.IsNullOrEmpty(text) || style == null) return;
            Color old = style.normal.textColor;
            style.normal.textColor = shadowColor;
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
            style.normal.textColor = textColor;
            GUI.Label(rect, text, style);
            style.normal.textColor = old;
        }

        /// <summary>The game's wooden button; green while hovered or selected. Returns true when clicked.</summary>
        public static bool DrawNativeWoodButton(Rect rect, string text, bool isSelected = false, Color? baseTint = null)
        {
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                bool lit = isSelected || rect.Contains(Event.current.mousePosition);
                Color tint = lit ? new Color(0.65f, 1.0f, 0.28f, 1.0f) : (baseTint ?? Color.white);
                if (!DrawNativeSprite(rect, "Button", tint))
                    DrawFallbackBox(rect, lit ? new Color(0.35f, 0.58f, 0.18f, 0.96f) : new Color(0.48f, 0.32f, 0.16f, 0.96f), new Color(0.20f, 0.12f, 0.05f, 1f));
                DrawShadowLabel(rect, text, NanoStyles.WoodButtonText, lit ? new Color(1f, 1f, 0.85f, 1f) : Color.white, Color.black);
            }
            return clicked;
        }

        /// <summary>A panel framed in a UI theme (<see cref="NanoThemes"/>; null = the player's theme).</summary>
        public static void DrawThemedFrame(Rect rect, string themeName = null, float opacity = 1f)
        {
            NanoTheme p = NanoThemes.Get(themeName);
            GUI.DrawTexture(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4),
                ColorTexture(new Color(p.BorderColor.r, p.BorderColor.g, p.BorderColor.b, p.BorderColor.a * opacity)));
            if (!DrawNativeSprite(rect, p.FrameSprite, new Color(p.FrameTint.r, p.FrameTint.g, p.FrameTint.b, p.FrameTint.a * opacity)))
                GUI.DrawTexture(rect, ColorTexture(new Color(p.InnerBgColor.r, p.InnerBgColor.g, p.InnerBgColor.b, opacity * 0.94f)));
        }

        /// <summary>An inventory slot of the game.</summary>
        public static void DrawInvSlot(Rect rect, Color? tint = null)
        {
            if (!DrawNativeSprite(rect, "InvSlot", tint ?? Color.white))
                DrawFallbackBox(rect, new Color(0.14f, 0.10f, 0.07f, 0.90f), new Color(0.45f, 0.32f, 0.20f, 0.95f));
        }

        /// <summary>A dark square slot; green when selected.</summary>
        public static void DrawDarkSlotBox(Rect rect, bool selected = false, bool hovered = false)
        {
            Color tint = selected ? new Color(0.65f, 1.0f, 0.35f, 1.0f) : (hovered ? new Color(0.85f, 0.95f, 0.65f, 0.96f) : new Color(0.88f, 0.80f, 0.70f, 0.94f));
            if (!DrawNativeSprite(rect, "SetButtonSqareSl", tint) && !DrawNativeSprite(rect, "InvSlot", tint))
                DrawFallbackBox(rect, selected ? new Color(0.28f, 0.45f, 0.18f, 0.95f) : new Color(0.16f, 0.11f, 0.07f, 0.90f), new Color(0.48f, 0.35f, 0.22f, 0.95f));
        }

        /// <summary>A panel in the player's UI theme (the game's wood by default); a tint draws plain wood in that tint.</summary>
        public static void DrawWoodPanel(Rect rect, float opacity = 1f, Color? tintOverride = null)
        {
            if (tintOverride == null) { DrawThemedFrame(rect, null, opacity); return; }
            DrawPlainWoodPanel(rect, opacity, tintOverride.Value);
        }

        private static void DrawPlainWoodPanel(Rect rect, float opacity, Color tint)
        {
            GUI.DrawTexture(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), ColorTexture(new Color(0.10f, 0.06f, 0.03f, opacity * 0.95f)));
            if (!DrawNativeSprite(rect, "WoodPanel", tint))
                DrawFallbackBox(rect, new Color(0.28f, 0.18f, 0.10f, opacity), new Color(0.52f, 0.36f, 0.20f, opacity));
        }
    }

    /// <summary>Text styles shared by Nano mods (the same ones Aether's menu uses), in the game's Zantroke font once it is loaded.</summary>
    public static class NanoStyles
    {
        private static GUIStyle _title, _tool, _desc, _section, _woodText, _small, _author, _menuHeader, _menuTitle, _modName, _badge, _mapLabel, _field;
        private static Font _font;
        private static float _nextFontSearch;

        // Like Aether's own menu: only titles, tool lines and author lines use the game's font; the rest keeps Unity's default,
        // which is narrower (menu headers and tab labels are sized for it).
        public static GUIStyle Title => Make(ref _title, 15, FontStyle.Bold, TextAnchor.MiddleLeft, false, Color.white, true);
        public static GUIStyle Tool => Make(ref _tool, 12, FontStyle.Bold, TextAnchor.MiddleLeft, false, new Color(0.95f, 0.92f, 0.82f), true);
        public static GUIStyle Author => Make(ref _author, 11, FontStyle.Italic, TextAnchor.MiddleLeft, false, new Color(0.88f, 0.82f, 0.72f), true);
        public static GUIStyle Desc => Make(ref _desc, 13, FontStyle.Normal, TextAnchor.UpperLeft, true, new Color(0.88f, 0.86f, 0.80f));
        public static GUIStyle SectionTitle => Make(ref _section, 13, FontStyle.Bold, TextAnchor.MiddleLeft, false, new Color(1.0f, 0.85f, 0.25f));
        public static GUIStyle Badge => Make(ref _badge, 10, FontStyle.Bold, TextAnchor.MiddleCenter, false, Color.white);
        public static GUIStyle MinimapLabel => Make(ref _mapLabel, 11, FontStyle.Bold, TextAnchor.MiddleCenter, false, Color.white);
        /// <summary>A text field (search boxes, waypoint names).</summary>
        public static GUIStyle TextField
        {
            get
            {
                if (_field == null)
                {
                    _field = new GUIStyle(GUI.skin.textField) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                    _field.normal.textColor = Color.white;
                }
                return _field;
            }
        }
        public static GUIStyle WoodButtonText => Make(ref _woodText, 14, FontStyle.Bold, TextAnchor.MiddleCenter, false, Color.white);
        public static GUIStyle MenuHeader => Make(ref _menuHeader, 19, FontStyle.Bold, TextAnchor.MiddleCenter, false, new Color(0.55f, 1.0f, 0.25f));
        public static GUIStyle MenuTitle => Make(ref _menuTitle, 18, FontStyle.Bold, TextAnchor.MiddleLeft, false, new Color(0.65f, 1.0f, 0.30f));
        public static GUIStyle ModName => Make(ref _modName, 13, FontStyle.Bold, TextAnchor.MiddleLeft, false, Color.white);
        public static GUIStyle Small => Make(ref _small, 11, FontStyle.Bold, TextAnchor.MiddleCenter, false, Color.white);

        /// <summary>Zantroke, the serif font of the game's HUD and menus, or null before it is loaded.</summary>
        public static Font NativeFont
        {
            get
            {
                if (_font != null || Time.unscaledTime < _nextFontSearch) return _font;
                _nextFontSearch = Time.unscaledTime + 2f; // the font loads with the menus: look again every 2 s until found
                try
                {
                    // Zantroke is the serif font of the game's HUD and menus; BlockStoryInfinity only has a few glyphs.
                    Font fallback = null;
                    foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
                    {
                        if (f == null) continue;
                        if (string.Equals(f.name, "Zantroke", System.StringComparison.OrdinalIgnoreCase)) { _font = f; break; }
                        if (string.Equals(f.name, "BlockStoryInfinity", System.StringComparison.OrdinalIgnoreCase)) fallback = f;
                    }
                    if (_font == null)
                        foreach (UILabel l in Resources.FindObjectsOfTypeAll<UILabel>())
                        {
                            UIFont uf = l != null ? l.font : null;
                            if (uf != null && uf.isDynamic && uf.dynamicFont != null) { _font = uf.dynamicFont; break; }
                        }
                    if (_font == null && (Nano.InGame || Nano.InMainMenu)) _font = fallback;
                }
                catch { }
                return _font;
            }
        }

        private static GUIStyle Make(ref GUIStyle style, int size, FontStyle fontStyle, TextAnchor anchor, bool wrap, Color color, bool nativeFont = false)
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fontStyle, alignment = anchor, wordWrap = wrap, richText = true };
                style.normal.textColor = color;
            }
            if (nativeFont && style.font == null && NativeFont != null) style.font = NativeFont;
            return style;
        }
    }
}
