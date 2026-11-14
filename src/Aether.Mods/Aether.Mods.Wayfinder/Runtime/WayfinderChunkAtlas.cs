#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Aether.Core.Mapping;
using Blocksters.MathLib;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using Blocksters.Terrain.renderer;
using Aether.Nano;
using UnityEngine;

namespace Aether.Mods.Wayfinder
{
    /// <summary>
    /// Chunk-keyed map texture atlas, modeled after Wayfinder's region/tile architecture:
    /// every 32x32-block map chunk is rendered once into its own small texture and reused as
    /// the view pans and zooms. Zooming only changes the on-screen quad size, so there is no
    /// re-render, no partial upload and no bottom-to-top fill sweep.
    /// </summary>
    public sealed class WayfinderChunkAtlas
    {
        private sealed class Slot
        {
            public Texture2D Texture;
            /// <summary>CPU copy of the uploaded pixels (row 0 = low Z), used by the circle compositor.</summary>
            public Color32[] Pixels;
            public bool Used;
            public float LastRender;
            public float LastUsed;
            /// <summary>When the slot first became visible → smooth fade-in (no hard pop).</summary>
            public float FirstRenderAt = -1f;
            /// <summary>Chunk identity, so the cave fog can invalidate a slot that was rendered black.</summary>
            public int Cx;
            public int Cz;
            public int Band;
        }

        private const int MaxSlots = 512;
        private const float StaleSeconds = 5f;
        private const int UnexploredR = 10, UnexploredG = 12, UnexploredB = 16;
        private const int Halo = 1;
        private static readonly int Stride = WayfinderChunks.Size + Halo * 2;
        private const int MaxChunkRendersPerFrame = 4;
        private const double ChunkRenderBudgetMs = 2.5;

        private struct Candidate
        {
            public int Cx;
            public int Cz;
            public long Key;
            public bool Exists;
            public bool Missing;
            public float Distance;
        }

        private readonly Dictionary<long, Slot> slots = new Dictionary<long, Slot>(MaxSlots);
        private readonly List<Candidate> candidates = new List<Candidate>(64);
        private readonly List<long> removeKeys = new List<long>(8);
        // Cave/slice fog of war: (band, chunkX, chunkZ) the player has actually approached.
        private readonly HashSet<(int Band, int Cx, int Cz)> visitedCaveChunks = new HashSet<(int, int, int)>();
        // Chunks already probed against the cave store with no saved cells. Prevents 64
        // region lookups on every stale re-render while panning an unexplored area.
        private readonly HashSet<(int Band, int Cx, int Cz)> caveFogMisses = new HashSet<(int, int, int)>();
        // Per-cell density cache used to reject isolated x-ray cells in the surface store.
        private readonly Dictionary<long, bool> cellDensity = new Dictionary<long, bool>(128);
        private const int MinDenseCells = 4; // centre + at least 3 known neighbours
        private const float FadeSeconds = 0.28f;
        private const int CaveVisitChunkRadius = 1; // 3x3 chunks ≈ 96 blocks around the player
        private readonly Color32[] scratch = new Color32[Stride * Stride];
        private readonly TerrainSample[] chunkSamples = new TerrainSample[Stride * Stride];
        // True when scratch already holds the final pixel (tile-cache hit or unexplored fill).
        // Never encode this in BlockId: real block ids collide with sentinel values.
        private readonly bool[] finalPixel = new bool[Stride * Stride];
        private readonly bool[] openMask = new bool[Stride * Stride];
        private readonly bool[] interiorMask = new bool[Stride * Stride];
        private readonly Color32[] upload = new Color32[WayfinderChunks.Size * WayfinderChunks.Size];
        private readonly List<(int x, int z, Color color)> lightSources = new List<(int x, int z, Color color)>(64);
        private Color32[] composeBuffer;
        private int composeWidth;
        private int composeHeight;
        private static readonly Stopwatch RenderWatch = new Stopwatch();
        private static readonly Comparison<Candidate> CandidateOrder = (a, b) =>
        {
            if (a.Missing != b.Missing) return a.Missing ? -1 : 1;
            return a.Distance.CompareTo(b.Distance);
        };

        private static readonly FieldInfo PwChunksField = typeof(PriorityWorld).GetField("chunks",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo PwC0xField = typeof(PriorityWorld).GetField("c0x",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo PwC0yField = typeof(PriorityWorld).GetField("c0y",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo PwC0zField = typeof(PriorityWorld).GetField("c0z",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static readonly WayfinderChunkAtlas Shared = new WayfinderChunkAtlas();

        private string lastLayerKey;

        private WayfinderChunkAtlas() { }

        public int SlotCount => slots.Count;

        /// <summary>
        /// Marks the cave/slice band around the player as explored. Chunks outside this visited
        /// set render as unexplored black, so the map never reveals loaded-but-unvisited terrain.
        /// Both the floor band and the rounded band are marked: the renderer uses rounded Y for
        /// Interior, and a fractional camera Y can otherwise cross a band boundary unseen.
        /// </summary>
        public void MarkCaveVisited(float worldX, float worldZ, int worldY)
        {
            int band = WayfinderChunks.BandCoord(worldY);
            MarkCaveBandVisited(worldX, worldZ, band);

            int rounded = Mathf.RoundToInt(worldY);
            int roundedBand = WayfinderChunks.BandCoord(rounded);
            if (roundedBand != band) MarkCaveBandVisited(worldX, worldZ, roundedBand);
        }

        private void MarkCaveBandVisited(float worldX, float worldZ, int band)
        {
            int cx = WayfinderChunks.ChunkCoord(Mathf.FloorToInt(worldX));
            int cz = WayfinderChunks.ChunkCoord(Mathf.FloorToInt(worldZ));
            for (int dz = -CaveVisitChunkRadius; dz <= CaveVisitChunkRadius; dz++)
            {
                for (int dx = -CaveVisitChunkRadius; dx <= CaveVisitChunkRadius; dx++)
                {
                    if (!visitedCaveChunks.Add((band, cx + dx, cz + dz))) continue;
                    caveFogMisses.Remove((band, cx + dx, cz + dz));
                    RemoveSlotsForChunk(cx + dx, cz + dz, band);
                }
            }
        }

        /// <summary>Called on world/dimension changes, not on layer or grid switches.</summary>
        public void ClearExploration()
        {
            visitedCaveChunks.Clear();
            caveFogMisses.Clear();
        }

        /// <summary>Drop negative cave-store probes after a store opens or its contents change.</summary>
        public void ClearCaveStoreProbes()
        {
            caveFogMisses.Clear();
        }

        private bool IsCaveChunkVisited(int band, int cx, int cz)
        {
            if (visitedCaveChunks.Contains((band, cx, cz))) return true;
            if (caveFogMisses.Contains((band, cx, cz))) return false;

            // Reload case: the cave store records whole rendered chunks, so a saved cell is
            // evidence that the chunk was visited. Seed the in-memory fog lazily instead of
            // scanning every band/region at startup.
            WayfinderCaveStore store = WayfinderModule.CaveMapStore;
            if (store != null && store.HasAnyCaveCell(cx, cz, band))
            {
                visitedCaveChunks.Add((band, cx, cz));
                return true;
            }

            caveFogMisses.Add((band, cx, cz));
            return false;
        }

        private void RemoveSlotsForChunk(int cx, int cz, int band)
        {
            removeKeys.Clear();
            foreach (KeyValuePair<long, Slot> pair in slots)
            {
                Slot s = pair.Value;
                if (s != null && s.Band == band && s.Cx == cx && s.Cz == cz)
                    removeKeys.Add(pair.Key);
            }
            for (int i = 0; i < removeKeys.Count; i++)
            {
                if (slots.TryGetValue(removeKeys[i], out Slot s) && s != null && s.Texture != null)
                    UnityEngine.Object.Destroy(s.Texture);
                slots.Remove(removeKeys[i]);
            }
        }

        /// <summary>
        /// True when the terrain pixel at this world position is actually rendered (live or
        /// explored store), i.e. not the black unexplored fill. Dynamic markers (native radar
        /// POIs, mobs) use this so they never reveal unseen rooms through the fog.
        /// </summary>
        public bool IsRenderedExplored(
            int wx, int wz, string layerMode, bool underground, bool indoors,
            bool surfaceStructure, string renderMode, int gridStyle, int sliceY)
        {
            bool cave = IsCave(layerMode, underground, indoors);
            bool forceInterior = layerMode == "Interior";
            bool interiorContext = cave && (forceInterior || (indoors && surfaceStructure));
            byte mode = BuildMode(cave, renderMode, gridStyle, interiorContext, forceInterior);
            int band = forceInterior
                ? Mathf.Clamp(sliceY, short.MinValue, short.MaxValue)
                : (cave ? WayfinderChunks.BandCoord(sliceY) : 0);
            long key = WayfinderChunks.Key(wx >> 5, wz >> 5, mode, band);
            if (!slots.TryGetValue(key, out Slot slot) || slot == null || slot.Pixels == null)
                return false;

            // Do not pop markers on top of a chunk that is still fading in: wait until the
            // terrain is at least half visible so the marker appears with its ground.
            if (FadeAlpha(slot, Time.unscaledTime) < 0.5f) return false;

            int tx = wx & (WayfinderChunks.Size - 1);
            int tz = wz & (WayfinderChunks.Size - 1);
            Color32 c = slot.Pixels[tz * WayfinderChunks.Size + tx];
            return !(c.r == UnexploredR && c.g == UnexploredG && c.b == UnexploredB);
        }

        // =====================================================================
        //        DRAWING
        // =====================================================================

        /// <summary>
        /// Draws every rendered chunk that intersects the viewport and renders missing/stale
        /// chunks within a small per-frame budget. When <paramref name="circleTarget"/> is set,
        /// the visible chunks are composed into that texture with a circular alpha cut instead
        /// of drawing square quads (a true round minimap).
        /// Returns the number of visible chunk slots still missing their first render.
        /// </summary>
        public int DrawView(
            Rect viewport,
            IWorld world,
            float viewCenterX,
            float viewCenterZ,
            int playerY,
            int sliceY,
            bool underground,
            string layerMode,
            string renderMode,
            int gridStyle,
            bool indoors,
            float pxPerBlock,
            bool surfaceStructure,
            Texture2D circleTarget = null,
            float circleMaskRadius = 0f)
        {
            if (pxPerBlock <= 0f || viewport.width <= 0f || viewport.height <= 0f) return 0;

            bool cave = IsCave(layerMode, underground, indoors);
            // Interior cutaway: an enclosed surface structure renders the room in detail and
            // shows the already-explored surface as a dim, opaque backdrop (no x-ray).
            // The explicit Interior layer forces it (icebergs, skylit rooms, ambiguous
            // structures). Auto/Cave only apply it when the player is actually near the
            // surface: a shallow cave under a mountain must not show the overworld above it.
            bool indoorContext = cave && (layerMode == "Interior" || (indoors && surfaceStructure));
            // Interior is the precision layer (Wayfinder-style): sample the exact player Y,
            // not the 16-block band floor. It re-renders on Y changes; Auto/Cave keep bands.
            bool interiorExact = layerMode == "Interior";
            byte mode = BuildMode(cave, renderMode, gridStyle, indoorContext, interiorExact);
            int band = interiorExact
                ? Mathf.Clamp(sliceY, short.MinValue, short.MaxValue)
                : (cave ? WayfinderChunks.BandCoord(sliceY) : 0);
            int effectiveSlice = interiorExact
                ? band
                : (cave ? band * WayfinderChunks.BandSize : 0);

            // Layer/mode switches must not show the previous layer's chunks while the new
            // ones render: drop every texture that no longer matches the current style.
            // The band/Y is part of the slot key, so Interior's exact-Y changes reuse old
            // slots when the player returns to a height instead of re-rendering everything.
            string layerKey = $"{mode}";
            if (layerKey != lastLayerKey)
            {
                lastLayerKey = layerKey;
                Clear();
            }

            float halfSpanX = viewport.width * 0.5f / pxPerBlock;
            float halfSpanZ = viewport.height * 0.5f / pxPerBlock;
            int minCx = WayfinderChunks.ChunkCoord(Mathf.FloorToInt(viewCenterX - halfSpanX)) - 1;
            int maxCx = WayfinderChunks.ChunkCoord(Mathf.FloorToInt(viewCenterX + halfSpanX)) + 1;
            int minCz = WayfinderChunks.ChunkCoord(Mathf.FloorToInt(viewCenterZ - halfSpanZ)) - 1;
            int maxCz = WayfinderChunks.ChunkCoord(Mathf.FloorToInt(viewCenterZ + halfSpanZ)) + 1;

            // Uploads (and the circle compositor) stay on Repaint only: OnGUI also runs a
            // Layout pass, and recomposing there would double the CPU/GPU work.
            bool allowRender = Event.current == null || Event.current.type == EventType.Repaint;
            bool doCompose = circleTarget != null && allowRender;

            bool mayRecordSurface = !underground && !indoors;
            int targetW = 0;
            int targetH = 0;
            if (doCompose)
            {
                targetW = circleTarget.width;
                targetH = circleTarget.height;
                int needed = targetW * targetH;
                if (composeBuffer == null || composeBuffer.Length != needed)
                    composeBuffer = new Color32[needed];
                composeWidth = targetW;
                composeHeight = targetH;
                // Missing chunks read as the map's dark background, not as the game world.
                Color32 bg = new Color32(UnexploredR, UnexploredG, UnexploredB, 255);
                for (int i = 0; i < needed; i++) composeBuffer[i] = bg;
            }

            // Group-local coordinates: the group clips every chunk texture to the viewport,
            // so chunks never paint over window chrome or neighbouring panels.
            GUI.BeginGroup(viewport);
            float centerLX = viewport.width * 0.5f;
            float centerLY = viewport.height * 0.5f;
            float now = Time.unscaledTime;
            int pending = 0;
            candidates.Clear();

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    long key = WayfinderChunks.Key(cx, cz, mode, band);
                    float worldMinX = cx * (float)WayfinderChunks.Size;
                    float worldMinZ = cz * (float)WayfinderChunks.Size;
                    float fx0 = centerLX + (worldMinX - viewCenterX) * pxPerBlock;
                    float fx1 = centerLX + (worldMinX + WayfinderChunks.Size - viewCenterX) * pxPerBlock;
                    float fy0 = centerLY + (viewCenterZ - (worldMinZ + WayfinderChunks.Size)) * pxPerBlock;
                    float fy1 = centerLY + (viewCenterZ - worldMinZ) * pxPerBlock;
                    // Pixel-align each chunk quad: adjacent chunks share the same world edge and
                    // therefore the same rounded pixel, so there is no anti-aliased boundary
                    // pixel where the dark backdrop would bleed through as a seam line.
                    Rect rect = new Rect(Mathf.Round(fx0), Mathf.Round(fy0), Mathf.Round(fx1) - Mathf.Round(fx0), Mathf.Round(fy1) - Mathf.Round(fy0));
                    if (!rect.Overlaps(new Rect(0, 0, viewport.width, viewport.height))) continue;

                    if (slots.TryGetValue(key, out Slot slot) && slot != null && slot.Used)
                    {
                        slot.LastUsed = now;
                        float fade = FadeAlpha(slot, now);
                        if (circleTarget == null)
                        {
                            // StretchToFill, not ScaleToFit: rounding the X and Y edges
                            // independently can leave a chunk rect 1px out of square (e.g.
                            // 179x180), and ScaleToFit then draws the 32x32 texture at
                            // min(w,h), leaving a 1px uncovered strip that shows the dark
                            // canvas fill as a seam line.
                            Color previous = GUI.color;
                            GUI.color = new Color(1f, 1f, 1f, fade);
                            GUI.DrawTexture(rect, slot.Texture, ScaleMode.StretchToFill, true);
                            GUI.color = previous;
                        }
                        else if (doCompose)
                        {
                            BlitSlotToCompose(slot, rect, fade);
                        }

                        float age = now - slot.LastRender;
                        if (age > StaleSeconds)
                        {
                            candidates.Add(new Candidate
                            {
                                Exists = true,
                                Missing = false,
                                Cx = cx,
                                Cz = cz,
                                Key = key,
                                Distance = Distance2(cx, cz, viewCenterX, viewCenterZ)
                            });
                        }
                    }
                    else
                    {
                        pending++;
                        candidates.Add(new Candidate
                        {
                            Exists = true,
                            Missing = true,
                            Cx = cx,
                            Cz = cz,
                            Key = key,
                            Distance = Distance2(cx, cz, viewCenterX, viewCenterZ)
                        });
                    }
                }
            }

            if (doCompose)
            {
                ApplyCircleAlpha(circleMaskRadius);
                circleTarget.SetPixels32(composeBuffer);
                circleTarget.Apply(false);
                GUI.DrawTexture(new Rect(0f, 0f, viewport.width, viewport.height),
                    circleTarget, ScaleMode.StretchToFill, true);
            }

            GUI.EndGroup();

            // A cold map can need hundreds of chunks (25 % zoom). Fill a few per frame,
            // nearest-first, under a small time budget so opening/zooming never hitches.
            // Uploads stay on Repaint only, as the old renderer required.
            if (allowRender && world != null && candidates.Count > 0)
            {
                candidates.Sort(CandidateOrder);
                RenderWatch.Restart();
                int rendered = 0;
                for (int i = 0; i < candidates.Count && rendered < MaxChunkRendersPerFrame; i++)
                {
                    Candidate c = candidates[i];
                    RenderChunk(world, c.Cx, c.Cz, mode, cave, effectiveSlice, band, playerY, renderMode, gridStyle, indoorContext, mayRecordSurface);
                    rendered++;
                    if (RenderWatch.Elapsed.TotalMilliseconds >= ChunkRenderBudgetMs) break;
                }
            }

            return pending;
        }

        // =====================================================================
        //        CIRCLE MINIMAP COMPOSITOR
        // =====================================================================

        private static float FadeAlpha(Slot slot, float now)
        {
            if (slot == null || slot.FirstRenderAt < 0f) return 1f;
            float t = Mathf.Clamp01((now - slot.FirstRenderAt) / FadeSeconds);
            return t * t * (3f - 2f * t); // smoothstep: no hard pop
        }

        private void BlitSlotToCompose(Slot slot, Rect rect, float fade)
        {
            if (slot == null || slot.Pixels == null || composeBuffer == null) return;

            int rectLeft = Mathf.RoundToInt(rect.x);
            int rectTop = Mathf.RoundToInt(rect.y);
            int rectW = Mathf.RoundToInt(rect.width);
            int rectH = Mathf.RoundToInt(rect.height);
            if (rectW <= 0 || rectH <= 0) return;

            int x0 = Mathf.Max(0, rectLeft);
            int y0 = Mathf.Max(0, rectTop);
            int x1 = Mathf.Min(composeWidth, rectLeft + rectW);
            int y1 = Mathf.Min(composeHeight, rectTop + rectH);

            for (int guiY = y0; guiY < y1; guiY++)
            {
                int relY = guiY - rectTop;
                // Texture row 0 (z = 0) is drawn at the bottom of the quad.
                int tz = ((rectH - 1 - relY) * WayfinderChunks.Size) / rectH;
                if (tz < 0) tz = 0; else if (tz >= WayfinderChunks.Size) tz = WayfinderChunks.Size - 1;
                // SetPixels32 expects bottom-up rows.
                int bufRow = composeHeight - 1 - guiY;
                int bufBase = bufRow * composeWidth;
                int srcBase = tz * WayfinderChunks.Size;
                for (int guiX = x0; guiX < x1; guiX++)
                {
                    int relX = guiX - rectLeft;
                    int tx = (relX * WayfinderChunks.Size) / rectW;
                    if (tx < 0) tx = 0; else if (tx >= WayfinderChunks.Size) tx = WayfinderChunks.Size - 1;
                    Color32 c = slot.Pixels[srcBase + tx];
                    if (fade < 0.999f)
                    {
                        Color32 bg = new Color32(UnexploredR, UnexploredG, UnexploredB, 255);
                        c = Color32.Lerp(bg, c, fade);
                    }
                    composeBuffer[bufBase + guiX] = c;
                }
            }
        }

        private void ApplyCircleAlpha(float radius)
        {
            if (composeBuffer == null) return;
            float cx = composeWidth * 0.5f;
            float cy = composeHeight * 0.5f;
            float r = radius > 0f ? radius : Mathf.Min(cx, cy) - 1f;
            float inner = Mathf.Max(0f, r - 1.5f);

            for (int y = 0; y < composeHeight; y++)
            {
                float dy = y + 0.5f - cy;
                int row = y * composeWidth;
                for (int x = 0; x < composeWidth; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    Color32 c = composeBuffer[row + x];
                    if (dist >= r) c.a = 0;
                    else if (dist > inner) c.a = (byte)(255f * (r - dist) / 1.5f);
                    else c.a = 255;
                    composeBuffer[row + x] = c;
                }
            }
        }

        public void Clear()
        {
            foreach (Slot slot in slots.Values)
            {
                if (slot != null && slot.Texture != null) UnityEngine.Object.Destroy(slot.Texture);
            }
            slots.Clear();
        }

        private static float Distance2(int cx, int cz, float centerX, float centerZ)
        {
            float wx = cx * (float)WayfinderChunks.Size + WayfinderChunks.Size * 0.5f - centerX;
            float wz = cz * (float)WayfinderChunks.Size + WayfinderChunks.Size * 0.5f - centerZ;
            return wx * wx + wz * wz;
        }

        private static bool IsCave(string layerMode, bool underground, bool indoors)
        {
            // Auto follows the player into covered/interior spaces, even when a tall structure
            // roof is beyond the underground probe's 45-block reach.
            return layerMode == "Cave" || layerMode == "Interior" || layerMode == "Slice"
                || (layerMode == "Auto" && (underground || indoors));
        }

        private static byte BuildMode(bool cave, string renderMode, int gridStyle, bool indoors,
            bool interiorExact)
        {
            byte renderTypeIdx = (byte)(renderMode == "Night" ? 1 : (renderMode == "Topo" ? 2 : (renderMode == "Biome" ? 3 : 0)));
            // Bits: 16 = chunk grid, 32 = interior cutaway, 64 = region grid, 128 = exact-Y
            // Interior. They are part of the key so toggling never reuses the wrong tile cache.
            // The exact-Y bit also prevents an Interior Y=5 key from colliding with Cave band 5.
            byte gridBits = (byte)(((gridStyle & 1) != 0 ? 16 : 0) | ((gridStyle & 2) != 0 ? 64 : 0));
            return (byte)((cave ? 1 : 0) + (renderTypeIdx << 2) + gridBits
                + (indoors ? 32 : 0) + (interiorExact ? 128 : 0));
        }

        // =====================================================================
        //        CHUNK RENDERING
        // =====================================================================

        private void RenderChunk(
            IWorld world,
            int cx,
            int cz,
            byte mode,
            bool cave,
            int effectiveSlice,
            int band,
            int playerY,
            string renderMode,
            int gridStyle,
            bool indoorContext,
            bool mayRecordSurface)
        {
            long key = WayfinderChunks.Key(cx, cz, mode, band);
            // The fog/cave-store band is always the 16-block band, even when Interior samples
            // the exact player Y for precision.
            int fogBand = cave ? WayfinderChunks.BandCoord(effectiveSlice) : 0;

            if (slots.Count >= MaxSlots && !slots.ContainsKey(key)) EvictOldest();

            if (!slots.TryGetValue(key, out Slot slot))
            {
                slot = new Slot
                {
                    Texture = new Texture2D(WayfinderChunks.Size, WayfinderChunks.Size, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    },
                    Cx = cx,
                    Cz = cz,
                    Band = fogBand
                };
                slots[key] = slot;
            }

            // Fog of war: cave/slice chunks outside the player's visited area stay black.
            if (cave && !IsCaveChunkVisited(fogBand, cx, cz))
            {
                FillUnexploredSlot(slot, Time.unscaledTime);
                return;
            }

            int px = cx * WayfinderChunks.Size;
            int pz = cz * WayfinderChunks.Size;

            Chunk[,,] pwChunks = null;
            int c0x = 0, c0y = 0, c0z = 0;
            if (world is PriorityWorld pw && PwChunksField != null)
            {
                try
                {
                    pwChunks = PwChunksField.GetValue(pw) as Chunk[,,];
                    c0x = (int)PwC0xField.GetValue(pw);
                    c0y = (int)PwC0yField.GetValue(pw);
                    c0z = (int)PwC0zField.GetValue(pw);
                }
                catch { pwChunks = null; }
            }

            WayfinderStore store = WayfinderModule.SurfaceMapStore;
            WayfinderCaveStore caveStore = WayfinderModule.CaveMapStore;
            bool showSurfaceAboveCaves = WayfinderModule.ShowSurfaceAboveCaves == null
                || WayfinderModule.ShowSurfaceAboveCaves.Value;
            lightSources.Clear();
            cellDensity.Clear();
            float now = Time.unscaledTime;
            int sliceKey = cave ? effectiveSlice : 0;
            int size = WayfinderChunks.Size;
            Array.Clear(finalPixel, 0, finalPixel.Length);
            Array.Clear(chunkSamples, 0, chunkSamples.Length);
            int stride = Stride;

            // Sample a 1-block halo around the chunk too: slope shading and cave-wall edge
            // detection then use real neighbour data at chunk borders, which removes the
            // repeating boundary seams between adjacent chunk textures.
            for (int z = 0; z < stride; z++)
            {
                int wz = pz + z - Halo;
                for (int x = 0; x < stride; x++)
                {
                    int wx = px + x - Halo;
                    int idx = z * stride + x;

                    if (WayfinderTileCache.TryGet(wx, wz, mode, sliceKey, out Color32 cached, out short cachedH, out byte cachedFlags))
                    {
                        scratch[idx] = cached;
                        chunkSamples[idx] = default;
                        chunkSamples[idx].TopY = cachedH;
                        chunkSamples[idx].FromCache = true;
                        // Restore wall/room state: the cave wall-edge probe and the interior
                        // cutaway must not treat a cached wall as an open floor.
                        chunkSamples[idx].IsCaveWall = (cachedFlags & WayfinderTileCache.FlagCaveWall) != 0;
                        chunkSamples[idx].IsOpenRoom = (cachedFlags & WayfinderTileCache.FlagOpenRoom) != 0;
                        finalPixel[idx] = true;
                        continue;
                    }

                    bool sampled;
                    TerrainSample sample;
                    if (cave)
                    {
                        sampled = WayfinderEngine.SampleColumnLayerPublic(world, pwChunks, c0x, c0y, c0z, wx, wz, effectiveSlice, out sample);

                        // Persisted cave first: it is real explored data from this band. It is
                        // 4x4-block resolution, so only use it when the live column is missing
                        // (unloaded chunk) or empty.
                        if ((!sampled || sample.BlockId == 0) && caveStore != null
                            && caveStore.TryGetCave(wx, wz, fogBand, out WayfinderCaveCell savedCave))
                        {
                            sample = WayfinderEngine.FromCaveCell(savedCave);
                            sampled = true;
                        }

                        if ((!sampled || sample.BlockId == 0) && showSurfaceAboveCaves && store != null
                            && IsDenseStoreCell(store, wx, wz)
                            && store.TryGetSurface(wx, wz, out WayfinderCell surfaceCell))
                        {
                            sample = WayfinderEngine.FromCell(surfaceCell);
                            sample.IsSurfaceFallback = true;
                            sampled = true;
                        }
                        // No live surface sampling while underground: that revealed terrain the
                        // player never visited (the old AlwaysMapSurface x-ray).
                    }
                    else
                    {
                        sampled = WayfinderEngine.SampleColumnFastPublic(world, pwChunks, c0x, c0y, c0z, wx, wz, playerY, 44, 52, out sample);

                        if (sampled && sample.BlockId != 0)
                        {
                            // Only record what the player actually maps from the surface; being
                            // indoors/underground must never add cells above unknown terrain.
                            if (mayRecordSurface && store != null)
                            {
                                uint tint = renderMode == "Biome"
                                    ? WayfinderEngine.PackColor(BiomeHelper.GetBiomeTint(world, wx, sample.TopY, wz))
                                    : 0;
                                store.RecordSurface(wx, wz, WayfinderEngine.ToCell(sample, tint));
                            }
                        }
                        else if (store != null && IsDenseStoreCell(store, wx, wz)
                            && store.TryGetSurface(wx, wz, out WayfinderCell stored))
                        {
                            // Explored in a previous session: show it even while unloaded.
                            // Isolated cells are rejected: they are x-ray leftovers, not a
                            // surface area the player ever visited.
                            sample = WayfinderEngine.FromCell(stored);
                            sampled = true;
                        }
                    }

                    if (!sampled || sample.BlockId == 0)
                    {
                        scratch[idx] = new Color32(UnexploredR, UnexploredG, UnexploredB, 255);
                        chunkSamples[idx] = default;
                        finalPixel[idx] = true;
                        continue;
                    }

                    chunkSamples[idx] = sample;
                    finalPixel[idx] = false;
                    if (renderMode == "Night" && NanoPalette.IsLightSource(sample.BlockId))
                    {
                        lightSources.Add((x - Halo, z - Halo, NanoPalette.GetLightColor(sample.BlockId)));
                    }
                }
            }

            // Persist live cave/slice samples so an explored cave survives a reload. Only
            // interior pixels (no halo), live samples (no cache/store/fallback), and chunks
            // inside the visited fog gate are recorded: panning the map never maps new caves.
            if (cave && caveStore != null)
                RecordCaveChunk(caveStore, px, pz, fogBand);

            if (indoorContext) ApplyIndoorCutaway(px, pz, store);

            ShadeAndUpload(slot.Texture, px, pz, size, cave, sliceKey, mode, renderMode, gridStyle, world);

            if (slot.Pixels == null) slot.Pixels = new Color32[WayfinderChunks.Size * WayfinderChunks.Size];
            Array.Copy(upload, slot.Pixels, upload.Length);

            slot.Used = true;
            slot.LastRender = now;
            slot.LastUsed = now;
            if (slot.FirstRenderAt < 0f) slot.FirstRenderAt = now;
        }

        /// <summary>
        /// Aggregates the live 1-block cave samples of a 32x32 chunk into the store's 4x4
        /// cells. One live sample per cell is enough: the fallback is intentionally blockier
        /// than the live render, and cache/store/fallback pixels are skipped so stale data is
        /// never re-persisted. <paramref name="px"/>/<paramref name="pz"/> are world chunk mins.
        /// </summary>
        private void RecordCaveChunk(WayfinderCaveStore caveStore, int px, int pz, int band)
        {
            int size = WayfinderChunks.Size;
            for (int cellZ = 0; cellZ < size; cellZ += 4)
            {
                for (int cellX = 0; cellX < size; cellX += 4)
                {
                    // Prefer the canonical origin pixel: only canonical samples may update an
                    // existing stored cell (non-canonical ones merely seed empty cells).
                    int originIdx = (cellZ + Halo) * Stride + (cellX + Halo);
                    TerrainSample origin = chunkSamples[originIdx];
                    if (IsLiveCaveSample(origin))
                    {
                        caveStore.RecordCave(px + cellX, pz + cellZ, band,
                            WayfinderEngine.ToCaveCell(origin, 0));
                        continue;
                    }

                    bool recorded = false;
                    for (int z = cellZ; z < cellZ + 4 && !recorded; z++)
                    {
                        int rowBase = (z + Halo) * Stride + Halo;
                        for (int x = cellX; x < cellX + 4; x++)
                        {
                            TerrainSample sample = chunkSamples[rowBase + x];
                            if (!IsLiveCaveSample(sample)) continue;

                            caveStore.RecordCave(px + x, pz + z, band,
                                WayfinderEngine.ToCaveCell(sample, 0));
                            recorded = true;
                            break;
                        }
                    }
                }
            }
        }

        private static bool IsLiveCaveSample(in TerrainSample sample)
        {
            return sample.BlockId != 0
                && !sample.FromCache
                && !sample.FromStore
                && !sample.IsSurfaceFallback;
        }

        private void FillUnexploredSlot(Slot slot, float now)
        {
            Color32 black = new Color32(UnexploredR, UnexploredG, UnexploredB, 255);
            for (int i = 0; i < upload.Length; i++) upload[i] = black;
            slot.Texture.SetPixels32(upload);
            slot.Texture.Apply(false);
            if (slot.Pixels == null) slot.Pixels = new Color32[upload.Length];
            Array.Copy(upload, slot.Pixels, upload.Length);
            slot.Used = true;
            slot.LastRender = now;
            slot.LastUsed = now;
            if (slot.FirstRenderAt < 0f) slot.FirstRenderAt = now;
        }

        /// <summary>
        /// A store cell is only trusted when it belongs to a dense explored patch. The old
        /// underground sampler wrote isolated 4×4 cells around the player (the "dots" seen on
        /// the map): they are x-ray leftovers, not a surface area anyone visited.
        /// </summary>
        private bool IsDenseStoreCell(WayfinderStore store, int wx, int wz)
        {
            if (store == null) return false;
            int cx = WayfinderRegionMath.CellCoord(wx);
            int cz = WayfinderRegionMath.CellCoord(wz);
            long key = ((long)cx << 32) ^ (uint)cz;
            if (cellDensity.TryGetValue(key, out bool dense)) return dense;

            int known = 0;
            int cs = WayfinderRegionMath.CellSize;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (store.TryGetSurface((cx + dx) * cs + 1, (cz + dz) * cs + 1, out _))
                        known++;
                }
            }

            dense = known >= MinDenseCells;
            cellDensity[key] = dense;
            return dense;
        }

        /// <summary>
        /// Interior cutaway for a surface structure. Pixels belonging to an open room (plus a
        /// two-block wall ring) keep the detailed cave/slice render; everything else is replaced
        /// by the already-explored surface cell, drawn dimmed. Unexplored surface cells stay
        /// black, so standing indoors never reveals the surface above unknown terrain.
        /// </summary>
        private void ApplyIndoorCutaway(int px, int pz, WayfinderStore store)
        {
            int stride = Stride;
            int size = WayfinderChunks.Size;

            for (int i = 0; i < openMask.Length; i++)
                openMask[i] = chunkSamples[i].IsOpenRoom;

            Array.Clear(interiorMask, 0, interiorMask.Length);
            for (int z = 0; z < stride; z++)
            {
                for (int x = 0; x < stride; x++)
                {
                    if (!openMask[z * stride + x]) continue;
                    int z0 = Mathf.Max(0, z - 2);
                    int z1 = Mathf.Min(stride - 1, z + 2);
                    int x0 = Mathf.Max(0, x - 2);
                    int x1 = Mathf.Min(stride - 1, x + 2);
                    for (int nz = z0; nz <= z1; nz++)
                    {
                        int row = nz * stride;
                        for (int nx = x0; nx <= x1; nx++)
                            interiorMask[row + nx] = true;
                    }
                }
            }

            for (int z = 0; z < size; z++)
            {
                int wz = pz + z;
                for (int x = 0; x < size; x++)
                {
                    int idx = (z + Halo) * stride + (x + Halo);
                    if (interiorMask[idx]) continue;

                    if (store != null && IsDenseStoreCell(store, px + x, wz)
                        && store.TryGetSurface(px + x, wz, out WayfinderCell cell))
                    {
                        chunkSamples[idx] = WayfinderEngine.FromCell(cell);
                        chunkSamples[idx].IsSurfaceFallback = true;
                        finalPixel[idx] = false;
                        scratch[idx] = default;
                    }
                    else
                    {
                        chunkSamples[idx] = default;
                        finalPixel[idx] = true;
                        scratch[idx] = new Color32(UnexploredR, UnexploredG, UnexploredB, 255);
                    }
                }
            }
        }

        private void EvictOldest()
        {
            long oldestKey = 0;
            Slot oldest = null;
            foreach (KeyValuePair<long, Slot> pair in slots)
            {
                if (pair.Value == null) continue;
                if (oldest == null || pair.Value.LastUsed < oldest.LastUsed)
                {
                    oldest = pair.Value;
                    oldestKey = pair.Key;
                }
            }

            if (oldest != null)
            {
                if (oldest.Texture != null) UnityEngine.Object.Destroy(oldest.Texture);
                slots.Remove(oldestKey);
            }
        }

        // =====================================================================
        //        SHADING (absolute, view-independent, like Wayfinder's renderers)
        // =====================================================================

        private void ShadeAndUpload(
            Texture2D texture,
            int px,
            int pz,
            int size,
            bool cave,
            int sliceKey,
            byte mode,
            string renderType,
            int gridStyle,
            IWorld world)
        {
            int stride = Stride;
            for (int z = 0; z < size; z++)
            {
                int wz = pz + z;
                for (int x = 0; x < size; x++)
                {
                    int idx = (z + Halo) * stride + (x + Halo);
                    if (finalPixel[idx])
                    {
                        upload[z * size + x] = scratch[idx];
                        continue;
                    }

                    TerrainSample sample = chunkSamples[idx];
                    if (sample.BlockId == 0)
                    {
                        // Unreachable in practice (unexplored sets finalPixel) — keep upload consistent.
                        upload[z * size + x] = scratch[idx];
                        continue;
                    }

                    Color32 baseColor;
                    bool isEdgeWall = false;

                    if (cave && sample.IsCaveWall)
                    {
                        if (IsNeighbourBlock(idx - 1) && !chunkSamples[idx - 1].IsCaveWall) isEdgeWall = true;
                        else if (IsNeighbourBlock(idx + 1) && !chunkSamples[idx + 1].IsCaveWall) isEdgeWall = true;
                        else if (IsNeighbourBlock(idx - stride) && !chunkSamples[idx - stride].IsCaveWall) isEdgeWall = true;
                        else if (IsNeighbourBlock(idx + stride) && !chunkSamples[idx + stride].IsCaveWall) isEdgeWall = true;

                        Color32 rock = NanoPalette.GetBlockColor(sample.BlockId);
                        if (sample.IsOre)
                        {
                            baseColor = new Color32(
                                (byte)Mathf.Min(255, rock.r * 155 / 100),
                                (byte)Mathf.Min(255, rock.g * 155 / 100),
                                (byte)Mathf.Min(255, rock.b * 155 / 100), 255);
                        }
                        else
                        {
                            float wallFactor = isEdgeWall ? 0.34f : 0.50f;
                            baseColor = new Color32(
                                (byte)Mathf.Clamp(rock.r * wallFactor, 0, 255),
                                (byte)Mathf.Clamp(rock.g * wallFactor, 0, 255),
                                (byte)Mathf.Clamp(rock.b * (wallFactor * 1.05f), 0, 255), 255);
                        }
                    }
                    else if (sample.IsOre)
                    {
                        Color32 rock = NanoPalette.GetBlockColor(sample.BlockId);
                        baseColor = new Color32(
                            (byte)Mathf.Min(255, rock.r * 145 / 100),
                            (byte)Mathf.Min(255, rock.g * 145 / 100),
                            (byte)Mathf.Min(255, rock.b * 145 / 100), 255);
                    }
                    else
                    {
                        baseColor = NanoPalette.GetBlockColor(sample.BlockId);
                    }

                    if (sample.IsPainted && RYBUtil.RGB32 != null && sample.PaintHue < RYBUtil.RGB32.Length)
                    {
                        Color32 paint = RYBUtil.RGB32[sample.PaintHue];
                        baseColor = new Color32(
                            (byte)((baseColor.r * paint.r) / 255),
                            (byte)((baseColor.g * paint.g) / 255),
                            (byte)((baseColor.b * paint.b) / 255), 255);
                    }

                    if (sample.IsLiquid)
                    {
                        if (sample.LiquidId == 22) baseColor = new Color32(255, 120, 20, 255);
                        else if (sample.LiquidId == 108) baseColor = new Color32(75, 255, 35, 255);
                        else
                        {
                            Color32 bed = NanoPalette.GetBlockColor(sample.BedBlockId);
                            float depth = Mathf.Clamp01(sample.WaterDepth / 12f);
                            Color water = Color.Lerp(new Color(0.24f, 0.68f, 0.92f), new Color(0.06f, 0.18f, 0.52f), depth);
                            baseColor = Color.Lerp(bed, water, 0.55f + depth * 0.40f);
                        }
                    }

                    // Slope shading only: absolute neighbour heights (halo guarantees real
                    // border data), never player-relative, so a chunk looks identical no
                    // matter who is looking at it or from where.
                    int curY = sample.TopY;
                    int northY = curY;
                    if (IsNeighbourBlock(idx + stride))
                        northY = chunkSamples[idx + stride].TopY;
                    float shade = 1.0f + Mathf.Clamp((curY - northY) * 0.14f, -0.32f, 0.32f);

                    if (renderType == "Night")
                    {
                        float totalLight = 0f;
                        Color lightSum = Color.black;
                        for (int li = 0; li < lightSources.Count; li++)
                        {
                            var ls = lightSources[li];
                            float ldx = x - ls.x;
                            float ldz = z - ls.z;
                            float distSq = ldx * ldx + ldz * ldz;
                            if (distSq > 64f) continue;
                            float dist = Mathf.Sqrt(distSq);
                            float atten = 1.0f - (dist / 8.0f);
                            atten *= atten;
                            totalLight += atten;
                            lightSum += ls.color * atten;
                        }

                        if (NanoPalette.IsLightSource(sample.BlockId))
                        {
                            baseColor = Color.Lerp(baseColor, Color.white, 0.35f);
                            shade = 1.35f;
                        }
                        else
                        {
                            shade *= Mathf.Clamp01(0.30f + totalLight * 0.95f);
                            if (totalLight > 0.05f)
                            {
                                baseColor = Color.Lerp(baseColor, lightSum / totalLight, Mathf.Clamp01(totalLight * 0.40f));
                            }
                        }
                    }
                    else if (renderType == "Topo")
                    {
                        if (Mathf.Abs(curY) % 4 == 0) shade *= 0.70f;
                        if (Mathf.Abs(curY) % 20 == 0) shade *= 0.50f;

                        Color topoTint;
                        if (curY < 60) topoTint = Color.Lerp(new Color(0.30f, 0.70f, 0.35f), new Color(0.55f, 0.80f, 0.40f), (curY + 20f) / 80f);
                        else if (curY < 95) topoTint = Color.Lerp(new Color(0.85f, 0.80f, 0.45f), new Color(0.75f, 0.55f, 0.35f), (curY - 60f) / 35f);
                        else if (curY < 130) topoTint = Color.Lerp(new Color(0.65f, 0.55f, 0.45f), new Color(0.60f, 0.62f, 0.68f), (curY - 95f) / 35f);
                        else topoTint = Color.Lerp(new Color(0.75f, 0.80f, 0.88f), new Color(0.95f, 0.98f, 1.0f), Mathf.Clamp01((curY - 130f) / 50f));
                        baseColor = Color.Lerp(baseColor, topoTint, 0.35f);
                    }
                    else if (renderType == "Biome")
                    {
                        Color tint = sample.FromStore && sample.StoredBiomeTint != 0
                            ? (Color)WayfinderEngine.UnpackColor(sample.StoredBiomeTint)
                            : BiomeHelper.GetBiomeTint(world, px + x, curY, wz);
                        baseColor = Color.Lerp(baseColor, tint, 0.45f);
                    }

                    if (sample.IsSurfaceFallback)
                    {
                        // Opaque, muted context layer: the surface stays readable as "known
                        // ground" without competing with the detailed interior. Desaturate
                        // toward luminance, dim hard and add a slight cool tint.
                        float lum = baseColor.r * 0.299f + baseColor.g * 0.587f + baseColor.b * 0.114f;
                        float r = Mathf.Lerp(baseColor.r, lum, 0.45f) * 0.30f * 0.94f;
                        float g = Mathf.Lerp(baseColor.g, lum, 0.45f) * 0.30f * 0.97f;
                        float b = Mathf.Lerp(baseColor.b, lum, 0.45f) * 0.30f * 1.04f;
                        baseColor = new Color32(
                            (byte)Mathf.Clamp(r, 0f, 255f),
                            (byte)Mathf.Clamp(g, 0f, 255f),
                            (byte)Mathf.Clamp(b, 0f, 255f), 255);
                    }

                    Color32 finalCol = new Color32(
                        (byte)Mathf.Clamp(baseColor.r * shade, 0f, 255f),
                        (byte)Mathf.Clamp(baseColor.g * shade, 0f, 255f),
                        (byte)Mathf.Clamp(baseColor.b * shade, 0f, 255f), 255);

                    if (gridStyle != 0)
                    {
                        int gx = px + x;
                        int gz = pz + z;
                        bool chunkLine = (gridStyle & 1) != 0 && ((gx & 31) == 0 || (gz & 31) == 0);
                        bool regionLine = (gridStyle & 2) != 0 && ((gx & 255) == 0 || (gz & 255) == 0);
                        if (chunkLine)
                            finalCol = Color32.Lerp(finalCol, new Color32(50, 210, 255, 255), 0.65f);
                        if (regionLine)
                            finalCol = Color32.Lerp(finalCol, new Color32(255, 215, 80, 255), 0.85f);
                    }

                    scratch[idx] = finalCol;
                    upload[z * size + x] = finalCol;
                    if (sample.FromStore)
                    {
                        // Persisted cells are not cached: when the chunk loads, the live sample
                        // must be allowed to replace the stored (possibly stale) color.
                    }
                    else
                    {
                        byte flags = 0;
                        if (cave && sample.IsCaveWall) flags |= WayfinderTileCache.FlagCaveWall;
                        if (cave && sample.IsOpenRoom) flags |= WayfinderTileCache.FlagOpenRoom;
                        WayfinderTileCache.Put(px + x, wz, mode, sliceKey, finalCol, (short)curY, flags);
                    }
                }
            }

            texture.SetPixels32(upload);
            texture.Apply(false);
        }

        private bool IsNeighbourBlock(int idx)
        {
            // Cache entries were only ever Put for real blocks, so FromCache counts as one.
            TerrainSample n = chunkSamples[idx];
            return n.BlockId != 0 || n.FromCache;
        }
    }
}
