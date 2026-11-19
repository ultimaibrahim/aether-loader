using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aether.Nano
{
    /// <summary>
    /// Shared zero-allocation cinematic motion engine for Aether and every mod built on Aether Nano.
    /// Uses unscaled time with single-step-per-frame gating (immune to multi-pass OnGUI double stepping)
    /// and cubic-bezier(0.2, 1, 0.3, 1)-inspired ease-out-quart settle curves.
    /// </summary>
    public static class NanoMotion
    {
        private sealed class Channel
        {
            public float Raw;
            public float Value;
            public int LastFrame = -1;
            public bool WasOpening = true;
        }

        private static readonly Dictionary<string, Channel> Channels = new Dictionary<string, Channel>(StringComparer.Ordinal);

        /// <summary>When true, all transitions snap instantly (0ms) for reduced-motion preference.</summary>
        public static bool ReduceMotion { get; set; }

        /// <summary>
        /// Steps a named open/close transition at most once per <see cref="Time.frameCount"/> and returns
        /// an eased value in [0, 1]. Supports smooth exit animations even after <paramref name="isOpen"/> becomes false.
        /// </summary>
        public static float Sample(string id, bool isOpen, float openSeconds = 0.21f, float closeSeconds = 0.14f)
        {
            if (string.IsNullOrEmpty(id)) return isOpen ? 1f : 0f;

            if (!Channels.TryGetValue(id, out Channel ch))
            {
                ch = new Channel { Raw = isOpen ? 0.001f : 0f, Value = isOpen ? 0.001f : 0f, WasOpening = isOpen };
                Channels[id] = ch;
            }

            if (ReduceMotion)
            {
                ch.Raw = isOpen ? 1f : 0f;
                ch.Value = ch.Raw;
                ch.LastFrame = Time.frameCount;
                return ch.Value;
            }

            int frame = Time.frameCount;
            if (ch.LastFrame != frame)
            {
                ch.LastFrame = frame;
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                if (dt <= 0f) dt = 0.016f;

                if (isOpen)
                {
                    ch.WasOpening = true;
                    float dur = Mathf.Max(0.05f, openSeconds);
                    ch.Raw = Mathf.MoveTowards(ch.Raw, 1f, dt / dur);
                }
                else
                {
                    ch.WasOpening = false;
                    float dur = Mathf.Max(0.04f, closeSeconds);
                    ch.Raw = Mathf.MoveTowards(ch.Raw, 0f, dt / dur);
                }

                ch.Value = ch.WasOpening ? EaseOutQuart(ch.Raw) : EaseOutCubic(ch.Raw);
            }

            return ch.Value;
        }

        /// <summary>Immediately sets a channel to 0 or 1 (useful on scene changes).</summary>
        public static void Snap(string id, bool open)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!Channels.TryGetValue(id, out Channel ch))
            {
                ch = new Channel();
                Channels[id] = ch;
            }
            ch.Raw = open ? 1f : 0f;
            ch.Value = ch.Raw;
            ch.LastFrame = Time.frameCount;
        }

        /// <summary>Smooth exponential spring tracker for sliding highlights, pills, or numeric readouts.</summary>
        public static float SpringTrack(string id, float target, float speed = 18f)
        {
            if (string.IsNullOrEmpty(id)) return target;
            if (!Channels.TryGetValue(id, out Channel ch))
            {
                ch = new Channel { Raw = target, Value = target, LastFrame = Time.frameCount };
                Channels[id] = ch;
                return target;
            }

            if (ReduceMotion)
            {
                ch.Raw = ch.Value = target;
                ch.LastFrame = Time.frameCount;
                return target;
            }

            int frame = Time.frameCount;
            if (ch.LastFrame != frame)
            {
                ch.LastFrame = frame;
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                float k = 1f - Mathf.Exp(-Mathf.Max(1f, speed) * dt);
                ch.Value = Mathf.Lerp(ch.Value, target, k);
                if (Mathf.Abs(ch.Value - target) < 0.05f) ch.Value = target;
                ch.Raw = ch.Value;
            }
            return ch.Value;
        }

        /// <summary>
        /// Value-type scope that applies subtle modal scale + vertical glide + alpha fade to any IMGUI window.
        /// Zero heap allocations. Dispose restores <see cref="GUI.matrix"/> and <see cref="GUI.color"/>.
        /// </summary>
        public readonly struct Scope : IDisposable
        {
            private readonly Matrix4x4 _prevMatrix;
            private readonly Color _prevColor;
            private readonly bool _active;

            internal Scope(Matrix4x4 prevMatrix, Color prevColor, bool active)
            {
                _prevMatrix = prevMatrix;
                _prevColor = prevColor;
                _active = active;
            }

            public void Dispose()
            {
                if (!_active) return;
                GUI.matrix = _prevMatrix;
                GUI.color = _prevColor;
            }
        }

        /// <summary>
        /// Begins a cinematic modal transform (subtle scale from <paramref name="minScale"/> to 1.0,
        /// gentle vertical glide of <paramref name="slideY"/> px, and smooth opacity fade).
        /// </summary>
        public static Scope BeginModal(Rect pivotRect, float progress, float slideY = 12f, float minScale = 0.968f)
        {
            progress = Mathf.Clamp01(progress);
            if (progress >= 0.999f) return default;

            Matrix4x4 prevM = GUI.matrix;
            Color prevC = GUI.color;

            float scale = Mathf.Lerp(minScale, 1f, progress);
            float dy = (1f - progress) * slideY;

            GUI.color = new Color(prevC.r, prevC.g, prevC.b, prevC.a * progress);
            if (Mathf.Abs(dy) > 0.05f)
                GUI.matrix = prevM * Matrix4x4.Translate(new Vector3(0f, dy, 0f));
            if (Mathf.Abs(scale - 1f) > 0.0005f)
                GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), pivotRect.center);

            return new Scope(prevM, prevC, true);
        }

        /// <summary>
        /// Begins a cinematic drawer slide transform (e.g. Blueprint sliding in from the right edge).
        /// </summary>
        public static Scope BeginSlide(float progress, float slideX, float slideY = 0f)
        {
            progress = Mathf.Clamp01(progress);
            if (progress >= 0.999f) return default;

            Matrix4x4 prevM = GUI.matrix;
            Color prevC = GUI.color;

            float inv = 1f - progress;
            GUI.color = new Color(prevC.r, prevC.g, prevC.b, prevC.a * progress);
            GUI.matrix = prevM * Matrix4x4.Translate(new Vector3(slideX * inv, slideY * inv, 0f));

            return new Scope(prevM, prevC, true);
        }

        private static float EaseOutQuart(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u * u;
        }

        private static float EaseOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }
    }
}
