#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using Aether.Nano;
using BepInEx.Configuration;
using Blocksters.MathLib;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using com.mindblocks.i18n;
using UnityEngine;
using static Aether.Nano.Nano;
using static Aether.Nano.NanoUi;

namespace Aether.Mods.Lens
{
    /// <summary>
    /// Lens WAILA: block/creature inspection, real atlas cube, best-tool badge, native hearts and
    /// mob quest/level options. Owns its [Lens] config; the shell only forwards drawing/update.
    /// </summary>
    public sealed partial class LensModule
    {
        public static LensModule Current { get; private set; }
        /// <summary>First id of blocks added by mods (Aether's block registry starts there).</summary>
        private const ushort CustomBlockIdStart = 500;
        public string Id => "aether_lens";
        public bool IsInitialized { get; private set; }

        private static readonly RaycastHit[] lensHitBuffer = new RaycastHit[16];

        public static ConfigEntry<bool> LensEnabled { get; private set; }
        public static ConfigEntry<KeyCode> LensToggleKey { get; private set; }
        public static ConfigEntry<float> LensBgOpacity { get; private set; }
        public static ConfigEntry<int> LensCardWidth { get; private set; }
        public static ConfigEntry<string> LensAnchor { get; private set; } // "TopCenter", "TopLeft", "BottomCenter", "AboveCrosshair"
        public static ConfigEntry<float> LensOffsetX { get; private set; }
        public static ConfigEntry<float> LensOffsetY { get; private set; }
        public static ConfigEntry<string> LensTheme { get; private set; } // "DarkObsidian", "NativeWood", "MinimalistGlass", "RoyalGold", "CompactBar"
        public static ConfigEntry<bool> LensShowCoords { get; private set; }
        public static ConfigEntry<bool> Lens3DBlockIcon { get; private set; }
        public static ConfigEntry<string> LensMobHealthStyle { get; private set; }
        public static ConfigEntry<bool> LensShowMobLevel { get; private set; }
        public static ConfigEntry<bool> LensShowMobQuests { get; private set; }
        public static ConfigEntry<bool> LensHideNativeMobHealth { get; private set; }
        private static bool lastHideNativeHp;
        private static float lastNativeHpScan;


        public void Initialize()
        {
            if (IsInitialized) return;
            Current = this;
            LensEnabled = Config.Bind("Lens", "Enabled", true, "Enable or disable Lens HUD.");
            LensToggleKey = Config.Bind("Lens", "ToggleKey", KeyCode.F4, "Hotkey to toggle Lens HUD.");
            LensBgOpacity = Config.Bind("Lens", "BgOpacity", 0.96f, new ConfigDescription("HUD background opacity.", new AcceptableValueRange<float>(0.3f, 1f)));
            LensCardWidth = Config.Bind("Lens", "CardWidth", 300, new ConfigDescription("Lens card width in UI pixels. CompactBar uses this minus 30.", new AcceptableValueRange<int>(240, 420)));
            LensAnchor = Config.Bind("Lens", "Anchor", "TopCenter", "Lens screen anchor: TopCenter, TopLeft, BottomCenter, AboveCrosshair.");
            LensOffsetX = Config.Bind("Lens", "OffsetX", 0f, new ConfigDescription("Horizontal pixel offset.", new AcceptableValueRange<float>(-500f, 500f)));
            LensOffsetY = Config.Bind("Lens", "OffsetY", 0f, new ConfigDescription("Vertical pixel offset.", new AcceptableValueRange<float>(-500f, 500f)));
            LensTheme = Config.Bind("Lens", "Theme", "NativeWood", "Visual design theme: NativeWood, DarkObsidian, MinimalistGlass, RoyalGold, CompactBar.");
            LensShowCoords = Config.Bind("Lens", "ShowCoordinates", false, "Show target block coordinates in Lens HUD.");
            Lens3DBlockIcon = Config.Bind("Lens", "3DBlockIcon", true, "Render placeable blocks as 3D isometric cubes in Lens WAILA HUD.");
            LensMobHealthStyle = Config.Bind("Lens", "MobHealthStyle", "Hearts", new ConfigDescription("Mob health display in Lens: Hearts, Bar, or None.", new AcceptableValueList<string>("Hearts", "Bar", "None")));
            LensShowMobLevel = Config.Bind("Lens", "ShowMobLevel", true, "Show the mob level in the Lens card.");
            LensShowMobQuests = Config.Bind("Lens", "ShowMobQuests", true, "Show whether a quest is available for quest NPCs in the Lens card.");
            LensHideNativeMobHealth = Config.Bind("Lens", "HideNativeMobHealth", false, "Hide Block Story's native overhead mob health bars while Lens is showing the mob card.");
            BindScale();
            IsInitialized = true;
            Log?.LogInfo("[Lens] WAILA module initialized (own assembly, per-mod config section).");
        }

        public void OnUpdate() { UpdateLensNativeMobHealthBars(); }
        public void OnLateUpdate() { }
        public void OnSceneChanged(string sceneName) { nativeHeartProbed = false; }
        public bool IsModalOpen => false;
        public void ForceClose() { }

        public void DrawHud() => DrawLensHUD();

        // GetName/GetDisplayName only exist on Health; pets and mod mobs may offer them under the same names.
        private static readonly Dictionary<Type, System.Reflection.MethodInfo[]> nameMethodCache =
            new Dictionary<Type, System.Reflection.MethodInfo[]>();

        private static string FromNameMethods(HealthBase health, params string[] methodNames)
        {
            Type t = health.GetType();
            if (!nameMethodCache.TryGetValue(t, out var methods))
            {
                methods = new System.Reflection.MethodInfo[methodNames.Length];
                for (int i = 0; i < methodNames.Length; i++)
                    methods[i] = t.GetMethod(methodNames[i], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null);
                nameMethodCache[t] = methods;
            }
            foreach (var m in methods)
            {
                if (m == null || m.ReturnType != typeof(string)) continue;
                try { string v = m.Invoke(health, null) as string; if (!string.IsNullOrEmpty(v)) return v; } catch { }
            }
            return null;
        }

        /// <summary>Mod mobs often expose their level as an int property "Level".</summary>
        private static int ModMobLevel(HealthBase health)
        {
            try
            {
                var p = health.GetType().GetProperty("Level", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (p != null && p.PropertyType == typeof(int)) return (int)p.GetValue(health);
            }
            catch { }
            return 0;
        }

        private static string RawMobName(HealthBase health) =>
            FromNameMethods(health, "GetName") ?? health.gameObject.name.Replace("(Clone)", "").Trim();

        private static string GetLocalizedMobName(HealthBase health)
        {
            if (health == null) return "Entity";
            try
            {
                string display = FromNameMethods(health, "GetDisplayName");
                if (!string.IsNullOrEmpty(display))
                    return display;

                string raw = RawMobName(health);
                string translated = T.Mobs(raw);
                if (!string.IsNullOrEmpty(translated))
                    return translated;

                return raw;
            }
            catch
            {
                return health.gameObject.name.Replace("(Clone)", "").Trim();
            }
        }


        private void DrawLensHUD()
        {
            // Pure drawing: skip the Layout pass so the physics + voxel raycasts run once per frame
            // instead of twice (Layout and Repaint).
            if (Event.current != null && Event.current.type != EventType.Repaint) return;

            Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
            if (cam == null) return;

            Vector3 origin = cam.transform.position;
            Vector3 direction = cam.transform.forward;

            // 1. Pointed Entity Raycast
            Ray ray = new Ray(origin, direction);
            // Collide (not Ignore): mod-made mobs often use trigger colliders as their hitbox. Oversized triggers
            // (aggro ranges) are filtered by EntityHitRules.
            int hitCount = Physics.RaycastNonAlloc(ray, lensHitBuffer, 18f, ~0, QueryTriggerInteraction.Collide);
            if (hitCount > 0)
            {
                float closestDist = float.MaxValue;
                HealthBase closestHealth = null;

                for (int i = 0; i < hitCount; i++)
                {
                    var hHit = lensHitBuffer[i];
                    if (hHit.collider == null) continue;
                    if (!NanoWorld.AcceptsEntityHit(hHit.collider))
                        continue;

                    // HealthBase is the common base of native mobs (Health), pets (PetHealth) and mod mobs
                    // alike; looking only for Health made every non-native mob invisible.
                    HealthBase health = hHit.collider.GetComponentInParent<HealthBase>();
                    if (health != null && !health.IsDead())
                    {
                        if (TerrainLoader.instance != null && TerrainLoader.instance.player != null &&
                            health.transform == TerrainLoader.instance.player)
                            continue;
                        if (health is PlayerHealth) continue;
                        if (hHit.distance < closestDist && hHit.distance > 0.4f)
                        {
                            closestDist = hHit.distance;
                            closestHealth = health;
                        }
                    }
                }

                if (closestHealth != null)
                {
                    DrawEntityHUD(closestHealth);
                    return;
                }
            }

            // 2. Deterministic Voxel Raycast using RayTracer.trace
            IWorld world = GetActiveWorld();
            if (world == null) return;

            float reach = (CachedDigger != null && CachedDigger.reach > 0) ? CachedDigger.reach : 6.0f;
            float reachSq = reach * reach;

            foreach (RayTracer.Hit voxelHit in RayTracer.trace(origin, direction))
            {
                float dx = (voxelHit.x + 0.5f) - origin.x;
                float dy = (voxelHit.y + 0.5f) - origin.y;
                float dz = (voxelHit.z + 0.5f) - origin.z;
                float distSq = dx * dx + dy * dy + dz * dz;

                if (distSq > reachSq)
                    break;

                Block block;
                try
                {
                    block = NanoWorld.GetBlockSafe(world, voxelHit.x, voxelHit.y, voxelHit.z);
                }
                catch
                {
                    break;
                }

                BlockType bType = block.type;
                if (block.id == 0 || bType == null || bType.air)
                    continue;

                if (bType.liquid || !bType.visible)
                    continue;

                Vector3i targetPos = new Vector3i(voxelHit.x, voxelHit.y, voxelHit.z);
                DrawBlockHUD(targetPos, block, GetHeldDigger());
                return;
            }
        }

        private static void DrawLensCardBackground(Rect rect, string theme, float opacity)
        {
            NanoTheme palette = GetThemePalette(theme);

            Color border = palette.BorderColor;
            border.a *= opacity;
            GUI.DrawTexture(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), GetColorTexture(border));

            Color background = palette.InnerBgColor;
            background.a = Mathf.Clamp(background.a + 0.02f, 0.58f, 0.76f) * opacity;
            GUI.DrawTexture(rect, GetColorTexture(background));

            Color accent = palette.AccentColor;
            accent.a *= opacity;
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), GetColorTexture(accent));
        }

        private void UpdateLensNativeMobHealthBars()
        {
            bool hide = LensHideNativeMobHealth != null && LensHideNativeMobHealth.Value;
            bool stateChanged = hide != lastHideNativeHp;

            if (!stateChanged && Time.unscaledTime - lastNativeHpScan < 0.5f) return;
            lastNativeHpScan = Time.unscaledTime;
            lastHideNativeHp = hide;

            try
            {
                Health[] all = UnityEngine.Object.FindObjectsOfType<Health>();
                Transform player = TerrainLoader.instance != null ? TerrainLoader.instance.player : null;

                for (int i = 0; i < all.Length; i++)
                {
                    Health h = all[i];
                    if (h == null) continue;
                    if (player != null && h.transform == player) continue;

                    if (hide)
                    {
                        // Hide only the bar and numeric HP, never NameLabel/MobLvl.
                        if (h.barObject != null && h.barObject.activeSelf) h.barObject.SetActive(false);
                        if (h.HPLabel != null && h.HPLabel.gameObject.activeSelf) h.HPLabel.gameObject.SetActive(false);
                    }
                    else if (stateChanged)
                    {
                        h.RefreshHealthBarUI();
                    }
                }
            }
            catch { }
        }

        private static Texture2D GetToolStatusTexture(bool ok)
        {
            if (ok && toolCheckTexture != null) return toolCheckTexture;
            if (!ok && toolCrossTexture != null) return toolCrossTexture;

            const int size = 16;
            Color32[] pixels = new Color32[size * size];
            Color32 bg = ok ? new Color32(58, 190, 92, 245) : new Color32(210, 62, 62, 245);
            Color32 border = ok ? new Color32(22, 90, 40, 255) : new Color32(100, 22, 22, 255);
            Color32 mark = new Color32(255, 255, 255, 255);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - 7.5f;
                    float dy = y - 7.5f;
                    float d = dx * dx + dy * dy;
                    if (d <= 56f) pixels[y * size + x] = (d > 42f) ? border : bg;
                }
            }

            if (ok)
            {
                DrawPixelLine(pixels, size, 4, 8, 7, 11, 2, mark);
                DrawPixelLine(pixels, size, 7, 11, 12, 5, 2, mark);
            }
            else
            {
                DrawPixelLine(pixels, size, 5, 5, 11, 11, 2, mark);
                DrawPixelLine(pixels, size, 11, 5, 5, 11, 2, mark);
            }

            // The IMGUI/NGUI badge rendering is rotated and mirrors the glyph in this setup.
            // Rotate 180°, then mirror horizontally so the check/X reads correctly.
            Color32[] rotated = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    rotated[y * size + x] = pixels[(size - 1 - y) * size + (size - 1 - x)];
            }

            Color32[] mirrored = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    mirrored[y * size + x] = rotated[y * size + (size - 1 - x)];
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(mirrored);
            tex.Apply();

            if (ok) toolCheckTexture = tex;
            else toolCrossTexture = tex;
            return tex;
        }

        private static void DrawPixelLine(Color32[] pixels, int size, int x0, int y0, int x1, int y1, int thickness, Color32 color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                for (int oy = 0; oy < thickness; oy++)
                {
                    for (int ox = 0; ox < thickness; ox++)
                    {
                        int px = x0 + ox;
                        int py = y0 + oy;
                        if (px >= 0 && px < size && py >= 0 && py < size)
                            pixels[py * size + px] = color;
                    }
                }

                if (x0 == x1 && y0 == y1) break;
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }



        private static string lastLoggedHeldItem = "\0";

        private static readonly Dictionary<string, Digger.DiggerType> KnownDiggerItems =
            new Dictionary<string, Digger.DiggerType>(StringComparer.OrdinalIgnoreCase)
            {
                // Axe
                ["Axe"] = Digger.DiggerType.Axe,
                ["Iron Axe"] = Digger.DiggerType.Axe,

                // Pickaxe ("Dig" is the stone pickaxe item)
                ["Dig"] = Digger.DiggerType.Pickaxe,
                ["Iron Dig"] = Digger.DiggerType.Pickaxe,
                ["Goblin PickAxe"] = Digger.DiggerType.Pickaxe,

                // Shovel
                ["Shovel"] = Digger.DiggerType.Shovel,
                ["Iron Shovel"] = Digger.DiggerType.Shovel,

                // Hammer
                ["Hammer"] = Digger.DiggerType.Hammer,
                ["Iron Hammer"] = Digger.DiggerType.Hammer,

                // Special
                ["Multitool"] = Digger.DiggerType.Multitool
            };

        /// <summary>
        /// The weapon slot also holds buckets, cauldrons, controllers and sensors. Those prefabs
        /// can carry a default Digger component, so we only trust the small set of real digging
        /// tools; everything else is the bare hand.
        /// </summary>
        private static bool TryGetKnownDiggerType(string itemName, out Digger.DiggerType type)
        {
            type = Digger.DiggerType.Pickaxe;
            return !string.IsNullOrEmpty(itemName) && KnownDiggerItems.TryGetValue(itemName, out type);
        }

        /// <summary>
        /// Returns the Digger of the currently equipped weapon, or null for the bare hand.
        /// TerrainLoader.digger is a default hand Digger (it reports Shovel), so it must never
        /// be used when the equipment slot is empty.
        /// </summary>
        private static Digger GetHeldDigger()
        {
            try
            {
                InvEquipment equipment = PlayerHealth.Instance != null ? PlayerHealth.Instance.equipment : null;
                InvGameItem held = equipment != null ? equipment.GetItem(InvBaseItem.Slot.Weapon) : null;
                InvBaseItem baseItem = held != null ? held.baseItem : null;
                string itemName = baseItem != null ? baseItem.name : null;

                bool isKnownDigger = TryGetKnownDiggerType(itemName, out Digger.DiggerType knownType);
                GameObject attachment = isKnownDigger && baseItem.attachment != null ? baseItem.attachment : null;
                Digger heldDigger = attachment != null ? attachment.GetComponent<Digger>() : null;

                string heldState = heldDigger != null
                    ? $"tool:{itemName} ({heldDigger.diggerType})"
                    : baseItem != null
                        ? $"hand:{itemName} ({baseItem.category})"
                        : "hand:empty";

                if (!string.Equals(heldState, lastLoggedHeldItem, StringComparison.Ordinal))
                {
                    lastLoggedHeldItem = heldState;
                    Log?.LogInfo($"[Aether.Lens] Held tool: {heldState}");
                }

                // If a known digger item lacks its attachment Digger, treat it as Hand; do not
                // invent a MonoBehaviour from the table.
                return heldDigger;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUsingBestTool(Digger digger, HarvestTool bestTool)
        {
            if (digger == null || bestTool == HarvestTool.None || bestTool == HarvestTool.Hand)
                return false;

            if (digger.diggerType == Digger.DiggerType.Multitool)
                return true;

            switch (bestTool)
            {
                case HarvestTool.Pickaxe: return digger.diggerType == Digger.DiggerType.Pickaxe;
                case HarvestTool.Axe: return digger.diggerType == Digger.DiggerType.Axe;
                case HarvestTool.Shovel: return digger.diggerType == Digger.DiggerType.Shovel;
                case HarvestTool.Hammer: return digger.diggerType == Digger.DiggerType.Hammer;
                default: return false;
            }
        }

        private static string GetBlockOriginName(BlockType bType, string itemName)
        {
            if (bType == null) return "Block Story";

            // Who registered it: the host (Aether) knows mods' items and custom blocks; Nano alone does not.
            string owner = NanoOwners.OfItem(itemName) ?? NanoOwners.OfBlock(bType.id);
            if (owner != null) return owner;

            return bType.id >= CustomBlockIdStart ? Tr("Mod", "Mod") : "Block Story";
        }

        private static Texture2D heartFullTexture;
        private static Texture2D heartEmptyTexture;
        private static Texture2D toolCheckTexture;
        private static Texture2D toolCrossTexture;
        private static bool loggedFontFailure;
        private static bool nativeHeartProbed;
        private static UIAtlas nativeHeartAtlas;
        private static string nativeHeartFullSprite;
        private static string nativeHeartEmptySprite;
        private static HealthBase lastHealthAnimTarget;
        private static float lensHealthAnim = 1f;

        private static void ProbeNativeHearts()
        {
            if (nativeHeartProbed) return;
            nativeHeartProbed = true;

            try
            {
                UISprite[] sprites = Resources.FindObjectsOfTypeAll<UISprite>();
                int bestFullScore = -1;
                string candidates = "";

                // Select the real heart sprite, not the first "life" match (Life Magic is a mana icon).
                for (int i = 0; i < sprites.Length; i++)
                {
                    UISprite sprite = sprites[i];
                    if (sprite == null || sprite.atlas == null || sprite.atlas.texture == null) continue;
                    string spriteName = sprite.spriteName ?? "";
                    string label = spriteName.ToLowerInvariant();
                    if (!label.Contains("heart") && !label.Contains("life")) continue;
                    if (label.Contains("empty") || label.Contains("half") || label.Contains("background")) continue;
                    if (sprite.atlas.GetSprite(spriteName) == null) continue;

                    if (candidates.Length < 240)
                        candidates += $"{sprite.atlas.name}:{spriteName}; ";

                    int score = label == "heart" || label == "heartfull" || label == "heart full" ? 120
                        : label.Contains("heart") ? 80
                        : label == "life" ? 40
                        : 10;
                    if (score > bestFullScore)
                    {
                        bestFullScore = score;
                        nativeHeartAtlas = sprite.atlas;
                        nativeHeartFullSprite = spriteName;
                    }
                }

                // The empty sprite must belong to the same atlas as the full sprite.
                int bestEmptyScore = -1;
                if (nativeHeartAtlas != null)
                {
                    for (int i = 0; i < sprites.Length; i++)
                    {
                        UISprite sprite = sprites[i];
                        if (sprite == null || sprite.atlas != nativeHeartAtlas) continue;
                        string label = (sprite.spriteName ?? "").ToLowerInvariant();
                        if (!label.Contains("empty") && !label.Contains("background")) continue;
                        if (!label.Contains("heart") && !label.Contains("life")) continue;
                        if (nativeHeartAtlas.GetSprite(sprite.spriteName) == null) continue;

                        int score = label == "heartempty" || label == "heart empty" ? 100 : 50;
                        if (score > bestEmptyScore)
                        {
                            bestEmptyScore = score;
                            nativeHeartEmptySprite = sprite.spriteName;
                        }
                    }
                }

                if (nativeHeartAtlas != null && !string.IsNullOrEmpty(nativeHeartFullSprite))
                {
                    var fullSprite = nativeHeartAtlas.GetSprite(nativeHeartFullSprite);
                    Texture tex = nativeHeartAtlas.texture;
                    Log?.LogInfo($"[Aether.Lens] Native heart sprites: full='{nativeHeartFullSprite}', empty='{nativeHeartEmptySprite}', atlas='{nativeHeartAtlas.name}', coords={nativeHeartAtlas.coordinates}, outer={fullSprite.outer}, texture={tex.width}x{tex.height}; candidates={candidates}");
                }
                else
                {
                    Log?.LogWarning($"[Aether.Lens] Native heart sprite not found; using procedural fallback. candidates={candidates}");
                }
            }
            catch (Exception ex)
            {
                nativeHeartAtlas = null;
                nativeHeartFullSprite = null;
                nativeHeartEmptySprite = null;
                Log?.LogWarning($"[Aether.Lens] Native heart probe failed: {ex.Message}");
            }
        }

        private static bool DrawNativeHeart(Rect rect, bool full)
        {
            ProbeNativeHearts();
            if (nativeHeartAtlas == null) return false;

            string spriteName = full
                ? nativeHeartFullSprite
                : (!string.IsNullOrEmpty(nativeHeartEmptySprite) ? nativeHeartEmptySprite : nativeHeartFullSprite);
            if (string.IsNullOrEmpty(spriteName)) return false;

            Color old = GUI.color;
            try
            {
                var sprite = nativeHeartAtlas.GetSprite(spriteName);
                Texture atlasTex = nativeHeartAtlas.texture;
                if (sprite == null || atlasTex == null) return false;

                Rect uv = sprite.outer;
                if (nativeHeartAtlas.coordinates == UIAtlas.Coordinates.Pixels)
                    uv = NGUIMath.ConvertToTexCoords(uv, atlasTex.width, atlasTex.height);

                // HeartEmpty already has its final color; tinting it dark made it disappear.
                GUI.color = full || !string.IsNullOrEmpty(nativeHeartEmptySprite)
                    ? Color.white
                    : new Color(0.26f, 0.10f, 0.12f, 0.80f);
                GUI.DrawTextureWithTexCoords(rect, atlasTex, uv);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                GUI.color = old;
            }
        }

        private static float GetAnimatedHealthRatio(HealthBase health, float target)
        {
            if (health != lastHealthAnimTarget)
            {
                lastHealthAnimTarget = health;
                lensHealthAnim = target;
            }
            else
            {
                lensHealthAnim = Mathf.Lerp(lensHealthAnim, target, Mathf.Clamp01(Time.unscaledDeltaTime * 10f));
            }
            return Mathf.Clamp01(lensHealthAnim);
        }

        private static Texture2D GetHeartTexture(bool full)
        {
            if (full && heartFullTexture != null) return heartFullTexture;
            if (!full && heartEmptyTexture != null) return heartEmptyTexture;

            const int w = 9;
            const int h = 8;
            string[] mask =
            {
                "0110110",
                "1111111",
                "1111111",
                "0111110",
                "0011100",
                "0001000"
            };

            Color32[] pixels = new Color32[w * h];
            Color32 color = full
                ? new Color32(235, 55, 65, 255)
                : new Color32(85, 20, 25, 190);

            for (int y = 0; y < mask.Length; y++)
            {
                for (int x = 0; x < mask[y].Length; x++)
                {
                    int px = x + 1;
                    int py = (h - 1) - (y + 1);
                    if (px < w && py >= 0 && py < h && mask[y][x] == '1')
                        pixels[py * w + px] = color;
                }
            }

            Texture2D heart = new Texture2D(w, h, TextureFormat.ARGB32, false);
            heart.filterMode = FilterMode.Point;
            heart.wrapMode = TextureWrapMode.Clamp;
            heart.SetPixels32(pixels);
            heart.Apply();

            if (full) heartFullTexture = heart;
            else heartEmptyTexture = heart;
            return heart;
        }

        private static bool TryGetMobQuestState(HealthBase health, out bool isQuestNpc, out bool hasAvailableQuest)
        {
            isQuestNpc = false;
            hasAvailableQuest = false;

            try
            {
                QuestManager manager = QuestManager.instance;
                if (manager == null || manager.questsNPC == null || health == null) return false;

                string raw = RawMobName(health);

                if (!manager.questsNPC.TryGetValue(raw, out var quests) || quests == null) return false;

                isQuestNpc = true;
                for (int i = 0; i < quests.Count; i++)
                {
                    Quest quest = quests[i];
                    if (quest != null && quest.IsAvailable())
                    {
                        hasAvailableQuest = true;
                        break;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void DrawEntityHUD(HealthBase health)
        {

            string name = GetLocalizedMobName(health);

            MobLvl mobLvl = health.GetComponent<MobLvl>();
            bool showLevel = LensShowMobLevel == null || LensShowMobLevel.Value;
            string lvlPrefix = Tr("Lv.", "Nv.");
            int mobLevel = mobLvl != null && mobLvl.lvl > 0 ? mobLvl.lvl : ModMobLevel(health);
            string lvlText = (showLevel && mobLevel > 0) ? $" [{lvlPrefix} {mobLevel}]" : "";

            float curHp = health.GetHealth();
            float maxHp = health.GetMaxHealth() > 0 ? health.GetMaxHealth() : curHp;
            float hpRatio = Mathf.Clamp01(curHp / maxHp);

            bool isCompact = (LensTheme.Value == "CompactBar");
            float boxWidth = GetLensCardWidth(isCompact);
            float boxHeight = isCompact ? 44f : 68f;

            Rect hudRect = GetLensScreenRect(boxWidth, boxHeight);
            DrawLensCardBackground(hudRect, LensTheme.Value, LensBgOpacity.Value);

            float textX = hudRect.x + 14f;
            float textW = boxWidth - 28f;

            DrawShadowLabel(new Rect(textX, hudRect.y + 4, textW, 24),
                $"{name}{lvlText}", TitleStyle, new Color(0.92f, 0.96f, 1.0f), Color.black);

            string healthStyle = LensMobHealthStyle != null ? LensMobHealthStyle.Value : "Hearts";
            float statusY = 30f;

            if (string.Equals(healthStyle, "Hearts", StringComparison.OrdinalIgnoreCase))
            {
                float animatedRatio = GetAnimatedHealthRatio(health, hpRatio);
                int fullHearts = Mathf.Clamp(Mathf.RoundToInt(animatedRatio * 10f), 0, 10);
                float heartSize = isCompact ? 10f : 12f;
                float heartSpacing = heartSize + 2f;
                float heartY = isCompact ? hudRect.y + 24f : hudRect.y + 28f;

                for (int i = 0; i < 10; i++)
                {
                    Rect heartRect = new Rect(textX + i * heartSpacing, heartY, heartSize, heartSize);
                    if (!DrawNativeHeart(heartRect, i < fullHearts))
                        GUI.DrawTexture(heartRect, GetHeartTexture(i < fullHearts));
                }

                statusY = isCompact ? 36f : 46f;
            }
            else if (string.Equals(healthStyle, "Bar", StringComparison.OrdinalIgnoreCase))
            {
                float animatedRatio = GetAnimatedHealthRatio(health, hpRatio);
                float barY = isCompact ? hudRect.y + 26f : hudRect.y + 28f;
                float barHeight = isCompact ? 12f : 16f;
                Rect barBg = new Rect(textX, barY, textW, barHeight);
                GUI.DrawTexture(barBg, GetColorTexture(new Color(0.05f, 0.18f, 0.08f, 0.96f)));
                GUI.DrawTexture(new Rect(barBg.x, barBg.y, barBg.width * animatedRatio, barBg.height),
                    GetColorTexture(new Color(0.20f, 0.85f, 0.25f, 1f)));
                DrawShadowLabel(barBg, $"{curHp:F0} / {maxHp:F0} ({(hpRatio * 100f):F0}%)",
                    SmallStyle, Color.white, Color.black);
                statusY = isCompact ? 38f : 46f;
            }
            else
            {
                statusY = 30f;
            }

            bool showQuests = LensShowMobQuests == null || LensShowMobQuests.Value;
            DrawShadowLabel(new Rect(textX, hudRect.y + statusY, 145f, 16),
                NanoOwners.OfEntity(RawMobName(health)) ?? "Block Story",
                AuthorStyle, new Color(0.45f, 0.65f, 1.0f), Color.black);

            if (showQuests)
            {
                bool isQuestNpc = TryGetMobQuestState(health, out bool questNpc, out bool hasAvailableQuest) && questNpc;
                string questText = isQuestNpc
                    ? (hasAvailableQuest ? Tr("Quest available", "Misión disponible") : Tr("No quest available", "Sin misión disponible"))
                    : Tr("No quests", "Sin misiones");
                Color questColor = isQuestNpc && hasAvailableQuest
                    ? new Color(0.45f, 1.0f, 0.45f)
                    : new Color(0.65f, 0.68f, 0.72f);

                DrawShadowLabel(new Rect(textX + 150f, hudRect.y + statusY, textW - 150f, 16),
                    questText, AuthorStyle, questColor, Color.black);
            }
        }

        private void DrawBlockHUD(Vector3i pos, Block block, Digger digger)
        {
            BlockType bType = block.type;
            string rawName = bType != null ? bType.name : "Block";
            InvBaseItem baseItem = ResolveBlockItem(rawName);
            string locName = GetLocalizedBlockName(rawName, baseItem);
            HarvestTool bestTool = GetBestHarvestTool(bType);
            string toolNeeded = GetHarvestToolName(bestTool);
            InvBaseItem toolIcon = FindToolIcon(bestTool);
            string originName = GetBlockOriginName(bType, rawName);
            bool showToolCheck = bestTool != HarvestTool.None && bestTool != HarvestTool.Hand;
            bool usingBestTool = IsUsingBestTool(digger, bestTool);

            bool isCompact = (LensTheme.Value == "CompactBar");
            float boxWidth = GetLensCardWidth(isCompact);
            float boxHeight = isCompact ? 44f : 68f;

            Rect hudRect = GetLensScreenRect(boxWidth, boxHeight);
            DrawLensCardBackground(hudRect, LensTheme.Value, LensBgOpacity.Value);

            float iconSize = isCompact ? 34f : 56f;
            Rect iconSlot = new Rect(hudRect.x + 8, hudRect.y + (boxHeight - iconSize) * 0.5f, iconSize, iconSize);

            bool drew3DCube = false;
            bool canRenderCube = bType != null && bType.shape == BlockType.Shape.Cube && bType.visible && !bType.liquid;
            if (Lens3DBlockIcon.Value && canRenderCube)
            {
                Texture2D isoCube = IsometricBlockIconRenderer.GetOrCreateBlockCubeTexture(block, bType, baseItem);
                if (isoCube != null)
                {
                    GUI.DrawTexture(new Rect(iconSlot.x + 2, iconSlot.y + 2, iconSize - 4, iconSize - 4), isoCube);
                    drew3DCube = true;
                }
            }

            if (!drew3DCube)
            {
                if (!DrawItemIcon(new Rect(iconSlot.x + 2, iconSlot.y + 2, iconSize - 4, iconSize - 4), baseItem))
                {
                    Color32 blockColor = NanoPalette.GetBlockColor(block.id);
                    GUI.DrawTexture(new Rect(iconSlot.x + 4, iconSlot.y + 4, iconSize - 8, iconSize - 8), GetColorTexture(blockColor));
                }
            }

            float textX = hudRect.x + iconSize + 16f;

            float toolSize = isCompact ? 26f : 48f;
            Rect toolRect = new Rect(
                hudRect.x + hudRect.width - toolSize - (isCompact ? 8f : 12f),
                hudRect.y + (boxHeight - toolSize) * 0.5f,
                toolSize,
                toolSize);
            float textW = Mathf.Max(90f, toolRect.x - textX - 8f);

            if (toolIcon != null)
                DrawItemIcon(toolRect, toolIcon, Color.white);

            if (showToolCheck)
            {
                float badgeSize = isCompact ? 14f : 20f;
                Rect badgeRect = new Rect(
                    toolRect.xMax - badgeSize * 0.62f,
                    toolRect.yMax - badgeSize * 0.62f,
                    badgeSize,
                    badgeSize);
                Texture2D status = GetToolStatusTexture(usingBestTool);
                GUI.DrawTexture(badgeRect, status);
            }

            if (isCompact)
            {
                string info = LensShowCoords.Value
                    ? $"{locName} • {toolNeeded} • {originName} • ({pos.X},{pos.Y},{pos.Z})"
                    : $"{locName} • {toolNeeded} • {originName}";
                DrawShadowLabel(new Rect(textX, hudRect.y + 10, textW, 20), info, ToolStyle, new Color(0.85f, 0.95f, 1.0f), Color.black);
            }
            else
            {
                DrawShadowLabel(new Rect(textX, hudRect.y + 5, textW, 24), locName, TitleStyle, new Color(0.88f, 0.96f, 1.0f), Color.black);

                string toolLabel = $"{Tr("Tool", "Herramienta")}: {toolNeeded}";
                if (LensShowCoords.Value)
                    toolLabel += $"  ({pos.X}, {pos.Y}, {pos.Z})";

                Color toolColor = showToolCheck
                    ? (usingBestTool ? new Color(0.55f, 1.0f, 0.55f) : new Color(1.0f, 0.48f, 0.42f))
                    : new Color(1.0f, 0.84f, 0.35f);
                DrawShadowLabel(new Rect(textX, hudRect.y + 28, textW, 18), toolLabel, ToolStyle, toolColor, Color.black);
                DrawShadowLabel(new Rect(textX, hudRect.y + 48, boxWidth - iconSize - 24f, 16), originName, AuthorStyle, new Color(0.45f, 0.65f, 1.0f), Color.black);
            }
        }

        private enum HarvestTool
        {
            None,
            Hand,
            Pickaxe,
            Axe,
            Shovel,
            Hammer
        }

        /// <summary>
        /// The game's break rate is <c>block.type.*strength * diggerPower</c> and the break
        /// percentage is elapsed / breakrate, so the LOWEST strength is the FASTEST tool.
        /// This mirrors Digger.DiggerType exactly instead of guessing from the block name.
        /// </summary>
        private HarvestTool GetBestHarvestTool(BlockType bType)
        {
            if (bType == null) return HarvestTool.Hand;
            if (!bType.diggable || bType.air || bType.liquid) return HarvestTool.None;

            float pick = bType.strength;
            float shovel = bType.shovelstrength;
            float axe = bType.axestrength;
            float hammer = bType.hammerstrength;
            float best = Mathf.Min(Mathf.Min(pick, shovel), Mathf.Min(axe, hammer));

            if (best <= 0.0001f) return HarvestTool.Hand;

            // Tie order matches the hand fallback (shovelstrength) and the common tool choice.
            if (shovel <= best + 0.0001f) return HarvestTool.Shovel;
            if (pick <= best + 0.0001f) return HarvestTool.Pickaxe;
            if (axe <= best + 0.0001f) return HarvestTool.Axe;
            return HarvestTool.Hammer;
        }

        private string GetHarvestToolName(HarvestTool tool)
        {
            switch (tool)
            {
                case HarvestTool.Pickaxe: return Tr("Pickaxe", "Pico");
                case HarvestTool.Axe: return Tr("Axe", "Hacha");
                case HarvestTool.Shovel: return Tr("Shovel", "Pala");
                case HarvestTool.Hammer: return Tr("Hammer", "Martillo");
                case HarvestTool.None: return Tr("Unbreakable", "Irrompible");
                default: return Tr("Hand", "Mano");
            }
        }

        private static bool IsModBlock(BlockType bType)
        {
            if (bType == null) return false;

            return NanoOwners.OfBlock(bType.id) != null || bType.id >= CustomBlockIdStart;
        }

        private static readonly Dictionary<string, InvBaseItem> toolIconCache =
            new Dictionary<string, InvBaseItem>(StringComparer.OrdinalIgnoreCase);

        private InvBaseItem FindToolIcon(HarvestTool tool)
        {
            string toolName;
            switch (tool)
            {
                case HarvestTool.Pickaxe: toolName = "Pickaxe"; break;
                case HarvestTool.Axe: toolName = "Axe"; break;
                case HarvestTool.Shovel: toolName = "Shovel"; break;
                case HarvestTool.Hammer: toolName = "Hammer"; break;
                default: return null;
            }

            if (toolIconCache.TryGetValue(toolName, out InvBaseItem cached)) return cached;

            InvBaseItem exact = SafeFindItemByName(toolName);
            if (exact != null)
            {
                toolIconCache[toolName] = exact;
                return exact;
            }

            try
            {
                InvDatabase[] databases = InvDatabase.list;
                if (databases != null)
                {
                    for (int d = 0; d < databases.Length; d++)
                    {
                        InvDatabase db = databases[d];
                        if (db == null || db.items == null) continue;
                        for (int i = 0; i < db.items.Count; i++)
                        {
                            InvBaseItem item = db.items[i];
                            if (item == null || item.iconAtlas == null || string.IsNullOrEmpty(item.name)) continue;
                            if (item.name.IndexOf(toolName, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                toolIconCache[toolName] = item;
                                return item;
                            }
                        }
                    }
                }
            }
            catch { }

            toolIconCache[toolName] = null;
            return null;
        }

    }
}
