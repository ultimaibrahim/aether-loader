using System;

namespace Aether.Core.Voxel
{
    public delegate bool VoxelRaycastHitHandler(int x, int y, int z, int face, float distance);

    /// <summary>
    /// High-performance zero-allocation Amanatides &amp; Woo DDA (Digital Differential Analyzer) voxel raycaster.
    /// Traverses the voxel grid and invokes stack callbacks without generating heap enumerator objects.
    /// </summary>
    public static class FastVoxelRaycast
    {
        public static bool Trace(
            float startX, float startY, float startZ,
            float dirX, float dirY, float dirZ,
            float maxDistance,
            VoxelRaycastHitHandler hitHandler)
        {
            if (hitHandler == null) throw new ArgumentNullException(nameof(hitHandler));

            // Normalize direction if needed
            float len = MathF.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
            if (len < 1e-6f) return false;

            dirX /= len;
            dirY /= len;
            dirZ /= len;

            int x = (int)MathF.Floor(startX);
            int y = (int)MathF.Floor(startY);
            int z = (int)MathF.Floor(startZ);

            int stepX = dirX >= 0 ? 1 : -1;
            int stepY = dirY >= 0 ? 1 : -1;
            int stepZ = dirZ >= 0 ? 1 : -1;

            float tDeltaX = MathF.Abs(dirX) < 1e-6f ? float.PositiveInfinity : MathF.Abs(1.0f / dirX);
            float tDeltaY = MathF.Abs(dirY) < 1e-6f ? float.PositiveInfinity : MathF.Abs(1.0f / dirY);
            float tDeltaZ = MathF.Abs(dirZ) < 1e-6f ? float.PositiveInfinity : MathF.Abs(1.0f / dirZ);

            float tMaxX = dirX >= 0 ? (x + 1.0f - startX) * tDeltaX : (startX - x) * tDeltaX;
            float tMaxY = dirY >= 0 ? (y + 1.0f - startY) * tDeltaY : (startY - y) * tDeltaY;
            float tMaxZ = dirZ >= 0 ? (z + 1.0f - startZ) * tDeltaZ : (startZ - z) * tDeltaZ;

            int face = 0;
            float currentDist = 0.0f;

            while (currentDist <= maxDistance)
            {
                // Inspect voxel hit
                if (hitHandler(x, y, z, face, currentDist))
                {
                    return true;
                }

                if (tMaxX < tMaxY)
                {
                    if (tMaxX < tMaxZ)
                    {
                        x += stepX;
                        currentDist = tMaxX;
                        tMaxX += tDeltaX;
                        face = stepX > 0 ? 4 : 5; // West/East face
                    }
                    else
                    {
                        z += stepZ;
                        currentDist = tMaxZ;
                        tMaxZ += tDeltaZ;
                        face = stepZ > 0 ? 2 : 3; // North/South face
                    }
                }
                else
                {
                    if (tMaxY < tMaxZ)
                    {
                        y += stepY;
                        currentDist = tMaxY;
                        tMaxY += tDeltaY;
                        face = stepY > 0 ? 0 : 1; // Bottom/Top face
                    }
                    else
                    {
                        z += stepZ;
                        currentDist = tMaxZ;
                        tMaxZ += tDeltaZ;
                        face = stepZ > 0 ? 2 : 3; // North/South face
                    }
                }
            }

            return false;
        }
    }
}
