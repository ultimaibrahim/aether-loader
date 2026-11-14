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
        /// <summary>The theme used when a mod asks for none: NativeWood, or the player's choice in Aether's HUD settings.</summary>
        public static string Default { get; set; } = "NativeWood";

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
                        NameEs = "Obsidiana Oscura",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.30f, 0.26f, 0.45f, 1.0f),
                        BorderColor = new Color(0.20f, 0.14f, 0.34f, 0.98f),
                        InnerBgColor = new Color(0.035f, 0.03f, 0.06f, 0.92f),
                        ButtonNormalTint = new Color(0.35f, 0.30f, 0.50f, 0.95f),
                        ButtonSelectedTint = new Color(0.55f, 1.0f, 0.35f, 1.0f),
                        ButtonHoverTint = new Color(0.50f, 0.45f, 0.70f, 1.0f),
                        TitleColor = new Color(0.75f, 1.0f, 0.35f),
                        AccentColor = new Color(0.55f, 0.85f, 0.40f),
                        TextColor = new Color(0.92f, 0.94f, 0.96f),
                        ShadowColor = new Color(0f, 0f, 0f, 0.95f)
                    };

                case "MinimalistGlass":
                    return new NanoTheme
                    {
                        Id = "MinimalistGlass",
                        NameEn = "Crystal Glass",
                        NameEs = "Cristal",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.60f, 0.75f, 0.85f, 0.80f),
                        BorderColor = new Color(0.40f, 0.60f, 0.75f, 0.90f),
                        InnerBgColor = new Color(0.03f, 0.05f, 0.07f, 0.78f),
                        ButtonNormalTint = new Color(0.55f, 0.68f, 0.78f, 0.90f),
                        ButtonSelectedTint = new Color(0.40f, 0.90f, 1.0f, 1.0f),
                        ButtonHoverTint = new Color(0.75f, 0.85f, 0.95f, 1.0f),
                        TitleColor = new Color(0.82f, 0.95f, 1.0f),
                        AccentColor = new Color(0.40f, 0.85f, 1.0f),
                        TextColor = new Color(0.92f, 0.95f, 0.98f),
                        ShadowColor = new Color(0f, 0.02f, 0.05f, 0.95f)
                    };

                case "RoyalGold":
                    return new NanoTheme
                    {
                        Id = "RoyalGold",
                        NameEn = "Royal Gold",
                        NameEs = "Oro Real",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.90f, 0.72f, 0.28f, 1.0f),
                        BorderColor = new Color(0.55f, 0.42f, 0.10f, 0.98f),
                        InnerBgColor = new Color(0.06f, 0.04f, 0.02f, 0.94f),
                        ButtonNormalTint = new Color(0.78f, 0.64f, 0.24f, 0.96f),
                        ButtonSelectedTint = new Color(1.0f, 0.84f, 0.25f, 1.0f),
                        ButtonHoverTint = new Color(1.0f, 0.92f, 0.60f, 1.0f),
                        TitleColor = new Color(1.0f, 0.88f, 0.30f),
                        AccentColor = new Color(1.0f, 0.78f, 0.22f),
                        TextColor = new Color(0.98f, 0.95f, 0.86f),
                        ShadowColor = new Color(0.10f, 0.06f, 0.01f, 0.95f)
                    };

                case "CompactBar":
                    return new NanoTheme
                    {
                        Id = "CompactBar",
                        NameEn = "Compact Bar",
                        NameEs = "Barra",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(0.25f, 0.32f, 0.40f, 0.90f),
                        BorderColor = new Color(0.18f, 0.25f, 0.32f, 0.95f),
                        InnerBgColor = new Color(0.02f, 0.03f, 0.05f, 0.90f),
                        ButtonNormalTint = new Color(0.35f, 0.45f, 0.52f, 0.92f),
                        ButtonSelectedTint = new Color(0.40f, 1.0f, 0.40f, 1.0f),
                        ButtonHoverTint = new Color(0.65f, 0.80f, 0.92f, 1.0f),
                        TitleColor = new Color(0.75f, 1.0f, 0.35f),
                        AccentColor = new Color(0.40f, 1.0f, 0.40f),
                        TextColor = new Color(0.92f, 0.95f, 0.97f),
                        ShadowColor = new Color(0f, 0f, 0f, 0.95f)
                    };

                case "NativeWood":
                default:
                    return new NanoTheme
                    {
                        Id = "NativeWood",
                        NameEn = "Native Carved Wood",
                        NameEs = "Madera Tallada Autóctona",
                        FrameSprite = "WoodPanel",
                        FrameTint = new Color(1.0f, 0.96f, 0.90f, 1.0f),
                        BorderColor = new Color(0.12f, 0.08f, 0.04f, 0.95f),
                        InnerBgColor = new Color(0.08f, 0.05f, 0.03f, 0.65f),
                        ButtonNormalTint = new Color(0.88f, 0.80f, 0.70f, 0.95f),
                        ButtonSelectedTint = new Color(0.65f, 1.0f, 0.35f, 1.0f), // Vibrant leaf lime
                        ButtonHoverTint = new Color(0.96f, 0.92f, 0.80f, 1.0f),
                        TitleColor = new Color(0.65f, 1.0f, 0.30f),
                        AccentColor = new Color(1.0f, 0.85f, 0.25f),
                        TextColor = new Color(0.96f, 0.94f, 0.88f),
                        ShadowColor = new Color(0f, 0f, 0f, 0.95f)
                    };
            }
        }
    }
}
