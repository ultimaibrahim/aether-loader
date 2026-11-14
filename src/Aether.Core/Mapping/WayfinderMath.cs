using System;

namespace Aether.Core.Mapping
{
    /// <summary>
    /// Pure math for the Wayfinder: zoom, texture step, radar span and pan anchoring.
    /// Deliberately free of UnityEngine so it can be gated by the test project.
    /// </summary>
    public static class WayfinderMath
    {
        public const float MinZoom = 0.25f;
        public const float MaxZoom = 4.0f;
        public const float MinRadarRange = 24f;
        public const float MaxRadarRange = 96f;
        public const int MinSliceY = 0;
        public const int MaxSliceY = 255;
        public const float MaxPanBlocks = 2048f;

        public static float ClampZoom(float zoom)
        {
            if (float.IsNaN(zoom)) return 1f;
            if (zoom < MinZoom) return MinZoom;
            if (zoom > MaxZoom) return MaxZoom;
            return zoom;
        }

        /// <summary>
        /// Blocks per texture pixel. The sampled window must always be at least as large as
        /// the visible window, otherwise the UV crop clamps and markers drift off the terrain.
        /// </summary>
        public static int StepForZoom(float zoom)
        {
            zoom = ClampZoom(zoom);
            int step = 1;
            while (step * zoom < 1f && step < 64) step <<= 1;
            return step;
        }

        /// <summary>World blocks covered by the visible (cropped) map window.</summary>
        public static float VisibleSpan(int textureSize, float zoom)
        {
            zoom = ClampZoom(zoom);
            return textureSize / zoom;
        }

        /// <summary>UV fraction of the sampled texture that the visible window occupies.</summary>
        public static float UvSpan(int textureSize, int step, float visibleSpan)
        {
            if (textureSize <= 0) return 1f;
            float sampled = textureSize * (step < 1 ? 1 : step);
            float uv = visibleSpan / sampled;
            if (uv < 0f) return 0f;
            if (uv > 1f) return 1f;
            return uv;
        }

        /// <summary>Blocks per pixel needed to cover <paramref name="range"/> blocks of radius.</summary>
        public static int StepForRadar(float range, int textureSize)
        {
            if (textureSize <= 0) return 1;
            range = ClampRadarRange(range);
            int step = (int)Math.Ceiling((range * 2f) / textureSize);
            return step < 1 ? 1 : step;
        }

        public static float ClampRadarRange(float range)
        {
            if (float.IsNaN(range)) return 48f;
            if (range < MinRadarRange) return MinRadarRange;
            if (range > MaxRadarRange) return MaxRadarRange;
            return range;
        }

        public static int ClampSliceY(int y)
        {
            if (y < MinSliceY) return MinSliceY;
            if (y > MaxSliceY) return MaxSliceY;
            return y;
        }

        public static float ClampPan(float pan)
        {
            if (float.IsNaN(pan)) return 0f;
            if (pan < -MaxPanBlocks) return -MaxPanBlocks;
            if (pan > MaxPanBlocks) return MaxPanBlocks;
            return pan;
        }

        /// <summary>
        /// Pan X that keeps <paramref name="anchorWorldX"/> under a cursor offset after a zoom.
        /// Screen X = center + (world - (player + pan)) * pixelsPerBlock.
        /// </summary>
        public static float PanXForAnchor(float anchorWorldX, float playerX, float relativePixels, float pixelsPerBlock)
        {
            if (pixelsPerBlock <= 0f) return 0f;
            return anchorWorldX - playerX - relativePixels / pixelsPerBlock;
        }

        /// <summary>
        /// Pan Z that keeps <paramref name="anchorWorldZ"/> under a cursor offset after a zoom.
        /// Screen Y = center - (world - (player + pan)) * pixelsPerBlock.
        /// </summary>
        public static float PanZForAnchor(float anchorWorldZ, float playerZ, float relativePixels, float pixelsPerBlock)
        {
            if (pixelsPerBlock <= 0f) return 0f;
            return anchorWorldZ - playerZ + relativePixels / pixelsPerBlock;
        }

        /// <summary>
        /// Screen offset of the last rendered map texture when the view has moved.
        /// Keeping the texture anchored lets the minimap/fullscreen pan smoothly while
        /// terrain work stays on its slower refresh cadence.
        /// </summary>
        public static float AnchoredMapOffsetX(float renderedCenterX, float viewCenterX, float pixelsPerBlock)
        {
            return (renderedCenterX - viewCenterX) * pixelsPerBlock;
        }

        /// <summary>Y is inverted because world Z grows toward the bottom of the map.</summary>
        public static float AnchoredMapOffsetY(float renderedCenterZ, float viewCenterZ, float pixelsPerBlock)
        {
            return (viewCenterZ - renderedCenterZ) * pixelsPerBlock;
        }
    }
}
