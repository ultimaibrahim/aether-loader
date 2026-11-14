using System;
using Aether.Nano;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Blocksters.Terrain.chunk;
using Blocksters.Terrain.renderer;
using UnityEngine;
using UnityEngine.Rendering;

namespace Aether.Mods.Lens
{
    /// <summary>
    /// True 3D isometric block cube renderer.
    ///
    /// The cube faces are sampled from Block Story's real block atlas
    /// (<see cref="AtlasLoader.SharedBlocksMaterial"/>) using each <see cref="Surface.Texture"/>
    /// index, not from the flat item icon. Top / left / right faces therefore show the actual
    /// grass, snow, ore, wood, etc. textures, with the surface hue applied for painted blocks.
    /// Falls back to the item sprite only when the block is not a cube or the atlas is not ready.
    /// </summary>
    internal static class IsometricBlockIconRenderer
    {
        private const int TileSize = 32;
        private const int CubeSize = 64;

        private static readonly Dictionary<long, Texture2D> isoCubeCache = new Dictionary<long, Texture2D>();
        private static readonly Dictionary<string, Texture2D> namedCubeCache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, Color32[]> blockTileCache = new Dictionary<int, Color32[]>();
        private static readonly HashSet<int> blockTileFailures = new HashSet<int>();

        private static AtlasLoader atlasLoader;
        private static Material atlasMaterial;
        private static Texture atlasTexture;
        private static int atlasH = 8;
        private static int atlasV = 32;
        private static float atlasPadding;
        private static bool loggedAtlasReady;
        private static bool loggedAtlasFailure;
        private static bool loggedTileSample;
        private static bool savedAtlasSnapshot;
        private static Color32[] atlasPixels;
        private static int atlasPixelWidth;
        private static int atlasPixelHeight;
        private static bool atlasPixelsReady;

        public static void ClearCache()
        {
            HashSet<Texture2D> destroyed = new HashSet<Texture2D>();
            foreach (var kvp in isoCubeCache)
            {
                if (kvp.Value != null && destroyed.Add(kvp.Value)) UnityEngine.Object.Destroy(kvp.Value);
            }
            foreach (var kvp in namedCubeCache)
            {
                if (kvp.Value != null && destroyed.Add(kvp.Value)) UnityEngine.Object.Destroy(kvp.Value);
            }

            isoCubeCache.Clear();
            namedCubeCache.Clear();
            blockTileCache.Clear();
            blockTileFailures.Clear();
            atlasTexture = null;
            atlasPixels = null;
            atlasPixelWidth = 0;
            atlasPixelHeight = 0;
            atlasPixelsReady = false;
        }

        public static Texture2D GetOrCreateBlockCubeTexture(Block block, BlockType bType, InvBaseItem baseItem)
        {
            return GetOrCreateBlockCubeTexture(block.id, block.data, bType, baseItem);
        }

        public static Texture2D GetOrCreateBlockCubeTexture(int blockId, BlockType bType, InvBaseItem baseItem)
        {
            return GetOrCreateBlockCubeTexture(blockId, 0, bType, baseItem);
        }

        public static Texture2D GetOrCreateBlockCubeTexture(int blockId, ushort data, BlockType bType, InvBaseItem baseItem)
        {
            long key = ((long)(uint)blockId << 16) | data;
            if (blockId > 0 && isoCubeCache.TryGetValue(key, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            string nameKey = bType != null ? bType.name : (baseItem != null ? baseItem.name : null);
            string namedKey = string.IsNullOrEmpty(nameKey) ? null : $"{nameKey}:{data}";
            if (namedKey != null && namedCubeCache.TryGetValue(namedKey, out Texture2D nameCached) && nameCached != null)
            {
                if (blockId > 0) isoCubeCache[key] = nameCached;
                return nameCached;
            }

            // Direct atlas sampling is the stable path: the terrain shader needs the full
            // chunk context (texture array slice, global keywords) to render outside a chunk.
            Texture2D newCube = GenerateIsometricCube(blockId, data, bType, baseItem);

            if (newCube != null)
            {
                if (blockId > 0) isoCubeCache[key] = newCube;
                if (namedKey != null) namedCubeCache[namedKey] = newCube;
            }
            return newCube;
        }

        public static Texture2D GetOrCreateItemCubeTexture(InvBaseItem baseItem)
        {
            if (baseItem == null || string.IsNullOrEmpty(baseItem.name)) return null;

            if (namedCubeCache.TryGetValue(baseItem.name + ":0", out Texture2D cached) && cached != null)
            {
                return cached;
            }

            int blockId = 0;
            BlockType bType = null;
            try
            {
                if (BlockTypes.byName != null && BlockTypes.byName.TryGetValue(baseItem.name, out bType) && bType != null)
                {
                    blockId = bType.id;
                }
            }
            catch { }

            return GetOrCreateBlockCubeTexture(blockId, 0, bType, baseItem);
        }

        /// <summary>
        /// Renders a 1x1 block with Block Story's own terrain material
        /// (<c>TerrainMobile</c> / <c>UBuild/Earth</c>) and an orthographic isometric camera.
        /// This is the only path that sees exactly what the chunk shader sees, including the
        /// texture-array/atlas layout, hue and material properties.
        /// </summary>
        private static Texture2D TryRenderBlockWithGameMaterial(BlockType bType, ushort data)
        {
            if (bType == null || bType.shape != BlockType.Shape.Cube || !bType.visible || bType.liquid)
                return null;
            if (atlasMaterial == null && !TryGetAtlas()) return null;
            if (atlasMaterial == null) return null;

            GameObject root = null;
            GameObject camGo = null;
            GameObject lightGo = null;
            RenderTexture rt = null;
            Mesh mesh = null;

            try
            {
                mesh = BuildBlockMesh(bType, data);
                if (mesh == null) return null;

                root = new GameObject("AetherLensBlockPreview");
                root.hideFlags = HideFlags.HideAndDontSave;
                root.layer = 0;
                root.transform.position = new Vector3(-0.5f, -0.5f, -0.5f);

                MeshFilter filter = root.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                MeshRenderer renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = atlasMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                camGo = new GameObject("AetherLensBlockCamera");
                camGo.hideFlags = HideFlags.HideAndDontSave;
                Camera cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = 1.65f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << 0;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 20f;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.transform.position = new Vector3(2.2f, 2.2f, -2.2f);
                cam.transform.LookAt(Vector3.zero);

                lightGo = new GameObject("AetherLensBlockLight");
                lightGo.hideFlags = HideFlags.HideAndDontSave;
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1f;
                lightGo.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

                rt = RenderTexture.GetTemporary(CubeSize, CubeSize, 24, RenderTextureFormat.ARGB32);
                RenderTexture prev = RenderTexture.active;
                try
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;

                    Texture2D result = new Texture2D(CubeSize, CubeSize, TextureFormat.ARGB32, false);
                    result.ReadPixels(new Rect(0, 0, CubeSize, CubeSize), 0, 0);
                    result.Apply();
                    result.filterMode = FilterMode.Bilinear;
                    result.wrapMode = TextureWrapMode.Clamp;
                    return result;
                }
                finally
                {
                    cam.targetTexture = null;
                    RenderTexture.active = prev;
                }
            }
            catch (Exception ex)
            {
                LensModule.Log?.LogWarning($"[Aether.Lens] Material cube render failed: {ex.Message}");
                return null;
            }
            finally
            {
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
                if (root != null) UnityEngine.Object.Destroy(root);
                if (camGo != null) UnityEngine.Object.Destroy(camGo);
                if (lightGo != null) UnityEngine.Object.Destroy(lightGo);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Mesh BuildBlockMesh(BlockType bType, ushort data)
        {
            Surface top = bType.GetSurface(0, data);
            Surface right = bType.GetSurface(3, data);
            Surface front = bType.GetSurface(4, data);

            if (top.Texture < 0) top = bType.GetSurface(0, 0);
            if (right.Texture < 0) right = top;
            if (front.Texture < 0) front = top;

            Vector3[] vertices = new Vector3[12];
            Vector2[] uv = new Vector2[12];
            Color32[] colors = new Color32[12];
            int[] triangles = new int[36];

            // Top face (y = 1): vertices 0..3
            vertices[0] = new Vector3(0f, 1f, 1f);
            vertices[1] = new Vector3(1f, 1f, 1f);
            vertices[2] = new Vector3(1f, 1f, 0f);
            vertices[3] = new Vector3(0f, 1f, 0f);

            // Right face (x = 1): vertices 4..7
            vertices[4] = new Vector3(1f, 0f, 1f);
            vertices[5] = new Vector3(1f, 1f, 1f);
            vertices[6] = new Vector3(1f, 1f, 0f);
            vertices[7] = new Vector3(1f, 0f, 0f);

            // Front face (z = 0): vertices 8..11
            vertices[8] = new Vector3(0f, 0f, 0f);
            vertices[9] = new Vector3(0f, 1f, 0f);
            vertices[10] = new Vector3(1f, 1f, 0f);
            vertices[11] = new Vector3(1f, 0f, 0f);

            ApplyFace(uv, colors, 0, top, new Color32(255, 255, 255, 255));
            ApplyFace(uv, colors, 4, right, new Color32(205, 205, 205, 255));
            ApplyFace(uv, colors, 8, front, new Color32(150, 150, 150, 255));

            // Double-sided triangles so the preview never disappears due to winding/culling.
            SetFaceTriangles(triangles, 0, 0);
            SetFaceTriangles(triangles, 6, 1);
            SetFaceTriangles(triangles, 12, 4);
            SetFaceTriangles(triangles, 18, 5);
            SetFaceTriangles(triangles, 24, 8);
            SetFaceTriangles(triangles, 30, 9);

            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ApplyFace(Vector2[] uv, Color32[] colors, int offset, Surface surface, Color32 shade)
        {
            int col = surface.Texture & 7;
            int row = surface.Texture >> 3;
            float hue = (float)RYBUtil.RGBEnc2[surface.Hue & 0x3F] * 0.00024414062f;
            Vector2 encoded = new Vector2(hue, (col << 5) | row);

            for (int i = 0; i < 4; i++)
            {
                uv[offset + i] = encoded;
                colors[offset + i] = shade;
            }
        }

        private static void SetFaceTriangles(int[] triangles, int offset, int vertex)
        {
            triangles[offset + 0] = vertex + 0;
            triangles[offset + 1] = vertex + 1;
            triangles[offset + 2] = vertex + 2;
            triangles[offset + 3] = vertex + 0;
            triangles[offset + 4] = vertex + 2;
            triangles[offset + 5] = vertex + 3;
        }

        private static Texture2D GenerateIsometricCube(int blockId, ushort data, BlockType bType, InvBaseItem baseItem)
        {
            // Graphics.DrawTexture + ReadPixels only produce pixels during the Repaint pass.
            if (Event.current != null && Event.current.type != EventType.Repaint) return null;

            Color32[] topTile = null;
            Color32[] leftTile = null;
            Color32[] rightTile = null;
            Color32 topHue = White;
            Color32 leftHue = White;
            Color32 rightHue = White;
            bool atlasReady = false;

            bool transparentColored = bType is TrColoredBlockType || bType is GlowTrColoredBlockType;
            if (bType != null && bType.shape == BlockType.Shape.Cube && bType.visible && !bType.liquid && !transparentColored)
            {
                Surface top = bType.GetSurface(0, data);
                Surface left = bType.GetSurface(2, data);
                Surface right = bType.GetSurface(3, data);

                int topIdx = top.Texture >= 0 ? top.Texture : bType.MainTexture;
                int leftIdx = left.Texture >= 0 ? left.Texture : topIdx;
                int rightIdx = right.Texture >= 0 ? right.Texture : topIdx;

                topTile = GetBlockTile(topIdx);
                leftTile = GetBlockTile(leftIdx);
                rightTile = GetBlockTile(rightIdx);

                topHue = SurfaceHueToColor(top.Hue);
                leftHue = SurfaceHueToColor(left.Hue);
                rightHue = SurfaceHueToColor(right.Hue);

                if (topTile != null)
                {
                    if (leftTile == null) leftTile = topTile;
                    if (rightTile == null) rightTile = topTile;
                    atlasReady = true;
                }
            }

            Texture2D spriteTex = null;
            Color32 fallbackCol = blockId > 0
                ? NanoPalette.GetBlockColor((ushort)blockId)
                : new Color32((byte)140, (byte)140, (byte)140, 255);

            if (!atlasReady)
            {
                spriteTex = ExtractItemSprite(baseItem);
                if (fallbackCol.a == 0 && baseItem != null)
                {
                    Color c = baseItem.color;
                    fallbackCol = c.a > 0.05f ? (Color32)c : new Color32((byte)145, (byte)140, (byte)130, 255);
                }
            }

            if (!atlasReady && !loggedAtlasFailure)
            {
                loggedAtlasFailure = true;
                LensModule.Log?.LogWarning($"[Aether.Lens] Block atlas unavailable for '{bType?.name ?? "null"}'; using item icon fallback. atlasTexture={(atlasTexture != null ? atlasTexture.name : "null")}, material={(atlasMaterial != null ? atlasMaterial.name : "null")}");
            }

            Color32[] pixels = new Color32[CubeSize * CubeSize];

            for (int py = 0; py < CubeSize; py++)
            {
                int rowOffset = py * CubeSize;
                for (int px = 0; px < CubeSize; px++)
                {
                    // A. Top face
                    float uTop = 0.5f * ((py - 34f) / 12f - (px - 32f) / 28f);
                    float vTop = 0.5f * ((px - 32f) / 28f + (py - 34f) / 12f);
                    if (uTop >= 0f && uTop <= 1f && vTop >= 0f && vTop <= 1f)
                    {
                        Color32 c = atlasReady
                            ? SampleTile(topTile, uTop, vTop, topHue, 1.0f)
                            : SampleSpriteOrColor(spriteTex, uTop, vTop, fallbackCol, 1.0f);
                        pixels[rowOffset + px] = c;
                        continue;
                    }

                    // B. Left face
                    float uLeft = (px - 4f) / 28f;
                    float vLeft = (py - (14f - 12f * uLeft)) / 32f;
                    if (uLeft >= 0f && uLeft <= 1f && vLeft >= 0f && vLeft <= 1f)
                    {
                        Color32 c = atlasReady
                            ? SampleTile(leftTile, uLeft, vLeft, leftHue, 0.78f)
                            : SampleSpriteOrColor(spriteTex, uLeft, vLeft, fallbackCol, 0.78f);
                        pixels[rowOffset + px] = c;
                        continue;
                    }

                    // C. Right face
                    float uRight = (px - 32f) / 28f;
                    float vRight = (py - (2f + 12f * uRight)) / 32f;
                    if (uRight >= 0f && uRight <= 1f && vRight >= 0f && vRight <= 1f)
                    {
                        Color32 c = atlasReady
                            ? SampleTile(rightTile, uRight, vRight, rightHue, 0.55f)
                            : SampleSpriteOrColor(spriteTex, uRight, vRight, fallbackCol, 0.55f);
                        pixels[rowOffset + px] = c;
                        continue;
                    }

                    pixels[rowOffset + px] = new Color32(0, 0, 0, 0);
                }
            }

            if (spriteTex != null)
            {
                UnityEngine.Object.Destroy(spriteTex);
            }

            Texture2D isoCube = new Texture2D(CubeSize, CubeSize, TextureFormat.ARGB32, false);
            isoCube.filterMode = FilterMode.Bilinear;
            isoCube.wrapMode = TextureWrapMode.Clamp;
            isoCube.SetPixels32(pixels);
            isoCube.Apply();
            return isoCube;
        }

        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        private static Color32 SurfaceHueToColor(ushort hue)
        {
            if (hue == 0) return White;
            try
            {
                int idx = hue & 0x3F;
                return RYBUtil.RGB32[idx];
            }
            catch
            {
                return White;
            }
        }

        private static Color32 SampleTile(Color32[] tile, float u, float v, Color32 hue, float shade)
        {
            if (tile == null) return new Color32(0, 0, 0, 0);

            int sx = Mathf.Clamp((int)(u * TileSize), 0, TileSize - 1);
            int sy = Mathf.Clamp((int)(v * TileSize), 0, TileSize - 1);
            Color32 c = tile[sy * TileSize + sx];
            if (c.a < 0.05f) return new Color32(0, 0, 0, 0);

            float r = c.r * (hue.r / 255f) * shade;
            float g = c.g * (hue.g / 255f) * shade;
            float b = c.b * (hue.b / 255f) * shade;
            return new Color32(
                (byte)Mathf.Clamp((int)r, 0, 255),
                (byte)Mathf.Clamp((int)g, 0, 255),
                (byte)Mathf.Clamp((int)b, 0, 255),
                c.a);
        }

        private static Color32[] GetBlockTile(int textureIndex)
        {
            if (textureIndex < 0 || textureIndex > 255) return null;
            if (blockTileCache.TryGetValue(textureIndex, out Color32[] cached)) return cached;
            if (blockTileFailures.Contains(textureIndex)) return null;

            if (!TryLoadAtlasPixels()) return null;

            int tileW = Mathf.Max(8, atlasPixelWidth / Mathf.Max(1, atlasH));
            int tileH = Mathf.Max(8, atlasPixelHeight / Mathf.Max(1, atlasV));
            int col = textureIndex % Mathf.Max(1, atlasH);
            int row = textureIndex / Mathf.Max(1, atlasH);
            int x0 = col * tileW;
            // ReadPixels is bottom-up and the atlas content is packed from the bottom.
            int y0 = row * tileH;

            if (x0 + tileW > atlasPixelWidth || y0 + tileH > atlasPixelHeight)
            {
                blockTileFailures.Add(textureIndex);
                return null;
            }

            Color32[] pixels = new Color32[TileSize * TileSize];
            for (int sy = 0; sy < TileSize; sy++)
            {
                int srcY = y0 + Mathf.Clamp(sy * tileH / TileSize, 0, tileH - 1);
                int rowOffset = srcY * atlasPixelWidth;
                for (int sx = 0; sx < TileSize; sx++)
                {
                    int srcX = x0 + Mathf.Clamp(sx * tileW / TileSize, 0, tileW - 1);
                    pixels[sy * TileSize + sx] = atlasPixels[rowOffset + srcX];
                }
            }

            if (!loggedTileSample)
            {
                loggedTileSample = true;
                Color32 sample = pixels[(TileSize / 2) * TileSize + (TileSize / 2)];
                LensModule.Log?.LogInfo($"[Aether.Lens] Atlas tile {textureIndex}: center pixel rgba=({sample.r},{sample.g},{sample.b},{sample.a}), atlas={atlasTexture?.name}, tile={tileW}x{tileH} at ({x0},{y0})");
            }

            blockTileCache[textureIndex] = pixels;
            return pixels;
        }

        private static bool TryLoadAtlasPixels()
        {
            if (atlasPixelsReady) return atlasPixels != null;
            atlasPixelsReady = true;

            if (!TryGetAtlas()) { atlasPixelsReady = false; return false; }

            RenderTexture rt = null;
            Texture2D readback = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                int w = atlasTexture.width;
                int h = atlasTexture.height;
                if (w <= 0 || h <= 0) return false;

                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(atlasTexture, rt);
                RenderTexture.active = rt;

                readback = new Texture2D(w, h, TextureFormat.ARGB32, false);
                readback.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readback.Apply();
                atlasPixels = readback.GetPixels32();
                atlasPixelWidth = w;
                atlasPixelHeight = h;
                return true;
            }
            catch (Exception ex)
            {
                LensModule.Log?.LogWarning($"[Aether.Lens] Atlas readback failed: {ex.Message}");
                atlasPixels = null;
                return false;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readback != null) UnityEngine.Object.Destroy(readback);
            }
        }

        private static bool TryGetAtlas()
        {
            if (atlasTexture != null) return true;

            try
            {
                if (atlasLoader == null)
                    atlasLoader = UnityEngine.Object.FindAnyObjectByType<AtlasLoader>();

                if (atlasLoader != null)
                {
                    // MeshBuilder encodes texture indices as (column & 7, row >> 3), so the
                    // atlas is always 8 columns wide; only the row count is variable.
                    atlasH = 8;
                    atlasV = atlasLoader.VerticalTextures > 0 ? atlasLoader.VerticalTextures : 32;
                    if (atlasV < 8) atlasV = 32;
                    atlasPadding = Mathf.Clamp01(atlasLoader.Padding);
                }

                if (atlasMaterial == null)
                    atlasMaterial = AtlasLoader.SharedBlocksMaterial;

                // In gameplay the block material is normally assigned to the live chunk
                // renderer; AtlasLoader may not exist in every scene.
                if (atlasMaterial == null)
                {
                    ChunkLoader chunk = UnityEngine.Object.FindAnyObjectByType<ChunkLoader>();
                    if (chunk != null)
                    {
                        Renderer renderer = chunk.GetComponent<Renderer>();
                        if (renderer != null)
                        {
                            Material[] materials = renderer.sharedMaterials;
                            if (materials != null)
                            {
                                for (int i = 0; i < materials.Length; i++)
                                {
                                    if (materials[i] != null && materials[i].mainTexture != null)
                                    {
                                        atlasMaterial = materials[i];
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (atlasMaterial != null)
                {
                    atlasTexture = atlasMaterial.mainTexture;

                    if (atlasTexture == null)
                    {
                        try
                        {
                            string[] names = atlasMaterial.GetTexturePropertyNames();
                            for (int i = 0; i < names.Length; i++)
                            {
                                string n = names[i];
                                if (n == "_MainTex" || n.IndexOf("Atlas", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("Block", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    Texture t = atlasMaterial.GetTexture(n);
                                    if (t != null)
                                    {
                                        atlasTexture = t;
                                        break;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // The material's _MainTex tiling is the authoritative atlas layout.
                    // TerrainMobile uses (0.125, 0.05) => 8 columns x 20 rows of 128x128.
                    try
                    {
                        Vector2 textureScale = atlasMaterial.GetTextureScale("_MainTex");
                        if (textureScale.x > 0.0001f && textureScale.y > 0.0001f)
                        {
                            int cols = Mathf.RoundToInt(1f / textureScale.x);
                            int rows = Mathf.RoundToInt(1f / textureScale.y);
                            if (cols >= 1 && cols <= 32) atlasH = cols;
                            if (rows >= 1 && rows <= 64) atlasV = rows;
                        }
                    }
                    catch { }

                    if (!savedAtlasSnapshot && atlasTexture != null)
                    {
                        savedAtlasSnapshot = true;
                        SaveAtlasSnapshot();
                    }

                    if (!loggedAtlasReady)
                    {
                        loggedAtlasReady = true;
                        string props = "";
                        try { props = string.Join(",", atlasMaterial.GetTexturePropertyNames()); } catch { }
                        LensModule.Log?.LogInfo($"[Aether.Lens] Block atlas ready: '{(atlasTexture != null ? atlasTexture.name : "null")}' {(atlasTexture != null ? atlasTexture.width + "x" + atlasTexture.height : "?")}, H={atlasH}, V={atlasV}, pad={atlasPadding:F3}, material='{atlasMaterial.name}', shader='{atlasMaterial.shader.name}', mainTex='{(atlasMaterial.mainTexture != null ? atlasMaterial.mainTexture.name : "null")}', props=[{props}]");
                    }
                }
            }
            catch { }

            return atlasTexture != null;
        }

        private static void SaveAtlasSnapshot()
        {
            try
            {
                if (!TryLoadAtlasPixels() || atlasPixels == null) return;

                Texture2D snapshot = new Texture2D(atlasPixelWidth, atlasPixelHeight, TextureFormat.ARGB32, false);
                snapshot.SetPixels32(atlasPixels);
                snapshot.Apply();

                string path = Path.Combine(Paths.ConfigPath, "aether_lens_atlas.png");
                File.WriteAllBytes(path, snapshot.EncodeToPNG());
                LensModule.Log?.LogInfo($"[Aether.Lens] Atlas snapshot written: {path} ({atlasPixelWidth}x{atlasPixelHeight})");
                UnityEngine.Object.Destroy(snapshot);
            }
            catch (Exception ex)
            {
                LensModule.Log?.LogWarning($"[Aether.Lens] Atlas snapshot failed: {ex.Message}");
            }
        }

        private static Color32[] ExtractAtlasTile(int textureIndex)
        {
            RenderTexture rt = null;
            Texture2D readback = null;
            RenderTexture prev = RenderTexture.active;
            try
            {
                int col = textureIndex & 7;
                int row = textureIndex >> 3;
                float pad = atlasPadding;
                float u = (col + pad * 0.5f) / atlasH;
                // Graphics.DrawTexture uses top-left source coordinates; the terrain atlas
                // stores its tiles from the bottom row up. Flip V or the sample lands in the
                // transparent area above the used atlas region.
                float v = 1f - (row + 1f) / atlasV + pad * 0.5f / atlasV;
                float w = (1f - pad) / atlasH;
                float h = (1f - pad) / atlasV;

                rt = RenderTexture.GetTemporary(TileSize, TileSize, 0, RenderTextureFormat.ARGB32);
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.clear);

                Color prevColor = GUI.color;
                GUI.color = Color.white;
                Graphics.DrawTexture(new Rect(0, 0, TileSize, TileSize), atlasTexture, new Rect(u, v, w, h), 0, 0, 0, 0);
                GUI.color = prevColor;

                readback = new Texture2D(TileSize, TileSize, TextureFormat.ARGB32, false);
                readback.ReadPixels(new Rect(0, 0, TileSize, TileSize), 0, 0);
                readback.Apply();
                return readback.GetPixels32();
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readback != null) UnityEngine.Object.Destroy(readback);
            }
        }

        private static Texture2D ExtractItemSprite(InvBaseItem baseItem)
        {
            try
            {
                if (baseItem == null || baseItem.iconAtlas == null || string.IsNullOrEmpty(baseItem.iconName))
                    return null;

                var sprite = baseItem.iconAtlas.GetSprite(baseItem.iconName);
                Texture atlasTex = baseItem.iconAtlas.texture;
                if (sprite == null || atlasTex == null) return null;

                Rect uv = sprite.outer;
                if (baseItem.iconAtlas.coordinates == UIAtlas.Coordinates.Pixels)
                    uv = NGUIMath.ConvertToTexCoords(uv, atlasTex.width, atlasTex.height);

                RenderTexture rt = RenderTexture.GetTemporary(TileSize, TileSize, 0, RenderTextureFormat.ARGB32);
                RenderTexture prev = RenderTexture.active;
                Texture2D spriteTex = null;
                try
                {
                    RenderTexture.active = rt;
                    GL.Clear(true, true, Color.clear);

                    Color prevColor = GUI.color;
                    GUI.color = Color.white;
                    Graphics.DrawTexture(new Rect(0, 0, TileSize, TileSize), atlasTex, uv, 0, 0, 0, 0);
                    GUI.color = prevColor;

                    spriteTex = new Texture2D(TileSize, TileSize, TextureFormat.ARGB32, false);
                    spriteTex.ReadPixels(new Rect(0, 0, TileSize, TileSize), 0, 0);
                    spriteTex.Apply();
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                }

                return spriteTex;
            }
            catch
            {
                return null;
            }
        }

        private static Color32 SampleSpriteOrColor(Texture2D spriteTex, float u, float v, Color32 fallback, float shade)
        {
            Color32 baseCol;
            if (spriteTex != null)
            {
                int sx = Mathf.Clamp((int)(u * TileSize), 0, TileSize - 1);
                int sy = Mathf.Clamp((int)(v * TileSize), 0, TileSize - 1);
                Color c = spriteTex.GetPixel(sx, sy);
                baseCol = c.a > 0.15f ? (Color32)c : fallback;
            }
            else
            {
                baseCol = fallback;
            }

            byte r = (byte)Mathf.Clamp((int)(baseCol.r * shade), 0, 255);
            byte g = (byte)Mathf.Clamp((int)(baseCol.g * shade), 0, 255);
            byte b = (byte)Mathf.Clamp((int)(baseCol.b * shade), 0, 255);
            return new Color32(r, g, b, 255);
        }

        private static Color32 BlendColor(Color32 baseCol, Color32 blendCol, float blendRatio)
        {
            byte r = (byte)Mathf.Clamp((int)Mathf.Lerp(baseCol.r, blendCol.r, blendRatio), 0, 255);
            byte g = (byte)Mathf.Clamp((int)Mathf.Lerp(baseCol.g, blendCol.g, blendRatio), 0, 255);
            byte b = (byte)Mathf.Clamp((int)Mathf.Lerp(baseCol.b, blendCol.b, blendRatio), 0, 255);
            byte a = (byte)Mathf.Clamp((int)Mathf.Lerp(baseCol.a, blendCol.a, blendRatio), 0, 255);
            return new Color32(r, g, b, a);
        }
    }
}
