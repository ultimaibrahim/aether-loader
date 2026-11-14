#pragma warning disable CS0618
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Aether.Core.Mapping;
using Blocksters.MathLib;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using Blocksters.Terrain.renderer;
using UnityEngine;
using Aether.Nano;
using static Aether.Nano.NanoUi;

namespace Aether.Mods.Wayfinder
{
    /// <summary>
    /// Optional knobs for <see cref="WayfinderModule.ExportWayfinderPng"/>.
    /// </summary>
    public sealed class WayfinderExportOptions
    {
        /// <summary>
        /// Centre the 4096x4096-block export on the current fullscreen map centre (pan)
        /// instead of the player. Falls back to the player when the map is closed.
        /// </summary>
        public bool UseCurrentFullscreenView;

        public bool IncludeWaypoints = true;
        public bool IncludeWaypointLabels = true;
        public bool IncludePlayerMarker = true;
        public bool IncludeLegend = true;

        /// <summary>
        /// Hide isolated cells exactly like the live map (3x3 cell test, at least 4 known).
        /// Disable only for diagnostics.
        /// </summary>
        public bool FilterIsolatedCells = true;
    }

    public sealed partial class WayfinderModule
    {
        // =====================================================================
        //        WAYFINDER PNG EXPORT
        // =====================================================================
        // Fixed contract: 1 texture pixel == one persisted 4x4-block cell.
        // 1024 px * 4 blocks == 4096 blocks (16 x 16 Wayfinder regions, one px/cell).
        // Texture row 0 is the south edge (min Z). SetPixels32 is bottom-up, so the
        // PNG's top row (row 0 after encoding) is the north edge (max Z). West is left.
        // =====================================================================
        private const int ExportTextureSize = 1024;
        private const int ExportCellCount = ExportTextureSize;
        private const int ExportBlockSpan = ExportCellCount * WayfinderRegionMath.CellSize; // 4096
        private const int ExportHaloCells = 1; // density + north slope
        private const int ExportSampleSize = ExportCellCount + ExportHaloCells * 2; // 1026
        private const int ExportInvalidTopY = -500;
        private const int ExportMaxCellCoordinate = 1_000_000; // +/- 4M blocks, keeps int math safe

        private static readonly Color32 ExportUnexploredColor = new Color32(10, 12, 16, 255);

        /// <summary>Path of the last successful export; null when the last attempt failed.</summary>
        public static string LastWayfinderExportPath { get; private set; }

        /// <summary>Reason of the last failed/skipped export; null on success.</summary>
        public static string LastWayfinderExportError { get; private set; }

        /// <summary>West-most exported cell (world cell X, multiply by 4 for the block edge).</summary>
        public static int LastWayfinderExportOriginCellX { get; private set; }

        /// <summary>South-most exported cell (world cell Z, multiply by 4 for the block edge).</summary>
        public static int LastWayfinderExportOriginCellZ { get; private set; }

        private bool journeyMapExportBusy;
        private bool journeyMapExportScheduled;

        /// <summary>True while an export is queued or running; the UI can disable the button.</summary>
        public bool IsWayfinderExportBusy => journeyMapExportBusy || journeyMapExportScheduled;

        /// <summary>
        /// Localized one-line status for a toolbar/toast. Empty until an export has run.
        /// Technical details remain in <see cref="LastWayfinderExportError"/> and the log.
        /// </summary>
        public static string GetWayfinderExportStatusText()
        {
            if (!string.IsNullOrEmpty(LastWayfinderExportError))
                return Tr("Export failed", "Error al exportar");

            if (!string.IsNullOrEmpty(LastWayfinderExportPath))
                return Tr("Map exported", "Mapa exportado") + ": " + Path.GetFileName(LastWayfinderExportPath);

            return "";
        }

        /// <summary>
        /// Renders the explored surface around the player with the default options.
        /// Returns the written path, or null when no world/store/data is available.
        /// Never throws; inspect <see cref="LastWayfinderExportError"/> for the reason.
        /// </summary>
        public string ExportWayfinderPng()
        {
            return ExportWayfinderPng(null);
        }

        /// <summary>
        /// Renders the explored surface to a 1024x1024 PNG (one pixel per 4x4-block cell),
        /// covering 4096x4096 blocks centred on the player (or the fullscreen map centre).
        /// The store is read on demand only; no texture is created or uploaded while idle.
        /// </summary>
        public string ExportWayfinderPng(WayfinderExportOptions options)
        {
            if (journeyMapExportBusy)
                return ExportFail("An export is already in progress.");

            journeyMapExportBusy = true;
            Texture2D texture = null;
            string tempPath = null;
            int exportOriginCellX = 0;
            int exportOriginCellZ = 0;

            LastWayfinderExportPath = null;
            LastWayfinderExportError = null;

            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                IWorld world = GetActiveWorld();
                if (world == null)
                    return ExportFail("No active world is loaded.");

                WayfinderStore store = SurfaceMapStore;
                if (store == null)
                    return ExportFail("The explored-map store is not available (SaveExploredMap may be off, or no single-player world is loaded).");

                Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
                if (cam == null)
                    return ExportFail("No active player camera.");

                string worldDirectory = !string.IsNullOrEmpty(journeyMapWorldDirectory)
                    ? journeyMapWorldDirectory
                    : ResolveWayfinderWorldDirectory();
                if (string.IsNullOrEmpty(worldDirectory))
                    return ExportFail("Could not resolve the world directory.");

                if (store.KnownCellCount <= 0)
                    return ExportFail("No explored surface cells have been recorded for this world.");

                Vector3 playerPosition = cam.transform.position;
                float centerX = playerPosition.x;
                float centerZ = playerPosition.z;

                if (options != null && options.UseCurrentFullscreenView && isWayfinderOpen)
                {
                    centerX += mapPanBlocksX;
                    centerZ += mapPanBlocksZ;
                }

                if (float.IsNaN(centerX) || float.IsInfinity(centerX)) centerX = 0f;
                if (float.IsNaN(centerZ) || float.IsInfinity(centerZ)) centerZ = 0f;

                // Explicit origin: the south-west cell of the exported square. Centre the
                // 1024-cell window on the player's cell, then clamp to a range that keeps
                // cell*4 safely inside Int32 (the game's worlds are far smaller).
                int centerCellX = Mathf.Clamp(
                    WayfinderRegionMath.CellCoord(Mathf.RoundToInt(centerX)),
                    -ExportMaxCellCoordinate, ExportMaxCellCoordinate);
                int centerCellZ = Mathf.Clamp(
                    WayfinderRegionMath.CellCoord(Mathf.RoundToInt(centerZ)),
                    -ExportMaxCellCoordinate, ExportMaxCellCoordinate);

                exportOriginCellX = centerCellX - ExportCellCount / 2;
                exportOriginCellZ = centerCellZ - ExportCellCount / 2;

                bool includeWaypoints = options == null || options.IncludeWaypoints;
                bool includeWaypointLabels = options == null || options.IncludeWaypointLabels;
                bool includePlayer = options == null || options.IncludePlayerMarker;
                bool includeLegend = options == null || options.IncludeLegend;
                bool filterIsolated = options == null || options.FilterIsolatedCells;

                // Snapshot enabled waypoints before the long synchronous scan. The UI keeps
                // running on the main thread, but this makes the export independent of list
                // edits/order and guarantees disabled points never appear.
                List<ExportWaypointSnapshot> waypointSnapshot = includeWaypoints
                    ? SnapshotExportWaypoints()
                    : null;

                // -----------------------------------------------------------------
                // 1. Sample each cell exactly once at its canonical (x*4, z*4)
                //    coordinate. The store is the only terrain source: no live world
                //    reads, so an export can never reveal unexplored terrain.
                //    A one-cell halo supplies the density test and north slope.
                // -----------------------------------------------------------------
                int sampleOriginCellX = exportOriginCellX - ExportHaloCells;
                int sampleOriginCellZ = exportOriginCellZ - ExportHaloCells;
                int sampleCellCount = ExportSampleSize * ExportSampleSize;

                bool[] cellKnown = new bool[sampleCellCount];
                short[] cellHeights = new short[sampleCellCount];
                Color32[] cellColors = new Color32[sampleCellCount];
                int knownCells = 0;

                for (int sampleRow = 0; sampleRow < ExportSampleSize; sampleRow++)
                {
                    int sampleCellZ = sampleOriginCellZ + sampleRow;
                    int worldZ = sampleCellZ * WayfinderRegionMath.CellSize;
                    int rowOffset = sampleRow * ExportSampleSize;

                    for (int sampleColumn = 0; sampleColumn < ExportSampleSize; sampleColumn++)
                    {
                        int sampleCellX = sampleOriginCellX + sampleColumn;
                        int worldX = sampleCellX * WayfinderRegionMath.CellSize;

                        if (!store.TryGetSurface(worldX, worldZ, out WayfinderCell cell))
                            continue;
                        if (cell.BlockId == 0 || cell.TopY <= ExportInvalidTopY)
                            continue;

                        int index = rowOffset + sampleColumn;
                        cellKnown[index] = true;
                        cellHeights[index] = cell.TopY;
                        cellColors[index] = ExportCellColor(cell);
                        knownCells++;
                    }
                }

                if (knownCells == 0)
                    return ExportFail("No explored surface cells in the exported 4096x4096-block area.");

                // -----------------------------------------------------------------
                // 2. Density gate and north-facing hill shading. Both are local
                //    lookups into the sampled cell cache; no extra store calls.
                // -----------------------------------------------------------------
                bool[] cellTrusted = new bool[sampleCellCount];
                int trustedCells = 0;

                for (int sampleRow = ExportHaloCells;
                     sampleRow < ExportHaloCells + ExportCellCount;
                     sampleRow++)
                {
                    int rowOffset = sampleRow * ExportSampleSize;

                    for (int sampleColumn = ExportHaloCells;
                         sampleColumn < ExportHaloCells + ExportCellCount;
                         sampleColumn++)
                    {
                        int index = rowOffset + sampleColumn;
                        if (!cellKnown[index]) continue;

                        if (filterIsolated)
                        {
                            int neighbours = 0;
                            for (int nz = -1; nz <= 1; nz++)
                            {
                                int neighbourRow = (sampleRow + nz) * ExportSampleSize;
                                for (int nx = -1; nx <= 1; nx++)
                                {
                                    if (cellKnown[neighbourRow + sampleColumn + nx])
                                        neighbours++;
                                }
                            }

                            // The live map accepts a centre with at least three known neighbours.
                            if (neighbours < 4) continue;
                        }

                        int northIndex = index + ExportSampleSize;
                        short northTopY = cellKnown[northIndex]
                            ? cellHeights[northIndex]
                            : cellHeights[index];

                        cellColors[index] = ExportShadeColor(
                            cellColors[index], cellHeights[index], northTopY);
                        cellTrusted[index] = true;
                        trustedCells++;
                    }
                }

                if (trustedCells == 0)
                    return ExportFail("The exported area contains only isolated cells; explore a larger surface patch first.");

                // -----------------------------------------------------------------
                // 3. Build the 1024x1024 texture. Array row 0 is min Z (south),
                //    array row 1023 is max Z (north). See the contract at the top.
                // -----------------------------------------------------------------
                Color32[] pixels = new Color32[ExportTextureSize * ExportTextureSize];
                int exploredPixels = 0;

                for (int arrayRow = 0; arrayRow < ExportTextureSize; arrayRow++)
                {
                    int sampleRowOffset = (arrayRow + ExportHaloCells) * ExportSampleSize + ExportHaloCells;
                    int destinationOffset = arrayRow * ExportTextureSize;

                    for (int pixelX = 0; pixelX < ExportTextureSize; pixelX++)
                    {
                        int sampleIndex = sampleRowOffset + pixelX;
                        if (cellTrusted[sampleIndex])
                        {
                            pixels[destinationOffset + pixelX] = cellColors[sampleIndex];
                            exploredPixels++;
                        }
                        else
                        {
                            pixels[destinationOffset + pixelX] = ExportUnexploredColor;
                        }
                    }
                }

                int exploredPercent = Mathf.Clamp(
                    Mathf.RoundToInt(100f * exploredPixels / pixels.Length), 0, 100);

                // -----------------------------------------------------------------
                // 4. Overlays. Waypoint positions are projected in 4-block cells:
                //    x/4, z/4. Off-canvas markers are skipped.
                // -----------------------------------------------------------------
                const int markerRadius = 5;
                const int labelScale = 2;

                if (waypointSnapshot != null)
                {
                    int labelsDrawn = 0;
                    for (int i = 0; i < waypointSnapshot.Count; i++)
                    {
                        ExportWaypointSnapshot waypoint = waypointSnapshot[i];

                        float screenX = waypoint.X / 4f - exportOriginCellX;
                        float screenY = ExportCellCount - (waypoint.Z / 4f - exportOriginCellZ);

                        if (screenX < -markerRadius || screenX > ExportCellCount + markerRadius
                            || screenY < -markerRadius || screenY > ExportCellCount + markerRadius)
                            continue;

                        int markerX = Mathf.RoundToInt(screenX);
                        int markerY = Mathf.RoundToInt(screenY);

                        ExportDrawDiamond(pixels, markerX, markerY, markerRadius + 2,
                            new Color32(5, 8, 12, 255));
                        ExportDrawDiamond(pixels, markerX, markerY, markerRadius, waypoint.Color);
                        ExportDrawDiamond(pixels, markerX, markerY, 2,
                            new Color32(250, 250, 250, 255));

                        if (!includeWaypointLabels || labelsDrawn >= 24) continue;

                        string label = string.IsNullOrWhiteSpace(waypoint.Name)
                            ? Tr("WAYPOINT", "PUNTO")
                            : waypoint.Name.Trim();
                        if (label.Length > 22)
                            label = label.Substring(0, 21) + ".";

                        int labelWidth = ExportTextWidth(label, labelScale);
                        int labelHeight = 5 * labelScale;
                        int labelX = markerX + markerRadius + 4;
                        if (labelX + labelWidth > ExportTextureSize - 4)
                            labelX = markerX - markerRadius - 4 - labelWidth;
                        if (labelX < 2) continue;

                        int labelY = Mathf.Clamp(
                            markerY - labelHeight / 2, 2, ExportTextureSize - labelHeight - 2);

                        ExportFillRect(pixels, labelX - 2, labelY - 1,
                            labelWidth + 4, labelHeight + 2, new Color32(0, 0, 0, 175));
                        ExportDrawText(pixels, labelX, labelY, label, labelScale,
                            new Color32(235, 242, 250, 255), true);
                        labelsDrawn++;
                    }
                }

                if (includePlayer)
                {
                    float playerX = playerPosition.x / 4f - exportOriginCellX;
                    float playerY = ExportCellCount - (playerPosition.z / 4f - exportOriginCellZ);

                    if (playerX >= -markerRadius && playerX <= ExportCellCount + markerRadius
                        && playerY >= -markerRadius && playerY <= ExportCellCount + markerRadius)
                    {
                        ExportDrawPlayerArrow(
                            pixels, playerX, playerY, cam.transform.eulerAngles.y, 7);
                    }
                }

                if (includeLegend)
                {
                    ExportDrawHeader(pixels, centerX, centerZ, exploredPercent);
                    ExportDrawLegend(pixels);
                }

                // -----------------------------------------------------------------
                // 5. Texture, encode, atomic-ish write. The texture is never cached
                //    and is destroyed in finally; 1024x1024 RGBA32 is ~4 MiB.
                // -----------------------------------------------------------------
                texture = new Texture2D(
                    ExportTextureSize, ExportTextureSize, TextureFormat.RGBA32, false)
                {
                    name = "AetherWayfinderExport",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.SetPixels32(pixels);
                texture.Apply(false);

                byte[] png = texture.EncodeToPNG();
                if (png == null || png.Length == 0)
                    return ExportFail("Unity returned no PNG data.");

                string dataDirectory = Aether.Core.Config.LegacyNames.ResolveWorldDataFolder(worldDirectory);
                Directory.CreateDirectory(dataDirectory);

                string baseName = "export_" + DateTime.Now.ToString(
                    "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string outputPath = Path.Combine(dataDirectory, baseName + ".png");
                int suffix = 2;
                while (File.Exists(outputPath))
                    outputPath = Path.Combine(dataDirectory, baseName + "_" + suffix++ + ".png");

                tempPath = outputPath + ".tmp";
                File.WriteAllBytes(tempPath, png);
                File.Move(tempPath, outputPath);
                tempPath = null;

                LastWayfinderExportPath = outputPath;
                LastWayfinderExportError = null;
                LastWayfinderExportOriginCellX = exportOriginCellX;
                LastWayfinderExportOriginCellZ = exportOriginCellZ;

                try
                {
                    Log?.LogInfo(
                        $"[Wayfinder] Exported map: {outputPath} " +
                        $"({ExportTextureSize}x{ExportTextureSize}, 1 px = 4 blocks, " +
                        $"{ExportBlockSpan}x{ExportBlockSpan} blocks, origin cell " +
                        $"({exportOriginCellX},{exportOriginCellZ}) = block " +
                        $"({exportOriginCellX * 4},{exportOriginCellZ * 4}), " +
                        $"{trustedCells} cells, {exploredPercent}% explored, " +
                        $"{stopwatch.ElapsedMilliseconds} ms).");
                }
                catch { }

                return outputPath;
            }
            catch (Exception ex)
            {
                return ExportFail($"Export failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                journeyMapExportBusy = false;

                if (texture != null)
                {
                    try { UnityEngine.Object.Destroy(texture); }
                    catch { }
                }

                if (!string.IsNullOrEmpty(tempPath))
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch { }
                }
            }
        }

        /// <summary>
        /// Draws the native-styled "Export PNG" button and queues an export on MouseDown only.
        /// Call it from OnGUI exactly once per event (consistent control order). The event is
        /// consumed so the map underneath cannot start a drag; no export work runs on
        /// Layout/Repaint. Returns true when this click queued an export.
        /// </summary>
        public bool DrawExportWayfinderButton(Rect rect)
        {
            try
            {
                bool clicked = false;
                Event current = Event.current;

                if (current != null
                    && current.type == EventType.MouseDown
                    && current.button == 0
                    && rect.Contains(current.mousePosition))
                {
                    current.Use();
                    if (!IsWayfinderExportBusy)
                    {
                        clicked = true;
                        RequestWayfinderExport();
                    }
                }

                string label;
                if (rect.width < 96f)
                {
                    label = IsWayfinderExportBusy ? "..." : Tr("PNG", "PNG");
                }
                else
                {
                    label = IsWayfinderExportBusy
                        ? Tr("Exporting...", "Exportando...")
                        : Tr("Export PNG", "Exportar PNG");
                }

                // Draw only; the actual trigger is the MouseDown path above, so Repaint/Layout
                // never start the heavy scan and a click cannot double-fire on MouseUp.
                DrawNativeWoodButton(rect, label);
                return clicked;
            }
            catch (Exception ex)
            {
                LogExportWarning("button", ex);
                return false;
            }
        }

        /// <summary>
        /// Queues an export for the next frame so a button click does not pay the scan/PNG
        /// cost inside the IMGUI event. It does not need an Update hook.
        /// </summary>
        public void RequestWayfinderExport(WayfinderExportOptions options = null)
        {
            if (journeyMapExportBusy || journeyMapExportScheduled) return;

            journeyMapExportScheduled = true;
            try
            {
                StartCoroutine(ExportWayfinderPngDeferred(options));
            }
            catch
            {
                journeyMapExportScheduled = false;
                ExportWayfinderPng(options);
            }
        }

        private IEnumerator ExportWayfinderPngDeferred(WayfinderExportOptions options)
        {
            // Let the click event and the current OnGUI pass finish first.
            yield return null;
            journeyMapExportScheduled = false;
            ExportWayfinderPng(options);
        }

        private static string ExportFail(string message)
        {
            LastWayfinderExportPath = null;
            LastWayfinderExportError = message;

            try { Log?.LogWarning("[Wayfinder] PNG export skipped: " + message); }
            catch { }

            return null;
        }

        private static void LogExportWarning(string where, Exception ex)
        {
            try { Log?.LogWarning($"[Wayfinder] PNG export {where} warning: {ex.GetType().Name}: {ex.Message}"); }
            catch { }
        }

        private struct ExportWaypointSnapshot
        {
            public string Name;
            public int X;
            public int Y;
            public int Z;
            public Color32 Color;
        }

        private List<ExportWaypointSnapshot> SnapshotExportWaypoints()
        {
            List<ExportWaypointSnapshot> snapshot = new List<ExportWaypointSnapshot>(waypoints.Count);

            for (int i = 0; i < waypoints.Count; i++)
            {
                WaypointEntry waypoint = waypoints[i];
                if (waypoint == null || !waypoint.Enabled) continue;

                snapshot.Add(new ExportWaypointSnapshot
                {
                    Name = waypoint.Name,
                    X = waypoint.X,
                    Y = waypoint.Y,
                    Z = waypoint.Z,
                    Color = GetWaypointColor(waypoint.ColorIndex)
                });
            }

            return snapshot;
        }

        // =====================================================================
        //        CELL COLOURS & SHADING
        // =====================================================================
        private static Color32 ExportCellColor(in WayfinderCell cell)
        {
            Color32 baseColor = NanoPalette.GetBlockColor(cell.BlockId);

            if (cell.IsOre)
                baseColor = ExportScaleColor(baseColor, 1.45f);

            if (cell.IsPainted && RYBUtil.RGB32 != null && cell.PaintHue < RYBUtil.RGB32.Length)
            {
                Color32 paint = RYBUtil.RGB32[cell.PaintHue];
                baseColor = new Color32(
                    (byte)((baseColor.r * paint.r) / 255),
                    (byte)((baseColor.g * paint.g) / 255),
                    (byte)((baseColor.b * paint.b) / 255),
                    255);
            }

            if (cell.IsLiquid)
            {
                if (cell.LiquidId == 22) // Lava
                {
                    baseColor = new Color32(255, 120, 20, 255);
                }
                else if (cell.LiquidId == 108) // Acid
                {
                    baseColor = new Color32(75, 255, 35, 255);
                }
                else
                {
                    Color32 bedColor = NanoPalette.GetBlockColor(cell.BedBlockId);
                    float depthFactor = Mathf.Clamp01(cell.WaterDepth / 12f);
                    Color shallow = new Color(0.24f, 0.68f, 0.92f);
                    Color deep = new Color(0.06f, 0.18f, 0.52f);
                    Color waterColor = Color.Lerp(shallow, deep, depthFactor);
                    baseColor = Color.Lerp(bedColor, waterColor, 0.55f + depthFactor * 0.40f);
                }
            }

            // Use the tint captured when the cell was explored, never a live biome lookup.
            // Zero means "no stored tint": keep the neutral palette colour unchanged.
            if (cell.BiomeTint != 0)
            {
                Color32 storedTint = WayfinderEngine.UnpackColor(cell.BiomeTint);
                baseColor = Color.Lerp(baseColor, storedTint, 0.35f);
            }

            return new Color32(baseColor.r, baseColor.g, baseColor.b, 255);
        }

        private static Color32 ExportScaleColor(Color32 color, float factor)
        {
            return new Color32(
                (byte)Mathf.Clamp(color.r * factor, 0f, 255f),
                (byte)Mathf.Clamp(color.g * factor, 0f, 255f),
                (byte)Mathf.Clamp(color.b * factor, 0f, 255f),
                255);
        }

        /// <summary>
        /// Exact live-map hill shading: north (+Z, up) neighbour, 14% per block,
        /// clamped to +/-32%. Bytes are clamped after the multiplication.
        /// </summary>
        private static Color32 ExportShadeColor(
            Color32 color, int topY, int northTopY)
        {
            float shade = 1f + Mathf.Clamp((topY - northTopY) * 0.14f, -0.32f, 0.32f);

            return new Color32(
                (byte)Mathf.Clamp(color.r * shade, 0f, 255f),
                (byte)Mathf.Clamp(color.g * shade, 0f, 255f),
                (byte)Mathf.Clamp(color.b * shade, 0f, 255f),
                255);
        }

        // =====================================================================
        //        PIXEL / VECTOR HELPERS (top-down coordinates)
        // =====================================================================
        private static void ExportBlendPixel(Color32[] pixels, int x, int y, Color32 source)
        {
            if ((uint)x >= (uint)ExportTextureSize || (uint)y >= (uint)ExportTextureSize) return;
            if (source.a == 0) return;

            int index = (ExportTextureSize - 1 - y) * ExportTextureSize + x;
            if (source.a >= 255)
            {
                pixels[index] = source;
                return;
            }

            Color32 destination = pixels[index];
            int inverse = 255 - source.a;
            pixels[index] = new Color32(
                (byte)((source.r * source.a + destination.r * inverse) / 255),
                (byte)((source.g * source.a + destination.g * inverse) / 255),
                (byte)((source.b * source.a + destination.b * inverse) / 255),
                255);
        }

        private static void ExportFillRect(
            Color32[] pixels, int x, int y, int width, int height, Color32 color)
        {
            int x0 = Mathf.Max(0, x);
            int y0 = Mathf.Max(0, y);
            int x1 = Mathf.Min(ExportTextureSize, x + width);
            int y1 = Mathf.Min(ExportTextureSize, y + height);

            for (int py = y0; py < y1; py++)
            {
                for (int px = x0; px < x1; px++)
                    ExportBlendPixel(pixels, px, py, color);
            }
        }

        private static void ExportDrawDiamond(
            Color32[] pixels, int centerX, int centerY, int radius, Color32 color)
        {
            if (radius < 1) radius = 1;

            for (int dy = -radius; dy <= radius; dy++)
            {
                int rowWidth = radius - Mathf.Abs(dy);
                for (int dx = -rowWidth; dx <= rowWidth; dx++)
                    ExportBlendPixel(pixels, centerX + dx, centerY + dy, color);
            }
        }

        private static void ExportDrawPlayerArrow(
            Color32[] pixels, float screenX, float screenY, float yawDegrees, int radius)
        {
            float yaw = yawDegrees * Mathf.Deg2Rad;
            // +Z is up on the map, so a yaw of 0 (looking +Z) points the arrow up.
            Vector2 forward = new Vector2(Mathf.Sin(yaw), -Mathf.Cos(yaw));
            if (forward.sqrMagnitude < 0.0001f) forward = new Vector2(0f, -1f);
            forward.Normalize();

            Vector2 side = new Vector2(-forward.y, forward.x);
            Vector2 center = new Vector2(screenX, screenY);

            Vector2 outerTip = center + forward * (radius * 1.6f);
            Vector2 outerLeft = center - forward * radius + side * (radius * 0.9f);
            Vector2 outerRight = center - forward * radius - side * (radius * 0.9f);
            ExportFillTriangle(pixels, outerTip, outerLeft, outerRight, new Color32(4, 7, 11, 255));

            Vector2 innerTip = center + forward * (radius * 1.25f);
            Vector2 innerLeft = center - forward * (radius * 0.62f) + side * (radius * 0.55f);
            Vector2 innerRight = center - forward * (radius * 0.62f) - side * (radius * 0.55f);
            ExportFillTriangle(pixels, innerTip, innerLeft, innerRight, new Color32(70, 235, 255, 255));

            int dot = Mathf.Max(1, radius / 3);
            ExportFillRect(pixels,
                Mathf.RoundToInt(center.x) - dot, Mathf.RoundToInt(center.y) - dot,
                dot * 2 + 1, dot * 2 + 1, new Color32(250, 255, 255, 255));
        }

        private static void ExportFillTriangle(
            Color32[] pixels, Vector2 a, Vector2 b, Vector2 c, Color32 color)
        {
            int minX = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
            int maxX = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
            int minY = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)));
            int maxY = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)));

            minX = Mathf.Max(0, minX);
            minY = Mathf.Max(0, minY);
            maxX = Mathf.Min(ExportTextureSize - 1, maxX);
            maxY = Mathf.Min(ExportTextureSize - 1, maxY);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                    if (ExportPointInTriangle(point, a, b, c))
                        ExportBlendPixel(pixels, x, y, color);
                }
            }
        }

        private static bool ExportPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = ExportTriangleSign(point, a, b);
            float d2 = ExportTriangleSign(point, b, c);
            float d3 = ExportTriangleSign(point, c, a);
            bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNegative && hasPositive);
        }

        private static float ExportTriangleSign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        // =====================================================================
        //        TINY 3x5 BITMAP FONT & PANELS
        // =====================================================================
        private static readonly Dictionary<char, string> ExportFont = new Dictionary<char, string>(64)
        {
            { 'A', "111101111101101" }, { 'B', "110101110101110" }, { 'C', "111100100100111" },
            { 'D', "110101101101110" }, { 'E', "111100110100111" }, { 'F', "111100110100100" },
            { 'G', "111100101101111" }, { 'H', "101101111101101" }, { 'I', "111010010010111" },
            { 'J', "001001001101111" }, { 'K', "101101110101101" }, { 'L', "100100100100111" },
            { 'M', "101111111101101" }, { 'N', "110101101101101" }, { 'O', "111101101101111" },
            { 'P', "111101111100100" }, { 'Q', "111101101111011" }, { 'R', "111101111110101" },
            { 'S', "111100111001111" }, { 'T', "111010010010010" }, { 'U', "101101101101111" },
            { 'V', "101101101101010" }, { 'W', "101101111111101" }, { 'X', "101101010101101" },
            { 'Y', "101101111010010" }, { 'Z', "111001010100111" },
            { '0', "111101101101111" }, { '1', "010110010010111" }, { '2', "111001111100111" },
            { '3', "111001111001111" }, { '4', "101101111001001" }, { '5', "111100111001111" },
            { '6', "111100111101111" }, { '7', "111001001010010" }, { '8', "111101111101111" },
            { '9', "111101111001111" },
            { ' ', "000000000000000" }, { ':', "000010000010000" }, { '-', "000000111000000" },
            { '.', "000000000000010" }, { ',', "000000000010100" }, { '/', "001001010100100" },
            { '%', "101001010100101" }, { '!', "010010010000010" }, { '?', "111001011000010" },
            { '(', "001010010010001" }, { ')', "100010010010100" }, { '+', "000010111010000" },
            { '=', "000111000111000" }, { '_', "000000000000111" }, { '#', "101111101111101" }
        };

        private static void ExportDrawText(
            Color32[] pixels, int x, int y, string text, int scale, Color32 color, bool shadow)
        {
            if (string.IsNullOrEmpty(text) || scale <= 0) return;

            if (shadow)
            {
                int offset = Mathf.Max(1, scale / 2);
                ExportDrawText(pixels, x + offset, y + offset, text, scale,
                    new Color32(0, 0, 0, 220), false);
            }

            int cursorX = x;
            for (int i = 0; i < text.Length; i++)
            {
                char c = ExportFoldChar(text[i]);
                if (c == ' ')
                {
                    cursorX += 4 * scale;
                    continue;
                }

                if (ExportFont.TryGetValue(c, out string glyph) && glyph.Length == 15)
                {
                    for (int row = 0; row < 5; row++)
                    {
                        int rowOffset = row * 3;
                        for (int column = 0; column < 3; column++)
                        {
                            if (glyph[rowOffset + column] != '1') continue;
                            ExportFillRect(pixels,
                                cursorX + column * scale, y + row * scale,
                                scale, scale, color);
                        }
                    }
                }

                cursorX += 4 * scale;
            }
        }

        private static int ExportTextWidth(string text, int scale)
        {
            if (string.IsNullOrEmpty(text) || scale <= 0) return 0;
            return text.Length * 4 * scale - scale;
        }

        private static char ExportFoldChar(char c)
        {
            c = char.ToUpperInvariant(c);
            switch (c)
            {
                case 'Á': case 'À': case 'Ä': case 'Â': case 'Ã': return 'A';
                case 'É': case 'È': case 'Ë': case 'Ê': return 'E';
                case 'Í': case 'Ì': case 'Ï': case 'Î': return 'I';
                case 'Ó': case 'Ò': case 'Ö': case 'Ô': case 'Õ': return 'O';
                case 'Ú': case 'Ù': case 'Ü': case 'Û': return 'U';
                case 'Ñ': return 'N';
                case 'Ç': return 'C';
                case '¡': return '!';
                case '¿': return '?';
                default: return c;
            }
        }

        private static void ExportDrawHeader(
            Color32[] pixels, float centerX, float centerZ, int exploredPercent)
        {
            string[] lines =
            {
                Tr("WAYFINDER EXPORT", "EXPORTAR WAYFINDER"),
                string.Format(CultureInfo.InvariantCulture, "X {0}   Z {1}",
                    Mathf.RoundToInt(centerX), Mathf.RoundToInt(centerZ)),
                string.Format(CultureInfo.InvariantCulture, "{0} X {0} BLOCKS",
                    ExportBlockSpan),
                string.Format(CultureInfo.InvariantCulture, "{0}% EXPLORED", exploredPercent)
            };

            const int scale = 3;
            int padding = 6 * scale;
            int lineHeight = 6 * scale;
            int width = 0;
            for (int i = 0; i < lines.Length; i++)
                width = Mathf.Max(width, ExportTextWidth(lines[i], scale));
            width += padding * 2;
            int height = lines.Length * lineHeight + padding * 2;

            int panelX = 12;
            int panelY = 12;
            ExportDrawPanel(pixels, panelX, panelY, width, height, scale);

            Color32 titleColor = new Color32(185, 240, 255, 255);
            Color32 textColor = new Color32(210, 220, 230, 255);
            Color32 mutedColor = new Color32(160, 185, 205, 255);

            for (int i = 0; i < lines.Length; i++)
            {
                Color32 color = i == 0 ? titleColor : (i == 3 ? mutedColor : textColor);
                ExportDrawText(pixels,
                    panelX + padding, panelY + padding + i * lineHeight,
                    lines[i], scale, color, true);
            }
        }

        private static void ExportDrawLegend(Color32[] pixels)
        {
            string[] labels =
            {
                Tr("EXPLORED", "EXPLORADO"),
                Tr("UNEXPLORED", "SIN EXPLORAR"),
                Tr("PLAYER", "JUGADOR"),
                Tr("WAYPOINT", "PUNTO")
            };

            Color32[] colors =
            {
                new Color32(86, 158, 48, 255),
                new Color32(10, 12, 16, 255),
                new Color32(70, 235, 255, 255),
                new Color32(255, 209, 51, 255)
            };

            const int scale = 2;
            int padding = 6 * scale;
            int rowHeight = 6 * scale;
            int swatch = 5 * scale;
            int gap = 2 * scale;
            int textWidth = 0;
            for (int i = 0; i < labels.Length; i++)
                textWidth = Mathf.Max(textWidth, ExportTextWidth(labels[i], scale));

            int width = padding * 2 + swatch + gap + textWidth;
            int height = padding * 2 + labels.Length * rowHeight;
            int panelX = 12;
            int panelY = ExportTextureSize - height - 12;

            ExportDrawPanel(pixels, panelX, panelY, width, height, scale);

            for (int i = 0; i < labels.Length; i++)
            {
                int rowY = panelY + padding + i * rowHeight;
                int centerY = rowY + rowHeight / 2;
                int centerX = panelX + padding + swatch / 2;

                if (i < 2)
                {
                    ExportFillRect(pixels,
                        centerX - swatch / 2, centerY - swatch / 2,
                        swatch, swatch, new Color32(4, 7, 11, 255));
                    ExportFillRect(pixels,
                        centerX - swatch / 2 + 1, centerY - swatch / 2 + 1,
                        Mathf.Max(1, swatch - 2), Mathf.Max(1, swatch - 2), colors[i]);
                }
                else
                {
                    ExportDrawDiamond(pixels, centerX, centerY,
                        Mathf.Max(2, swatch / 2), colors[i]);
                }

                ExportDrawText(pixels,
                    panelX + padding + swatch + gap, rowY + scale / 2,
                    labels[i], scale, new Color32(215, 228, 240, 255), true);
            }
        }

        private static void ExportDrawPanel(
            Color32[] pixels, int x, int y, int width, int height, int scale)
        {
            ExportFillRect(pixels, x, y, width, height, new Color32(3, 7, 12, 225));

            int border = Mathf.Max(1, scale / 2);
            Color32 borderColor = new Color32(70, 95, 120, 255);
            ExportFillRect(pixels, x, y, width, border, borderColor);
            ExportFillRect(pixels, x, y + height - border, width, border, borderColor);
            ExportFillRect(pixels, x, y, border, height, borderColor);
            ExportFillRect(pixels, x + width - border, y, border, height, borderColor);
        }
    }
}
