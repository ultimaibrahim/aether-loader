using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace Aether.Nano
{
    /// <summary>
    /// Aether's own look inside the game, the same as its website: ember on warm ink, paper text, Gloock for names,
    /// Schibsted Grotesk for text and JetBrains Mono for versions and keys, plus the open cube. The game's wood panels stay
    /// the game's; this is for what is Aether's (its badge, notices, headers). Fonts load on first use, so a mod that never
    /// asks for one pays nothing. Every font can be null (the file could not be written or read): styles then keep Unity's.
    /// </summary>
    public static class NanoBrand
    {
        // ---- Palette (the website's dark theme, since it is drawn over the game) ------------------------------------------
        public static readonly Color Ember = Hex(0xFF8A3D);      // accent: names, active states
        public static readonly Color EmberDeep = Hex(0xC2500A);  // the brand colour on light ground, chip fills
        public static readonly Color Ink = Hex(0x0C0A08, 0.94f); // card ground
        public static readonly Color InkSoft = Hex(0x1C1611);    // chips, hover
        public static readonly Color Line = Hex(0x2A2119);       // hairlines
        public static readonly Color Paper = Hex(0xEADFC6);      // text on ink
        public static readonly Color PaperLight = Hex(0xFBFAF8); // ground for paper chips
        public static readonly Color Mute = Hex(0xA89B83);       // secondary text
        public static readonly Color Ok = Hex(0x6FCF97);
        public static readonly Color Warn = Hex(0xE8A93C);
        public static readonly Color Bad = Hex(0xF06E6E);

        // ---- Fonts --------------------------------------------------------------------------------------------------------
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        private static readonly HashSet<string> Failed = new HashSet<string>();

        /// <summary>Gloock: mod names, titles, the "aether" wordmark.</summary>
        public static Font Display => Load("Gloock-Regular.ttf");
        /// <summary>Schibsted Grotesk Regular: body text.</summary>
        public static Font Body => Load("SchibstedGrotesk-Regular.ttf");
        /// <summary>Schibsted Grotesk SemiBold: labels, buttons, chips.</summary>
        public static Font BodySemi => Load("SchibstedGrotesk-SemiBold.ttf");
        /// <summary>JetBrains Mono: versions, keys, paths.</summary>
        public static Font Mono => Load("JetBrainsMono-Regular.ttf");

        private static Font Load(string file)
        {
            if (Fonts.TryGetValue(file, out Font cached) && cached != null) return cached;
            if (Failed.Contains(file)) return null;
            try
            {
                // Unity reads a dynamic font from a file path, so the embedded font is written once to BepInEx's cache.
                string path = Extract(file, Path.Combine(Paths.CachePath, "aether-nano", "fonts"));
                var font = new Font(path) { name = Path.GetFileNameWithoutExtension(file) };
                font.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Fonts[file] = font;
                return font;
            }
            catch (Exception ex)
            {
                Failed.Add(file);
                Nano.Log.LogWarning($"Brand font {file} not available, using Unity's: {ex.Message}");
                return null;
            }
        }

        private static string Extract(string file, string dir)
        {
            using Stream s = typeof(NanoBrand).Assembly.GetManifestResourceStream("Aether.Nano.Brand." + file)
                             ?? throw new FileNotFoundException("embedded " + file);
            string path = Path.Combine(dir, Nano.Version + "-" + file); // versioned: a newer Nano never reads an older file
            if (File.Exists(path) && new FileInfo(path).Length == s.Length) return path;
            Directory.CreateDirectory(dir);
            using (FileStream f = File.Create(path)) s.CopyTo(f);
            return path;
        }

        // ---- Cube and solid textures --------------------------------------------------------------------------------------
        private static Texture2D _cube;
        private static bool _cubeFailed;

        /// <summary>Aether's open cube (dark-theme drawing, 57×64), or null if it could not be read.</summary>
        public static Texture2D Cube
        {
            get
            {
                if (_cube != null || _cubeFailed) return _cube;
                try
                {
                    using Stream s = typeof(NanoBrand).Assembly.GetManifestResourceStream("Aether.Nano.Brand.aether-cube.png");
                    var bytes = new byte[s.Length];
                    int read = 0;
                    while (read < bytes.Length) read += s.Read(bytes, read, bytes.Length - read);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontUnloadUnusedAsset };
                    if (!tex.LoadImage(bytes)) throw new InvalidDataException("png");
                    _cube = tex;
                }
                catch (Exception ex) { _cubeFailed = true; Nano.Log.LogWarning("Aether cube not available: " + ex.Message); }
                return _cube;
            }
        }

        private static readonly Dictionary<Color, Texture2D> Solids = new Dictionary<Color, Texture2D>();

        /// <summary>A 1×1 texture of this colour, cached.</summary>
        public static Texture2D Solid(Color c)
        {
            if (Solids.TryGetValue(c, out Texture2D t) && t != null) return t;
            t = new Texture2D(1, 1) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            t.SetPixel(0, 0, c);
            t.Apply();
            Solids[c] = t;
            return t;
        }

        // ---- Drawing helpers ----------------------------------------------------------------------------------------------

        /// <summary>A label style in a brand font (Unity's font when it is null).</summary>
        public static GUIStyle Style(Font font, int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool wrap = false)
        {
            var s = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, alignment = anchor, wordWrap = wrap, richText = true, clipping = TextClipping.Clip };
            s.padding = new RectOffset(0, 0, 0, 0);
            s.normal.textColor = s.hover.textColor = color;
            return s;
        }

        /// <summary>
        /// Aether's card: warm ink with a hairline frame and an ember rule along the top (no side stripe). The rule is the
        /// mark that it is Aether talking, not the game.
        /// </summary>
        public static void DrawCard(Rect r, float ruleAlpha = 1f)
        {
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, Solid(Ink));
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), Solid(Line));
            GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), Solid(Line));
            GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), Solid(Line));
            GUI.color = new Color(1, 1, 1, ruleAlpha);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2), Solid(Ember));
            GUI.color = prev;
        }

        /// <summary>The cube scaled to <paramref name="height"/>, its top-left at (x, y); returns its width.</summary>
        public static float DrawCube(float x, float y, float height)
        {
            Texture2D cube = Cube;
            if (cube == null) return 0f;
            float w = height * cube.width / cube.height;
            GUI.DrawTexture(new Rect(x, y, w, height), cube, ScaleMode.ScaleToFit, true);
            return w;
        }

        private static Color Hex(int rgb, float a = 1f) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    }
}
