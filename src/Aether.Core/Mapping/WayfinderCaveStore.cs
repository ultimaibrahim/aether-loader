using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Aether.Core.Mapping
{
    /// <summary>
    /// Flags for a persisted cave/slice sample. Values intentionally mirror the surface
    /// <see cref="WayfinderCellFlags"/> where the meaning is shared, and add cave-only state.
    /// </summary>
    [Flags]
    public enum WayfinderCaveCellFlags : byte
    {
        None = 0,
        Liquid = 1 << 0,
        Painted = 1 << 1,

        /// <summary>The sampled column is solid at the slice floor (a cave wall, not a floor).</summary>
        CaveWall = 1 << 2,

        Ore = 1 << 3,

        /// <summary>Enclosed/open room pixel used by the atlas' interior-cutaway rule.</summary>
        OpenRoom = 1 << 4
    }

    /// <summary>
    /// One persisted cave/slice sample for a 4x4-block cell inside a 16-block Y band.
    /// <para>
    /// <see cref="BlockId"/> is the representative block at the band floor (or the visible floor
    /// block when the sampled column is open). <see cref="TopY"/> is the highest solid world Y
    /// found in the band (absolute, not band-relative), which lets the atlas shade depth when
    /// live world data is unavailable. A canonical sample is taken at the cell origin
    /// (world X/Z multiples of 4) and at the band floor, <c>band * WayfinderChunks.BandSize</c>.
    /// </para>
    /// Deliberately Unity-free so serialization can be unit-tested.
    /// </summary>
    public struct WayfinderCaveCell
    {
        public ushort BlockId;
        public short TopY;
        public ushort LiquidId;
        public byte LiquidDepth;
        public ushort PaintHue;
        public byte Flags;

        /// <summary>Optional packed block/biome tint; 0 means "no tint".</summary>
        public uint Tint;

        public bool IsLiquid => (Flags & (byte)WayfinderCaveCellFlags.Liquid) != 0;
        public bool IsPainted => (Flags & (byte)WayfinderCaveCellFlags.Painted) != 0;
        public bool IsCaveWall => (Flags & (byte)WayfinderCaveCellFlags.CaveWall) != 0;
        public bool IsOre => (Flags & (byte)WayfinderCaveCellFlags.Ore) != 0;
        public bool IsOpenRoom => (Flags & (byte)WayfinderCaveCellFlags.OpenRoom) != 0;

        public bool SameCave(in WayfinderCaveCell other)
        {
            return BlockId == other.BlockId
                && TopY == other.TopY
                && LiquidId == other.LiquidId
                && LiquidDepth == other.LiquidDepth
                && PaintHue == other.PaintHue
                && Flags == other.Flags
                && Tint == other.Tint;
        }
    }

    /// <summary>
    /// Per-world, per-band cave/slice store. Files live under
    /// <c>&lt;map directory&gt;/cave/b&lt;band&gt;/c.&lt;rx&gt;.&lt;rz&gt;.bin</c>, where the map directory
    /// is the same per-world <c>aether_wayfinder</c> folder used by <see cref="WayfinderStore"/>.
    /// <para>
    /// Each region covers 256x256 blocks (64x64 4-block cells) at one 16-block Y band
    /// (<c>band = floor(worldY / WayfinderChunks.BandSize)</c>, clamped to
    /// <see cref="WayfinderChunks.MinBand"/>..<see cref="WayfinderChunks.MaxBand"/>). Dirty
    /// regions are written atomically and on a small budget so cave exploration never causes a
    /// save hitch. Like the surface store this class is single-threaded and intended for the
    /// Unity main thread.
    /// </para>
    /// <para>
    /// Format v1: 4-byte magic <c>AERC</c>, version, cell size, record size, signed band, rx, rz,
    /// count; a 512-byte presence bitset (LSB-first, <c>index = localZ * 64 + localX</c>); then
    /// <c>count</c> records in ascending index order. A record is 14 bytes: block id (u16),
    /// absolute top Y (i16), liquid id (u16), liquid depth (u8), paint hue (u16), flags (u8) and
    /// tint (u32).
    /// </para>
    /// </summary>
    public sealed class WayfinderCaveStore
    {
        public const string FileMagic = "AERC";
        public const byte FormatVersion = 1;
        public const byte CurrentRecordSize = 14;

        /// <summary>Bytes from the magic through the cell count (before the presence bitset).</summary>
        public const int HeaderBytes = 4 + 1 + 1 + 1 + 4 + 4 + 4 + 4;

        /// <summary>One bit per 4x4 cell: 4096 cells = 512 bytes.</summary>
        public const int PresenceBytes = (WayfinderRegionMath.CellsPerRegion + 7) / 8;

        public const int MaxCachedRegions = 32;
        public const string CaveDirectoryName = "cave";

        /// <summary>
        /// Cave regions use their own <c>c.</c> prefix. The surface store scans <c>r.*.bin</c>
        /// at the map root, so even a future recursive scan cannot confuse the two formats.
        /// </summary>
        public const string RegionFilePrefix = "c";

        private const byte MinRecordSize = 2;
        private const byte MaxRecordSize = 64;

        private sealed class Region
        {
            public int Band;
            public int Rx;
            public int Rz;
            public readonly Dictionary<ushort, WayfinderCaveCell> Cells = new Dictionary<ushort, WayfinderCaveCell>();
            public bool Dirty;
            public long LastTouch;
        }

        private readonly struct RegionKey : IEquatable<RegionKey>
        {
            public readonly int Band;
            public readonly int Rx;
            public readonly int Rz;

            public RegionKey(int band, int rx, int rz)
            {
                Band = band;
                Rx = rx;
                Rz = rz;
            }

            public bool Equals(RegionKey other)
            {
                return Band == other.Band && Rx == other.Rx && Rz == other.Rz;
            }

            public override bool Equals(object? obj)
            {
                return obj is RegionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Band;
                    hash = hash * 397 ^ Rx;
                    hash = hash * 397 ^ Rz;
                    return hash;
                }
            }
        }

        private readonly string directory;
        private readonly Action<string>? log;
        private readonly Dictionary<RegionKey, Region> regions = new Dictionary<RegionKey, Region>();
        private readonly HashSet<RegionKey> dirty = new HashSet<RegionKey>();
        private readonly HashSet<RegionKey> countedRegions = new HashSet<RegionKey>();
        private readonly List<RegionKey> flushScratch = new List<RegionKey>();
        private long tick;

        public long KnownCellCount { get; private set; }
        public int CachedRegionCount => regions.Count;
        public int DirtyRegionCount => dirty.Count;
        public int KnownRegionCount => countedRegions.Count;
        public int MaxCached => MaxCachedRegions;
        public string Directory => directory;
        public string CaveDirectory => Path.Combine(directory, CaveDirectoryName);

        public WayfinderCaveStore(string directory, Action<string>? log = null)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("directory is required", nameof(directory));
            this.directory = directory;
            this.log = log;
            System.IO.Directory.CreateDirectory(CaveDirectory);
            ScanIndex();
        }

        public static string BandDirectoryName(int band)
        {
            band = WayfinderChunks.ClampBand(band);
            return "b" + band.ToString(CultureInfo.InvariantCulture);
        }

        public static string RegionFileName(int rx, int rz)
        {
            return RegionFilePrefix + "." + rx.ToString(CultureInfo.InvariantCulture)
                + "." + rz.ToString(CultureInfo.InvariantCulture) + ".bin";
        }

        /// <summary>Absolute world Y of the floor of a 16-block cave band.</summary>
        public static int BandFloorY(int band)
        {
            return WayfinderChunks.ClampBand(band) * WayfinderChunks.BandSize;
        }

        /// <summary>Band index for a world Y, following <see cref="WayfinderChunks.BandCoord"/>.</summary>
        public static int BandForY(int worldY)
        {
            return WayfinderChunks.BandCoord(worldY);
        }

        /// <summary>Path of the region file for a band/rx/rz triple (also used by tests).</summary>
        public string RegionPath(int band, int rx, int rz)
        {
            band = WayfinderChunks.ClampBand(band);
            return Path.Combine(CaveDirectory, BandDirectoryName(band), RegionFileName(rx, rz));
        }

        /// <summary>
        /// Reads region headers and the small presence map so the UI can report saved cave
        /// regions/cells without loading every record. Corrupt/unknown files are skipped and
        /// can be rebuilt by play.
        /// </summary>
        private void ScanIndex()
        {
            try
            {
                foreach (string bandDirectory in System.IO.Directory.GetDirectories(CaveDirectory, "b*"))
                {
                    if (!TryParseBandDirectory(Path.GetFileName(bandDirectory), out int band))
                        continue;

                    foreach (string path in System.IO.Directory.GetFiles(bandDirectory, RegionFilePrefix + ".*.bin"))
                    {
                        // Windows' legacy wildcard rules can match "c.0.0.bin.tmp" for "*.bin";
                        // only real region files may contribute to the startup counts.
                        if (!path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!TryParseRegionFileName(path, out int fileRx, out int fileRz))
                        {
                            LogWarn($"Ignoring cave file with an invalid name {path}");
                            continue;
                        }

                        try
                        {
                            if (!TryReadHeader(path, out RegionHeader header))
                            {
                                LogWarn($"Ignoring invalid cave region {path}");
                                continue;
                            }
                            if (header.Band != band || header.Rx != fileRx || header.Rz != fileRz)
                            {
                                LogWarn($"Ignoring cave region whose header does not match its path {path}");
                                continue;
                            }
                            if (header.Count <= 0)
                                continue;

                            var key = new RegionKey(header.Band, header.Rx, header.Rz);
                            if (countedRegions.Add(key))
                                KnownCellCount += header.Count;
                        }
                        catch
                        {
                            // A corrupt region is simply not counted; play can rebuild it.
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Reads a saved cave/slice sample. <paramref name="band"/> is clamped to
        /// <see cref="WayfinderChunks.MinBand"/>..<see cref="WayfinderChunks.MaxBand"/>; use
        /// <see cref="WayfinderChunks.BandCoord"/> to derive it from a world Y.
        /// </summary>
        public bool TryGetCave(int worldX, int worldZ, int band, out WayfinderCaveCell cell)
        {
            band = WayfinderChunks.ClampBand(band);
            int cellX = WayfinderRegionMath.CellCoord(worldX);
            int cellZ = WayfinderRegionMath.CellCoord(worldZ);
            int rx = WayfinderRegionMath.FloorDiv(cellX, WayfinderRegionMath.RegionCells);
            int rz = WayfinderRegionMath.FloorDiv(cellZ, WayfinderRegionMath.RegionCells);
            Region region = GetRegion(band, rx, rz);
            int localX = cellX - rx * WayfinderRegionMath.RegionCells;
            int localZ = cellZ - rz * WayfinderRegionMath.RegionCells;
            ushort index = (ushort)(localZ * WayfinderRegionMath.RegionCells + localX);
            return region.Cells.TryGetValue(index, out cell);
        }

        /// <summary>
        /// True when a 32x32-block chunk has any saved cave cell for the band. Used to seed the
        /// in-memory cave fog after a reload; the cave store records whole rendered chunks densely,
        /// so a saved cell is evidence that the chunk was visited.
        /// </summary>
        public bool HasAnyCaveCell(int chunkX, int chunkZ, int band)
        {
            band = WayfinderChunks.ClampBand(band);
            int wx = chunkX * WayfinderChunks.Size;
            int wz = chunkZ * WayfinderChunks.Size;
            int cellX = WayfinderRegionMath.CellCoord(wx);
            int cellZ = WayfinderRegionMath.CellCoord(wz);
            int rx = WayfinderRegionMath.FloorDiv(cellX, WayfinderRegionMath.RegionCells);
            int rz = WayfinderRegionMath.FloorDiv(cellZ, WayfinderRegionMath.RegionCells);

            // A 32-block chunk always fits inside one 256-block region. If the region was never
            // recorded, no probe can succeed: return without touching the region cache/disk.
            if (!countedRegions.Contains(new RegionKey(band, rx, rz))) return false;

            for (int z = 0; z < WayfinderChunks.Size / 4; z++)
            {
                for (int x = 0; x < WayfinderChunks.Size / 4; x++)
                {
                    if (TryGetCave(wx + x * 4, wz + z * 4, band, out _)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Records a live cave/slice sample. A canonical (multiple-of-4) sample may update an
        /// existing cell; non-canonical samples only seed empty cells. This mirrors
        /// <see cref="WayfinderStore.RecordSurface"/> so progressive/zoomed sampling cannot
        /// dirty a region on every pass. <paramref name="band"/> is clamped like
        /// <see cref="TryGetCave"/>.
        /// </summary>
        public void RecordCave(int worldX, int worldZ, int band, in WayfinderCaveCell cell)
        {
            band = WayfinderChunks.ClampBand(band);
            int cellX = WayfinderRegionMath.CellCoord(worldX);
            int cellZ = WayfinderRegionMath.CellCoord(worldZ);
            int rx = WayfinderRegionMath.FloorDiv(cellX, WayfinderRegionMath.RegionCells);
            int rz = WayfinderRegionMath.FloorDiv(cellZ, WayfinderRegionMath.RegionCells);
            Region region = GetRegion(band, rx, rz);
            int localX = cellX - rx * WayfinderRegionMath.RegionCells;
            int localZ = cellZ - rz * WayfinderRegionMath.RegionCells;
            ushort index = (ushort)(localZ * WayfinderRegionMath.RegionCells + localX);

            bool canonical = (worldX & (WayfinderRegionMath.CellSize - 1)) == 0
                && (worldZ & (WayfinderRegionMath.CellSize - 1)) == 0;

            if (region.Cells.TryGetValue(index, out WayfinderCaveCell existing))
            {
                if (!canonical || existing.SameCave(cell)) return;
            }
            else
            {
                KnownCellCount++;
            }

            region.Cells[index] = cell;
            region.Dirty = true;
            var key = new RegionKey(band, rx, rz);
            dirty.Add(key);
            countedRegions.Add(key);
        }

        /// <summary>Writes at most <paramref name="maxRegions"/> dirty regions.</summary>
        public int Flush(int maxRegions = 2)
        {
            if (dirty.Count == 0 || maxRegions <= 0) return 0;

            flushScratch.Clear();
            foreach (RegionKey key in dirty)
            {
                flushScratch.Add(key);
                if (flushScratch.Count >= maxRegions) break;
            }

            int written = 0;
            for (int i = 0; i < flushScratch.Count; i++)
            {
                RegionKey key = flushScratch[i];
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

        private Region GetRegion(int band, int rx, int rz)
        {
            var key = new RegionKey(band, rx, rz);
            if (regions.TryGetValue(key, out Region existing))
            {
                existing.LastTouch = ++tick;
                return existing;
            }

            Region region = LoadRegion(band, rx, rz);
            region.LastTouch = ++tick;

            // A missing/empty region is not "known" until it has a saved cell.
            if (region.Cells.Count > 0 && countedRegions.Add(key))
                KnownCellCount += region.Cells.Count;

            // Evict before inserting the new region. Otherwise a fresh (not-yet-dirty) region
            // could be chosen for eviction and the caller would write into a detached object.
            if (regions.Count >= MaxCachedRegions)
                EvictOne();
            regions[key] = region;
            return region;
        }

        private void EvictOne()
        {
            RegionKey oldestKey = default;
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

        private Region LoadRegion(int band, int rx, int rz)
        {
            var region = new Region { Band = band, Rx = rx, Rz = rz };
            string path = RegionPath(band, rx, rz);
            if (!File.Exists(path)) return region;

            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    if (!TryReadHeader(reader, out RegionHeader header))
                    {
                        LogWarn($"Ignoring invalid cave region {path}");
                        return region;
                    }

                    if (header.Band != band || header.Rx != rx || header.Rz != rz)
                    {
                        LogWarn($"Ignoring cave region with mismatched header {path}");
                        return region;
                    }

                    long expected = HeaderBytes + PresenceBytes + (long)header.Count * header.RecordSize;
                    if (header.Count < 0 || header.Count > WayfinderRegionMath.CellsPerRegion
                        || stream.Length != expected)
                    {
                        LogWarn($"Ignoring cave region with an invalid length {path}");
                        return region;
                    }

                    byte[] presence = reader.ReadBytes(PresenceBytes);
                    if (presence.Length != PresenceBytes || PopCount(presence) != header.Count)
                    {
                        LogWarn($"Ignoring cave region with invalid presence map {path}");
                        return region;
                    }

                    byte[] record = new byte[header.RecordSize];
                    for (int index = 0; index < WayfinderRegionMath.CellsPerRegion; index++)
                    {
                        if (!HasBit(presence, index)) continue;

                        int read = 0;
                        while (read < record.Length)
                        {
                            int chunk = reader.Read(record, read, record.Length - read);
                            if (chunk <= 0) throw new EndOfStreamException();
                            read += chunk;
                        }

                        region.Cells[(ushort)index] = ReadRecord(record);
                    }
                }
            }
            catch (Exception ex)
            {
                LogWarn($"Could not read cave region {path}: {ex.Message}");
                region.Cells.Clear();
            }
            return region;
        }

        private bool SaveRegion(Region region)
        {
            string path = RegionPath(region.Band, region.Rx, region.Rz);
            string backup = path + ".bak";
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string bandDirectory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(bandDirectory))
                    System.IO.Directory.CreateDirectory(bandDirectory);

                byte[] presence = new byte[PresenceBytes];
                foreach (var pair in region.Cells)
                    SetBit(presence, pair.Key);

                using (var stream = File.Create(temp))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(FileMagic.ToCharArray());
                    writer.Write(FormatVersion);
                    writer.Write((byte)WayfinderRegionMath.CellSize);
                    writer.Write(CurrentRecordSize);
                    writer.Write(region.Band);
                    writer.Write(region.Rx);
                    writer.Write(region.Rz);
                    writer.Write(region.Cells.Count);

                    writer.Write(presence);

                    // Ascending cell order keeps the file deterministic and matches the bitset.
                    for (int index = 0; index < WayfinderRegionMath.CellsPerRegion; index++)
                    {
                        if (!HasBit(presence, index)) continue;
                        if (region.Cells.TryGetValue((ushort)index, out WayfinderCaveCell cell))
                            WriteRecord(writer, cell);
                    }

                    writer.Flush();
                    stream.Flush(true);
                }

                if (!File.Exists(path))
                {
                    File.Move(temp, path);
                    return true;
                }

                TryDelete(backup);
                try
                {
                    File.Replace(temp, path, backup);
                    TryDelete(backup);
                }
                catch
                {
                    // Portable fallback: keep the previous file as a backup until the new
                    // file is in place. Never delete the only copy before the move.
                    TryDelete(backup);
                    File.Move(path, backup);
                    try
                    {
                        File.Move(temp, path);
                        TryDelete(backup);
                    }
                    catch
                    {
                        try
                        {
                            if (!File.Exists(path) && File.Exists(backup))
                                File.Move(backup, path);
                        }
                        catch { }
                        throw;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogWarn($"Could not write cave region {path}: {ex.Message}");
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private readonly struct RegionHeader
        {
            public readonly int Band;
            public readonly int Rx;
            public readonly int Rz;
            public readonly int Count;
            public readonly byte RecordSize;

            public RegionHeader(int band, int rx, int rz, int count, byte recordSize)
            {
                Band = band;
                Rx = rx;
                Rz = rz;
                Count = count;
                RecordSize = recordSize;
            }
        }

        private static bool TryReadHeader(string path, out RegionHeader header)
        {
            header = default;
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream))
                {
                    if (!TryReadHeader(reader, out header))
                        return false;

                    long expected = HeaderBytes + PresenceBytes + (long)header.Count * header.RecordSize;
                    if (stream.Length != expected)
                        return false;

                    // The presence map is small (512 bytes) and lets the index reject corrupt
                    // files without pulling every record into memory.
                    stream.Position = HeaderBytes;
                    byte[] presence = new byte[PresenceBytes];
                    int read = 0;
                    while (read < presence.Length)
                    {
                        int chunk = stream.Read(presence, read, presence.Length - read);
                        if (chunk <= 0) return false;
                        read += chunk;
                    }
                    return PopCount(presence) == header.Count;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadHeader(BinaryReader reader, out RegionHeader header)
        {
            header = default;
            if (reader.BaseStream.Length - reader.BaseStream.Position < HeaderBytes)
                return false;

            string magic;
            try { magic = new string(reader.ReadChars(4)); }
            catch { return false; }

            byte version = reader.ReadByte();
            byte cellSize = reader.ReadByte();
            byte recordSize = reader.ReadByte();
            int band = reader.ReadInt32();
            int rx = reader.ReadInt32();
            int rz = reader.ReadInt32();
            int count = reader.ReadInt32();

            if (magic != FileMagic
                || version != FormatVersion
                || cellSize != WayfinderRegionMath.CellSize
                || recordSize < MinRecordSize
                || recordSize > MaxRecordSize
                || count < 0
                || count > WayfinderRegionMath.CellsPerRegion)
            {
                return false;
            }

            header = new RegionHeader(band, rx, rz, count, recordSize);
            return true;
        }

        private static WayfinderCaveCell ReadRecord(byte[] record)
        {
            var cell = new WayfinderCaveCell();
            if (record.Length >= 2) cell.BlockId = ReadUInt16(record, 0);
            if (record.Length >= 4) cell.TopY = ReadInt16(record, 2);
            if (record.Length >= 6) cell.LiquidId = ReadUInt16(record, 4);
            if (record.Length >= 7) cell.LiquidDepth = record[6];
            if (record.Length >= 9) cell.PaintHue = ReadUInt16(record, 7);
            if (record.Length >= 10) cell.Flags = record[9];
            if (record.Length >= 14) cell.Tint = ReadUInt32(record, 10);
            return cell;
        }

        private static void WriteRecord(BinaryWriter writer, in WayfinderCaveCell cell)
        {
            writer.Write(cell.BlockId);
            writer.Write(cell.TopY);
            writer.Write(cell.LiquidId);
            writer.Write(cell.LiquidDepth);
            writer.Write(cell.PaintHue);
            writer.Write(cell.Flags);
            writer.Write(cell.Tint);
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        private static short ReadInt16(byte[] buffer, int offset)
        {
            return unchecked((short)ReadUInt16(buffer, offset));
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24));
        }

        private static bool TryParseRegionFileName(string path, out int rx, out int rz)
        {
            rx = 0;
            rz = 0;
            string[] parts = Path.GetFileName(path).Split('.');
            if (parts.Length != 4
                || parts[0] != RegionFilePrefix
                || !string.Equals(parts[3], "bin", StringComparison.OrdinalIgnoreCase))
                return false;

            return int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out rx)
                && int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out rz);
        }

        private static bool TryParseBandDirectory(string name, out int band)
        {
            band = 0;
            if (string.IsNullOrEmpty(name) || name[0] != 'b' || name.Length < 2 || name.Length > 6)
                return false;
            if (!int.TryParse(name.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out band))
                return false;
            return band >= WayfinderChunks.MinBand && band <= WayfinderChunks.MaxBand;
        }

        private static int PopCount(byte[] bits)
        {
            int count = 0;
            for (int i = 0; i < bits.Length; i++)
            {
                int value = bits[i];
                while (value != 0)
                {
                    value &= value - 1;
                    count++;
                }
            }
            return count;
        }

        private static bool HasBit(byte[] bits, int index)
        {
            return (bits[index >> 3] & (1 << (index & 7))) != 0;
        }

        private static void SetBit(byte[] bits, int index)
        {
            bits[index >> 3] |= (byte)(1 << (index & 7));
        }

        private void LogWarn(string message)
        {
            try { log?.Invoke(message); } catch { }
        }
    }
}
