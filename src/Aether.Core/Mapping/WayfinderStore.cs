using System;
using System.Collections.Generic;
using System.IO;

namespace Aether.Core.Mapping
{
    [Flags]
    public enum WayfinderCellFlags : byte
    {
        None = 0,
        Liquid = 1 << 0,
        Painted = 1 << 1,
        CaveWall = 1 << 2,
        Ore = 1 << 3
    }

    /// <summary>
    /// Compact surface sample persisted for one 4x4 block cell.
    /// Deliberately Unity-free so serialization can be unit-tested.
    /// </summary>
    public struct WayfinderCell
    {
        public ushort BlockId;
        public short TopY;
        public ushort LiquidId;
        public ushort BedBlockId;
        public byte WaterDepth;
        public ushort PaintHue;
        public byte Flags;
        public uint BiomeTint;

        public bool IsLiquid => (Flags & (byte)WayfinderCellFlags.Liquid) != 0;
        public bool IsPainted => (Flags & (byte)WayfinderCellFlags.Painted) != 0;
        public bool IsCaveWall => (Flags & (byte)WayfinderCellFlags.CaveWall) != 0;
        public bool IsOre => (Flags & (byte)WayfinderCellFlags.Ore) != 0;

        public bool SameSurface(in WayfinderCell other)
        {
            return BlockId == other.BlockId
                && TopY == other.TopY
                && LiquidId == other.LiquidId
                && BedBlockId == other.BedBlockId
                && WaterDepth == other.WaterDepth
                && PaintHue == other.PaintHue
                && Flags == other.Flags;
        }
    }

    public static class WayfinderRegionMath
    {
        public const int CellSize = 4;
        public const int RegionBlocks = 256;
        public const int RegionCells = RegionBlocks / CellSize;
        public const int CellsPerRegion = RegionCells * RegionCells;

        public static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            if ((value % divisor != 0) && ((value < 0) != (divisor < 0)))
                quotient--;
            return quotient;
        }

        public static int CellCoord(int world) => FloorDiv(world, CellSize);
        public static int RegionCoord(int world) => FloorDiv(world, RegionBlocks);

        public static int LocalCellCoord(int world)
        {
            int cell = CellCoord(world);
            return cell - RegionCoord(world) * RegionCells;
        }
    }

    /// <summary>
    /// Per-world explored-surface store. Each region is a sparse binary file under
    /// <c>&lt;world&gt;/aether_wayfinder/</c>; dirty regions are written atomically and on a budget.
    /// </summary>
    public sealed class WayfinderStore
    {
        private const string Magic = "AERM";
        private const byte Version = 1;
        private const int MaxCachedRegions = 96;

        private sealed class Region
        {
            public int Rx;
            public int Rz;
            public readonly Dictionary<ushort, WayfinderCell> Cells = new Dictionary<ushort, WayfinderCell>();
            public bool Dirty;
            public long LastTouch;
        }

        private readonly string directory;
        private readonly Action<string>? log;
        private readonly Dictionary<long, Region> regions = new Dictionary<long, Region>();
        private readonly HashSet<long> dirty = new HashSet<long>();
        private readonly HashSet<long> countedRegions = new HashSet<long>();
        private readonly List<long> flushScratch = new List<long>();
        private long tick;

        public long KnownCellCount { get; private set; }
        public int CachedRegionCount => regions.Count;
        public int DirtyRegionCount => dirty.Count;
        public int KnownRegionCount => countedRegions.Count;
        public int MaxCached => MaxCachedRegions;

        public WayfinderStore(string directory, Action<string>? log = null)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("directory is required", nameof(directory));
            this.directory = directory;
            this.log = log;
            Directory.CreateDirectory(directory);
            ScanIndex();
        }

        /// <summary>
        /// Reads only the region headers so the UI can report explored regions/cells without
        /// loading every record. Corrupt files are ignored and can be rebuilt by exploring.
        /// </summary>
        private void ScanIndex()
        {
            try
            {
                foreach (string path in Directory.GetFiles(directory, "r.*.bin"))
                {
                    try
                    {
                        using (var stream = File.OpenRead(path))
                        using (var reader = new BinaryReader(stream))
                        {
                            string magic = new string(reader.ReadChars(4));
                            byte version = reader.ReadByte();
                            byte cellSize = reader.ReadByte();
                            int rx = reader.ReadInt32();
                            int rz = reader.ReadInt32();
                            int count = reader.ReadInt32();
                            if (magic != Magic || version != Version
                                || cellSize != WayfinderRegionMath.CellSize
                                || count < 0 || count > WayfinderRegionMath.CellsPerRegion)
                                continue;

                            long key = Key(rx, rz);
                            if (countedRegions.Add(key))
                                KnownCellCount += count;
                        }
                    }
                    catch
                    {
                        // A corrupt region is simply not counted; the map will try to rebuild it.
                    }
                }
            }
            catch { }
        }

        private static long Key(int rx, int rz) => ((long)rx << 32) ^ (uint)rz;
        private static int Rx(long key) => (int)(key >> 32);
        private static int Rz(long key) => unchecked((int)(uint)key);

        public static string RegionFileName(int rx, int rz) => $"r.{rx}.{rz}.bin";

        public bool TryGetSurface(int worldX, int worldZ, out WayfinderCell cell)
        {
            int cellX = WayfinderRegionMath.CellCoord(worldX);
            int cellZ = WayfinderRegionMath.CellCoord(worldZ);
            int rx = WayfinderRegionMath.FloorDiv(cellX, WayfinderRegionMath.RegionCells);
            int rz = WayfinderRegionMath.FloorDiv(cellZ, WayfinderRegionMath.RegionCells);
            Region region = GetRegion(rx, rz);
            int localX = cellX - rx * WayfinderRegionMath.RegionCells;
            int localZ = cellZ - rz * WayfinderRegionMath.RegionCells;
            ushort index = (ushort)(localZ * WayfinderRegionMath.RegionCells + localX);
            return region.Cells.TryGetValue(index, out cell);
        }

        /// <summary>
        /// Records a live surface sample. A canonical (multiple-of-4) sample may update an
        /// existing cell; non-canonical samples only seed empty cells so zoomed-out sampling
        /// still fills the map without causing dirty churn on every pass.
        /// </summary>
        public void RecordSurface(int worldX, int worldZ, in WayfinderCell cell)
        {
            int cellX = WayfinderRegionMath.CellCoord(worldX);
            int cellZ = WayfinderRegionMath.CellCoord(worldZ);
            int rx = WayfinderRegionMath.FloorDiv(cellX, WayfinderRegionMath.RegionCells);
            int rz = WayfinderRegionMath.FloorDiv(cellZ, WayfinderRegionMath.RegionCells);
            Region region = GetRegion(rx, rz);
            int localX = cellX - rx * WayfinderRegionMath.RegionCells;
            int localZ = cellZ - rz * WayfinderRegionMath.RegionCells;
            ushort index = (ushort)(localZ * WayfinderRegionMath.RegionCells + localX);

            bool canonical = (worldX & (WayfinderRegionMath.CellSize - 1)) == 0
                && (worldZ & (WayfinderRegionMath.CellSize - 1)) == 0;

            if (region.Cells.TryGetValue(index, out WayfinderCell existing))
            {
                if (!canonical || existing.SameSurface(cell)) return;
            }
            else
            {
                KnownCellCount++;
            }

            region.Cells[index] = cell;
            region.Dirty = true;
            long key = Key(rx, rz);
            dirty.Add(key);
            countedRegions.Add(key);
        }

        /// <summary>Writes at most <paramref name="maxRegions"/> dirty regions.</summary>
        public int Flush(int maxRegions = 2)
        {
            if (dirty.Count == 0 || maxRegions <= 0) return 0;

            flushScratch.Clear();
            foreach (long key in dirty)
            {
                flushScratch.Add(key);
                if (flushScratch.Count >= maxRegions) break;
            }

            int written = 0;
            for (int i = 0; i < flushScratch.Count; i++)
            {
                long key = flushScratch[i];
                if (!regions.TryGetValue(key, out Region region) || !region.Dirty)
                {
                    dirty.Remove(key);
                    continue;
                }

                if (SaveRegion(region))
                {
                    region.Dirty = false;
                    dirty.Remove(key);
                    written++;
                }
            }
            return written;
        }

        public int FlushAll()
        {
            int total = 0;
            int guard = 0;
            while (dirty.Count > 0 && guard++ < 1000)
            {
                int written = Flush(8);
                total += written;
                if (written == 0) break;
            }
            return total;
        }

        private Region GetRegion(int rx, int rz)
        {
            long key = Key(rx, rz);
            if (regions.TryGetValue(key, out Region existing))
            {
                existing.LastTouch = ++tick;
                return existing;
            }

            Region region = LoadRegion(rx, rz);
            region.LastTouch = ++tick;
            regions[key] = region;

            if (countedRegions.Add(key))
                KnownCellCount += region.Cells.Count;

            if (regions.Count > MaxCachedRegions)
                EvictOne();
            return region;
        }

        private void EvictOne()
        {
            long oldestKey = 0;
            Region? oldest = null;
            foreach (var pair in regions)
            {
                if (pair.Value.Dirty) continue;
                if (oldest == null || pair.Value.LastTouch < oldest.LastTouch)
                {
                    oldest = pair.Value;
                    oldestKey = pair.Key;
                }
            }

            if (oldest == null)
            {
                // All cached regions were touched this pass; flush one before dropping it.
                Flush(1);
                foreach (var pair in regions)
                {
                    if (pair.Value.Dirty) continue;
                    if (oldest == null || pair.Value.LastTouch < oldest.LastTouch)
                    {
                        oldest = pair.Value;
                        oldestKey = pair.Key;
                    }
                }
            }

            if (oldest != null) regions.Remove(oldestKey);
        }

        private Region LoadRegion(int rx, int rz)
        {
            var region = new Region { Rx = rx, Rz = rz };
            string path = Path.Combine(directory, RegionFileName(rx, rz));
            if (!File.Exists(path)) return region;

            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    string magic = new string(reader.ReadChars(4));
                    byte version = reader.ReadByte();
                    byte cellSize = reader.ReadByte();
                    int fileRx = reader.ReadInt32();
                    int fileRz = reader.ReadInt32();
                    int count = reader.ReadInt32();

                    if (magic != Magic || version != Version
                        || cellSize != WayfinderRegionMath.CellSize
                        || fileRx != rx || fileRz != rz
                        || count < 0 || count > WayfinderRegionMath.CellsPerRegion)
                    {
                        LogWarn($"Ignoring invalid map region {path}");
                        return region;
                    }

                    for (int i = 0; i < count; i++)
                    {
                        ushort index = reader.ReadUInt16();
                        if (index >= WayfinderRegionMath.CellsPerRegion) break;
                        region.Cells[index] = new WayfinderCell
                        {
                            BlockId = reader.ReadUInt16(),
                            TopY = reader.ReadInt16(),
                            LiquidId = reader.ReadUInt16(),
                            BedBlockId = reader.ReadUInt16(),
                            WaterDepth = reader.ReadByte(),
                            PaintHue = reader.ReadUInt16(),
                            Flags = reader.ReadByte(),
                            BiomeTint = reader.ReadUInt32()
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                LogWarn($"Could not read map region {path}: {ex.Message}");
                region.Cells.Clear();
            }
            return region;
        }

        private bool SaveRegion(Region region)
        {
            string path = Path.Combine(directory, RegionFileName(region.Rx, region.Rz));
            string temp = path + ".tmp";
            try
            {
                using (var stream = File.Create(temp))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Magic.ToCharArray());
                    writer.Write(Version);
                    writer.Write((byte)WayfinderRegionMath.CellSize);
                    writer.Write(region.Rx);
                    writer.Write(region.Rz);
                    writer.Write(region.Cells.Count);
                    foreach (var pair in region.Cells)
                    {
                        writer.Write(pair.Key);
                        WayfinderCell cell = pair.Value;
                        writer.Write(cell.BlockId);
                        writer.Write(cell.TopY);
                        writer.Write(cell.LiquidId);
                        writer.Write(cell.BedBlockId);
                        writer.Write(cell.WaterDepth);
                        writer.Write(cell.PaintHue);
                        writer.Write(cell.Flags);
                        writer.Write(cell.BiomeTint);
                    }
                }

                if (File.Exists(path))
                {
                    try { File.Replace(temp, path, null); }
                    catch
                    {
                        File.Delete(path);
                        File.Move(temp, path);
                    }
                }
                else
                {
                    File.Move(temp, path);
                }
                return true;
            }
            catch (Exception ex)
            {
                LogWarn($"Could not write map region {path}: {ex.Message}");
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return false;
            }
        }

        private void LogWarn(string message)
        {
            try { log?.Invoke(message); } catch { }
        }
    }
}
