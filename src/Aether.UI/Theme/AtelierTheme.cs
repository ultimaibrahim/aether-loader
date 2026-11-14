using UnityEngine;

namespace Aether.UI.Theme
{
    /// <summary>
    /// Colour tokens for Aether's own overlays (crash guard and the like), now Aether's brand: warm ink, ember, paper.
    /// The names are kept so callers do not change; the values are <see cref="Aether.Nano.NanoBrand"/>'s.
    /// </summary>
    public static class AtelierTheme
    {
        // Colors (brand: same as the website's dark theme)
        public static readonly Color DeepVoid = new(0.047f, 0.039f, 0.031f, 1.0f);         // #0C0A08 ink
        public static readonly Color ObsidianGlass = new(0.047f, 0.039f, 0.031f, 0.94f);    // #0C0A08 ink, 94%
        public static readonly Color EtherealSurface = new(0.110f, 0.086f, 0.067f, 0.97f);  // #1C1611 ink soft
        public static readonly Color CelestialLavender = new(1.000f, 0.541f, 0.239f, 1.0f); // #FF8A3D ember (accent)
        public static readonly Color LavenderLight = new(1.000f, 0.698f, 0.478f, 1.0f);     // #FFB27A ember, light (hover)
        public static readonly Color StarlightCyan = new(0.910f, 0.663f, 0.235f, 1.0f);     // #E8A93C warn (culprit)
        public static readonly Color PureCrystal = new(0.918f, 0.875f, 0.776f, 1.0f);       // #EADFC6 paper
        public static readonly Color SubduedText = new(0.659f, 0.608f, 0.514f, 1.0f);       // #A89B83 mute

        // Geometry
        public const int SquircleRadius = 14;
        public const int ModalDepth = -5000; // Guaranteed to render above IMGUI and NGUI modals

        // Cached solid textures
        private static Texture2D? _obsidianTex;
        private static Texture2D? _surfaceTex;
        private static Texture2D? _lavenderTex;

        public static Texture2D ObsidianTexture => _obsidianTex ??= CreateColorTexture(ObsidianGlass);
        public static Texture2D SurfaceTexture => _surfaceTex ??= CreateColorTexture(EtherealSurface);
        public static Texture2D LavenderTexture => _lavenderTex ??= CreateColorTexture(CelestialLavender);

        public static Texture2D CreateColorTexture(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
