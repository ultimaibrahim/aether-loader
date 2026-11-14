using System;

namespace Aether.Core.Mapping
{
    /// <summary>
    /// Pure math for chunk-keyed map textures (the Wayfinder tile model).
    /// Deliberately Unity-free so it can be gated by the test project.
    /// </summary>
    public static class WayfinderChunks
    {
        /// <summary>Blocks per map chunk texture.</summary>
        public const int Size = 32;

        /// <summary>
        /// Vertical quantization for cave/slice chunks. The live world slice depends on the
        /// player's Y, so chunk textures are keyed per band instead of per block to stay reusable.
        /// </summary>
        public const int BandSize = 16;

        public const int MinBand = -2048;
        public const int MaxBand = 2047;

        public static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            if ((value % divisor != 0) && ((value < 0) != (divisor < 0)))
                quotient--;
            return quotient;
        }

        public static int ChunkCoord(int world) => FloorDiv(world, Size);

        public static int BandCoord(int y)
        {
            int band = FloorDiv(y, BandSize);
            return ClampBand(band);
        }

        public static int ClampBand(int band)
        {
            if (band < MinBand) return MinBand;
            if (band > MaxBand) return MaxBand;
            return band;
        }

        /// <summary>
        /// Packs (chunkX, chunkZ, mode, band) into one stable 64-bit key.
        /// mode: 8 bits, band: 12 bits signed, chunkX: 23 bits signed, chunkZ: 21 bits signed.
        /// </summary>
        public static long Key(int chunkX, int chunkZ, byte mode, int band)
        {
            band = band < MinBand ? MinBand : (band > MaxBand ? MaxBand : band);
            return ((long)(mode & 0xFF) << 56)
                 | ((long)(band & 0xFFF) << 44)
                 | ((long)(chunkX & 0x7FFFFF) << 21)
                 | (long)(chunkZ & 0x1FFFFF);
        }
    }
}
