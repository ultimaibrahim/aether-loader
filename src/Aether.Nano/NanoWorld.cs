using System.Reflection;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using UnityEngine;

namespace Aether.Nano
{
    /// <summary>
    /// Safe reads of the voxel world. <see cref="PriorityWorld"/>'s indexer generates or loads a missing chunk on the spot,
    /// which costs frames and starves the terrain loader; these read the chunks already in memory and return air otherwise.
    /// </summary>
    public static class NanoWorld
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo PwChunks = typeof(PriorityWorld).GetField("chunks", Any);
        private static readonly FieldInfo PwC0x = typeof(PriorityWorld).GetField("c0x", Any);
        private static readonly FieldInfo PwC0y = typeof(PriorityWorld).GetField("c0y", Any);
        private static readonly FieldInfo PwC0z = typeof(PriorityWorld).GetField("c0z", Any);
        private static readonly FieldInfo ChunkBlocks = typeof(Chunk).GetField("blocks", Any);

        /// <summary>The world being played, or null in menus.</summary>
        public static IWorld Active()
        {
            TerrainLoader loader = TerrainLoader.instance;
            if (loader == null) return null;
            return loader.world ?? loader.getWorld();
        }

        public static Block GetBlockSafe(IWorld world, int wx, int y, int wz)
        {
            if (world is PriorityWorld pw && PwChunks != null)
            {
                try
                {
                    if (PwChunks.GetValue(pw) is Chunk[,,] chunks)
                        return GetBlockFast(chunks, (int)PwC0x.GetValue(pw), (int)PwC0y.GetValue(pw), (int)PwC0z.GetValue(pw), wx, y, wz);
                }
                catch { }
            }
            return default;
        }

        public static Block GetBlockFast(Chunk[,,] chunks, int c0x, int c0y, int c0z, int wx, int y, int wz)
        {
            if (chunks == null) return default;
            int cx = (wx >> 5) - c0x, cy = (y >> 5) - c0y, cz = (wz >> 5) - c0z;
            if (cx < 0 || cy < 0 || cz < 0 || cx >= chunks.GetLength(0) || cy >= chunks.GetLength(1) || cz >= chunks.GetLength(2))
                return default;
            Chunk c = chunks[cx, cy, cz];
            if (c == null || c.empty || ChunkBlocks == null) return default;
            return ChunkBlocks.GetValue(c) is Block[,,] blocks ? blocks[wx & 0x1F, y & 0x1F, wz & 0x1F] : default;
        }

        /// <summary>A block that hides the sky: not air, liquid, invisible, glass, torches, flowers, leaves or coral.</summary>
        public static bool IsSolidCeiling(ushort id)
        {
            if (id <= 2 || id == 68) return false;
            switch (id)
            {
                case 4: case 5: case 6:                     // flowers
                case 15: case 16: case 23: case 43: case 45: case 46: case 47: case 53:
                case 164: case 245: case 246: case 249:     // leaves
                case 27:                                    // cactus
                case 29:                                    // torch
                case 31:                                    // glass
                case 40: case 41: case 42:                  // coral, sea grass
                case 56: case 163:                          // swamp grass, moss
                    return false;
            }
            BlockType bt = id < BlockTypes.blocks.Length ? BlockTypes.blocks[id] : null;
            return bt != null && !bt.air && !bt.liquid && bt.visible;
        }

        /// <summary>Largest trigger (half-diagonal of its bounds, in blocks) still counted as a body hitbox.</summary>
        public const float MaxTriggerExtent = 4.5f;

        /// <summary>
        /// Whether a raycast hit counts as "pointing at a creature": mod mobs often use trigger colliders as their body, but the
        /// big trigger volumes mobs also carry (aggro ranges) must not make them visible from far away.
        /// </summary>
        public static bool AcceptsEntityHit(Collider collider) =>
            collider != null && (!collider.isTrigger || collider.bounds.extents.magnitude is float m && m > 0f && m <= MaxTriggerExtent);

        /// <summary>Underground = at least two of five columns around <paramref name="pos"/> have a solid ceiling within 45 blocks.</summary>
        public static bool IsUnderground(IWorld world, Vector3 pos)
        {
            if (world == null) return false;
            int px = Mathf.FloorToInt(pos.x), py = Mathf.FloorToInt(pos.y), pz = Mathf.FloorToInt(pos.z);
            int[,] offsets = { { 0, 0 }, { 2, 0 }, { -2, 0 }, { 0, 2 }, { 0, -2 } };
            int covered = 0;
            for (int r = 0; r < 5; r++)
            {
                int ox = px + offsets[r, 0], oz = pz + offsets[r, 1];
                for (int dy = 1; dy <= 45; dy++)
                {
                    if (!IsSolidCeiling(GetBlockSafe(world, ox, py + dy, oz).id)) continue;
                    if (++covered >= 2) return true;
                    break;
                }
            }
            return false;
        }
    }
}
