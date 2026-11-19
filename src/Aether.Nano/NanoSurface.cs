using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aether.Nano
{
    /// <summary>One choice in a segmented control (<see cref="NanoSurface.DrawSegmented{T}"/>).</summary>
    public readonly struct NanoSegmentOption<T>
    {
        public readonly T Value;
        public readonly string Label;
        public readonly Color? Swatch;

        public NanoSegmentOption(T value, string label, Color? swatch = null)
        {
            Value = value;
            Label = label;
            Swatch = swatch;
        }
    }

    /// <summary>
    /// Shared Aether Craft IMGUI surface and control primitives: GPU-antialiased squircle cards,
    /// multi-layer ambient shadows, specular inner bevels, vertical tonal gradients, rhombi from
    /// Aether's sign language, and custom anti-slop settings controls (switches, segmented bars,
    /// squircle sliders). Zero per-frame texture allocations.
    /// </summary>
    public static class NanoSurface
    {
        private static readonly Dictionary<ulong, Texture2D> Gradients = new Dictionary<ulong, Texture2D>();
        private static Texture2D _ellipticalShadow;
        private static Texture2D _rhombusFilled;
        private static Texture2D _rhombusOutline;
        private static Texture2D _fadeUpMask;

        private static GUIStyle _sectionTitle;
        private static GUIStyle _sectionSub;
        private static GUIStyle _rowLabel;
        private static GUIStyle _monoVal;
        private static GUIStyle _monoChip;
        private static GUIStyle _segIdle;
        private static GUIStyle _segActive;
        private static GUIStyle _btnPrimary;
        private static GUIStyle _btnSecondary;

        private static int _activeSliderId;

        // -----------------------------------------------------------------
        //                        CACHED TEXTURES
        // -----------------------------------------------------------------

        /// <summary>Cached 1x64 vertical gradient texture (top to bottom, bilinear).</summary>
        public static Texture2D VerticalGradient(Color top, Color bottom)
        {
            Color32 a = top;
            Color32 b = bottom;
            ulong key =
                ((ulong)a.r << 56) | ((ulong)a.g << 48) | ((ulong)a.b << 40) | ((ulong)a.a << 32) |
                ((ulong)b.r << 24) | ((ulong)b.g << 16) | ((ulong)b.b << 8) | b.a;

            if (Gradients.TryGetValue(key, out Texture2D cached) && cached != null)
                return cached;

            const int h = 64;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            // Texture2D y=0 is bottom, y=h-1 is top.
            var pixels = new Color32[h];
            for (int y = 0; y < h; y++)
            {
                float t = y / (float)(h - 1);
                pixels[y] = Color32.Lerp(b, a, t);
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Gradients[key] = tex;
            return tex;
        }

        /// <summary>Soft radial elliptical contact shadow for grounding 3D block icons without a box.</summary>
        public static Texture2D EllipticalShadow()
        {
            if (_ellipticalShadow != null) return _ellipticalShadow;

            const int w = 64;
            const int h = 24;
            var pixels = new Color32[w * h];
            float cx = (w - 1) * 0.5f;
            float cy = (h - 1) * 0.5f;

            for (int y = 0; y < h; y++)
            {
                float ny = (y - cy) / cy;
                for (int x = 0; x < w; x++)
                {
                    float nx = (x - cx) / cx;
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float falloff = Mathf.Clamp01(1f - d);
                    float a = falloff * falloff * 0.85f;
                    pixels[y * w + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _ellipticalShadow = tex;
            return tex;
        }

        /// <summary>
        /// Crisp anti-aliased rhombus glyph (32x32 white mask; tint via <see cref="GUI.color"/>).
        /// Matches Aether Monitor's sign vocabulary: filled = active/mod/available, outlined = secondary.
        /// </summary>
        public static Texture2D Rhombus(bool filled)
        {
            if (filled && _rhombusFilled != null) return _rhombusFilled;
            if (!filled && _rhombusOutline != null) return _rhombusOutline;

            const int size = 32;
            const float half = (size - 1) * 0.5f;
            const float radius = 12.0f;
            const float stroke = 3.2f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // L1 distance defines an exact diamond; divide by sqrt(2) for Euclidean edge AA.
                    float dist = (Mathf.Abs(x - half) + Mathf.Abs(y - half) - radius) * 0.70710678f;
                    float alpha;
                    if (filled)
                    {
                        alpha = Mathf.Clamp01(0.5f - dist);
                    }
                    else
                    {
                        float outer = Mathf.Clamp01(0.5f - dist);
                        float inner = Mathf.Clamp01(0.5f - (dist + stroke));
                        alpha = Mathf.Clamp01(outer - inner);
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            if (filled) _rhombusFilled = tex;
            else _rhombusOutline = tex;
            return tex;
        }

        // -----------------------------------------------------------------
        //                    GPU SQUIRCLE / SURFACE PRIMITIVES
        // -----------------------------------------------------------------

        /// <summary>Filled rounded rectangle using Unity's GPU smooth-corners IMGUI path.</summary>
        public static void FillRounded(Rect rect, Color color, float radius)
        {
            if (rect.width <= 0.5f || rect.height <= 0.5f || color.a <= 0.002f) return;
            float r = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);
            Color prev = GUI.color;
            color.a *= prev.a;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, Vector4.zero, new Vector4(r, r, r, r));
            GUI.color = prev;
        }

        /// <summary>Filled rounded rectangle with per-corner radii (topLeft, topRight, bottomRight, bottomLeft).</summary>
        public static void FillRounded(Rect rect, Color color, Vector4 radii)
        {
            if (rect.width <= 0.5f || rect.height <= 0.5f || color.a <= 0.002f) return;
            float maxR = Mathf.Min(rect.width, rect.height) * 0.5f;
            var r = new Vector4(
                Mathf.Min(radii.x, maxR),
                Mathf.Min(radii.y, maxR),
                Mathf.Min(radii.z, maxR),
                Mathf.Min(radii.w, maxR));
            Color prev = GUI.color;
            color.a *= prev.a;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, Vector4.zero, r);
            GUI.color = prev;
        }

        /// <summary>Filled rounded rectangle with a subtle vertical tonal gradient (cached texture).</summary>
        public static void FillRoundedGradient(Rect rect, Color top, Color bottom, float radius, float alphaScale = 1f)
        {
            if (rect.width <= 0.5f || rect.height <= 0.5f || alphaScale <= 0.002f) return;
            float r = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);
            Color prev = GUI.color;
            var tint = new Color(1f, 1f, 1f, alphaScale * prev.a);
            GUI.color = Color.white;
            GUI.DrawTexture(rect, VerticalGradient(top, bottom), ScaleMode.StretchToFill, true, 0f, tint, Vector4.zero, new Vector4(r, r, r, r));
            GUI.color = prev;
        }

        /// <summary>Anti-aliased rounded hairline border.</summary>
        public static void StrokeRounded(Rect rect, Color color, float width, float radius)
        {
            if (rect.width <= 0.5f || rect.height <= 0.5f || color.a <= 0.002f) return;
            float r = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);
            Color prev = GUI.color;
            color.a *= prev.a;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, new Vector4(width, width, width, width), new Vector4(r, r, r, r));
            GUI.color = prev;
        }

        /// <summary>Multi-layer soft ambient shadow beneath a floating HUD card.</summary>
        public static void DrawDropShadow(Rect rect, float radius, float opacity)
        {
            if (opacity <= 0.01f) return;
            // Three progressive offset layers: crisp contact shadow -> medium lift -> ambient bloom.
            FillRounded(new Rect(rect.x - 1f, rect.y + 1.5f, rect.width + 2f, rect.height + 1.5f),
                new Color(0f, 0f, 0f, 0.30f * opacity), radius + 1f);
            FillRounded(new Rect(rect.x - 3f, rect.y + 3.5f, rect.width + 6f, rect.height + 3.5f),
                new Color(0f, 0f, 0f, 0.16f * opacity), radius + 3f);
            FillRounded(new Rect(rect.x - 6f, rect.y + 6f, rect.width + 12f, rect.height + 6f),
                new Color(0f, 0f, 0f, 0.08f * opacity), radius + 6f);
        }

        /// <summary>
        /// Signature Aether floating card surface: soft multi-layer drop shadow, vertical tonal
        /// gradient inside a GPU squircle, 1px specular inner bevel and crisp hairline border.
        /// Optional top hairline rule is off by default (zero decorative stripes).
        /// </summary>
        public static void DrawCardSurface(
            Rect rect,
            Color bgTop,
            Color bgBottom,
            Color border,
            Color topRule,
            Color specularHighlight,
            float radius,
            float opacity,
            float ruleAlpha = 0f,
            float washAlpha = 0f)
        {
            if (opacity <= 0.005f) return;

            // 1. Multi-layer volumetric shadow
            DrawDropShadow(rect, radius, opacity);

            // 2. Rounded vertical-gradient surface
            FillRoundedGradient(rect, bgTop, bgBottom, radius, opacity);

            // 3. Optional subtle top ambient wash (only when explicitly requested)
            if (washAlpha > 0.001f && ruleAlpha > 0.001f)
            {
                float washH = Mathf.Min(18f, rect.height * 0.38f);
                var washRect = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, washH);
                Color washTop = new Color(topRule.r, topRule.g, topRule.b, washAlpha * ruleAlpha * opacity);
                Color washBot = new Color(topRule.r, topRule.g, topRule.b, 0f);
                Color prev = GUI.color;
                var tint = new Color(1f, 1f, 1f, prev.a);
                float r = Mathf.Max(0f, radius - 1f);
                GUI.color = Color.white;
                GUI.DrawTexture(washRect, VerticalGradient(washTop, washBot), ScaleMode.StretchToFill, true, 0f, tint, Vector4.zero, new Vector4(r, r, 0f, 0f));
                GUI.color = prev;
            }

            // 4. Specular inner highlight along top inner bevel
            float bevelInset = Mathf.Min(radius * 0.72f, rect.width * 0.25f);
            if (specularHighlight.a > 0.005f && rect.width > bevelInset * 2f + 4f)
            {
                Color spec = specularHighlight;
                spec.a *= opacity;
                FillRounded(new Rect(rect.x + bevelInset, rect.y + 1f, rect.width - bevelInset * 2f, 1f), spec, 0.5f);
            }

            // 5. Hairline outer frame
            Color borderCol = border;
            borderCol.a *= opacity;
            StrokeRounded(rect, borderCol, 1f, radius);

            // 6. Optional top hairline rule (disabled by default)
            if (ruleAlpha > 0.005f && topRule.a > 0.005f)
            {
                float ruleInset = Mathf.Min(radius * 0.65f, 9f);
                float ruleW = Mathf.Max(0f, rect.width - ruleInset * 2f);
                if (ruleW > 8f)
                {
                    Color rule = topRule;
                    rule.a *= opacity * ruleAlpha;
                    FillRounded(new Rect(rect.x + ruleInset, rect.y, ruleW, 2f), rule, 1f);
                }
            }
        }

        /// <summary>Single cached 1x32 white mask fading from opaque at bottom to transparent at top.</summary>
        private static Texture2D FadeUpMask()
        {
            if (_fadeUpMask != null) return _fadeUpMask;
            const int h = 32;
            var tex = new Texture2D(1, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[h];
            for (int y = 0; y < h; y++)
            {
                // y=0 is bottom (opaque), y=h-1 is top (transparent)
                float t = 1f - y / (float)(h - 1);
                pixels[y] = new Color32(255, 255, 255, (byte)(t * t * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _fadeUpMask = tex;
            return tex;
        }

        /// <summary>
        /// Bottom-edge progress strip embedded inside a rounded card (e.g. Lens block destruction
        /// progress). Draws nothing when <paramref name="progress"/> is 0. Zero texture allocations.
        /// </summary>
        public static void DrawBottomEdgeProgress(Rect cardRect, float progress, Color accent, float cardRadius, float opacity = 1f)
        {
            progress = Mathf.Clamp01(progress);
            if (progress <= 0.002f || opacity <= 0.005f) return;

            float inset = Mathf.Min(cardRadius * 0.65f, 9f);
            float trackW = Mathf.Max(0f, cardRect.width - inset * 2f);
            if (trackW < 8f) return;

            const float barH = 3f;
            float barX = cardRect.x + inset;
            float barY = cardRect.yMax - barH - 1f;
            float fillW = Mathf.Max(4f, trackW * progress);

            // Subtle recessed track
            FillRounded(new Rect(barX, barY, trackW, barH), new Color(0f, 0f, 0f, 0.40f * opacity), 1.5f);

            // Warm upward glow wash directly above the active fill (single cached mask, tinted on GPU)
            float glowH = 9f;
            Color prev = GUI.color;
            Color glowTint = new Color(accent.r, accent.g, accent.b, (0.14f + progress * 0.12f) * opacity * prev.a);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(barX, barY - glowH, fillW, glowH), FadeUpMask(), ScaleMode.StretchToFill, true, 0f, glowTint, Vector4.zero, Vector4.zero);
            GUI.color = prev;

            // Active progress bar (warms subtly near completion)
            Color tipColor = Color.Lerp(accent, NanoBrand.PaperLight, progress * 0.25f);
            tipColor.a = opacity;
            FillRounded(new Rect(barX, barY, fillW, barH), tipColor, 1.5f);
        }

        /// <summary>Recessed (debossed) squircle slot for tool capsules, search fields, or slider tracks.</summary>
        public static void DrawDebossedSlot(Rect rect, Color fill, Color border, float radius = 8f, float opacity = 1f)
        {
            if (opacity <= 0.005f) return;
            Color f = fill; f.a *= opacity;
            Color b = border; b.a *= opacity;
            FillRounded(rect, f, radius);

            // Subtle top inner shadow + faint bottom catchlight for debossed depth.
            float inset = Mathf.Min(radius * 0.65f, rect.width * 0.25f);
            if (rect.width > inset * 2f + 4f)
            {
                FillRounded(new Rect(rect.x + inset, rect.y + 1f, rect.width - inset * 2f, 1f),
                    new Color(0f, 0f, 0f, 0.28f * opacity), 0.5f);
                FillRounded(new Rect(rect.x + inset, rect.yMax - 1f, rect.width - inset * 2f, 1f),
                    new Color(1f, 0.96f, 0.90f, 0.06f * opacity), 0.5f);
            }

            StrokeRounded(rect, b, 1f, radius);
        }

        /// <summary>Compact squircle chip (e.g. level pill, coordinates tag, keycap).</summary>
        public static void DrawChip(Rect rect, string text, GUIStyle style, Color bg, Color border, Color textColor, float radius = 5f, float opacity = 1f)
        {
            if (opacity <= 0.005f) return;
            Color bgC = bg; bgC.a *= opacity;
            Color bdC = border; bdC.a *= opacity;
            FillRounded(rect, bgC, radius);
            StrokeRounded(rect, bdC, 1f, radius);

            if (!string.IsNullOrEmpty(text) && style != null)
            {
                Color prevText = style.normal.textColor;
                Color tc = textColor; tc.a *= opacity * GUI.color.a;
                style.normal.textColor = tc;
                GUI.Label(rect, text, style);
                style.normal.textColor = prevText;
            }
        }

        /// <summary>Beveled rounded progress bar with optional trailing damage ghost fill.</summary>
        public static void DrawProgressBar(
            Rect rect,
            float fillRatio,
            float ghostRatio,
            Color trackBg,
            Color border,
            Color fillColor,
            Color ghostColor,
            float radius = 4f,
            float opacity = 1f)
        {
            fillRatio = Mathf.Clamp01(fillRatio);
            ghostRatio = Mathf.Clamp01(ghostRatio);

            DrawDebossedSlot(rect, trackBg, border, radius, opacity);

            float innerX = rect.x + 1.5f;
            float innerY = rect.y + 1.5f;
            float innerW = Mathf.Max(0f, rect.width - 3f);
            float innerH = Mathf.Max(1f, rect.height - 3f);
            float innerR = Mathf.Max(1f, radius - 1.5f);

            if (ghostRatio > fillRatio + 0.005f)
            {
                Color gc = ghostColor; gc.a *= opacity;
                FillRounded(new Rect(innerX, innerY, innerW * ghostRatio, innerH), gc, innerR);
            }

            if (fillRatio > 0.002f)
            {
                float fw = Mathf.Max(innerH * 0.5f, innerW * fillRatio);
                fw = Mathf.Min(fw, innerW);
                Color top = Color.Lerp(fillColor, Color.white, 0.14f);
                top.a = fillColor.a;
                Color bot = Color.Lerp(fillColor, Color.black, 0.18f);
                bot.a = fillColor.a;
                FillRoundedGradient(new Rect(innerX, innerY, fw, innerH), top, bot, innerR, opacity);

                // Crisp 1px top specular glaze on the active fill
                if (fw > 6f && innerH >= 5f)
                {
                    FillRounded(new Rect(innerX + 2f, innerY + 0.5f, fw - 4f, 1f),
                        new Color(1f, 1f, 1f, 0.24f * opacity), 0.5f);
                }
            }
        }

        /// <summary>Draws a soft text label with a gentle 1px vertical drop shadow (no harsh thick offset).</summary>
        public static void DrawCrispLabel(Rect rect, string text, GUIStyle style, Color color, float shadowAlpha = 0.55f)
        {
            if (string.IsNullOrEmpty(text) || style == null) return;
            Color prev = style.normal.textColor;
            float a = GUI.color.a;
            if (shadowAlpha > 0.01f)
            {
                style.normal.textColor = new Color(0f, 0f, 0f, shadowAlpha * color.a * a);
                GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), text, style);
            }
            style.normal.textColor = new Color(color.r, color.g, color.b, color.a * a);
            GUI.Label(rect, text, style);
            style.normal.textColor = prev;
        }

        /// <summary>Draws a tinted rhombus sign (filled or outlined) centered in <paramref name="rect"/>.</summary>
        public static void DrawRhombus(Rect rect, Color color, bool filled = true)
        {
            Color prev = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * prev.a);
            GUI.DrawTexture(rect, Rhombus(filled), ScaleMode.ScaleToFit, true);
            GUI.color = prev;
        }

        // -----------------------------------------------------------------
        //                 CUSTOM ANTI-SLOP SETTINGS CONTROLS
        // -----------------------------------------------------------------

        private static void EnsureControlStyles()
        {
            if (_sectionTitle != null) return;
            _sectionTitle = NanoBrand.Style(NanoBrand.Mono, 11, NanoBrand.Ember);
            _sectionSub = NanoBrand.Style(NanoBrand.Body, 11, NanoBrand.Mute, TextAnchor.MiddleRight);
            _rowLabel = NanoBrand.Style(NanoBrand.BodySemi, 13, NanoBrand.Paper);
            _monoVal = NanoBrand.Style(NanoBrand.Mono, 12, NanoBrand.Paper, TextAnchor.MiddleRight);
            _monoChip = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Ember, TextAnchor.MiddleCenter);
            _segIdle = NanoBrand.Style(NanoBrand.BodySemi, 12, NanoBrand.Mute, TextAnchor.MiddleCenter);
            _segActive = NanoBrand.Style(NanoBrand.BodySemi, 12, NanoBrand.Paper, TextAnchor.MiddleCenter);
            _btnPrimary = NanoBrand.Style(NanoBrand.BodySemi, 12, new Color(0.08f, 0.05f, 0.02f, 1f), TextAnchor.MiddleCenter);
            _btnSecondary = NanoBrand.Style(NanoBrand.BodySemi, 12, NanoBrand.Paper, TextAnchor.MiddleCenter);
        }

        /// <summary>Editorial uppercase mono kicker with an ember hairline accent beneath.</summary>
        public static float DrawSectionHeader(Rect area, float y, string title, string rightNote = null)
        {
            EnsureControlStyles();
            var labelRect = new Rect(area.x, y, area.width, 18f);
            DrawCrispLabel(labelRect, title.ToUpperInvariant(), _sectionTitle, NanoBrand.Ember, 0.35f);
            if (!string.IsNullOrEmpty(rightNote))
                DrawCrispLabel(labelRect, rightNote, _sectionSub, NanoBrand.Mute, 0.25f);

            float lineY = y + 20f;
            FillRounded(new Rect(area.x, lineY, area.width, 1f), NanoBrand.Line, 0.5f);
            return y + 28f;
        }

        /// <summary>Tactile squircle row with a custom Aether switch (ember track + paper knob).</summary>
        public static bool DrawToggleRow(Rect rect, string label, bool value, string keyHint = null)
        {
            EnsureControlStyles();
            bool hover = rect.Contains(Event.current.mousePosition);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                Color bg = hover
                    ? new Color(0.14f, 0.11f, 0.09f, 0.90f)
                    : new Color(0.08f, 0.065f, 0.052f, 0.72f);
                Color border = hover ? new Color(0.30f, 0.23f, 0.17f, 0.95f) : NanoBrand.Line;
                FillRounded(rect, bg, 8f);
                StrokeRounded(rect, border, 1f, 8f);

                float switchW = 38f;
                float switchH = 20f;
                float switchX = rect.xMax - switchW - 10f;
                float switchY = rect.y + (rect.height - switchH) * 0.5f;

                float rightLimit = switchX - 8f;
                if (!string.IsNullOrEmpty(keyHint))
                {
                    float keyW = Mathf.Max(28f, _monoChip.CalcSize(new GUIContent(keyHint)).x + 12f);
                    var keyRect = new Rect(rightLimit - keyW, rect.y + (rect.height - 18f) * 0.5f, keyW, 18f);
                    DrawChip(keyRect, keyHint, _monoChip, NanoBrand.InkSoft, NanoBrand.Line, NanoBrand.Ember, 4f);
                    rightLimit = keyRect.x - 8f;
                }

                DrawCrispLabel(new Rect(rect.x + 11f, rect.y, Mathf.Max(40f, rightLimit - rect.x - 11f), rect.height),
                    label, _rowLabel, value ? NanoBrand.Paper : NanoBrand.Mute, 0.35f);

                // Custom switch track + geometric squircle knob
                var trackRect = new Rect(switchX, switchY, switchW, switchH);
                Color trackFill = value ? NanoBrand.Ember : NanoBrand.InkSoft;
                Color trackBorder = value ? new Color(1f, 0.70f, 0.48f, 0.55f) : NanoBrand.Line;
                FillRounded(trackRect, trackFill, switchH * 0.5f);
                StrokeRounded(trackRect, trackBorder, 1f, switchH * 0.5f);

                float knobSize = switchH - 6f;
                float knobX = value ? (trackRect.xMax - knobSize - 3f) : (trackRect.x + 3f);
                var knobRect = new Rect(knobX, trackRect.y + 3f, knobSize, knobSize);
                Color knobColor = value ? new Color(0.11f, 0.07f, 0.04f, 1f) : NanoBrand.Mute;
                FillRounded(knobRect, knobColor, 4.5f);
            }

            return clicked ? !value : value;
        }

        /// <summary>Recessed squircle segmented control with optional per-option color swatches.</summary>
        public static bool DrawSegmented<T>(Rect rect, NanoSegmentOption<T>[] options, T current, out T next)
        {
            EnsureControlStyles();
            next = current;
            if (options == null || options.Length == 0) return false;

            bool changed = false;
            if (Event.current.type == EventType.Repaint)
                DrawDebossedSlot(rect, new Color(0.06f, 0.048f, 0.038f, 0.88f), NanoBrand.Line, 8f);

            float pad = 3f;
            float gap = 3f;
            float segW = (rect.width - pad * 2f - gap * (options.Length - 1)) / options.Length;
            float segH = rect.height - pad * 2f;

            for (int i = 0; i < options.Length; i++)
            {
                var opt = options[i];
                var segRect = new Rect(rect.x + pad + i * (segW + gap), rect.y + pad, segW, segH);
                bool active = EqualityComparer<T>.Default.Equals(opt.Value, current);
                bool hover = segRect.Contains(Event.current.mousePosition);

                if (GUI.Button(segRect, GUIContent.none, GUIStyle.none) && !active)
                {
                    next = opt.Value;
                    changed = true;
                }

                if (Event.current.type == EventType.Repaint)
                {
                    if (active)
                    {
                        Color accent = opt.Swatch ?? NanoBrand.Ember;
                        FillRoundedGradient(segRect, new Color(0.20f, 0.155f, 0.12f, 0.98f), new Color(0.13f, 0.10f, 0.078f, 0.98f), 6f);
                        FillRounded(new Rect(segRect.x + 5f, segRect.y + 1f, Mathf.Max(4f, segRect.width - 10f), 1f),
                            new Color(1f, 0.96f, 0.90f, 0.10f), 0.5f);
                        StrokeRounded(segRect, new Color(accent.r, accent.g, accent.b, 0.52f), 1f, 6f);
                    }
                    else if (hover)
                    {
                        FillRounded(segRect, new Color(0.14f, 0.11f, 0.085f, 0.65f), 6f);
                    }

                    if (opt.Swatch.HasValue)
                    {
                        float dot = 7f;
                        GUIStyle st = active ? _segActive : _segIdle;
                        float labelW = Mathf.Min(segRect.width - 18f, st.CalcSize(new GUIContent(opt.Label)).x);
                        float totalW = dot + 5f + labelW;
                        float startX = segRect.x + (segRect.width - totalW) * 0.5f;
                        FillRounded(new Rect(startX, segRect.y + (segRect.height - dot) * 0.5f, dot, dot), opt.Swatch.Value, 2.5f);
                        DrawCrispLabel(new Rect(startX + dot + 5f, segRect.y, labelW + 4f, segRect.height),
                            opt.Label, st, active ? NanoBrand.Paper : (hover ? NanoBrand.Paper : NanoBrand.Mute), 0.3f);
                    }
                    else
                    {
                        DrawCrispLabel(segRect, opt.Label, active ? _segActive : _segIdle,
                            active ? NanoBrand.Paper : (hover ? NanoBrand.Paper : NanoBrand.Mute), 0.3f);
                    }
                }
            }

            return changed;
        }

        /// <summary>
        /// Custom squircle slider replacing Unity's native GUI.HorizontalSlider: debossed track,
        /// ember progress fill, beveled geometric thumb, and right-aligned JetBrains Mono readout.
        /// </summary>
        public static float DrawSlider(Rect rect, string label, float value, float min, float max, string valueText, float step = 0f, float labelWidth = 122f)
        {
            EnsureControlStyles();
            const float valueW = 64f;
            float trackX = rect.x + labelWidth;
            float trackW = Mathf.Max(60f, rect.width - labelWidth - valueW - 10f);
            var hitRect = new Rect(trackX - 6f, rect.y, trackW + 12f, rect.height);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event ev = Event.current;
            float nextValue = value;

            switch (ev.type)
            {
                case EventType.MouseDown:
                    if (ev.button == 0 && hitRect.Contains(ev.mousePosition))
                    {
                        _activeSliderId = id;
                        GUIUtility.hotControl = id;
                        nextValue = SampleSlider(ev.mousePosition.x, trackX, trackW, min, max, step);
                        ev.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id && _activeSliderId == id)
                    {
                        nextValue = SampleSlider(ev.mousePosition.x, trackX, trackW, min, max, step);
                        ev.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id && _activeSliderId == id)
                    {
                        _activeSliderId = 0;
                        GUIUtility.hotControl = 0;
                        ev.Use();
                    }
                    break;
            }

            if (ev.type == EventType.Repaint)
            {
                bool active = GUIUtility.hotControl == id;
                bool hover = hitRect.Contains(ev.mousePosition);
                float t = max > min ? Mathf.Clamp01((nextValue - min) / (max - min)) : 0f;

                DrawCrispLabel(new Rect(rect.x, rect.y, labelWidth - 6f, rect.height), label, _rowLabel,
                    active || hover ? NanoBrand.Paper : NanoBrand.Mute, 0.35f);

                // Recessed track
                float trackH = 6f;
                float trackY = rect.y + (rect.height - trackH) * 0.5f;
                var trackRect = new Rect(trackX, trackY, trackW, trackH);
                DrawDebossedSlot(trackRect, new Color(0.05f, 0.04f, 0.03f, 0.92f), NanoBrand.Line, 3f);

                // Active ember fill
                float fillW = trackW * t;
                if (fillW > 2f)
                {
                    FillRoundedGradient(new Rect(trackX + 1f, trackY + 1f, Mathf.Max(2f, fillW - 1f), trackH - 2f),
                        NanoBrand.EmberSoft, NanoBrand.EmberDeep, 2f);
                }

                // Geometric squircle thumb
                float thumbSize = active ? 15f : 13f;
                float thumbX = trackX + fillW - thumbSize * 0.5f;
                float thumbY = rect.y + (rect.height - thumbSize) * 0.5f;
                var thumbRect = new Rect(thumbX, thumbY, thumbSize, thumbSize);

                if (active || hover)
                {
                    FillRounded(new Rect(thumbRect.x - 3f, thumbRect.y - 3f, thumbRect.width + 6f, thumbRect.height + 6f),
                        new Color(NanoBrand.Ember.r, NanoBrand.Ember.g, NanoBrand.Ember.b, active ? 0.24f : 0.14f), 7f);
                }

                FillRoundedGradient(thumbRect, NanoBrand.PaperLight, NanoBrand.Paper, 4.5f);
                StrokeRounded(thumbRect, active ? NanoBrand.Ember : new Color(0.20f, 0.15f, 0.11f, 0.9f), 1.2f, 4.5f);

                // Right-aligned JetBrains Mono value readout
                DrawCrispLabel(new Rect(rect.xMax - valueW, rect.y, valueW, rect.height),
                    valueText, _monoVal, active ? NanoBrand.Ember : NanoBrand.Paper, 0.35f);
            }

            return nextValue;
        }

        private static float SampleSlider(float mouseX, float trackX, float trackW, float min, float max, float step)
        {
            float t = Mathf.Clamp01((mouseX - trackX) / Mathf.Max(1f, trackW));
            float raw = Mathf.Lerp(min, max, t);
            if (step > 0f) raw = Mathf.Round(raw / step) * step;
            return Mathf.Clamp(raw, min, max);
        }

        /// <summary>Tactile Aether squircle action button (primary ember or secondary ink).</summary>
        public static bool DrawActionButton(Rect rect, string label, bool primary = false)
        {
            EnsureControlStyles();
            bool hover = rect.Contains(Event.current.mousePosition);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                if (primary)
                {
                    Color top = hover ? NanoBrand.EmberSoft : NanoBrand.Ember;
                    Color bot = hover ? NanoBrand.Ember : NanoBrand.EmberDeep;
                    FillRoundedGradient(rect, top, bot, 8f);
                    FillRounded(new Rect(rect.x + 6f, rect.y + 1f, Mathf.Max(4f, rect.width - 12f), 1f),
                        new Color(1f, 1f, 1f, 0.30f), 0.5f);
                    StrokeRounded(rect, new Color(1f, 0.72f, 0.52f, 0.55f), 1f, 8f);
                    DrawCrispLabel(rect, label, _btnPrimary, new Color(0.08f, 0.04f, 0.01f, 1f), 0f);
                }
                else
                {
                    Color top = hover ? new Color(0.18f, 0.14f, 0.11f, 0.96f) : new Color(0.12f, 0.095f, 0.075f, 0.92f);
                    Color bot = hover ? new Color(0.13f, 0.10f, 0.08f, 0.96f) : new Color(0.08f, 0.065f, 0.052f, 0.92f);
                    FillRoundedGradient(rect, top, bot, 8f);
                    StrokeRounded(rect, hover ? NanoBrand.Ember : NanoBrand.Line, 1f, 8f);
                    DrawCrispLabel(rect, label, _btnSecondary, hover ? NanoBrand.PaperLight : NanoBrand.Paper, 0.35f);
                }
            }

            return clicked;
        }

        // =====================================================================
        //           CUSTOM AETHER SCROLLVIEW (ZERO UNITY NATIVE BARS)
        // =====================================================================

        private struct ScrollFrame
        {
            public Rect ViewRect;
            public float ContentHeight;
            public float ScrollY;
            public int ControlId;
            public bool HasOverflow;
        }

        private static readonly ScrollFrame[] ScrollStack = new ScrollFrame[8];
        private static int _scrollDepth;
        private static float _scrollDragGrabOffset;

        /// <summary>
        /// Begins a custom Aether vertical scroll view with zero Unity native grey scrollbars.
        /// Draws a razor-slim floating squircle scrollbar in <see cref="EndScrollView"/> with
        /// direct thumb dragging, track click-to-jump, and smooth wheel scrolling.
        /// </summary>
        public static Vector2 BeginScrollView(Rect viewRect, Vector2 scrollPos, float contentHeight, out float innerWidth)
        {
            float maxScroll = Mathf.Max(0f, contentHeight - viewRect.height);
            bool hasOverflow = maxScroll > 1f;
            innerWidth = hasOverflow ? Mathf.Max(20f, viewRect.width - 11f) : viewRect.width;

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            scrollPos.x = 0f;
            scrollPos.y = Mathf.Clamp(scrollPos.y, 0f, maxScroll);

            if (hasOverflow)
            {
                Event ev = Event.current;
                Rect trackHit = new Rect(viewRect.xMax - 11f, viewRect.y, 11f, viewRect.height);
                Rect thumbRect = ComputeScrollThumbRect(viewRect, contentHeight, scrollPos.y, 6f);

                if (ev.type == EventType.MouseDown && ev.button == 0 && trackHit.Contains(ev.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    if (thumbRect.Contains(ev.mousePosition))
                    {
                        _scrollDragGrabOffset = ev.mousePosition.y - thumbRect.y;
                    }
                    else
                    {
                        _scrollDragGrabOffset = thumbRect.height * 0.5f;
                        scrollPos.y = ScrollFromTrackMouse(ev.mousePosition.y, viewRect, thumbRect.height, maxScroll, _scrollDragGrabOffset);
                    }
                    ev.Use();
                }
                else if (ev.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
                {
                    scrollPos.y = ScrollFromTrackMouse(ev.mousePosition.y, viewRect, thumbRect.height, maxScroll, _scrollDragGrabOffset);
                    ev.Use();
                }
                else if (ev.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    ev.Use();
                }
                else if (ev.type == EventType.ScrollWheel && viewRect.Contains(ev.mousePosition))
                {
                    scrollPos.y = Mathf.Clamp(scrollPos.y + ev.delta.y * 28f, 0f, maxScroll);
                    ev.Use();
                }
            }

            if (_scrollDepth < ScrollStack.Length)
            {
                ScrollStack[_scrollDepth++] = new ScrollFrame
                {
                    ViewRect = viewRect,
                    ContentHeight = contentHeight,
                    ScrollY = scrollPos.y,
                    ControlId = controlId,
                    HasOverflow = hasOverflow
                };
            }

            // Use Unity's clipping & coordinate translation with both native scrollbars completely hidden
            scrollPos = GUI.BeginScrollView(
                viewRect,
                scrollPos,
                new Rect(0f, 0f, innerWidth, Mathf.Max(viewRect.height, contentHeight)),
                GUIStyle.none,
                GUIStyle.none);

            scrollPos.x = 0f;
            scrollPos.y = Mathf.Clamp(scrollPos.y, 0f, maxScroll);
            if (_scrollDepth > 0)
                ScrollStack[_scrollDepth - 1].ScrollY = scrollPos.y;

            return scrollPos;
        }

        /// <summary>Ends a custom Aether scroll view and paints the slim floating scrollbar on Repaint.</summary>
        public static void EndScrollView()
        {
            GUI.EndScrollView();
            if (_scrollDepth <= 0) return;

            ScrollFrame frame = ScrollStack[--_scrollDepth];
            if (!frame.HasOverflow || Event.current.type != EventType.Repaint) return;

            Rect view = frame.ViewRect;
            Rect trackHit = new Rect(view.xMax - 12f, view.y, 12f, view.height);
            bool active = GUIUtility.hotControl == frame.ControlId;
            bool hover = active || trackHit.Contains(Event.current.mousePosition);

            // Slim vertical track (4px idle -> 5px on hover/drag)
            float trackW = hover ? 5f : 4f;
            float padY = 4f;
            Rect trackRect = new Rect(view.xMax - trackW - 2f, view.y + padY, trackW, Mathf.Max(8f, view.height - padY * 2f));
            FillRounded(trackRect, new Color(0.03f, 0.024f, 0.020f, hover ? 0.65f : 0.40f), trackW * 0.5f);

            // Smooth rounded pill thumb (warm parchment idle -> Ember when hovered/dragged)
            float thumbW = hover ? 5f : 4f;
            Rect thumbRect = ComputeScrollThumbRect(view, frame.ContentHeight, frame.ScrollY, thumbW);
            Color thumbCol = active
                ? NanoBrand.Ember
                : (hover
                    ? Color.Lerp(NanoBrand.Paper, NanoBrand.Ember, 0.55f)
                    : new Color(NanoBrand.Mute.r, NanoBrand.Mute.g, NanoBrand.Mute.b, 0.58f));
            FillRounded(thumbRect, thumbCol, thumbW * 0.5f);
        }

        private static Rect ComputeScrollThumbRect(Rect view, float contentH, float scrollY, float thumbW)
        {
            const float padY = 4f;
            float trackH = Mathf.Max(16f, view.height - padY * 2f);
            float ratio = Mathf.Clamp01(view.height / Mathf.Max(1f, contentH));
            float thumbH = Mathf.Clamp(trackH * ratio, 26f, trackH);
            float maxScroll = Mathf.Max(1f, contentH - view.height);
            float t = Mathf.Clamp01(scrollY / maxScroll);
            float thumbY = view.y + padY + (trackH - thumbH) * t;
            return new Rect(view.xMax - thumbW - 2f, thumbY, thumbW, thumbH);
        }

        private static float ScrollFromTrackMouse(float mouseY, Rect view, float thumbH, float maxScroll, float grabOffset)
        {
            const float padY = 4f;
            float trackTop = view.y + padY;
            float travel = Mathf.Max(1f, view.height - padY * 2f - thumbH);
            float t = Mathf.Clamp01((mouseY - grabOffset - trackTop) / travel);
            return t * maxScroll;
        }
    }
}
