using Aether.Nano;
using UnityEngine;
using static Aether.Nano.NanoUi;

namespace Aether.Mods.Wayfinder
{
    /// <summary>Wayfinder's settings page (Nano shows it in Aether's F10 menu or in its own window).</summary>
    public sealed partial class WayfinderModule
    {
        private static readonly (string Id, string NameEn, string NameEs)[] MinimapAnchorOptions =
        {
            ("TopRight", "Top-Right", "Arriba-Derecha"),
            ("TopLeft", "Top-Left", "Arriba-Izquierda"),
            ("BottomRight", "Bottom-Right", "Abajo-Derecha"),
            ("BottomLeft", "Bottom-Left", "Abajo-Izquierda")
        };

        private static readonly (string Id, string NameEn, string NameEs)[] MapThemeOptions =
        {
            ("NativeWood", "Carved Wood", "Madera"),
            ("DarkObsidian", "Dark Obsidian", "Obsidiana"),
            ("MinimalistGlass", "Crystal Glass", "Cristal"),
            ("RoyalGold", "Royal Gold", "Oro Real")
        };

        public void DrawSettings(Rect area, float sy)
        {
            Color gold = new Color(1.0f, 0.85f, 0.25f);
            float w = area.width;
            DrawShadowLabel(new Rect(10, sy, w - 24, 22), Tr("MINIMAP & MAP SETTINGS", "AJUSTES DEL MINIMAPA Y EL MAPA"), SectionTitleStyle, gold, Color.black);
            sy += 26f;

            string minimapKey = MinimapToggleKey != null ? MinimapToggleKey.Value.ToString() : "N";
            string mapKey = WayfinderKey != null ? WayfinderKey.Value.ToString() : "M";
            if (DrawNativeWoodButton(new Rect(12, sy, 240, 32),
                    MinimapEnabled.Value ? Tr($"Corner Minimap: ON ({minimapKey})", $"Minimapa Esquina: SÍ ({minimapKey})") : Tr($"Corner Minimap: OFF ({minimapKey})", $"Minimapa Esquina: NO ({minimapKey})"),
                    MinimapEnabled.Value))
                MinimapEnabled.Value = !MinimapEnabled.Value;
            if (Aether.Nano.Nano.InGame && DrawNativeWoodButton(new Rect(262, sy, 240, 32), Tr($"Open Full Wayfinder ({mapKey})", $"Abrir Mapa Completo ({mapKey})"), true))
            {
                NanoHud.CloseHostMenus?.Invoke();
                NanoWindow.Close();
                SetWayfinderOpen(true);
            }
            sy += 38f;

            DrawShadowLabel(new Rect(12, sy, 110, 24), Tr("Shape:", "Forma:"), ToolStyle, Color.white, Color.black);
            if (DrawNativeWoodButton(new Rect(124, sy, 110, 26), Tr("Square", "Cuadrado"), MinimapShape.Value == "Square")) MinimapShape.Value = "Square";
            if (DrawNativeWoodButton(new Rect(244, sy, 110, 26), Tr("Circle", "Círculo"), MinimapShape.Value == "Circle")) MinimapShape.Value = "Circle";
            sy += 34f;

            DrawShadowLabel(new Rect(12, sy, 110, 24), Tr("Minimap Anchor:", "Anclaje:"), ToolStyle, Color.white, Color.black);
            for (int i = 0; i < MinimapAnchorOptions.Length; i++)
            {
                var o = MinimapAnchorOptions[i];
                if (DrawNativeWoodButton(new Rect(124f + (i % 2) * 190f, sy + (i / 2) * 30f, 180f, 26f), Tr(o.NameEn, o.NameEs), MinimapAnchor.Value == o.Id))
                    MinimapAnchor.Value = o.Id;
            }
            sy += 64f;

            DrawShadowLabel(new Rect(12, sy, 110, 24), Tr("Theme:", "Tema:"), ToolStyle, Color.white, Color.black);
            for (int i = 0; i < MapThemeOptions.Length; i++)
            {
                var o = MapThemeOptions[i];
                if (DrawNativeWoodButton(new Rect(124f + (i % 2) * 190f, sy + (i / 2) * 30f, 180f, 26f), Tr(o.NameEn, o.NameEs), MinimapTheme.Value == o.Id))
                    MinimapTheme.Value = o.Id;
            }
            sy += 64f;

            sy = Slider(w, sy, $"OffsetX: {MinimapOffsetX.Value:F0} px", MinimapOffsetX.Value, -400f, 400f, 0.5f, v => MinimapOffsetX.Value = v);
            sy = Slider(w, sy, $"OffsetY: {MinimapOffsetY.Value:F0} px", MinimapOffsetY.Value, -400f, 400f, 0.5f, v => MinimapOffsetY.Value = v);
            sy = Slider(w, sy, Tr($"Minimap Size: {MinimapSize.Value:F0} px", $"Tamaño del minimapa: {MinimapSize.Value:F0} px"), MinimapSize.Value, 110f, 340f, 0.5f, v => MinimapSize.Value = v, 220f);
            sy = Slider(w, sy, Tr($"Wayfinder Scale: {FullscreenMapScale.Value * 100f:F0}%", $"Escala del mapa: {FullscreenMapScale.Value * 100f:F0}%"), FullscreenMapScale.Value, 0.65f, 1.0f, 0.01f, v => FullscreenMapScale.Value = v, 220f);

            DrawShadowLabel(new Rect(10, sy, w - 24, 22), Tr("MAP DATA & PERFORMANCE", "DATOS DEL MAPA Y RENDIMIENTO"), SectionTitleStyle, gold, Color.black);
            sy += 24f;
            Toggle(new Rect(12, sy, 146, 28), ShowMobsOnMinimap, "Mobs: ON", "Mobs: OFF", "Mobs: SÍ", "Mobs: NO");
            Toggle(new Rect(164, sy, 146, 28), ShowWaypointLabelsOnMinimap, "Labels: ON", "Labels: OFF", "Etiquetas: SÍ", "Etiquetas: NO");
            Toggle(new Rect(316, sy, 146, 28), ShowNativeRadarPois, "Radar POIs: ON", "Radar POIs: OFF", "POIs Radar: SÍ", "POIs Radar: NO");
            sy += 34f;
            Toggle(new Rect(12, sy, 240, 28), SaveExploredMap, "Explored Map: ON", "Explored Map: OFF", "Mapa Explorado: SÍ", "Mapa Explorado: NO");
            if (HasSurfaceStore && DrawNativeWoodButton(new Rect(262, sy, 200, 28), Tr("Save Map Now", "Guardar Mapa Ahora"))) SaveMapNow();
            sy += 34f;
            Toggle(new Rect(12, sy, 300, 28), ShowSurfaceAboveCaves, "Surface Above Caves: ON", "Surface Above Caves: OFF", "Superficie sobre Cuevas: SÍ", "Superficie sobre Cuevas: NO");
            Toggle(new Rect(320, sy, 300, 28), AlwaysMapSurface, "Map Surface Underground: ON", "Map Surface Underground: OFF", "Mapear Superficie Bajo Tierra: SÍ", "Mapear Superficie Bajo Tierra: NO");
            sy += 34f;
            sy = Slider(w, sy, $"Minimap refresh: {MinimapRefreshSeconds.Value:F2}s", MinimapRefreshSeconds.Value, 0.25f, 2.0f, 0.01f, v => MinimapRefreshSeconds.Value = v, 190f);
            sy = Slider(w, sy, $"Full map refresh: {FullscreenRefreshSeconds.Value:F2}s", FullscreenRefreshSeconds.Value, 0.20f, 2.0f, 0.01f, v => FullscreenRefreshSeconds.Value = v, 190f);
            sy = Slider(w, sy, $"Entity Y range: {EntityVerticalRange.Value:F0}", EntityVerticalRange.Value, 8f, 256f, 0.5f, v => EntityVerticalRange.Value = v, 190f);

            sy += 6f;
            if (NanoHud.OpenHostEditor != null && DrawNativeWoodButton(new Rect(12, sy, 298, 32), Tr("✥ Drag Minimap Position", "✥ Arrastrar Posición Minimapa"), true))
                NanoHud.OpenHostEditor();
        }

        private static void Toggle(Rect r, BepInEx.Configuration.ConfigEntry<bool> entry, string onEn, string offEn, string onEs, string offEs)
        {
            if (entry == null) return;
            if (DrawNativeWoodButton(r, entry.Value ? Tr(onEn, onEs) : Tr(offEn, offEs), entry.Value)) entry.Value = !entry.Value;
        }

        private static float Slider(float w, float sy, string label, float value, float min, float max, float step, System.Action<float> set, float sliderX = 145f)
        {
            DrawShadowLabel(new Rect(14, sy, sliderX - 10f, 22), label, DescStyle, Color.white, Color.black);
            float v = GUI.HorizontalSlider(new Rect(sliderX, sy + 4, w - sliderX - 20f, 20), value, min, max);
            if (Mathf.Abs(v - value) > step) set(v);
            return sy + 26f;
        }
    }
}
