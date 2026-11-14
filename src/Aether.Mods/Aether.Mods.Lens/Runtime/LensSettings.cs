using Aether.Nano;
using UnityEngine;
using static Aether.Nano.Nano;
using static Aether.Nano.NanoUi;

namespace Aether.Mods.Lens
{
    /// <summary>Lens's settings page (Nano shows it in Aether's F10 menu or in its own window).</summary>
    public sealed partial class LensModule
    {
        private static readonly (string Id, string LabelEn, string LabelEs)[] LensAnchorOptions =
        {
            ("TopCenter", "Top-Center", "Centro-Arriba"),
            ("TopLeft", "Top-Left", "Arriba-Izquierda"),
            ("BottomCenter", "Bottom-Center", "Centro-Abajo"),
            ("AboveCrosshair", "Above Crosshair", "Punto de Mira")
        };

        private static readonly (string Id, string NameEn, string NameEs)[] LensThemeOptions =
        {
            ("NativeWood", "Carved Wood", "Madera"),
            ("DarkObsidian", "Dark Obsidian", "Obsidiana"),
            ("MinimalistGlass", "Crystal Glass", "Cristal"),
            ("RoyalGold", "Royal Gold", "Oro Real"),
            ("CompactBar", "Compact Bar", "Barra")
        };

        public void DrawSettings(Rect area, float sy)
        {
            Color gold = new Color(1.0f, 0.85f, 0.25f);
            DrawShadowLabel(new Rect(10, sy, area.width - 24, 22), Tr("REAL-TIME LENS SETTINGS", "AJUSTES DE LENS EN TIEMPO REAL"), SectionTitleStyle, gold, Color.black);

            string key = LensToggleKey.Value.ToString();
            if (DrawNativeWoodButton(new Rect(12, sy + 26, 240, 32),
                    LensEnabled.Value ? Tr($"Lens HUD: ENABLED ({key})", $"HUD de Lens: ACTIVADO ({key})") : Tr($"Lens HUD: DISABLED ({key})", $"HUD de Lens: DESACTIVADO ({key})"),
                    LensEnabled.Value))
                LensEnabled.Value = !LensEnabled.Value;
            if (DrawNativeWoodButton(new Rect(260, sy + 26, 240, 32),
                    LensShowCoords.Value ? Tr("Coordinates: SHOWN", "Coordenadas: VISIBLES") : Tr("Coordinates: HIDDEN", "Coordenadas: OCULTAS"),
                    LensShowCoords.Value))
                LensShowCoords.Value = !LensShowCoords.Value;
            if (DrawNativeWoodButton(new Rect(12, sy + 62, 240, 32),
                    Lens3DBlockIcon.Value ? Tr("3D Block Icon: ON", "Icono Bloque 3D: SÍ") : Tr("3D Block Icon: OFF", "Icono Bloque 3D: NO"),
                    Lens3DBlockIcon.Value))
                Lens3DBlockIcon.Value = !Lens3DBlockIcon.Value;

            sy += 100f;
            DrawShadowLabel(new Rect(12, sy, 110, 24), Tr("Screen Anchor:", "Anclaje:"), ToolStyle, Color.white, Color.black);
            for (int a = 0; a < LensAnchorOptions.Length; a++)
            {
                var o = LensAnchorOptions[a];
                if (DrawNativeWoodButton(new Rect(124f + (a % 2) * 190f, sy + (a / 2) * 32f, 180f, 28f), Tr(o.LabelEn, o.LabelEs), LensAnchor.Value == o.Id))
                    LensAnchor.Value = o.Id;
            }
            sy += 66f;

            DrawShadowLabel(new Rect(12, sy, 110, 24), Tr("Visual Theme:", "Tema Visual:"), ToolStyle, Color.white, Color.black);
            for (int t = 0; t < LensThemeOptions.Length; t++)
            {
                var o = LensThemeOptions[t];
                Rect r = t < 3 ? new Rect(124f + t * 128f, sy, 122f, 28f) : new Rect(124f + (t - 3) * 190f, sy + 32f, 180f, 28f);
                if (DrawNativeWoodButton(r, Tr(o.NameEn, o.NameEs), LensTheme.Value == o.Id)) LensTheme.Value = o.Id;
            }
            sy += 68f;

            sy = Slider(area, sy, $"OffsetX: {LensOffsetX.Value:F0} px", LensOffsetX.Value, -500f, 500f, 0.5f, v => LensOffsetX.Value = v);
            sy = Slider(area, sy, $"OffsetY: {LensOffsetY.Value:F0} px", LensOffsetY.Value, -500f, 500f, 0.5f, v => LensOffsetY.Value = v);
            sy = Slider(area, sy, Tr($"Scale: {LensScale.Value:F2}x", $"Escala: {LensScale.Value:F2}x"), LensScale.Value, 0.5f, 1.5f, 0.02f, v => LensScale.Value = v);

            sy += 6f;
            if (NanoHud.OpenHostEditor != null)
            {
                if (DrawNativeWoodButton(new Rect(12, sy, 298, 32), Tr("✥ Drag Lens HUD Position", "✥ Arrastrar Posición Lens"), true))
                    NanoHud.OpenHostEditor();
                sy += 42f;
            }

            DrawShadowLabel(new Rect(10, sy, area.width - 24, 22), Tr("MOB CARD", "TARJETA DE MOBS"), SectionTitleStyle, gold, Color.black);
            string health = LensMobHealthStyle.Value;
            if (DrawNativeWoodButton(new Rect(12, sy + 26, 118, 30), Tr("Hearts", "Corazones"), health == "Hearts")) LensMobHealthStyle.Value = "Hearts";
            if (DrawNativeWoodButton(new Rect(140, sy + 26, 110, 30), Tr("Bar", "Barra"), health == "Bar")) LensMobHealthStyle.Value = "Bar";
            if (DrawNativeWoodButton(new Rect(260, sy + 26, 100, 30), Tr("None", "Ninguna"), health == "None")) LensMobHealthStyle.Value = "None";
            if (DrawNativeWoodButton(new Rect(12, sy + 62, 170, 30), LensShowMobLevel.Value ? Tr("Level: ON", "Nivel: SÍ") : Tr("Level: OFF", "Nivel: NO"), LensShowMobLevel.Value))
                LensShowMobLevel.Value = !LensShowMobLevel.Value;
            if (DrawNativeWoodButton(new Rect(192, sy + 62, 170, 30), LensShowMobQuests.Value ? Tr("Quests: ON", "Misiones: SÍ") : Tr("Quests: OFF", "Misiones: NO"), LensShowMobQuests.Value))
                LensShowMobQuests.Value = !LensShowMobQuests.Value;
            if (DrawNativeWoodButton(new Rect(12, sy + 98, 350, 30),
                    LensHideNativeMobHealth.Value ? Tr("Native mob HP: HIDDEN", "Vida nativa de mobs: OCULTA") : Tr("Native mob HP: SHOWN", "Vida nativa de mobs: VISIBLE"),
                    LensHideNativeMobHealth.Value))
                LensHideNativeMobHealth.Value = !LensHideNativeMobHealth.Value;
        }

        private static float Slider(Rect area, float sy, string label, float value, float min, float max, float step, System.Action<float> set)
        {
            DrawShadowLabel(new Rect(14, sy, 140, 22), label, DescStyle, Color.white, Color.black);
            float v = GUI.HorizontalSlider(new Rect(145, sy + 4, area.width - 165, 20), value, min, max);
            if (Mathf.Abs(v - value) > step) set(v);
            return sy + 26f;
        }
    }
}
