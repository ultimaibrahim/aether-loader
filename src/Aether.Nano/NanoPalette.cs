using System.Collections.Generic;
using UnityEngine;

namespace Aether.Nano
{
/// <summary>Map colors of the game's blocks and which ones give light (shared by Lens, Wayfinder and Aether's HUD).</summary>
public static class NanoPalette
{
    public static bool IsLightSource(ushort id)
    {
        return id == 29 || id == 27 || id == 22 || (id >= 17 && id <= 20) || id == 66 || id == 67 || id == 108;
    }

    public static Color GetLightColor(ushort id)
    {
        if (id == 22) return new Color(1.0f, 0.45f, 0.15f); // Lava
        if (id == 17 || id == 66) return new Color(0.35f, 1.0f, 0.45f); // Fluorite / Cryptonite
        if (id == 18 || id == 20) return new Color(0.35f, 0.75f, 1.0f); // Blue crystal
        if (id == 19) return new Color(1.0f, 0.35f, 0.45f); // Red crystal
        if (id == 108) return new Color(0.40f, 1.0f, 0.20f); // Acid
        return new Color(1.0f, 0.88f, 0.55f); // Torch warm glow
    }

    private static readonly Color32[] Colors = new Color32[345];

    static NanoPalette()
    {
        Colors[0] = new Color32(0, 0, 0, 0);
        Colors[1] = new Color32(0, 0, 0, 0);
        Colors[2] = new Color32(0, 0, 0, 0);
        Colors[3] = new Color32(86, 158, 48, 255);    // Sod / Grass
        Colors[4] = new Color32(65, 120, 225, 255);   // Blue Flower
        Colors[5] = new Color32(235, 210, 45, 255);   // Yellow Flower
        Colors[6] = new Color32(220, 50, 45, 255);    // Red Flower
        Colors[7] = new Color32(124, 84, 50, 255);    // Dirt
        Colors[8] = new Color32(128, 128, 132, 255);  // Stone / Rock
        Colors[9] = new Color32(218, 202, 145, 255);  // Sand
        Colors[10] = new Color32(45, 105, 210, 255);  // Water
        Colors[11] = new Color32(245, 250, 255, 255); // Snow
        Colors[12] = new Color32(245, 205, 55, 255);  // Gold Ore
        Colors[13] = new Color32(105, 75, 45, 255);   // Bark
        Colors[14] = new Color32(125, 95, 60, 255);   // Palm Bark
        Colors[15] = new Color32(60, 140, 35, 255);   // Leaves
        Colors[16] = new Color32(75, 160, 40, 255);   // Palm Leaves
        Colors[17] = new Color32(50, 235, 105, 255);  // Green Fluorite
        Colors[18] = new Color32(50, 165, 255, 255);  // Blue Crystals
        Colors[19] = new Color32(245, 55, 70, 255);   // Red Crystals
        Colors[20] = new Color32(35, 125, 230, 255);  // Crystal Block Blue
        Colors[21] = new Color32(40, 40, 44, 255);    // Coal Ore
        Colors[22] = new Color32(255, 110, 10, 255);  // Lava
        Colors[23] = new Color32(185, 40, 30, 255);   // Red Leaves
        Colors[24] = new Color32(65, 25, 28, 255);    // Devil Rock
        Colors[25] = new Color32(180, 140, 90, 255);  // Plank
        Colors[26] = new Color32(170, 85, 65, 255);   // Bricks
        Colors[27] = new Color32(70, 130, 45, 255);   // Cactus
        Colors[28] = new Color32(200, 45, 35, 255);   // TNT
        Colors[29] = new Color32(255, 210, 60, 255);  // Torch
        Colors[30] = new Color32(225, 215, 65, 255);  // Sulphur Ore
        Colors[31] = new Color32(200, 225, 245, 255); // Glass
        Colors[40] = new Color32(225, 195, 55, 255);  // Yellow Coral
        Colors[41] = new Color32(55, 175, 115, 255);  // Green Coral
        Colors[42] = new Color32(40, 120, 70, 255);   // Sea Grass
        Colors[43] = new Color32(90, 170, 50, 255);   // Birch Leaves
        Colors[44] = new Color32(220, 220, 215, 255); // Birch Bark
        Colors[45] = new Color32(70, 145, 40, 255);   // Baobab Leaves
        Colors[46] = new Color32(40, 105, 55, 255);   // Pine Leaves
        Colors[47] = new Color32(55, 135, 30, 255);   // Oak Leaves
        Colors[48] = new Color32(60, 85, 45, 255);    // Swamp
        Colors[51] = new Color32(160, 215, 245, 255); // Ice
        Colors[53] = new Color32(35, 125, 35, 255);   // Jungle Leaves
        Colors[56] = new Color32(75, 110, 55, 255);   // Swamp Grass
        Colors[57] = new Color32(205, 150, 120, 255); // Iron Ore
        Colors[59] = new Color32(170, 172, 175, 255); // Clear Stone
        Colors[60] = new Color32(90, 105, 85, 255);   // Swamp Stone
        Colors[63] = new Color32(195, 175, 125, 255); // Sand Stone
        Colors[64] = new Color32(95, 165, 190, 255);  // Unobtainium
        Colors[66] = new Color32(65, 245, 65, 255);   // Cryptonite
        Colors[67] = new Color32(185, 245, 45, 255);  // Klorium
        Colors[77] = new Color32(85, 95, 110, 255);   // Forgotten Stone
        Colors[82] = new Color32(205, 175, 125, 255); // Clear Plank
        Colors[84] = new Color32(225, 215, 175, 255); // Birch Plank
        Colors[85] = new Color32(195, 150, 95, 255);  // Palm Plank
        Colors[108] = new Color32(75, 255, 35, 255);  // Acid
        Colors[116] = new Color32(200, 200, 205, 255);// Iron Block
        Colors[117] = new Color32(245, 210, 45, 255); // Gold Block
        Colors[138] = new Color32(235, 235, 240, 255);// Marble Block
        Colors[142] = new Color32(155, 120, 115, 255);// Granite
        Colors[143] = new Color32(115, 45, 50, 255);  // Devil Bricks
        Colors[145] = new Color32(175, 225, 250, 255);// Ice Bricks
        Colors[153] = new Color32(200, 205, 215, 255);// Moon Sand
        Colors[155] = new Color32(40, 180, 190, 255); // Space Grass
        Colors[156] = new Color32(90, 70, 95, 255);   // Space Dirt
        Colors[163] = new Color32(95, 140, 60, 255);  // Moss
        Colors[164] = new Color32(50, 180, 180, 255); // Space Leafs
        Colors[165] = new Color32(70, 60, 90, 255);   // Space Bark
        Colors[202] = new Color32(245, 160, 40, 255); // Amber
        Colors[245] = new Color32(120, 140, 100, 255);// Lost Leaves
        Colors[246] = new Color32(215, 105, 25, 255); // Autumn Leaves
        Colors[249] = new Color32(240, 145, 175, 255);// Sakura Leaves
        Colors[253] = new Color32(230, 220, 190, 255);// Elf Bricks
        Colors[259] = new Color32(45, 95, 75, 255);   // Forgotten Grass
        Colors[273] = new Color32(75, 52, 33, 255);   // Mud
        Colors[274] = new Color32(240, 240, 245, 255);// Salt
        Colors[275] = new Color32(50, 115, 235, 255); // Cobalt Ore
        Colors[277] = new Color32(52, 130, 40, 255);  // Jungle Sod
        Colors[288] = new Color32(80, 50, 95, 255);   // Nebula Rock
        Colors[292] = new Color32(25, 22, 30, 255);   // Dark Matter
        Colors[293] = new Color32(40, 30, 50, 255);   // Obsidian
        Colors[294] = new Color32(190, 175, 215, 255);// Dream Bricks
        Colors[299] = new Color32(135, 130, 128, 255);// Gravel
        Colors[303] = new Color32(185, 75, 245, 255); // Amethyst
        Colors[329] = new Color32(180, 140, 45, 255); // Fall Grass
        Colors[344] = new Color32(235, 235, 230, 255);// Wool

        for (int i = 3; i < Colors.Length; i++)
        {
            if (Colors[i].a == 0)
            {
                Colors[i] = new Color32(132, 128, 122, 255);
            }
        }
    }

    public static Color32 GetBlockColor(ushort blockId)
    {
        if (blockId >= Colors.Length) return new Color32(128, 128, 128, 255);
        return Colors[blockId];
    }


}
}
