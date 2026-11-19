using Aether.Nano;
using UnityEngine;
using static Aether.Nano.Nano;

namespace Aether.Mods.Lens
{
    /// <summary>Lens's settings page (Nano shows it in Aether's F10 menu or in its own window).</summary>
    public sealed partial class LensModule
    {
        private static bool previewMobMode;

        private static readonly NanoSegmentOption<bool>[] PreviewModeEn =
        {
            new NanoSegmentOption<bool>(false, "Block"),
            new NanoSegmentOption<bool>(true, "Creature")
        };
        private static readonly NanoSegmentOption<bool>[] PreviewModeEs =
        {
            new NanoSegmentOption<bool>(false, "Bloque"),
            new NanoSegmentOption<bool>(true, "Criatura")
        };

        private static readonly NanoSegmentOption<string>[] AnchorOptionsEn =
        {
            new NanoSegmentOption<string>("TopCenter", "Top-Center"),
            new NanoSegmentOption<string>("TopLeft", "Top-Left"),
            new NanoSegmentOption<string>("BottomCenter", "Bottom"),
            new NanoSegmentOption<string>("AboveCrosshair", "Crosshair")
        };
        private static readonly NanoSegmentOption<string>[] AnchorOptionsEs =
        {
            new NanoSegmentOption<string>("TopCenter", "Arriba"),
            new NanoSegmentOption<string>("TopLeft", "Arriba-Izq."),
            new NanoSegmentOption<string>("BottomCenter", "Abajo"),
            new NanoSegmentOption<string>("AboveCrosshair", "Mira")
        };

        private static readonly NanoSegmentOption<string>[] ThemeRow1En =
        {
            new NanoSegmentOption<string>("Aether", "Aether", NanoBrand.Ember),
            new NanoSegmentOption<string>("NativeWood", "Timber", new Color(0.89f, 0.66f, 0.29f)),
            new NanoSegmentOption<string>("DarkObsidian", "Obsidian", new Color(0.62f, 0.51f, 0.77f))
        };
        private static readonly NanoSegmentOption<string>[] ThemeRow1Es =
        {
            new NanoSegmentOption<string>("Aether", "Aether", NanoBrand.Ember),
            new NanoSegmentOption<string>("NativeWood", "Madera", new Color(0.89f, 0.66f, 0.29f)),
            new NanoSegmentOption<string>("DarkObsidian", "Obsidiana", new Color(0.62f, 0.51f, 0.77f))
        };

        private static readonly NanoSegmentOption<string>[] ThemeRow2En =
        {
            new NanoSegmentOption<string>("MinimalistGlass", "Glass", new Color(0.62f, 0.78f, 0.85f)),
            new NanoSegmentOption<string>("RoyalGold", "Antique Gold", new Color(0.84f, 0.61f, 0.23f)),
            new NanoSegmentOption<string>("CompactBar", "Visor Strip", NanoBrand.EmberSoft)
        };
        private static readonly NanoSegmentOption<string>[] ThemeRow2Es =
        {
            new NanoSegmentOption<string>("MinimalistGlass", "Cristal", new Color(0.62f, 0.78f, 0.85f)),
            new NanoSegmentOption<string>("RoyalGold", "Oro Antiguo", new Color(0.84f, 0.61f, 0.23f)),
            new NanoSegmentOption<string>("CompactBar", "Cinta", NanoBrand.EmberSoft)
        };

        private static readonly NanoSegmentOption<string>[] HealthOptionsEn =
        {
            new NanoSegmentOption<string>("Hearts", "Hearts", new Color(0.92f, 0.34f, 0.36f)),
            new NanoSegmentOption<string>("Bar", "Health Bar", NanoBrand.Ok),
            new NanoSegmentOption<string>("None", "Hidden")
        };
        private static readonly NanoSegmentOption<string>[] HealthOptionsEs =
        {
            new NanoSegmentOption<string>("Hearts", "Corazones", new Color(0.92f, 0.34f, 0.36f)),
            new NanoSegmentOption<string>("Bar", "Barra", NanoBrand.Ok),
            new NanoSegmentOption<string>("None", "Oculta")
        };

        public void DrawSettings(Rect area, float sy)
        {
            bool es = IsSpanish();
            float pad = 12f;
            float w = Mathf.Max(260f, area.width - pad * 2f);
            Rect col = new Rect(pad, sy, w, area.height);

            // 1. Live Interactive Preview Stage
            sy = DrawLivePreviewStage(col, sy, es);
            sy += 12f;

            // 2. Display & Behavior
            sy = NanoSurface.DrawSectionHeader(col, sy,
                es ? "Visor" : "Display",
                es ? "En tiempo real" : "Live");

            float halfW = (w - 8f) * 0.5f;
            string key = LensToggleKey.Value.ToString();

            LensEnabled.Value = NanoSurface.DrawToggleRow(
                new Rect(pad, sy, halfW, 32f),
                es ? "HUD de Lens" : "Lens HUD",
                LensEnabled.Value,
                key);
            Lens3DBlockIcon.Value = NanoSurface.DrawToggleRow(
                new Rect(pad + halfW + 8f, sy, halfW, 32f),
                es ? "Cubo 3D real" : "3D Block Cube",
                Lens3DBlockIcon.Value);
            sy += 38f;

            LensShowCoords.Value = NanoSurface.DrawToggleRow(
                new Rect(pad, sy, halfW, 32f),
                es ? "Coordenadas" : "Coordinates",
                LensShowCoords.Value);
            LensReduceMotion.Value = NanoSurface.DrawToggleRow(
                new Rect(pad + halfW + 8f, sy, halfW, 32f),
                es ? "Reducir movimiento" : "Reduce Motion",
                LensReduceMotion.Value);
            sy += 38f;

            LensHideCrosshairDigProgress.Value = NanoSurface.DrawToggleRow(
                new Rect(pad, sy, w, 32f),
                es ? "Ocultar progreso de minado en la mirilla" : "Hide Native Crosshair Break Progress",
                LensHideCrosshairDigProgress.Value);
            sy += 44f;

            // 3. Style & Layout
            sy = NanoSurface.DrawSectionHeader(col, sy,
                es ? "Estilo y posición" : "Style & layout",
                LensTheme.Value);

            if (NanoSurface.DrawSegmented(new Rect(pad, sy, w, 30f), es ? ThemeRow1Es : ThemeRow1En, LensTheme.Value, out string theme1))
                LensTheme.Value = theme1;
            sy += 34f;

            if (NanoSurface.DrawSegmented(new Rect(pad, sy, w, 30f), es ? ThemeRow2Es : ThemeRow2En, LensTheme.Value, out string theme2))
                LensTheme.Value = theme2;
            sy += 38f;

            if (NanoSurface.DrawSegmented(new Rect(pad, sy, w, 28f), es ? AnchorOptionsEs : AnchorOptionsEn, LensAnchor.Value, out string anchor))
                LensAnchor.Value = anchor;
            sy += 36f;

            float newOpacity = NanoSurface.DrawSlider(
                new Rect(pad, sy, w, 24f),
                es ? "Opacidad" : "Opacity",
                LensBgOpacity.Value, 0.30f, 1.00f,
                $"{Mathf.RoundToInt(LensBgOpacity.Value * 100f)}%", 0.01f);
            if (Mathf.Abs(newOpacity - LensBgOpacity.Value) > 0.005f) LensBgOpacity.Value = newOpacity;
            sy += 28f;

            float newWidth = NanoSurface.DrawSlider(
                new Rect(pad, sy, w, 24f),
                es ? "Ancho mínimo" : "Min Width",
                LensCardWidth.Value, 200f, 420f,
                $"{LensCardWidth.Value} px", 4f);
            int widthInt = Mathf.RoundToInt(newWidth);
            if (widthInt != LensCardWidth.Value) LensCardWidth.Value = widthInt;
            sy += 28f;

            float newScale = NanoSurface.DrawSlider(
                new Rect(pad, sy, w, 24f),
                es ? "Escala" : "Scale",
                LensScale.Value, 0.50f, 1.50f,
                $"{LensScale.Value:F2}x", 0.02f);
            if (Mathf.Abs(newScale - LensScale.Value) > 0.005f) LensScale.Value = newScale;
            sy += 28f;

            float newOffX = NanoSurface.DrawSlider(
                new Rect(pad, sy, w, 24f),
                es ? "Desfase X" : "Offset X",
                LensOffsetX.Value, -500f, 500f,
                $"{LensOffsetX.Value:F0} px", 2f);
            if (Mathf.Abs(newOffX - LensOffsetX.Value) > 0.5f) LensOffsetX.Value = newOffX;
            sy += 28f;

            float newOffY = NanoSurface.DrawSlider(
                new Rect(pad, sy, w, 24f),
                es ? "Desfase Y" : "Offset Y",
                LensOffsetY.Value, -500f, 500f,
                $"{LensOffsetY.Value:F0} px", 2f);
            if (Mathf.Abs(newOffY - LensOffsetY.Value) > 0.5f) LensOffsetY.Value = newOffY;
            sy += 34f;

            if (NanoHud.OpenCanvasEditor != null || NanoHud.OpenHostEditor != null)
            {
                float dragW = Mathf.Min(230f, w * 0.56f);
                if (NanoSurface.DrawActionButton(new Rect(pad, sy, dragW, 30f),
                        es ? "Arrastrar posición en pantalla" : "Drag Lens Position", true))
                    NanoHud.OpenEditor();

                float resetW = Mathf.Min(170f, w - dragW - 8f);
                if (resetW >= 96f && NanoSurface.DrawActionButton(new Rect(pad + dragW + 8f, sy, resetW, 30f),
                        es ? "Restablecer" : "Reset Offset", false))
                {
                    LensOffsetX.Value = 0f;
                    LensOffsetY.Value = 0f;
                    LensScale.Value = 1.0f;
                }
                sy += 40f;
            }
            else
            {
                if (NanoSurface.DrawActionButton(new Rect(pad, sy, 170f, 28f),
                        es ? "Restablecer posición" : "Reset Position", false))
                {
                    LensOffsetX.Value = 0f;
                    LensOffsetY.Value = 0f;
                    LensScale.Value = 1.0f;
                }
                sy += 38f;
            }

            // 4. Mob Card Options
            sy = NanoSurface.DrawSectionHeader(col, sy,
                es ? "Tarjeta de criaturas" : "Creature card");

            if (NanoSurface.DrawSegmented(new Rect(pad, sy, w, 30f), es ? HealthOptionsEs : HealthOptionsEn, LensMobHealthStyle.Value, out string healthStyle))
                LensMobHealthStyle.Value = healthStyle;
            sy += 38f;

            LensShowMobLevel.Value = NanoSurface.DrawToggleRow(
                new Rect(pad, sy, halfW, 32f),
                es ? "Mostrar nivel" : "Show Level",
                LensShowMobLevel.Value);
            LensShowMobQuests.Value = NanoSurface.DrawToggleRow(
                new Rect(pad + halfW + 8f, sy, halfW, 32f),
                es ? "Estado de misiones" : "Quest Status",
                LensShowMobQuests.Value);
            sy += 38f;

            LensHideNativeMobHealth.Value = NanoSurface.DrawToggleRow(
                new Rect(pad, sy, w, 32f),
                es ? "Ocultar barra de vida nativa del juego" : "Hide Native Overhead Mob HP Bars",
                LensHideNativeMobHealth.Value);
        }

        private float DrawLivePreviewStage(Rect col, float sy, bool es)
        {
            const float stageH = 120f;
            var stageRect = new Rect(col.x, sy, col.width, stageH);

            if (Event.current.type == EventType.Repaint)
            {
                // Subtle dusk-sky backdrop inside a debossed stage so opacity & shadow read honestly
                NanoSurface.FillRoundedGradient(stageRect,
                    new Color(0.11f, 0.14f, 0.17f, 0.88f),
                    new Color(0.065f, 0.055f, 0.048f, 0.94f),
                    10f);
                NanoSurface.StrokeRounded(stageRect, NanoBrand.Line, 1f, 10f);

                EnsureLensStyles();
                NanoSurface.DrawCrispLabel(
                    new Rect(stageRect.x + 12f, stageRect.y + 8f, 160f, 16f),
                    es ? "VISTA PREVIA" : "LIVE PREVIEW",
                    lensMonoSmall,
                    NanoBrand.Mute,
                    0.3f);
            }

            // Block / Creature preview switcher in top-right of the stage
            const float toggleW = 148f;
            var toggleRect = new Rect(stageRect.xMax - toggleW - 8f, stageRect.y + 6f, toggleW, 22f);
            if (NanoSurface.DrawSegmented(toggleRect, es ? PreviewModeEs : PreviewModeEn, previewMobMode, out bool nextMode))
                previewMobMode = nextMode;

            if (Event.current.type == EventType.Repaint)
            {
                bool isCompact = LensTheme.Value == "CompactBar";
                bool hideHealth = string.Equals(LensMobHealthStyle.Value, "None", System.StringComparison.OrdinalIgnoreCase);
                float cardW = ComputePreviewCardWidth(previewMobMode);
                float cardH = isCompact ? 40f : (previewMobMode && hideHealth ? 54f : 72f);

                // Respect scale proportionally while fitting comfortably inside the preview stage
                float maxStageW = stageRect.width - 24f;
                float maxStageH = stageH - 36f;
                float previewScale = Mathf.Min(GetLensScale(), Mathf.Min(maxStageW / cardW, maxStageH / cardH));
                previewScale = Mathf.Clamp(previewScale, 0.55f, 1.15f);

                float drawnW = cardW * previewScale;
                float drawnH = cardH * previewScale;
                float cardX = stageRect.x + (stageRect.width - drawnW) * 0.5f;
                float cardY = stageRect.y + 30f + (maxStageH - drawnH) * 0.5f;

                var hitPreview = new Rect(cardX, cardY, drawnW, drawnH);
                float demoProgress = !previewMobMode && hitPreview.Contains(Event.current.mousePosition)
                    ? Mathf.Repeat(Time.unscaledTime * 0.55f, 1f)
                    : 0f;

                Matrix4x4 prevMatrix = GUI.matrix;
                try
                {
                    if (Mathf.Abs(previewScale - 1f) > 0.001f)
                        GUIUtility.ScaleAroundPivot(new Vector2(previewScale, previewScale), new Vector2(cardX, cardY));

                    var cardRect = new Rect(cardX, cardY, cardW, cardH);
                    if (previewMobMode) DrawPreviewEntityCard(cardRect);
                    else DrawPreviewBlockCard(cardRect, demoProgress);
                }
                finally
                {
                    GUI.matrix = prevMatrix;
                }
            }

            return sy + stageH;
        }
    }
}
