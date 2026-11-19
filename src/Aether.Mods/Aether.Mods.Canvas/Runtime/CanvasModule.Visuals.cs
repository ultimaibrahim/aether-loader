using System;
using System.Collections.Generic;
using Aether.Nano;
using Blocksters.Terrain.chunk;
using UnityEngine;
using static Aether.Nano.Nano;

namespace Aether.Mods.Canvas
{
    public sealed partial class CanvasModule
    {
        private struct HudElementVisual
        {
            public Rect Bounds;
            public List<Rect> Parts;
            public bool HiddenByMode;
            public bool LiveDetected;
            public string HiddenReasonEn;
            public string HiddenReasonEs;
            public string ShortBadgeEn;
            public string ShortBadgeEs;
        }

        // Pre-allocated per-element silhouette buffers (zero per-frame GC allocations)
        private readonly List<Rect> _statsParts = new List<Rect>(16);
        private readonly List<Rect> _diaParts = new List<Rect>(8);
        private readonly List<Rect> _btnParts = new List<Rect>(16);
        private readonly List<Rect> _actParts = new List<Rect>(16);
        private readonly List<UIWidget> _widgetScratch = new List<UIWidget>(32);

        private static GUIStyle _ghostBadgeStyle, _ghostSubStyle, _noticeBoxStyle, _ghostLevelStyle;

        // Cached real Block Story UIAtlas sprite names for authentic 1:1 ghost previews (resolved once)
        private static bool _atlasSpritesScanned;
        private static string _sprFood, _sprHeart, _sprMana, _sprDiamond, _sprFly;

        private static void EnsureAtlasSprites()
        {
            if (_atlasSpritesScanned) return;
            _atlasSpritesScanned = true;
            try
            {
                UIAtlas atlas = NanoUi.GuiAtlas;
                if (atlas == null || atlas.spriteList == null) return;
                for (int i = 0; i < atlas.spriteList.Count; i++)
                {
                    string n = atlas.spriteList[i]?.name;
                    if (string.IsNullOrEmpty(n)) continue;
                    string lo = n.ToLowerInvariant();
                    if (_sprFood == null && (lo.Contains("meat") || lo.Contains("food") || lo.Contains("hunger") || lo.Contains("steak")))
                        _sprFood = n;
                    if (_sprHeart == null && (lo == "heart" || lo.Contains("heart") || lo.Contains("health")))
                        _sprHeart = n;
                    if (_sprMana == null && (lo.Contains("mana") || lo.Contains("flask") || lo.Contains("potion")))
                        _sprMana = n;
                    if (_sprDiamond == null && lo.Contains("diamond"))
                        _sprDiamond = n;
                    if (_sprFly == null && (lo.Contains("wing") || lo.Contains("fly")))
                        _sprFly = n;
                }
            }
            catch { }
        }

        // =====================================================================
        //                       GAME MODE DETECTION
        // =====================================================================

        internal static WorldMode GetCurrentWorldMode()
        {
            try { return TerrainLoader.Mode; }
            catch { return WorldMode.Story; }
        }

        internal static string GetWorldModeLabel(WorldMode mode, bool es)
        {
            switch (mode)
            {
                case WorldMode.Creative: return es ? "Modo Creativo" : "Creative Mode";
                case WorldMode.Hardcore: return es ? "Modo Extremo" : "Hardcore Mode";
                case WorldMode.Survival: return es ? "Modo Supervivencia" : "Survival Mode";
                default: return es ? "Modo Historia" : "Story Mode";
            }
        }

        internal static string GetWorldModeShort(WorldMode mode, bool es)
        {
            switch (mode)
            {
                case WorldMode.Creative: return es ? "Creativo" : "Creative";
                case WorldMode.Hardcore: return es ? "Extremo" : "Hardcore";
                case WorldMode.Survival: return es ? "Superviv." : "Survival";
                default: return es ? "Historia" : "Story";
            }
        }

        // =====================================================================
        //         EXACT PIXEL-FITTED NGUI WIDGET PROJECTION (NO <1f BUG)
        // =====================================================================

        private Camera ResolveHudCamera(GameObject go)
        {
            try
            {
                Inventory inv = Aether.Nano.Nano.Inventory;
                if (inv != null && inv.guiCamera != null && inv.guiCamera.enabled)
                    return inv.guiCamera;
            }
            catch { }

            if (go != null)
            {
                try { return NGUITools.FindCameraForLayer(go.layer); }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Projects every visible child <see cref="UIWidget"/> of <paramref name="go"/> into screen-space
        /// pixel rectangles (using the NGUI camera directly, avoiding world-space UIRoot scale pitfalls).
        /// </summary>
        private bool ProjectVisibleWidgets(GameObject go, List<Rect> outParts, out Rect unionBounds)
        {
            outParts.Clear();
            unionBounds = Rect.zero;
            if (go == null || !go.activeInHierarchy) return false;

            Camera uiCam = ResolveHudCamera(go);
            if (uiCam == null) return false;

            _widgetScratch.Clear();
            go.GetComponentsInChildren(false, _widgetScratch);
            if (_widgetScratch.Count == 0) return false;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            int visibleCount = 0;

            for (int i = 0; i < _widgetScratch.Count; i++)
            {
                UIWidget w = _widgetScratch[i];
                if (w == null || !w.enabled || !w.gameObject.activeInHierarchy) continue;
                if (w.finalAlpha <= 0.02f) continue;

                // NGUI widget local corners -> world -> screen pixels
                Vector2 relSize = w.relativeSize;
                Vector2 pivot = w.pivotOffset;
                if (relSize.x <= 0.0001f || relSize.y <= 0.0001f) continue;

                float cx = (pivot.x + 0.5f) * relSize.x;
                float cy = (pivot.y - 0.5f) * relSize.y;
                float hx = relSize.x * 0.5f;
                float hy = relSize.y * 0.5f;

                Transform t = w.cachedTransform;
                Vector3 c0 = uiCam.WorldToScreenPoint(t.TransformPoint(new Vector3(cx - hx, cy - hy, 0f)));
                Vector3 c1 = uiCam.WorldToScreenPoint(t.TransformPoint(new Vector3(cx - hx, cy + hy, 0f)));
                Vector3 c2 = uiCam.WorldToScreenPoint(t.TransformPoint(new Vector3(cx + hx, cy - hy, 0f)));
                Vector3 c3 = uiCam.WorldToScreenPoint(t.TransformPoint(new Vector3(cx + hx, cy + hy, 0f)));

                float wxMin = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
                float wxMax = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
                float wyMinScreen = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y));
                float wyMaxScreen = Mathf.Max(Mathf.Max(c0.y, c1.y), Mathf.Max(c2.y, c3.y));

                float guiX = wxMin;
                float guiY = Screen.height - wyMaxScreen;
                float guiW = wxMax - wxMin;
                float guiH = wyMaxScreen - wyMinScreen;

                // Ignore degenerate or full-screen background planes
                if (guiW < 4f || guiH < 4f) continue;
                if (guiW > Screen.width * 0.85f || guiH > Screen.height * 0.85f) continue;

                visibleCount++;
                if (guiX < minX) minX = guiX;
                if (guiY < minY) minY = guiY;
                if (guiX + guiW > maxX) maxX = guiX + guiW;
                if (guiY + guiH > maxY) maxY = guiY + guiH;

                AddMergedPart(outParts, new Rect(guiX, guiY, guiW, guiH));
            }

            if (visibleCount == 0) return false;

            float pad = 4f;
            float ux = Mathf.Clamp(minX - pad, 2f, Screen.width - 12f);
            float uy = Mathf.Clamp(minY - pad, 2f, Screen.height - 12f);
            float uw = Mathf.Clamp((maxX - minX) + pad * 2f, 16f, Screen.width - ux - 2f);
            float uh = Mathf.Clamp((maxY - minY) + pad * 2f, 16f, Screen.height - uy - 2f);
            unionBounds = new Rect(ux, uy, uw, uh);
            return true;
        }

        /// <summary>
        /// Merges overlapping/nearly-touching widget rectangles into cohesive UI sub-silhouettes
        /// (for example: icon + label + background of a single button merge into one crisp silhouette).
        /// </summary>
        private static void AddMergedPart(List<Rect> parts, Rect next)
        {
            const float mergeGap = 6f;
            for (int i = 0; i < parts.Count; i++)
            {
                Rect cur = parts[i];
                var expanded = new Rect(cur.x - mergeGap, cur.y - mergeGap, cur.width + mergeGap * 2f, cur.height + mergeGap * 2f);
                if (expanded.Overlaps(next))
                {
                    float x0 = Mathf.Min(cur.x, next.x);
                    float y0 = Mathf.Min(cur.y, next.y);
                    float x1 = Mathf.Max(cur.xMax, next.xMax);
                    float y1 = Mathf.Max(cur.yMax, next.yMax);
                    parts[i] = new Rect(x0, y0, x1 - x0, y1 - y0);
                    return;
                }
            }
            if (parts.Count < 16) parts.Add(next);
        }

        private HudElementVisual BuildStatsVisual(WorldMode mode)
        {
            bool live = ProjectVisibleWidgets(_statsObj, _statsParts, out Rect bounds);
            if (live)
            {
                return new HudElementVisual { Bounds = bounds, Parts = _statsParts, LiveDetected = true };
            }

            float s = StatsScale.Value;
            float w = 276f * s;
            float h = 90f * s;
            float x = Mathf.Clamp(StatsOffsetX.Value * Screen.width, 6f, Mathf.Max(6f, Screen.width - w - 6f));
            float y = Mathf.Clamp(-StatsOffsetY.Value * Screen.height, 6f, Mathf.Max(6f, Screen.height - h - 6f));

            bool creative = mode == WorldMode.Creative;
            return new HudElementVisual
            {
                Bounds = new Rect(x, y, w, h),
                Parts = _statsParts,
                LiveDetected = false,
                HiddenByMode = true,
                HiddenReasonEn = creative
                    ? "Hidden in Creative Mode (health, mana & XP bars are disabled by the game)."
                    : $"Currently inactive in {GetWorldModeLabel(mode, false)}.",
                HiddenReasonEs = creative
                    ? "Oculto en Modo Creativo (el juego desactiva salud, maná y XP)."
                    : $"Actualmente inactivo en {GetWorldModeLabel(mode, true)}.",
                ShortBadgeEn = creative ? "Hidden · Creative" : "Inactive",
                ShortBadgeEs = creative ? "Oculto · Creativo" : "Inactivo"
            };
        }

        private HudElementVisual BuildDiamondsVisual(WorldMode mode)
        {
            bool live = ProjectVisibleWidgets(_diamondObj, _diaParts, out Rect bounds);
            if (live)
            {
                return new HudElementVisual { Bounds = bounds, Parts = _diaParts, LiveDetected = true };
            }

            float s = DiamondsScale.Value;
            float w = 96f * s;
            float h = 52f * s;
            float x = Mathf.Clamp(DiamondsOffsetX.Value * Screen.width - w * 0.5f, 6f, Mathf.Max(6f, Screen.width - w - 6f));
            float y = Mathf.Clamp(-DiamondsOffsetY.Value * Screen.height, 6f, Mathf.Max(6f, Screen.height - h - 6f));

            return new HudElementVisual
            {
                Bounds = new Rect(x, y, w, h),
                Parts = _diaParts,
                LiveDetected = false,
                HiddenByMode = true,
                HiddenReasonEn = $"Hidden in {GetWorldModeLabel(mode, false)}.",
                HiddenReasonEs = $"Oculto en {GetWorldModeLabel(mode, true)}.",
                ShortBadgeEn = $"Hidden · {GetWorldModeShort(mode, false)}",
                ShortBadgeEs = $"Oculto · {GetWorldModeShort(mode, true)}"
            };
        }

        private HudElementVisual BuildButtonsVisual(WorldMode mode)
        {
            bool live = ProjectVisibleWidgets(_buttonsObj, _btnParts, out Rect bounds);
            if (live)
            {
                return new HudElementVisual { Bounds = bounds, Parts = _btnParts, LiveDetected = true };
            }

            float s = MenuButtonsScale.Value;
            float w = 180f * s;
            float h = 54f * s;
            float x = Mathf.Clamp(MenuButtonsOffsetX.Value * Screen.width - w - 8f, 6f, Mathf.Max(6f, Screen.width - w - 6f));
            float y = Mathf.Clamp(-MenuButtonsOffsetY.Value * Screen.height, 6f, Mathf.Max(6f, Screen.height - h - 6f));

            return new HudElementVisual
            {
                Bounds = new Rect(x, y, w, h),
                Parts = _btnParts,
                LiveDetected = false,
                HiddenByMode = true,
                HiddenReasonEn = $"No active menu buttons right now in {GetWorldModeLabel(mode, false)}.",
                HiddenReasonEs = $"Sin botones de menú visibles ahora en {GetWorldModeLabel(mode, true)}.",
                ShortBadgeEn = $"Hidden · {GetWorldModeShort(mode, false)}",
                ShortBadgeEs = $"Oculto · {GetWorldModeShort(mode, true)}"
            };
        }

        private HudElementVisual BuildActionsVisual(WorldMode mode)
        {
            bool live = ProjectVisibleWidgets(_actionsObj, _actParts, out Rect bounds);
            if (live)
            {
                return new HudElementVisual { Bounds = bounds, Parts = _actParts, LiveDetected = true };
            }

            float s = ActionsScale.Value;
            float w = 148f * s;
            float h = 112f * s;
            float x = Mathf.Clamp(ActionsOffsetX.Value * Screen.width - w - 12f, 6f, Mathf.Max(6f, Screen.width - w - 6f));
            float y = Mathf.Clamp(-ActionsOffsetY.Value * Screen.height - h - 12f, 6f, Mathf.Max(6f, Screen.height - h - 6f));

            return new HudElementVisual
            {
                Bounds = new Rect(x, y, w, h),
                Parts = _actParts,
                LiveDetected = false,
                HiddenByMode = true,
                HiddenReasonEn = $"Contextual actions are hidden right now in {GetWorldModeLabel(mode, false)} (bow, mount, flight, use).",
                HiddenReasonEs = $"Acciones contextuales ocultas ahora en {GetWorldModeLabel(mode, true)} (arco, montura, vuelo, usar).",
                ShortBadgeEn = "Contextual",
                ShortBadgeEs = "Contextual"
            };
        }

        // =====================================================================
        //      LOW-OPACITY GHOST PREVIEWS FOR MODE-HIDDEN NATIVE ELEMENTS
        // =====================================================================

        private static void EnsureVisualStyles()
        {
            if (_ghostBadgeStyle != null) return;
            _ghostBadgeStyle = NanoBrand.Style(NanoBrand.Mono, 9, NanoBrand.Paper, TextAnchor.MiddleCenter);
            _ghostSubStyle = NanoBrand.Style(NanoBrand.Body, 10, NanoBrand.Mute, TextAnchor.MiddleCenter, true);
            _noticeBoxStyle = NanoBrand.Style(NanoBrand.Body, 11, NanoBrand.Warn, TextAnchor.UpperLeft, true);
            _ghostLevelStyle = new GUIStyle(GUI.skin.label)
            {
                font = NanoStyles.NativeFont ?? NanoBrand.Display,
                fontSize = 34,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private static void DrawGhostNativePreview(string id, Rect box, bool es, string shortBadge)
        {
            if (Event.current.type != EventType.Repaint || box.width < 12f || box.height < 12f) return;
            EnsureVisualStyles();
            EnsureAtlasSprites();

            // Distinct low-opacity surface (~34%) so player tells mode-hidden vs visible UI at a glance
            const float ghostAlpha = 0.36f;
            Color ghostTint = new Color(1f, 1f, 1f, ghostAlpha);
            NanoSurface.FillRounded(box, new Color(0.06f, 0.05f, 0.04f, 0.22f), 9f);

            if (id == NativeStatsId)
            {
                // Faithful 1:1 Block Story Stats layout: serif Level numeral on left,
                // 4 food icons + 2 hearts on top row, 3 mana bottles below, slim XP bar at bottom
                float sc = Mathf.Clamp(box.height / 90f, 0.5f, 1.6f);
                float pad = 6f * sc;

                // Level numeral ("1") in Block Story's serif font
                var lvlRect = new Rect(box.x + pad, box.y + pad, 42f * sc, 54f * sc);
                _ghostLevelStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(34f * sc), 16, 52);
                NanoSurface.DrawCrispLabel(lvlRect, "1", _ghostLevelStyle, new Color(0.95f, 0.92f, 0.85f, ghostAlpha * 1.15f), 0.3f);

                float iconSz = 24f * sc;
                float gap = 4f * sc;
                float rowX = lvlRect.xMax + 10f * sc;
                float topY = box.y + 8f * sc;

                // Row 1: 4 food/meat icons + separator + 2 hearts
                for (int i = 0; i < 4; i++)
                {
                    var r = new Rect(rowX + i * (iconSz + gap), topY, iconSz, iconSz);
                    if (string.IsNullOrEmpty(_sprFood) || !NanoUi.DrawNativeSprite(r, _sprFood, ghostTint))
                    {
                        NanoSurface.FillRounded(r, new Color(0.76f, 0.42f, 0.36f, ghostAlpha), iconSz * 0.42f);
                        NanoSurface.StrokeRounded(r, new Color(0.95f, 0.86f, 0.78f, ghostAlpha * 0.8f), 1f, iconSz * 0.42f);
                    }
                }

                float heartX = rowX + 4 * (iconSz + gap) + 10f * sc;
                for (int i = 0; i < 2; i++)
                {
                    var r = new Rect(heartX + i * (iconSz + gap), topY, iconSz, iconSz);
                    if (string.IsNullOrEmpty(_sprHeart) || !NanoUi.DrawNativeSprite(r, _sprHeart, ghostTint))
                    {
                        NanoSurface.FillRounded(r, new Color(0.88f, 0.22f, 0.22f, ghostAlpha * 1.1f), iconSz * 0.45f);
                        NanoSurface.StrokeRounded(r, new Color(1f, 0.72f, 0.72f, ghostAlpha * 0.7f), 1f, iconSz * 0.45f);
                    }
                }

                // Row 2: 3 blue mana potion flasks underneath food icons
                float midY = topY + iconSz + 5f * sc;
                for (int i = 0; i < 3; i++)
                {
                    var r = new Rect(rowX + i * (iconSz + gap), midY, iconSz, iconSz);
                    if (string.IsNullOrEmpty(_sprMana) || !NanoUi.DrawNativeSprite(r, _sprMana, ghostTint))
                    {
                        NanoSurface.FillRounded(r, new Color(0.32f, 0.64f, 0.96f, ghostAlpha * 1.1f), iconSz * 0.45f);
                        NanoSurface.StrokeRounded(r, new Color(0.82f, 0.92f, 1f, ghostAlpha * 0.7f), 1f, iconSz * 0.45f);
                    }
                }

                // Slim XP bar underneath mana flasks
                float xpY = midY + iconSz + 6f * sc;
                DrawGhostBar(new Rect(rowX, xpY, Mathf.Max(36f, 68f * sc), Mathf.Max(4f, 6f * sc)),
                    new Color(0.46f, 0.82f, 0.54f, ghostAlpha * 1.1f), 0.55f);
            }
            else if (id == NativeDiamondsId)
            {
                float d = Mathf.Min(box.height * 0.54f, 32f);
                var dr = new Rect(box.center.x - d * 0.5f, box.y + 4f, d, d);
                if (string.IsNullOrEmpty(_sprDiamond) || !NanoUi.DrawNativeSprite(dr, _sprDiamond, ghostTint))
                    NanoSurface.DrawRhombus(dr, new Color(0.56f, 0.82f, 0.96f, ghostAlpha * 1.2f), true);
                NanoSurface.FillRounded(new Rect(box.center.x - 16f, box.yMax - 14f, 32f, 7f),
                    new Color(0.92f, 0.87f, 0.77f, ghostAlpha), 3f);
            }
            else if (id == NativeButtonsId)
            {
                float btn = Mathf.Clamp(box.height - 12f, 20f, 38f);
                int count = Mathf.Clamp(Mathf.FloorToInt((box.width - 10f) / (btn + 6f)), 1, 4);
                float startX = box.xMax - count * (btn + 6f);
                for (int i = 0; i < count; i++)
                {
                    var br = new Rect(startX + i * (btn + 6f), box.y + (box.height - btn) * 0.5f, btn, btn);
                    NanoSurface.FillRounded(br, new Color(0.24f, 0.19f, 0.14f, ghostAlpha), 6f);
                    NanoSurface.StrokeRounded(br, new Color(0.92f, 0.87f, 0.77f, ghostAlpha * 0.85f), 1f, 6f);
                }
            }
            else if (id == NativeActionsId)
            {
                float sz = Mathf.Clamp(Mathf.Min(box.width, box.height) * 0.52f, 28f, 64f);
                var r1 = new Rect(box.center.x - sz * 0.5f, box.center.y - sz * 0.5f, sz, sz);
                if (string.IsNullOrEmpty(_sprFly) || !NanoUi.DrawNativeSprite(r1, _sprFly, ghostTint))
                {
                    NanoSurface.DrawRhombus(r1, new Color(0.24f, 0.19f, 0.14f, ghostAlpha), true);
                    NanoSurface.DrawRhombus(r1, new Color(0.92f, 0.87f, 0.77f, ghostAlpha * 0.8f), false);
                }
            }

            // Quiet bottom-centered mode badge so the user immediately understands why it's dimmed
            if (!string.IsNullOrEmpty(shortBadge))
            {
                Measure.text = shortBadge;
                float pw = Mathf.Min(box.width - 8f, _ghostBadgeStyle.CalcSize(Measure).x + 12f);
                if (pw >= 36f)
                {
                    var pill = new Rect(box.center.x - pw * 0.5f, box.yMax - 19f, pw, 15f);
                    NanoSurface.DrawChip(pill, shortBadge, _ghostBadgeStyle,
                        new Color(0.06f, 0.048f, 0.038f, 0.74f),
                        new Color(NanoBrand.Warn.r, NanoBrand.Warn.g, NanoBrand.Warn.b, 0.48f),
                        new Color(NanoBrand.Paper.r, NanoBrand.Paper.g, NanoBrand.Paper.b, 0.85f), 4f);
                }
            }
        }

        private static void DrawGhostBar(Rect r, Color fill, float ratio)
        {
            float rad = r.height * 0.45f;
            NanoSurface.FillRounded(r, new Color(0.05f, 0.04f, 0.03f, 0.36f), rad);
            NanoSurface.FillRounded(new Rect(r.x, r.y, Mathf.Max(r.height, r.width * ratio), r.height), fill, rad);
            NanoSurface.StrokeRounded(r, new Color(0.92f, 0.87f, 0.77f, 0.22f), 1f, rad);
        }

        // =====================================================================
        //    SCULPTED CONTOUR / PRECISION BRACKET OVERLAY (NO CLUNKY BOXES)
        // =====================================================================

        /// <summary>
        /// Draws precision corner L-brackets + optional sculpted per-widget sub-contours tightly fitted
        /// around the real UI silhouette instead of a heavy flat square hitbox.
        /// </summary>
        private void DrawSculptedActionField(
            string id,
            Rect box,
            List<Rect> parts,
            string name,
            string readout,
            Color accent,
            bool hovered,
            bool hasLiveVisual,
            bool hiddenByMode,
            string hiddenBadge)
        {
            if (box.width < 8f || box.height < 8f || Event.current.type != EventType.Repaint) return;
            EnsureVisualStyles();

            bool selected = string.Equals(_selectedId, id, StringComparison.OrdinalIgnoreCase);
            bool dragging = string.Equals(_draggingId, id, StringComparison.OrdinalIgnoreCase);

            // Ghost/hidden-by-mode elements use visibly lower opacity & warm muted tone to distinguish from live UI
            Color effectiveAccent = hiddenByMode ? Color.Lerp(NanoBrand.Mute, NanoBrand.Warn, 0.42f) : accent;
            float baseMul = hiddenByMode ? 0.58f : 1.0f;

            // 1. Subtle per-widget silhouette contours (when the group has distinct visual pieces like Diamond + "55")
            if (parts != null && parts.Count > 0 && (hovered || selected || dragging))
            {
                Color partFill = new Color(effectiveAccent.r, effectiveAccent.g, effectiveAccent.b, (dragging ? 0.12f : 0.06f) * baseMul);
                Color partStroke = new Color(effectiveAccent.r, effectiveAccent.g, effectiveAccent.b, (dragging ? 0.85f : 0.55f) * baseMul);
                for (int i = 0; i < parts.Count; i++)
                {
                    Rect pr = new Rect(parts[i].x - 2f, parts[i].y - 2f, parts[i].width + 4f, parts[i].height + 4f);
                    float rad = Mathf.Clamp(Mathf.Min(pr.width, pr.height) * 0.22f, 4f, 12f);
                    NanoSurface.FillRounded(pr, partFill, rad);
                    NanoSurface.StrokeRounded(pr, partStroke, 1f, rad);
                }
            }
            else if (dragging || selected || hovered)
            {
                // Single cohesive element (Lens, Wayfinder, ghost group): soft fitted wash + hairline
                float rad = Mathf.Clamp(Mathf.Min(box.width, box.height) * 0.18f, 6f, 12f);
                NanoSurface.FillRounded(box,
                    new Color(effectiveAccent.r, effectiveAccent.g, effectiveAccent.b, (dragging ? 0.10f : 0.045f) * baseMul), rad);
                if (hiddenByMode)
                {
                    NanoSurface.StrokeRounded(box,
                        new Color(effectiveAccent.r, effectiveAccent.g, effectiveAccent.b, (selected || dragging ? 0.60f : 0.36f)), 1f, rad);
                }
                else
                {
                    NanoSurface.StrokeRounded(box,
                        new Color(effectiveAccent.r, effectiveAccent.g, effectiveAccent.b, dragging ? 0.90f : (selected ? 0.65f : 0.45f)),
                        dragging ? 1.5f : 1f, rad);
                }
            }

            // 2. Sculpted viewfinder corner L-brackets hugging the silhouette (never a cluttered full square when idle!)
            float arm = Mathf.Clamp(Mathf.Min(box.width, box.height) * 0.24f, 7f, 16f);
            float thick = (dragging || selected) ? 2f : 1.5f;
            Color bracketCol = dragging
                ? NanoBrand.PaperLight
                : (selected
                    ? new Color(effectiveAccent.r, effectiveAccent.g, effectiveAccent.b, 0.94f * baseMul)
                    : (hovered
                        ? new Color(effectiveAccent.r, effectiveAccent.g, effectiveAccent.b, 0.78f * baseMul)
                        : new Color(effectiveAccent.r, effectiveAccent.g, effectiveAccent.b, 0.42f * baseMul)));
            DrawCornerBrackets(box, arm, thick, bracketCol);

            // 3. Wireframe fallback label only for third-party Nano mods without DrawPreview
            if (!hasLiveVisual && !hiddenByMode)
                NanoSurface.DrawCrispLabel(box, name, _wireLabelStyle, NanoBrand.Paper, 0.45f);

            // 4. Floating tag pill ONLY while actively hovering or dragging (vanishes immediately once released)
            if (hovered || dragging)
            {
                string tagText = hiddenByMode && !string.IsNullOrEmpty(hiddenBadge)
                    ? $"{name}  ·  {hiddenBadge}"
                    : (string.IsNullOrEmpty(readout) ? name : $"{name}  ·  {readout}");

                Measure.text = tagText;
                float tagW = Mathf.Max(58f, _pillStyle.CalcSize(Measure).x + 14f);
                const float tagH = 18f;
                float tagX = Mathf.Clamp(box.center.x - tagW * 0.5f, 6f, Mathf.Max(6f, Screen.width - tagW - 6f));
                float tagY = box.y >= tagH + 8f ? (box.y - tagH - 5f) : (box.yMax + 5f);
                if (tagY + tagH > Screen.height - 6f) tagY = box.y + 4f;

                NanoSurface.DrawChip(
                    new Rect(tagX, tagY, tagW, tagH),
                    tagText,
                    _pillStyle,
                    new Color(0.055f, 0.044f, 0.035f, hiddenByMode ? 0.84f : 0.94f),
                    new Color(effectiveAccent.r, effectiveAccent.g, effectiveAccent.b, hiddenByMode ? 0.55f : 0.75f),
                    hiddenByMode ? NanoBrand.Paper : NanoBrand.PaperLight,
                    5f);
            }
        }

        private static void DrawCornerBrackets(Rect r, float arm, float thick, Color col)
        {
            Texture2D tex = NanoUi.ColorTexture(col);
            Color prev = GUI.color;
            GUI.color = Color.white;
            // Top-Left
            GUI.DrawTexture(new Rect(r.x, r.y, arm, thick), tex);
            GUI.DrawTexture(new Rect(r.x, r.y, thick, arm), tex);
            // Top-Right
            GUI.DrawTexture(new Rect(r.xMax - arm, r.y, arm, thick), tex);
            GUI.DrawTexture(new Rect(r.xMax - thick, r.y, thick, arm), tex);
            // Bottom-Left
            GUI.DrawTexture(new Rect(r.x, r.yMax - thick, arm, thick), tex);
            GUI.DrawTexture(new Rect(r.x, r.yMax - arm, thick, arm), tex);
            // Bottom-Right
            GUI.DrawTexture(new Rect(r.xMax - arm, r.yMax - thick, arm, thick), tex);
            GUI.DrawTexture(new Rect(r.xMax - thick, r.yMax - arm, thick, arm), tex);
            GUI.color = prev;
        }
    }
}
