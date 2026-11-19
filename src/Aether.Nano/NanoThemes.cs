using UnityEngine;

namespace Aether.Nano
{
    /// <summary>One visual theme of the wooden UI: frame sprite and tints, button tints and text colors.</summary>
    public struct NanoTheme
    {
        public string Id;
        public string NameEn;
        public string NameEs;
        public string FrameSprite;
        public Color FrameTint;
        public Color BorderColor;
        public Color InnerBgColor;
        public Color ButtonNormalTint;
        public Color ButtonSelectedTint;
        public Color ButtonHoverTint;
        public Color TitleColor;
        public Color AccentColor;
        public Color TextColor;
        public Color ShadowColor;
    }

    /// <summary>The UI themes shared by Aether and the mods on Nano (NativeWood, AncientStone, DwarvenGold, ArcaneGlass...).</summary>
    public static class NanoThemes
    {
        /// <summary>The theme used when a mod asks for none: Aether Ink, or the player's choice in Aether's HUD settings.</summary>
        public static string Default { get; set; } = "Aether";

        public static NanoTheme Get(string themeName = null)
        {
            string th = string.IsNullOrEmpty(themeName) ? Default : themeName;

            switch (th)
            {
                case "AncientStone":
                    return new NanoTheme
                    {
                        Id = "AncientStone",
                        NameEn = "Ancient Stone Slate",
                        NameEs = "Pizarra de Piedra Antigua",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.68f, 0.72f, 0.78f, 1.0f),
                        BorderColor = new Color(0.18f, 0.22f, 0.28f, 0.98f),
                        InnerBgColor = new Color(0.07f, 0.08f, 0.11f, 0.75f),
                        ButtonNormalTint = new Color(0.55f, 0.60f, 0.68f, 0.95f),
                        ButtonSelectedTint = new Color(0.35f, 0.88f, 1.0f, 1.0f), // Glowing runic cyan
                        ButtonHoverTint = new Color(0.75f, 0.82f, 0.92f, 1.0f),
                        TitleColor = new Color(0.40f, 0.85f, 1.0f),
                        AccentColor = new Color(0.75f, 0.90f, 1.0f),
                        TextColor = new Color(0.90f, 0.93f, 0.96f),
                        ShadowColor = new Color(0.02f, 0.03f, 0.05f, 0.95f)
                    };

                case "DwarvenGold":
                    return new NanoTheme
                    {
                        Id = "DwarvenGold",
                        NameEn = "Dwarven Gold & Bronze",
                        NameEs = "Oro y Bronce Enano",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.96f, 0.82f, 0.35f, 1.0f),
                        BorderColor = new Color(0.35f, 0.24f, 0.08f, 0.98f),
                        InnerBgColor = new Color(0.10f, 0.07f, 0.03f, 0.75f),
                        ButtonNormalTint = new Color(0.85f, 0.72f, 0.35f, 0.95f),
                        ButtonSelectedTint = new Color(1.0f, 0.88f, 0.20f, 1.0f), // Radiant treasure gold
                        ButtonHoverTint = new Color(1.0f, 0.95f, 0.65f, 1.0f),
                        TitleColor = new Color(1.0f, 0.88f, 0.25f),
                        AccentColor = new Color(1.0f, 0.70f, 0.20f),
                        TextColor = new Color(0.98f, 0.95f, 0.86f),
                        ShadowColor = new Color(0.12f, 0.08f, 0.02f, 0.95f)
                    };

                case "ArcaneGlass":
                    return new NanoTheme
                    {
                        Id = "ArcaneGlass",
                        NameEn = "Arcane Crystal Glass",
                        NameEs = "Cristal Arcano",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.65f, 0.75f, 1.0f, 0.75f),
                        BorderColor = new Color(0.50f, 0.40f, 0.85f, 0.85f),
                        InnerBgColor = new Color(0.06f, 0.06f, 0.14f, 0.68f),
                        ButtonNormalTint = new Color(0.60f, 0.65f, 0.90f, 0.85f),
                        ButtonSelectedTint = new Color(0.75f, 0.50f, 1.0f, 1.0f), // Arcane mana violet
                        ButtonHoverTint = new Color(0.85f, 0.88f, 1.0f, 0.95f),
                        TitleColor = new Color(0.65f, 0.85f, 1.0f),
                        AccentColor = new Color(0.85f, 0.60f, 1.0f),
                        TextColor = new Color(0.92f, 0.94f, 1.0f),
                        ShadowColor = new Color(0.04f, 0.02f, 0.10f, 0.95f)
                    };

                case "DarkObsidian":
                    return new NanoTheme
                    {
                        Id = "DarkObsidian",
                        NameEn = "Dark Obsidian",
                        NameEs = "Obsidiana",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.26f, 0.22f, 0.36f, 1.0f),
                        BorderColor = new Color(0.26f, 0.21f, 0.35f, 0.95f),
                        InnerBgColor = new Color(0.055f, 0.045f, 0.078f, 0.92f),
                        ButtonNormalTint = new Color(0.20f, 0.17f, 0.28f, 0.95f),
                        ButtonSelectedTint = new Color(0.62f, 0.51f, 0.77f, 1.0f), // Amethyst gem
                        ButtonHoverTint = new Color(0.34f, 0.29f, 0.46f, 1.0f),
                        TitleColor = new Color(0.93f, 0.91f, 0.95f),
                        AccentColor = new Color(0.62f, 0.51f, 0.77f),
                        TextColor = new Color(0.93f, 0.91f, 0.95f),
                        ShadowColor = new Color(0.015f, 0.01f, 0.03f, 0.92f)
                    };

                case "MinimalistGlass":
                    return new NanoTheme
                    {
                        Id = "MinimalistGlass",
                        NameEn = "Smoked Glass",
                        NameEs = "Cristal",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.55f, 0.66f, 0.74f, 0.80f),
                        BorderColor = new Color(0.48f, 0.58f, 0.66f, 0.48f),
                        InnerBgColor = new Color(0.047f, 0.063f, 0.078f, 0.72f),
                        ButtonNormalTint = new Color(0.24f, 0.31f, 0.36f, 0.85f),
                        ButtonSelectedTint = new Color(0.62f, 0.77f, 0.83f, 1.0f),
                        ButtonHoverTint = new Color(0.38f, 0.47f, 0.54f, 0.92f),
                        TitleColor = new Color(0.93f, 0.96f, 0.97f),
                        AccentColor = new Color(0.62f, 0.77f, 0.83f),
                        TextColor = new Color(0.93f, 0.96f, 0.97f),
                        ShadowColor = new Color(0.01f, 0.02f, 0.03f, 0.85f)
                    };

                case "RoyalGold":
                    return new NanoTheme
                    {
                        Id = "RoyalGold",
                        NameEn = "Antique Gold",
                        NameEs = "Oro Antiguo",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.86f, 0.68f, 0.28f, 1.0f),
                        BorderColor = new Color(0.44f, 0.33f, 0.14f, 0.95f),
                        InnerBgColor = new Color(0.075f, 0.055f, 0.028f, 0.93f),
                        ButtonNormalTint = new Color(0.32f, 0.24f, 0.11f, 0.95f),
                        ButtonSelectedTint = new Color(0.83f, 0.60f, 0.23f, 1.0f), // Amber/Gold gem
                        ButtonHoverTint = new Color(0.52f, 0.39f, 0.18f, 1.0f),
                        TitleColor = new Color(0.96f, 0.92f, 0.84f),
                        AccentColor = new Color(0.83f, 0.60f, 0.23f),
                        TextColor = new Color(0.96f, 0.92f, 0.84f),
                        ShadowColor = new Color(0.04f, 0.025f, 0.01f, 0.92f)
                    };

                case "CompactBar":
                    return new NanoTheme
                    {
                        Id = "CompactBar",
                        NameEn = "Visor Strip",
                        NameEs = "Cinta",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.20f, 0.16f, 0.12f, 0.92f),
                        BorderColor = new Color(0.18f, 0.14f, 0.11f, 0.95f),
                        InnerBgColor = new Color(0.047f, 0.039f, 0.031f, 0.90f),
                        ButtonNormalTint = new Color(0.16f, 0.12f, 0.09f, 0.92f),
                        ButtonSelectedTint = NanoBrand.Ember,
                        ButtonHoverTint = new Color(0.24f, 0.19f, 0.15f, 1.0f),
                        TitleColor = NanoBrand.Paper,
                        AccentColor = NanoBrand.Ember,
                        TextColor = NanoBrand.Paper,
                        ShadowColor = new Color(0.015f, 0.01f, 0.008f, 0.90f)
                    };

                case "NativeWood":
                    return new NanoTheme
                    {
                        Id = "NativeWood",
                        NameEn = "Carved Timber",
                        NameEs = "Madera",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(1.0f, 0.96f, 0.90f, 1.0f),
                        BorderColor = new Color(0.36f, 0.25f, 0.16f, 0.96f),
                        InnerBgColor = new Color(0.105f, 0.068f, 0.042f, 0.91f),
                        ButtonNormalTint = new Color(0.88f, 0.80f, 0.70f, 0.95f),
                        ButtonSelectedTint = new Color(0.89f, 0.66f, 0.29f, 1.0f),
                        ButtonHoverTint = new Color(0.96f, 0.92f, 0.80f, 1.0f),
                        TitleColor = new Color(0.95f, 0.91f, 0.84f),
                        AccentColor = new Color(0.89f, 0.66f, 0.29f),
                        TextColor = new Color(0.95f, 0.91f, 0.84f),
                        ShadowColor = new Color(0.03f, 0.02f, 0.01f, 0.92f)
                    };

                case "Aether":
                default:
                    return new NanoTheme
                    {
                        Id = "Aether",
                        NameEn = "Aether Ink",
                        NameEs = "Tinta Aether",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.22f, 0.17f, 0.13f, 1.0f),
                        BorderColor = new Color(0.18f, 0.14f, 0.11f, 0.96f),
                        InnerBgColor = new Color(0.047f, 0.039f, 0.031f, 0.92f),
                        ButtonNormalTint = new Color(0.14f, 0.11f, 0.09f, 0.96f),
                        ButtonSelectedTint = NanoBrand.Ember,
                        ButtonHoverTint = new Color(0.24f, 0.19f, 0.15f, 1.0f),
                        TitleColor = NanoBrand.Paper,
                        AccentColor = NanoBrand.Ember,
                        TextColor = NanoBrand.Paper,
                        ShadowColor = new Color(0.02f, 0.015f, 0.01f, 0.92f)
                    };
            }
        }
    }
}
