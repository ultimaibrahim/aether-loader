using Aether.Nano;
using UnityEngine;
using static Aether.Nano.NanoUi;

namespace Aether.Mods.Wayfinder
{
    /// <summary>Wayfinder's settings page (Nano shows it in Aether's F10 menu or in its own window).</summary>
    public sealed partial class WayfinderModule
    {
        private static readonly NanoSegmentOption<string>[] ShapeOptionsEn =
        {
            new NanoSegmentOption<string>("Square", "Square"),
            new NanoSegmentOption<string>("Circle", "Circle")
        };
        private static readonly NanoSegmentOption<string>[] ShapeOptionsEs =
        {
            new NanoSegmentOption<string>("Square", "Cuadrado"),
            new NanoSegmentOption<string>("Circle", "Círculo")
        };

        private static readonly NanoSegmentOption<string>[] AnchorOptionsEn =
        {
            new NanoSegmentOption<string>("TopRight", "Top-Right"),
            new NanoSegmentOption<string>("TopLeft", "Top-Left"),
            new NanoSegmentOption<string>("BottomRight", "Bottom-Right"),
            new NanoSegmentOption<string>("BottomLeft", "Bottom-Left")
        };
        private static readonly NanoSegmentOption<string>[] AnchorOptionsEs =
        {
            new NanoSegmentOption<string>("TopRight", "Arr-Der"),
            new NanoSegmentOption<string>("TopLeft", "Arr-Izq"),
            new NanoSegmentOption<string>("BottomRight", "Aba-Der"),
            new NanoSegmentOption<string>("BottomLeft", "Aba-Izq")
        };

        private static readonly NanoSegmentOption<string>[] MapThemeRow1En =
        {
            new NanoSegmentOption<string>("Aether", "Aether Ink", new Color(1.0f, 0.54f, 0.24f)),
            new NanoSegmentOption<string>("NativeWood", "Carved Wood", new Color(0.68f, 0.48f, 0.28f)),
            new NanoSegmentOption<string>("DarkObsidian", "Obsidian", new Color(0.56f, 0.62f, 0.72f))
        };
        private static readonly NanoSegmentOption<string>[] MapThemeRow1Es =
        {
            new NanoSegmentOption<string>("Aether", "Tinta Aether", new Color(1.0f, 0.54f, 0.24f)),
            new NanoSegmentOption<string>("NativeWood", "Madera", new Color(0.68f, 0.48f, 0.28f)),
            new NanoSegmentOption<string>("DarkObsidian", "Obsidiana", new Color(0.56f, 0.62f, 0.72f))
        };

        private static readonly NanoSegmentOption<string>[] MapThemeRow2En =
        {
            new NanoSegmentOption<string>("MinimalistGlass", "Crystal Glass", new Color(0.56f, 0.76f, 0.86f)),
            new NanoSegmentOption<string>("RoyalGold", "Royal Gold", new Color(0.84f, 0.64f, 0.26f))
        };
        private static readonly NanoSegmentOption<string>[] MapThemeRow2Es =
        {
            new NanoSegmentOption<string>("MinimalistGlass", "Cristal", new Color(0.56f, 0.76f, 0.86f)),
            new NanoSegmentOption<string>("RoyalGold", "Oro Real", new Color(0.84f, 0.64f, 0.26f))
        };

        public void DrawSettings(Rect area, float sy)
        {
            bool es = IsSpanish();
            float pad = 12f;
            float colW = Mathf.Max(260f, area.width - pad * 2f);
            float halfW = (colW - 8f) * 0.5f;
            var col = new Rect(pad, sy, colW, area.height);

            string minimapKey = MinimapToggleKey != null ? MinimapToggleKey.Value.ToString() : "N";
            string mapKey = WayfinderKey != null ? WayfinderKey.Value.ToString() : "M";

            // --- Section 1: Quick controls ---
            sy = NanoSurface.DrawSectionHeader(col, sy,
                es ? "Minimapa y mapa" : "Minimap & map",
                $"{minimapKey} · {mapKey}");

            MinimapEnabled.Value = NanoSurface.DrawToggleRow(
                new Rect(pad, sy, halfW, 32f),
                es ? $"Minimapa en esquina ({minimapKey})" : $"Corner Minimap ({minimapKey})",
                MinimapEnabled.Value);

            if (Aether.Nano.Nano.InGame)
            {
                if (NanoSurface.DrawActionButton(new Rect(pad + halfW + 8f, sy, halfW, 32f),
                        es ? $"Abrir Mapa Completo ({mapKey})" : $"Open Fullscreen Map ({mapKey})", true))
                {
                    NanoHud.CloseHostMenus?.Invoke();
                    NanoWindow.Close();
                    SetWayfinderOpen(true);
                    return;
                }
            }
            else if (NanoHud.OpenCanvasEditor != null || NanoHud.OpenHostEditor != null)
            {
                if (NanoSurface.DrawActionButton(new Rect(pad + halfW + 8f, sy, halfW, 32f),
                        es ? "Editar posición en Canvas" : "Edit Position in Canvas", false))
                {
                    NanoHud.OpenEditor();
                    return;
                }
            }
            sy += 40f;

            // --- Section 2: Appearance & layout ---
            sy = NanoSurface.DrawSectionHeader(col, sy,
                es ? "Apariencia y posición" : "Appearance & placement",
                MinimapTheme.Value);

            if (NanoSurface.DrawSegmented(new Rect(pad, sy, colW, 28f),
                    es ? ShapeOptionsEs : ShapeOptionsEn, MinimapShape.Value, out string nextShape))
                MinimapShape.Value = nextShape;
            sy += 34f;

            if (NanoSurface.DrawSegmented(new Rect(pad, sy, colW, 28f),
                    es ? AnchorOptionsEs : AnchorOptionsEn, MinimapAnchor.Value, out string nextAnchor))
                MinimapAnchor.Value = nextAnchor;
            sy += 34f;

            if (NanoSurface.DrawSegmented(new Rect(pad, sy, colW, 28f),
                    es ? MapThemeRow1Es : MapThemeRow1En, MinimapTheme.Value, out string t1))
                MinimapTheme.Value = t1;
            sy += 32f;

            if (NanoSurface.DrawSegmented(new Rect(pad, sy, colW, 28f),
                    es ? MapThemeRow2Es : MapThemeRow2En, MinimapTheme.Value, out string t2))
                MinimapTheme.Value = t2;
            sy += 38f;

            float nextSize = NanoSurface.DrawSlider(new Rect(pad, sy, colW, 24f),
                es ? "Tamaño del minimapa" : "Minimap size", MinimapSize.Value, 110f, 340f, $"{MinimapSize.Value:F0} px", 2f, 152f);
            if (Mathf.Abs(nextSize - MinimapSize.Value) > 0.5f) MinimapSize.Value = nextSize;
            sy += 30f;

            float nextScale = NanoSurface.DrawSlider(new Rect(pad, sy, colW, 24f),
                es ? "Escala mapa completo" : "Fullscreen map scale", FullscreenMapScale.Value, 0.65f, 1.0f, $"{FullscreenMapScale.Value * 100f:F0}%", 0.01f, 152f);
            if (Mathf.Abs(nextScale - FullscreenMapScale.Value) > 0.005f) FullscreenMapScale.Value = nextScale;
            sy += 30f;

            float nextOx = NanoSurface.DrawSlider(new Rect(pad, sy, colW, 24f),
                es ? "Desplazamiento X" : "Offset X", MinimapOffsetX.Value, -400f, 400f, $"{MinimapOffsetX.Value:F0} px", 1f, 152f);
            if (Mathf.Abs(nextOx - MinimapOffsetX.Value) > 0.5f) MinimapOffsetX.Value = nextOx;
            sy += 30f;

            float nextOy = NanoSurface.DrawSlider(new Rect(pad, sy, colW, 24f),
                es ? "Desplazamiento Y" : "Offset Y", MinimapOffsetY.Value, -400f, 400f, $"{MinimapOffsetY.Value:F0} px", 1f, 152f);
            if (Mathf.Abs(nextOy - MinimapOffsetY.Value) > 0.5f) MinimapOffsetY.Value = nextOy;
            sy += 38f;

            // --- Section 3: Radar, overlays & persistence ---
            sy = NanoSurface.DrawSectionHeader(col, sy,
                es ? "Datos, marcadores y rendimiento" : "Data, markers & performance");

            ShowMobsOnMinimap.Value = NanoSurface.DrawToggleRow(
                new Rect(pad, sy, halfW, 30f), es ? "Mobs en minimapa" : "Mobs on minimap", ShowMobsOnMinimap.Value);
            ShowWaypointLabelsOnMinimap.Value = NanoSurface.DrawToggleRow(
                new Rect(pad + halfW + 8f, sy, halfW, 30f), es ? "Etiquetas de waypoints" : "Waypoint labels", ShowWaypointLabelsOnMinimap.Value);
            sy += 36f;

            ShowNativeRadarPois.Value = NanoSurface.DrawToggleRow(
                new Rect(pad, sy, halfW, 30f), es ? "POIs de radar" : "Radar POIs", ShowNativeRadarPois.Value);
            ShowWorldWaypoints.Value = NanoSurface.DrawToggleRow(
                new Rect(pad + halfW + 8f, sy, halfW, 30f), es ? "Marcadores 3D en mundo" : "3D world markers", ShowWorldWaypoints.Value);
            sy += 36f;

            if (ShowWaypointBeacons != null)
            {
                ShowWaypointBeacons.Value = NanoSurface.DrawToggleRow(
                    new Rect(pad, sy, halfW, 30f), es ? "Haces 3D de waypoints" : "3D waypoint beams", ShowWaypointBeacons.Value);
            }
            SaveExploredMap.Value = NanoSurface.DrawToggleRow(
                new Rect(pad + halfW + 8f, sy, halfW, 30f), es ? "Guardar mapa explorado" : "Save explored map", SaveExploredMap.Value);
            sy += 36f;

            ShowSurfaceAboveCaves.Value = NanoSurface.DrawToggleRow(
                new Rect(pad, sy, halfW, 30f), es ? "Superficie sobre cuevas" : "Surface above caves", ShowSurfaceAboveCaves.Value);
            AlwaysMapSurface.Value = NanoSurface.DrawToggleRow(
                new Rect(pad + halfW + 8f, sy, halfW, 30f), es ? "Mapear superficie bajo tierra" : "Map surface underground", AlwaysMapSurface.Value);
            sy += 38f;

            float nextMiniRef = NanoSurface.DrawSlider(new Rect(pad, sy, colW, 24f),
                es ? "Refresco minimapa" : "Minimap refresh", MinimapRefreshSeconds.Value, 0.25f, 2.0f, $"{MinimapRefreshSeconds.Value:F2} s", 0.05f, 152f);
            if (Mathf.Abs(nextMiniRef - MinimapRefreshSeconds.Value) > 0.005f) MinimapRefreshSeconds.Value = nextMiniRef;
            sy += 30f;

            float nextFullRef = NanoSurface.DrawSlider(new Rect(pad, sy, colW, 24f),
                es ? "Refresco mapa completo" : "Full map refresh", FullscreenRefreshSeconds.Value, 0.20f, 2.0f, $"{FullscreenRefreshSeconds.Value:F2} s", 0.05f, 152f);
            if (Mathf.Abs(nextFullRef - FullscreenRefreshSeconds.Value) > 0.005f) FullscreenRefreshSeconds.Value = nextFullRef;
            sy += 30f;

            float nextEntY = NanoSurface.DrawSlider(new Rect(pad, sy, colW, 24f),
                es ? "Rango Y de entidades" : "Entity Y range", EntityVerticalRange.Value, 8f, 256f, $"{EntityVerticalRange.Value:F0} bl", 2f, 152f);
            if (Mathf.Abs(nextEntY - EntityVerticalRange.Value) > 0.5f) EntityVerticalRange.Value = nextEntY;
            sy += 38f;

            // --- Footer action buttons ---
            float actionX = pad;
            if (NanoHud.OpenCanvasEditor != null || NanoHud.OpenHostEditor != null)
            {
                if (NanoSurface.DrawActionButton(new Rect(actionX, sy, halfW, 32f),
                        es ? "Mover posición en Canvas" : "Move Position in Canvas", true))
                {
                    NanoHud.OpenEditor();
                    return;
                }
                actionX += halfW + 8f;
            }
            if (HasSurfaceStore)
            {
                if (NanoSurface.DrawActionButton(new Rect(actionX, sy, halfW, 32f),
                        es ? "Guardar mapa ahora" : "Save Map Now", false))
                {
                    SaveMapNow();
                }
            }
        }
    }
}
