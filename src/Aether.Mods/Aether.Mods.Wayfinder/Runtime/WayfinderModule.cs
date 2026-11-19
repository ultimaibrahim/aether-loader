#pragma warning disable CS0618
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Aether.Core.Mapping;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using Blocksters.MathLib;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using Blocksters.Terrain.renderer;
using Aether.Nano;
using static Aether.Nano.NanoUi;

namespace Aether.Mods.Wayfinder
{
    public sealed partial class WayfinderModule
    {
        // =====================================================================
        //        WAYFINDER CONFIGURATION ENTRIES
        // =====================================================================
        public static ConfigEntry<bool> MinimapEnabled { get; private set; }
        public static ConfigEntry<KeyCode> MinimapToggleKey { get; private set; }
        public static ConfigEntry<KeyCode> WayfinderKey { get; private set; }
        public static ConfigEntry<bool> FullscreenMapKeyMigrated { get; private set; }
        public static ConfigEntry<float> MinimapRadarRange { get; private set; }
        public static ConfigEntry<bool> MinimapShowCoords { get; private set; }
        public static ConfigEntry<string> MinimapShape { get; private set; }
        public static ConfigEntry<string> MinimapAnchor { get; private set; }
        public static ConfigEntry<float> MinimapOffsetX { get; private set; }
        public static ConfigEntry<float> MinimapOffsetY { get; private set; }
        public static ConfigEntry<string> MinimapTheme { get; private set; }
        public static ConfigEntry<string> MapLayerMode { get; private set; }
        public static ConfigEntry<string> MapRenderMode { get; private set; }
        public static ConfigEntry<bool> ShowChunkGrid { get; private set; }
        public static ConfigEntry<string> GridStyle { get; private set; }
        public static ConfigEntry<bool> ShowMapInfo { get; private set; }
        public static ConfigEntry<bool> Deathpoints { get; private set; }
        public static ConfigEntry<int> DeathpointLimit { get; private set; }
        public static ConfigEntry<int> MapSliceY { get; private set; }
        public static ConfigEntry<bool> ShowWorldWaypoints { get; private set; }
        public static ConfigEntry<string> SavedWaypointsConfig { get; private set; }
        public static ConfigEntry<float> MinimapSize { get; set; }
        public static ConfigEntry<float> MinimapScale { get; set; }
        public static ConfigEntry<float> FullscreenMapScale { get; private set; }
        public static ConfigEntry<float> MinimapRefreshSeconds { get; private set; }
        public static ConfigEntry<float> FullscreenRefreshSeconds { get; private set; }
        public static ConfigEntry<float> EntityVerticalRange { get; private set; }
        public static ConfigEntry<bool> ShowMobsOnMinimap { get; private set; }
        public static ConfigEntry<bool> ShowWaypointLabelsOnMinimap { get; private set; }
        public static ConfigEntry<bool> SaveExploredMap { get; private set; }
        public static ConfigEntry<bool> WaypointsMigrated { get; private set; }
        public static ConfigEntry<bool> ShowNativeRadarPois { get; private set; }
        public static ConfigEntry<bool> ShowSurfaceAboveCaves { get; private set; }
        public static ConfigEntry<bool> AlwaysMapSurface { get; private set; }

        /// <summary>Per-world explored-surface store; null when persistence is off or no world is loaded.</summary>
        public static WayfinderStore SurfaceMapStore { get; private set; }

        /// <summary>Per-world/per-band cave/slice store; null when persistence is off or no world is loaded.</summary>
        public static WayfinderCaveStore CaveMapStore { get; private set; }

        // =====================================================================
        //        WAYFINDER & WAYPOINTS STATE
        // =====================================================================
        public bool isWayfinderOpen { get; set; } = false;

        private string journeyMapWorldDirectory;
        private float lastWayfinderWorldCheck;
        private float lastWayfinderFlush;
        private static readonly FieldInfo fsWorldDirectoryField =
            typeof(FSWorldPersistence).GetField("directory", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // Fullscreen Map Interactive Pan & Zoom State
        private float fullscreenZoom = 1.0f; // WayfinderMath.MinZoom .. MaxZoom
        private float mapPanBlocksX = 0f;
        private float mapPanBlocksZ = 0f;
        private bool isDraggingMap = false;
        private Vector2 mapDragStartMouse;
        private float mapDragStartPanX;
        private float mapDragStartPanZ;
        private int currentSliceY = 64;

        // Waypoints State
        public class WaypointEntry
        {
            public string Name;
            public int X;
            public int Y;
            public int Z;
            public int ColorIndex;
            public bool Enabled;
            /// <summary>Auto-created on death; the file's optional 7th field.</summary>
            public bool IsDeath;
        }

        private readonly List<WaypointEntry> waypoints = new List<WaypointEntry>();
        private string newWpName = "Waypoint 1";
        private string newWpX = "0";
        private string newWpY = "64";
        private string newWpZ = "0";
        private int newWpColorIndex = 0;
        private Vector2 wpScrollPos;
        private WaypointEntry editingWaypoint;
        private bool editingWaypointIsNew;
        private bool waypointSortByDistance = true;

        private struct RadarPoi
        {
            public Vector3 Position;
            public RadarPoint.Icon Icon;
            public string Label;
        }

        private static readonly FieldInfo radarPointsField =
            typeof(Radar).GetField("points", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        private readonly List<RadarPoi> radarPois = new List<RadarPoi>(32);
        private float lastRadarPoiScanTime = float.MinValue;

        private static readonly Color[] WaypointColors = new Color[]
        {
            new Color(0.25f, 0.95f, 0.40f), // Emerald Green
            new Color(0.25f, 0.85f, 1.00f), // Sky Cyan
            new Color(1.00f, 0.82f, 0.20f), // Gold
            new Color(1.00f, 0.30f, 0.30f), // Crimson Red
            new Color(0.80f, 0.40f, 1.00f), // Amethyst Purple
            new Color(1.00f, 0.55f, 0.15f)  // Sunset Orange
        };

        private static Texture2D playerChevronTex;
        private static Texture2D circleBezelTex;
        private static Texture2D circleMinimapTex;

        // Static so the fullscreen toolbar does not allocate arrays on every OnGUI event.
        private static readonly string[] MapLayerModeIds = { "Auto", "Surface", "Cave", "Interior", "Slice" };
        private static readonly string[] MapRenderModeIds = { "Day", "Night", "Topo", "Biome" };

        private static readonly NanoSegmentOption<string>[] LayerSegmentsEn =
        {
            new NanoSegmentOption<string>("Auto", "Auto"),
            new NanoSegmentOption<string>("Surface", "Surface"),
            new NanoSegmentOption<string>("Cave", "Cave"),
            new NanoSegmentOption<string>("Interior", "Interior"),
            new NanoSegmentOption<string>("Slice", "Slice")
        };
        private static readonly NanoSegmentOption<string>[] LayerSegmentsEs =
        {
            new NanoSegmentOption<string>("Auto", "Auto"),
            new NanoSegmentOption<string>("Surface", "Superf."),
            new NanoSegmentOption<string>("Cave", "Cueva"),
            new NanoSegmentOption<string>("Interior", "Interior"),
            new NanoSegmentOption<string>("Slice", "Corte")
        };

        private static readonly NanoSegmentOption<string>[] RenderSegmentsEn =
        {
            new NanoSegmentOption<string>("Day", "Day"),
            new NanoSegmentOption<string>("Night", "Night"),
            new NanoSegmentOption<string>("Topo", "Topo"),
            new NanoSegmentOption<string>("Biome", "Biome")
        };
        private static readonly NanoSegmentOption<string>[] RenderSegmentsEs =
        {
            new NanoSegmentOption<string>("Day", "Día"),
            new NanoSegmentOption<string>("Night", "Noche"),
            new NanoSegmentOption<string>("Topo", "Topo"),
            new NanoSegmentOption<string>("Biome", "Bioma")
        };

        // Cached Aether Ink craft styles (zero per-frame allocations)
        private static GUIStyle _wfTitleStyle, _wfMetaStyle, _wfMonoStyle, _wfMonoSmallStyle, _wfMonoCenterStyle;
        private static GUIStyle _wfRowTitleStyle, _wfRowSubStyle, _wfLabelStyle, _wfChipStyle, _wfCardinalStyle, _wfInputStyle;
        private static readonly GUIContent _wfMeasure = new GUIContent();

        private static void EnsureWayfinderCraftStyles()
        {
            if (_wfTitleStyle != null) return;
            _wfTitleStyle = NanoBrand.Style(NanoBrand.Display, 21, NanoBrand.Paper);
            _wfMetaStyle = NanoBrand.Style(NanoBrand.Mono, 11, NanoBrand.Mute);
            _wfMonoStyle = NanoBrand.Style(NanoBrand.Mono, 11, NanoBrand.Paper);
            _wfMonoSmallStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Paper);
            _wfMonoCenterStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Paper, TextAnchor.MiddleCenter);
            _wfRowTitleStyle = NanoBrand.Style(NanoBrand.BodySemi, 12, NanoBrand.Paper);
            _wfRowSubStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Mute);
            _wfLabelStyle = NanoBrand.Style(NanoBrand.BodySemi, 11, NanoBrand.Mute);
            _wfChipStyle = NanoBrand.Style(NanoBrand.Mono, 9, NanoBrand.PaperLight, TextAnchor.MiddleCenter);
            _wfCardinalStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.PaperLight, TextAnchor.MiddleCenter);
            _wfInputStyle = new GUIStyle(GUI.skin.textField)
            {
                font = NanoBrand.Mono,
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(7, 7, 2, 2)
            };
            _wfInputStyle.normal.textColor = NanoBrand.PaperLight;
            _wfInputStyle.focused.textColor = NanoBrand.PaperLight;
            _wfInputStyle.normal.background = Texture2D.blackTexture;
            _wfInputStyle.focused.background = Texture2D.blackTexture;
            _wfInputStyle.hover.background = Texture2D.blackTexture;
            // Transparent skin background so our debossed squircle slot shows cleanly behind the text
            Texture2D clear = GetColorTexture(new Color(0f, 0f, 0f, 0f));
            _wfInputStyle.normal.background = clear;
            _wfInputStyle.focused.background = clear;
            _wfInputStyle.hover.background = clear;
        }

        private static void DrawWayfinderSurface(Rect rect, string theme, float opacity = 0.96f, float radius = 10f)
        {
            NanoTheme pal = NanoThemes.Get(string.IsNullOrEmpty(theme) ? "Aether" : theme);
            Color bot = new Color(pal.InnerBgColor.r, pal.InnerBgColor.g, pal.InnerBgColor.b, Mathf.Clamp01(pal.InnerBgColor.a * opacity));
            Color top = Color.Lerp(bot, new Color(0.14f, 0.11f, 0.085f, bot.a), 0.35f);
            Color spec = new Color(0.98f, 0.93f, 0.85f, 0.11f);
            NanoSurface.DrawCardSurface(rect, top, bot, pal.BorderColor, pal.AccentColor, spec, radius, 1f, 0f, 0f);
        }

        private static string DrawWayfinderTextField(Rect rect, string value, int maxLength)
        {
            if (Event.current.type == EventType.Repaint)
            {
                NanoSurface.DrawDebossedSlot(rect, new Color(0.036f, 0.028f, 0.022f, 0.96f), NanoBrand.Line, 6f);
            }
            return GUI.TextField(rect, value ?? string.Empty, maxLength, _wfInputStyle);
        }

        // =====================================================================
        //        INITIALIZATION & LIFECYCLE
        // =====================================================================
        private void InitializeWayfinder()
        {
            MinimapEnabled = Config.Bind("Wayfinder", "Enabled", true, "Enable terrain minimap in top-right corner.");
            MinimapToggleKey = Config.Bind("Wayfinder", "ToggleKey", KeyCode.N, "Hotkey to toggle corner minimap.");
            WayfinderKey = Config.Bind("Wayfinder", "FullscreenMapKey", KeyCode.M, "Hotkey to open expandable fullscreen Wayfinder & Waypoints.");
            FullscreenMapKeyMigrated = Config.Bind("Wayfinder", "FullscreenMapKeyMigrated", false,
                "Internal: set once when the old conflicting J default was migrated to M.");
            if (!FullscreenMapKeyMigrated.Value && WayfinderKey.Value == KeyCode.J)
            {
                // Game native "Toggle Minimap" is J in the saved prefs; opening the Aether map on J
                // raced it and showed the native pause/minimap. Move the old default to M once.
                WayfinderKey.Value = KeyCode.M;
                FullscreenMapKeyMigrated.Value = true;
                Config.Save();
            }
            MinimapRadarRange = Config.Bind("Wayfinder", "RadarRange", 48f, new ConfigDescription("Entity & terrain scan radius.", new AcceptableValueRange<float>(24f, 96f)));
            MinimapShowCoords = Config.Bind("Wayfinder", "ShowCoords", true, "Show player coordinates below minimap.");
            MinimapShape = Config.Bind("Wayfinder", "Shape", "Square", "Minimap frame shape: Square or Circle.");
            MinimapAnchor = Config.Bind("Wayfinder", "Anchor", "TopRight", "Corner minimap anchor: TopRight, TopLeft, BottomRight, BottomLeft.");
            MinimapOffsetX = Config.Bind("Wayfinder", "OffsetX", 0f, new ConfigDescription("Horizontal pixel offset.", new AcceptableValueRange<float>(-400f, 400f)));
            MinimapOffsetY = Config.Bind("Wayfinder", "OffsetY", 0f, new ConfigDescription("Vertical pixel offset.", new AcceptableValueRange<float>(-400f, 400f)));
            MinimapTheme = Config.Bind("Wayfinder", "Theme", "Aether", "Minimap theme: Aether, NativeWood, DarkObsidian, MinimalistGlass, RoyalGold.");
            var wfThemeMigrated = Config.Bind("Wayfinder", "AetherInkThemeMigrated", false,
                "Internal: true once the pre-1.2.4 NativeWood default was migrated to Aether.");
            if (!wfThemeMigrated.Value)
            {
                if (string.Equals(MinimapTheme.Value, "NativeWood", StringComparison.OrdinalIgnoreCase))
                    MinimapTheme.Value = "Aether";
                wfThemeMigrated.Value = true;
                Config.Save();
            }
            MapLayerMode = Config.Bind("Wayfinder", "LayerMode", "Auto",
                "Terrain layer mode: Auto, Surface, Cave, Interior, Slice. Interior forces the current-Y slice with the explored surface dimmed (for icebergs, rooms and other structures).");
            MapRenderMode = Config.Bind("Wayfinder", "RenderMode", "Day", "Map render mode: Day, Night, Topo, Biome.");
            ShowChunkGrid = Config.Bind("Wayfinder", "ShowChunkGrid", false, "Legacy alias: true = Chunks grid. Use GridStyle.");
            GridStyle = Config.Bind("Wayfinder", "GridStyle", "Off",
                new ConfigDescription("Map grid style: Off, Chunks, Regions, Both.",
                    new AcceptableValueList<string>("Off", "Chunks", "Regions", "Both")));
            ShowMapInfo = Config.Bind("Wayfinder", "ShowMapInfo", true,
                "Show FPS / in-game time / light on the fullscreen map and minimap.");
            Deathpoints = Config.Bind("Wayfinder", "Deathpoints", true,
                "Create a red waypoint where the player dies.");
            DeathpointLimit = Config.Bind("Wayfinder", "DeathpointLimit", 3,
                new ConfigDescription("Maximum death waypoints to keep (oldest removed first).",
                    new AcceptableValueRange<int>(1, 10)));
            // Migrate the old boolean once: a previously enabled chunk grid becomes Chunks.
            if (string.Equals(GridStyle.Value, "Off", StringComparison.OrdinalIgnoreCase) && ShowChunkGrid.Value)
                GridStyle.Value = "Chunks";
            MapSliceY = Config.Bind("Wayfinder", "SliceY", 64,
                new ConfigDescription("Current Y slice height for underground inspection.",
                    new AcceptableValueRange<int>(WayfinderMath.MinSliceY, WayfinderMath.MaxSliceY)));
            ShowWorldWaypoints = Config.Bind("Wayfinder", "ShowWorldWaypoints", true, "Render floating 3D waypoint markers in the world view.");
            EnsureBeaconConfig();
            SavedWaypointsConfig = Config.Bind("Wayfinder", "WaypointsData", "", "Serialized waypoints list (Name|X|Y|Z|ColorIdx|Enabled;...).");

            MinimapSize = Config.Bind("Wayfinder", "MinimapSize", 180f, new ConfigDescription("Corner minimap display size in pixels.", new AcceptableValueRange<float>(110f, 340f)));
            MinimapScale = Config.Bind("Wayfinder", "MinimapScale", 1.0f, new ConfigDescription("Corner minimap scale multiplier.", new AcceptableValueRange<float>(0.5f, 2.0f)));
            FullscreenMapScale = Config.Bind("Wayfinder", "FullscreenMapScale", 0.90f, new ConfigDescription("Fullscreen map scale factor relative to screen.", new AcceptableValueRange<float>(0.65f, 1.0f)));
            MinimapRefreshSeconds = Config.Bind("Wayfinder", "MinimapRefreshSeconds", 0.50f,
                new ConfigDescription("Minimum seconds between minimap terrain updates while moving.",
                    new AcceptableValueRange<float>(0.25f, 2.0f)));
            FullscreenRefreshSeconds = Config.Bind("Wayfinder", "FullscreenRefreshSeconds", 0.40f,
                new ConfigDescription("Minimum seconds between fullscreen terrain updates while moving.",
                    new AcceptableValueRange<float>(0.20f, 2.0f)));
            EntityVerticalRange = Config.Bind("Wayfinder", "EntityVerticalRange", 20f,
                new ConfigDescription("Hide radar dots farther than this vertical distance (native radar uses 20).",
                    new AcceptableValueRange<float>(8f, 256f)));
            ShowMobsOnMinimap = Config.Bind("Wayfinder", "ShowMobsOnMinimap", true, "Draw hostile/passive mob dots on the minimap.");
            ShowWaypointLabelsOnMinimap = Config.Bind("Wayfinder", "ShowWaypointLabelsOnMinimap", true, "Label visible waypoints on the minimap.");
            SaveExploredMap = Config.Bind("Wayfinder", "SaveExploredMap", true,
                "Persist explored surface cells per world under <world>/aether_wayfinder/.");
            WaypointsMigrated = Config.Bind("Wayfinder", "WaypointsMigrated", false,
                "Internal: set once old global [Wayfinder] WaypointsData has been imported into the first world.");
            ShowNativeRadarPois = Config.Bind("Wayfinder", "ShowNativeRadarPois", true,
                "Show the native radar's beds, vehicles, teleporters, NPCs and spawn point as POI markers.");
            ShowSurfaceAboveCaves = Config.Bind("Wayfinder", "ShowSurfaceAboveCaves", true,
                "While underground, dim the persisted surface map instead of leaving unexplored chunks black.");
            AlwaysMapSurface = Config.Bind("Wayfinder", "AlwaysMapSurface", false,
                "While in Cave/Slice, periodically sample the surface above you. Off by default: " +
                "revealing the surface of terrain you have not visited is an x-ray.");

            currentSliceY = WayfinderMath.ClampSliceY(MapSliceY.Value);
            LoadWaypoints();
        }

        /// <summary>
        /// Resolves the native world folder. FSWorldPersistence owns the canonical directory;
        /// reflecting it handles realms and renamed worlds better than rebuilding the path.
        /// </summary>
        private string ResolveWayfinderWorldDirectory()
        {
            try
            {
                if (MultiplayerSessionSettings.IsMultiplayer) return null;

                IWorldPersistence persistence = TerrainLoader.instance != null ? TerrainLoader.instance.persistence : null;
                if (persistence is FSWorldPersistence fs && fsWorldDirectoryField != null)
                {
                    DirectoryInfo dir = fsWorldDirectoryField.GetValue(fs) as DirectoryInfo;
                    if (dir != null && dir.Exists) return dir.FullName;
                }
            }
            catch { }

            string name = WorldManager.loadedWorldName;
            if (string.IsNullOrEmpty(name)) return null;
            return Path.Combine(Application.persistentDataPath, "worlds", name);
        }

        /// <summary>
        /// Called from Update and both map draw paths. Switching worlds flushes the previous
        /// store and reloads that world's waypoint list; the tile cache is world-specific too.
        /// </summary>
        private void EnsureWayfinderWorld()
        {
            if (Time.unscaledTime - lastWayfinderWorldCheck < 0.5f) return;
            lastWayfinderWorldCheck = Time.unscaledTime;

            if (TerrainLoader.instance == null || TerrainLoader.instance.world == null) return;

            string dir = ResolveWayfinderWorldDirectory();
            if (string.IsNullOrEmpty(dir)) return;

            if (dir == journeyMapWorldDirectory)
            {
                // The F10 toggle can enable/disable persistence without a world reload.
                bool wantStore = SaveExploredMap == null || SaveExploredMap.Value;
                if (wantStore && SurfaceMapStore == null)
                    OpenSurfaceStore(dir);
                else if (!wantStore && SurfaceMapStore != null)
                {
                    try { SurfaceMapStore.FlushAll(); } catch { }
                    SurfaceMapStore = null;
                }

                if (wantStore && CaveMapStore == null)
                    OpenCaveStore(dir);
                else if (!wantStore && CaveMapStore != null)
                {
                    try { CaveMapStore.FlushAll(); } catch { }
                    CaveMapStore = null;
                }
                return;
            }

            FlushWayfinderStorage();

            journeyMapWorldDirectory = dir;
            WayfinderTileCache.Invalidate();
            WayfinderChunkAtlas.Shared.Clear();
            WayfinderChunkAtlas.Shared.ClearExploration();
            lastWayfinderFlush = Time.unscaledTime;

            if (SaveExploredMap == null || SaveExploredMap.Value)
            {
                OpenSurfaceStore(dir);
                OpenCaveStore(dir);
            }
            else
            {
                SurfaceMapStore = null;
                CaveMapStore = null;
            }

            LoadWaypoints();
            Log.LogInfo($"[Wayfinder] World map ready: '{dir}' ({waypoints.Count} waypoints, persistence={(SurfaceMapStore != null ? "on" : "off")}).");
        }

        private void OpenSurfaceStore(string worldDirectory)
        {
            try
            {
                SurfaceMapStore = new WayfinderStore(
                    Aether.Core.Config.LegacyNames.ResolveWorldDataFolder(worldDirectory),
                    message => Log.LogWarning("[Wayfinder] " + message));
            }
            catch (Exception ex)
            {
                SurfaceMapStore = null;
                Log.LogWarning("[Wayfinder] Could not open explored-map store: " + ex.Message);
            }
        }

        private void OpenCaveStore(string worldDirectory)
        {
            try
            {
                CaveMapStore = new WayfinderCaveStore(
                    Aether.Core.Config.LegacyNames.ResolveWorldDataFolder(worldDirectory),
                    message => Log.LogWarning("[Wayfinder] " + message));
                Log.LogInfo($"[Wayfinder] Cave map ready: {CaveMapStore.KnownRegionCount} regions, " +
                            $"{CaveMapStore.KnownCellCount:N0} cells.");

                // The store may open after the minimap has already filled its tile cache. Cached
                // pixels have no live block id and cannot be persisted; force one live re-render
                // so the current area is recorded instead of coming back black after a reload.
                WayfinderTileCache.Invalidate();
                WayfinderChunkAtlas.Shared.Clear();
                WayfinderChunkAtlas.Shared.ClearCaveStoreProbes();
            }
            catch (Exception ex)
            {
                CaveMapStore = null;
                Log.LogWarning("[Wayfinder] Could not open cave-map store: " + ex.Message);
            }
        }

        internal void UpdateWayfinderStorage()
        {
            EnsureWayfinderWorld();

            if (SurfaceMapStore == null && CaveMapStore == null) return;
            if (Time.unscaledTime - lastWayfinderFlush < 5f) return;
            lastWayfinderFlush = Time.unscaledTime;

            try { SurfaceMapStore?.Flush(2); }
            catch (Exception ex) { Log.LogWarning("[Wayfinder] Map flush failed: " + ex.Message); }
            try { CaveMapStore?.Flush(2); }
            catch (Exception ex) { Log.LogWarning("[Wayfinder] Cave map flush failed: " + ex.Message); }
        }

        internal void FlushWayfinderStorage()
        {
            try { SurfaceMapStore?.FlushAll(); }
            catch (Exception ex) { Log.LogWarning("[Wayfinder] Map save failed: " + ex.Message); }
            try { CaveMapStore?.FlushAll(); }
            catch (Exception ex) { Log.LogWarning("[Wayfinder] Cave map save failed: " + ex.Message); }

            try
            {
                if (!string.IsNullOrEmpty(journeyMapWorldDirectory))
                    SaveWaypoints();
            }
            catch (Exception ex) { Log.LogWarning("[Wayfinder] Waypoint save failed: " + ex.Message); }
        }

        public void SetWayfinderOpen(bool open)
        {
            if (isWayfinderOpen == open) return;
            if (open && IsModMenuOpen)
                CloseModMenu();
            if (open && IsHudDragOpen)
                CloseHudDrag();
            if (open && IsKeybindManagerOpen)
                CloseKeybinds();

            isWayfinderOpen = open;
            if (open)
            {
                Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
                if (cam != null)
                {
                    Vector3 p = cam.transform.position;
                    newWpX = Mathf.RoundToInt(p.x).ToString();
                    newWpY = GetStandingY(GetActiveWorld(), p).ToString();
                    newWpZ = Mathf.RoundToInt(p.z).ToString();
                    // Slice mode remembers the user's chosen altitude; Auto/Cave follow the player.
                    currentSliceY = MapLayerMode.Value == "Slice"
                        ? WayfinderMath.ClampSliceY(MapSliceY.Value)
                        : WayfinderMath.ClampSliceY(Mathf.RoundToInt(p.y));
                }
                mapPanBlocksX = 0f;
                mapPanBlocksZ = 0f;
                ModalOpenedTimestamp = Time.unscaledTime;
                AcquireModalIsolation();
            }
            else
            {
                ModalClosedTimestamp = Time.unscaledTime;
                isDraggingMap = false;
                ReleaseModalIsolation();
            }
        }

        /// <summary>
        /// Single entry point for slice altitude changes: clamps to the world height, persists
        /// the value and forces both maps to resample. The tile cache is keyed by sliceY, so it
        /// is intentionally not invalidated here.
        /// </summary>
        private void SetSliceY(int value)
        {
            int clamped = WayfinderMath.ClampSliceY(value);
            if (MapSliceY != null) MapSliceY.Value = clamped;
            if (currentSliceY == clamped) return;

            currentSliceY = clamped;
        }

        public Rect GetMinimapScreenRect()
        {
            float baseSize = MinimapSize != null ? MinimapSize.Value : 180f;
            float scale = MinimapScale != null ? MinimapScale.Value : 1.0f;
            float mapSize = Mathf.Clamp(baseSize * scale, 110f, 340f);
            float offsetX = MinimapOffsetX != null ? MinimapOffsetX.Value : 0f;
            float offsetY = MinimapOffsetY != null ? MinimapOffsetY.Value : 0f;
            bool showCoords = MinimapShowCoords != null && MinimapShowCoords.Value;
            string anchor = MinimapAnchor != null ? MinimapAnchor.Value : "TopRight";
            float mapX, mapY;
            switch (anchor)
            {
                case "TopLeft":
                    mapX = 16f;
                    mapY = (MoveDiamondsUnderXP ? 160f : 16f);
                    break;
                case "BottomRight":
                    mapX = Screen.width - mapSize - 16f;
                    mapY = Screen.height - mapSize - (showCoords ? 76f : 24f);
                    break;
                case "BottomLeft":
                    mapX = 16f;
                    mapY = Screen.height - mapSize - (showCoords ? 76f : 24f);
                    break;
                case "TopRight":
                default:
                    mapX = Screen.width - mapSize - 16f;
                    mapY = (MoveTopRightButtonsToBottomRight ? 14f : 76f);
                    break;
            }
            return new Rect(mapX + offsetX, mapY + offsetY, mapSize, mapSize);
        }

        // =====================================================================
        //        WAYPOINTS PERSISTENCE
        // =====================================================================
        private string WayfinderDataDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(journeyMapWorldDirectory)) return null;
                return Path.Combine(journeyMapWorldDirectory, "aether_wayfinder");
            }
        }

        private string WaypointsFilePath
        {
            get
            {
                string dataDir = WayfinderDataDirectory;
                return dataDir == null ? null : Path.Combine(dataDir, "waypoints.txt");
            }
        }

        /// <summary>
        /// Waypoints are world-scoped. The first world after upgrading imports the old global
        /// [Wayfinder] WaypointsData once; later worlds start with their own file.
        /// </summary>
        private void LoadWaypoints()
        {
            waypoints.Clear();
            string path = WaypointsFilePath;

            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                ReadWaypointFile(path);
                newWpName = $"Waypoint {waypoints.Count + 1}";
                return;
            }

            bool migrated = WaypointsMigrated != null && WaypointsMigrated.Value;
            if (!migrated)
            {
                ParseConfigWaypoints();
                if (waypoints.Count > 0 && !string.IsNullOrEmpty(path))
                {
                    SaveWaypoints();
                    if (SavedWaypointsConfig != null) SavedWaypointsConfig.Value = "";
                    if (WaypointsMigrated != null) WaypointsMigrated.Value = true;
                    Config.Save();
                    Log.LogInfo($"[Wayfinder] Imported {waypoints.Count} legacy waypoints into '{path}'.");
                }
            }
            newWpName = $"Waypoint {waypoints.Count + 1}";
        }

        private void ParseConfigWaypoints()
        {
            string raw = SavedWaypointsConfig != null ? SavedWaypointsConfig.Value : "";
            if (string.IsNullOrEmpty(raw)) return;

            string[] entries = raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                string[] parts = entries[i].Split('|');
                if (parts.Length < 6) continue;
                if (int.TryParse(parts[1], out int x) &&
                    int.TryParse(parts[2], out int y) &&
                    int.TryParse(parts[3], out int z) &&
                    int.TryParse(parts[4], out int col) &&
                    bool.TryParse(parts[5], out bool en))
                {
                    waypoints.Add(new WaypointEntry
                    {
                        Name = parts[0],
                        X = x,
                        Y = y,
                        Z = z,
                        ColorIndex = Mathf.Clamp(col, 0, WaypointColors.Length - 1),
                        Enabled = en,
                        IsDeath = parts.Length >= 7 && bool.TryParse(parts[6], out bool legacyDeath) && legacyDeath
                    });
                }
            }
        }

        private void ReadWaypointFile(string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] parts = line.Split('|');
                    if (parts.Length < 6) continue;
                    if (int.TryParse(parts[1], out int x) &&
                        int.TryParse(parts[2], out int y) &&
                        int.TryParse(parts[3], out int z) &&
                        int.TryParse(parts[4], out int col) &&
                        bool.TryParse(parts[5], out bool en))
                    {
                        waypoints.Add(new WaypointEntry
                        {
                            Name = string.IsNullOrWhiteSpace(parts[0]) ? "Waypoint" : parts[0].Trim(),
                            X = x,
                            Y = y,
                            Z = z,
                            ColorIndex = Mathf.Clamp(col, 0, WaypointColors.Length - 1),
                            Enabled = en,
                            IsDeath = parts.Length >= 7 && bool.TryParse(parts[6], out bool deathFlag) && deathFlag
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning("[Wayfinder] Could not read waypoints: " + ex.Message);
            }
        }

        private static string SafeWaypointName(string name)
        {
            string safe = (name ?? "Waypoint").Replace("|", " ").Replace("\r", " ").Replace("\n", " ").Trim();
            return safe.Length == 0 ? "Waypoint" : safe;
        }

        private void SaveWaypoints()
        {
            string path = WaypointsFilePath;
            if (string.IsNullOrEmpty(path))
            {
                // Before a world is ready the list only lives in memory; keep the legacy config
                // entry as a fallback so an early save cannot lose data.
                if (SavedWaypointsConfig != null)
                {
                    StringBuilder legacy = new StringBuilder();
                    for (int i = 0; i < waypoints.Count; i++)
                    {
                        var w = waypoints[i];
                        legacy.Append($"{SafeWaypointName(w.Name)}|{w.X}|{w.Y}|{w.Z}|{w.ColorIndex}|{w.Enabled}|{w.IsDeath};");
                    }
                    SavedWaypointsConfig.Value = legacy.ToString();
                    Config.Save();
                }
                return;
            }

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Aether Wayfinder waypoints v1");
                for (int i = 0; i < waypoints.Count; i++)
                {
                    var wp = waypoints[i];
                    sb.Append(SafeWaypointName(wp.Name)).Append('|')
                      .Append(wp.X).Append('|')
                      .Append(wp.Y).Append('|')
                      .Append(wp.Z).Append('|')
                      .Append(wp.ColorIndex).Append('|')
                      .Append(wp.Enabled).Append('|')
                      .Append(wp.IsDeath).AppendLine();
                }

                string temp = path + ".tmp";
                File.WriteAllText(temp, sb.ToString());
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
            }
            catch (Exception ex)
            {
                Log.LogWarning("[Wayfinder] Could not save waypoints: " + ex.Message);
            }
        }

        private void BeginEditWaypoint(WaypointEntry wp, bool isNew = false)
        {
            if (wp == null) return;
            editingWaypoint = wp;
            editingWaypointIsNew = isNew;
            newWpName = wp.Name ?? "Waypoint";
            newWpX = wp.X.ToString();
            newWpY = wp.Y.ToString();
            newWpZ = wp.Z.ToString();
            newWpColorIndex = Mathf.Clamp(wp.ColorIndex, 0, WaypointColors.Length - 1);
        }

        private void CancelWaypointEdit()
        {
            if (editingWaypointIsNew && editingWaypoint != null && waypoints.Remove(editingWaypoint))
                SaveWaypoints();

            editingWaypoint = null;
            editingWaypointIsNew = false;
            newWpName = $"Waypoint {waypoints.Count + 1}";
            Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
            if (cam != null)
            {
                Vector3 p = cam.transform.position;
                newWpX = Mathf.RoundToInt(p.x).ToString();
                newWpY = GetStandingY(GetActiveWorld(), p).ToString();
                newWpZ = Mathf.RoundToInt(p.z).ToString();
            }
            newWpColorIndex = 0;
        }

        private void FindWaypointOnMap(WaypointEntry wp)
        {
            if (wp == null) return;
            Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
            Vector3 p = cam != null ? cam.transform.position : Vector3.zero;
            mapPanBlocksX = WayfinderMath.ClampPan(wp.X - p.x);
            mapPanBlocksZ = WayfinderMath.ClampPan(wp.Z - p.z);
            }

        private void SortWaypoints(Vector3 playerPos)
        {
            if (waypointSortByDistance)
            {
                Vector3 pos = playerPos;
                waypoints.Sort((a, b) =>
                    Vector3.SqrMagnitude(new Vector3(a.X, a.Y, a.Z) - pos)
                        .CompareTo(Vector3.SqrMagnitude(new Vector3(b.X, b.Y, b.Z) - pos)));
            }
            else
            {
                waypoints.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }
            SaveWaypoints();
            wpScrollPos = Vector2.zero;
        }

        private void ToggleAllWaypoints()
        {
            bool allEnabled = waypoints.Count > 0;
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (!waypoints[i].Enabled) { allEnabled = false; break; }
            }

            bool target = !allEnabled;
            for (int i = 0; i < waypoints.Count; i++)
                waypoints[i].Enabled = target;
            SaveWaypoints();
        }

        /// <summary>
        /// Surface Y for hover text and double-click waypoints: the persisted store first
        /// (works on unloaded chunks), then a live sample, then the player's Y.
        /// </summary>
        private int GetHoverSurfaceY(IWorld world, int wx, int wz, int fallbackY, bool allowLive)
        {
            WayfinderStore store = SurfaceMapStore;
            if (store != null && store.TryGetSurface(wx, wz, out WayfinderCell cell) && cell.TopY > -500)
                return cell.TopY;

            if (allowLive && world != null
                && WayfinderEngine.TrySampleSurface(world, wx, wz, fallbackY, out TerrainSample sample)
                && sample.BlockId != 0)
                return sample.TopY;

            return fallbackY;
        }

        private static Color GetWaypointColor(int colorIndex)
        {
            int length = WaypointColors.Length;
            if (length <= 0) return Color.white;

            // C# remainder keeps the sign: a negative index would throw. Normalise first.
            return WaypointColors[((colorIndex % length) + length) % length];
        }

        /// <summary>
        /// Reuses the native radar's own point registry (beds, vehicles, teleporters, NPCs,
        /// spawn) instead of scanning the scene. Native cadence is 1 Hz and points already
        /// carry maxDistance/maxYDistance, so we honour those limits.
        /// </summary>
        private void RefreshRadarPois(Vector3 playerPos, float maxDist)
        {
            if (ShowNativeRadarPois == null || !ShowNativeRadarPois.Value || radarPointsField == null)
            {
                radarPois.Clear();
                return;
            }

            if (Time.unscaledTime - lastRadarPoiScanTime < 1f) return;
            lastRadarPoiScanTime = Time.unscaledTime;
            radarPois.Clear();

            IDictionary points = null;
            try { points = radarPointsField.GetValue(null) as IDictionary; }
            catch { }
            if (points == null) return;

            float verticalLimit = EntityVerticalRange != null ? EntityVerticalRange.Value : 20f;
            foreach (DictionaryEntry entry in points)
            {
                RadarPoint point = entry.Value as RadarPoint;
                if (point == null || !point.isActiveAndEnabled) continue;

                switch (point.icon)
                {
                    case RadarPoint.Icon.SpawnPoint:
                    case RadarPoint.Icon.BedPoint:
                    case RadarPoint.Icon.Vehicle:
                    case RadarPoint.Icon.Teleport:
                    case RadarPoint.Icon.NPC:
                        break;
                    default:
                        continue; // Mobs stay on the Health path so they are not drawn twice.
                }

                Vector3 pos = point.transform.position;
                float allowedY = Mathf.Min(verticalLimit, point.maxYDistance > 0f ? point.maxYDistance : verticalLimit);
                if (Mathf.Abs(pos.y - playerPos.y) > allowedY) continue;

                float allowed = Mathf.Min(maxDist, point.maxDistance > 0f ? point.maxDistance : maxDist);
                if ((pos - playerPos).sqrMagnitude > allowed * allowed) continue;

                radarPois.Add(new RadarPoi
                {
                    Position = pos,
                    Icon = point.icon,
                    Label = GetPoiLabel(point.icon)
                });
                if (radarPois.Count >= 32) break;
            }
        }

        private static Color GetPoiColor(RadarPoint.Icon icon)
        {
            switch (icon)
            {
                case RadarPoint.Icon.BedPoint: return new Color(0.35f, 0.95f, 0.45f);
                case RadarPoint.Icon.Vehicle: return new Color(0.25f, 0.85f, 1.0f);
                case RadarPoint.Icon.Teleport: return new Color(0.85f, 0.45f, 1.0f);
                case RadarPoint.Icon.NPC: return new Color(1.0f, 0.85f, 0.30f);
                case RadarPoint.Icon.SpawnPoint: return new Color(0.35f, 0.55f, 1.0f);
                default: return Color.grey;
            }
        }

        private static string GetPoiLabel(RadarPoint.Icon icon)
        {
            switch (icon)
            {
                case RadarPoint.Icon.BedPoint: return Tr("Bed", "Cama");
                case RadarPoint.Icon.Vehicle: return Tr("Vehicle", "Vehículo");
                case RadarPoint.Icon.Teleport: return Tr("Teleport", "Teleporte");
                case RadarPoint.Icon.NPC: return Tr("NPC", "NPC");
                case RadarPoint.Icon.SpawnPoint: return Tr("Spawn", "Spawn");
                default: return "POI";
            }
        }

        // =====================================================================
        //        PROCEDURAL CHEVRON & BEZEL TEXTURES
        // =====================================================================
        public static Texture2D GetPlayerChevronTexture()
        {
            if (playerChevronTex != null) return playerChevronTex;

            int size = 28;
            playerChevronTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32 transparent = new Color32(0, 0, 0, 0);
            Color32 outline = new Color32(10, 10, 15, 255);
            Color32 fill = new Color32(50, 225, 255, 255);
            Color32 fillCenter = new Color32(245, 255, 255, 255);

            Color32[] cols = new Color32[size * size];
            for (int i = 0; i < cols.Length; i++) cols[i] = transparent;

            Vector2 tip = new Vector2(13.5f, 25.5f);
            Vector2 left = new Vector2(4.0f, 4.0f);
            Vector2 right = new Vector2(23.0f, 4.0f);
            Vector2 notch = new Vector2(13.5f, 9.5f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inLeftWing = PointInTriangle(p, left, notch, tip);
                    bool inRightWing = PointInTriangle(p, notch, right, tip);

                    if (inLeftWing || inRightWing)
                    {
                        float distToTip = Vector2.Distance(p, tip);
                        cols[y * size + x] = Color32.Lerp(fillCenter, fill, Mathf.Clamp01(distToTip / 18f));
                    }
                }
            }

            Color32[] outlined = new Color32[size * size];
            for (int i = 0; i < outlined.Length; i++) outlined[i] = transparent;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int idx = y * size + x;
                    if (cols[idx].a > 0)
                    {
                        outlined[idx] = cols[idx];
                    }
                    else
                    {
                        bool neighborFilled = false;
                        for (int dy = -1; dy <= 1 && !neighborFilled; dy++)
                        {
                            for (int dx = -1; dx <= 1 && !neighborFilled; dx++)
                            {
                                int nx = x + dx;
                                int ny = y + dy;
                                if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                                {
                                    if (cols[ny * size + nx].a > 0)
                                        neighborFilled = true;
                                }
                            }
                        }
                        if (neighborFilled)
                        {
                            outlined[idx] = outline;
                        }
                        else
                        {
                            outlined[idx] = transparent;
                        }
                    }
                }
            }

            playerChevronTex.SetPixels32(outlined);
            playerChevronTex.Apply(false);
            return playerChevronTex;
        }

        private static bool PointInTriangle(Vector2 pt, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            float d1 = Sign(pt, v1, v2);
            float d2 = Sign(pt, v2, v3);
            float d3 = Sign(pt, v3, v1);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        public static Texture2D GetCircleBezelTexture()
        {
            if (circleBezelTex != null) return circleBezelTex;

            int size = 256;
            circleBezelTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] cols = new Color32[size * size];
            float center = size * 0.5f;
            float outerRadius = center - 1f;
            float rimStart = center - 8f;
            float innerRadius = center - 14f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - center;
                    float dy = (y + 0.5f) - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist > outerRadius + 1f || dist < innerRadius - 1f)
                    {
                        cols[y * size + x] = new Color32(0, 0, 0, 0);
                    }
                    else if (dist >= outerRadius - 1f && dist <= outerRadius + 1f)
                    {
                        float alpha = Mathf.Clamp01(outerRadius + 1f - dist);
                        cols[y * size + x] = new Color32(18, 12, 8, (byte)(alpha * 240));
                    }
                    else if (dist >= rimStart && dist < outerRadius - 1f)
                    {
                        cols[y * size + x] = new Color32(215, 195, 155, 255);
                    }
                    else if (dist >= innerRadius && dist < rimStart)
                    {
                        cols[y * size + x] = new Color32(85, 70, 50, 255);
                    }
                    else
                    {
                        float alpha = Mathf.Clamp01(dist - (innerRadius - 1f));
                        cols[y * size + x] = new Color32(35, 25, 15, (byte)(alpha * 200));
                    }
                }
            }

            circleBezelTex.SetPixels32(cols);
            circleBezelTex.Apply(false);
            return circleBezelTex;
        }

        // =====================================================================
        //        CORNER MINIMAP GUI
        // =====================================================================
        private static float lastMapErrorLogTime;
        private static void LogMapException(string where, Exception ex)
        {
            // Log only type+message: Unity's Mono aborts while formatting some stack traces.
            if (Time.unscaledTime - lastMapErrorLogTime < 1f) return;
            lastMapErrorLogTime = Time.unscaledTime;
            Log.LogError($"[Wayfinder] {where}: {ex.GetType().Name}: {ex.Message}");
        }

        private static int GetMapGridStyle()
        {
            string v = GridStyle != null ? (GridStyle.Value ?? "Off") : "Off";
            if (string.Equals(v, "Chunks", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(v, "Regions", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(v, "Both", StringComparison.OrdinalIgnoreCase)) return 3;
            return 0;
        }

        private static void SetMapGridStyle(int style)
        {
            style &= 3;
            string v = style == 1 ? "Chunks" : style == 2 ? "Regions" : style == 3 ? "Both" : "Off";
            if (GridStyle != null) GridStyle.Value = v;
            if (ShowChunkGrid != null) ShowChunkGrid.Value = style != 0; // legacy mirror
        }

        /// <summary>In-game clock from DayTimeManager (time is a 0..1 day fraction).</summary>
        private static string GetMapClockLabel()
        {
            try
            {
                DayTimeManager dtm = DayTimeManager.instance;
                if (dtm == null) return "--:--";
                float t = Mathf.Repeat(dtm.time, 1f);
                int mins = Mathf.FloorToInt(t * 24f * 60f);
                return $"{mins / 60:00}:{mins % 60:00}";
            }
            catch { return "--:--"; }
        }

        private static string GetMapDayLabel()
        {
            try
            {
                DayTimeManager dtm = DayTimeManager.instance;
                return dtm != null && dtm.isDay ? Tr("Day", "Día") : Tr("Night", "Noche");
            }
            catch { return "--"; }
        }

        /// <summary>Sun / block light at the player's head (0..15 each).</summary>
        private static string GetMapLightLabel(IWorld world, Vector3 pos)
        {
            try
            {
                if (world == null) return "Light --";
                int x = Mathf.FloorToInt(pos.x);
                int y = Mathf.FloorToInt(pos.y) + 1;
                int z = Mathf.FloorToInt(pos.z);
                Block b = WayfinderEngine.GetBlockSafe(world, x, y, z);
                byte sun = b.light.S;
                byte block = (byte)Mathf.Max(b.light.R, Mathf.Max(b.light.G, b.light.B));
                return $"Light {sun}/{block}";
            }
            catch { return "Light --"; }
        }

        // Deathpoints: polled around the death panel, which stays open long enough to catch.
        private Vector3 lastAlivePlayerPos;
        private bool hasLastAlivePos;
        private bool lastPlayerWasDead;

        internal void CheckDeathpoint()
        {
            PlayerHealth ph = PlayerHealth.Instance;
            if (ph == null)
            {
                lastPlayerWasDead = false;
                return;
            }

            Vector3 pos = ph.transform.position;
            if (!ph.IsDead())
            {
                lastAlivePlayerPos = pos;
                hasLastAlivePos = true;
                lastPlayerWasDead = false;
                return;
            }

            if (lastPlayerWasDead) return;
            lastPlayerWasDead = true;
            if (Deathpoints == null || !Deathpoints.Value) return;
            CreateDeathpoint(hasLastAlivePos ? lastAlivePlayerPos : pos);
        }

        /// <summary>
        /// Resolves the player's feet Y from the camera/eye position. Waypoints used to store the
        /// rounded camera Y (head), which made beacons start at eye level and teleports target
        /// the head. Scan down a few blocks for the first standable block and return the air Y
        /// above it; fall back to the camera Y if the world is not loaded yet.
        /// </summary>
        private static int GetStandingY(IWorld world, Vector3 pos)
        {
            int fallback = Mathf.RoundToInt(pos.y);
            if (world == null) return fallback;

            int x = Mathf.FloorToInt(pos.x);
            int z = Mathf.FloorToInt(pos.z);
            int start = Mathf.FloorToInt(pos.y);
            for (int y = start; y >= start - 6 && y >= 0; y--)
            {
                try
                {
                    Block b = WayfinderEngine.GetBlockSafe(world, x, y, z);
                    if (!WayfinderModule.IsAirOrPassable(b.id)) return y + 1;
                }
                catch { break; }
            }
            return fallback;
        }

        private void CreateDeathpoint(Vector3 pos)
        {            int limit = DeathpointLimit != null ? Mathf.Clamp(DeathpointLimit.Value, 1, 10) : 3;
            int count = 0;
            for (int i = 0; i < waypoints.Count; i++)
                if (waypoints[i].IsDeath) count++;

            while (count >= limit)
            {
                int oldest = waypoints.FindIndex(w => w.IsDeath);
                if (oldest < 0) break;
                waypoints.RemoveAt(oldest);
                count--;
            }

            int number = 1;
            for (int i = 0; i < waypoints.Count; i++)
            {
                string name = waypoints[i].IsDeath ? (waypoints[i].Name ?? "") : "";
                int space = name.LastIndexOf(' ');
                if (space >= 0 && int.TryParse(name.Substring(space + 1), out int n) && n >= number)
                    number = n + 1;
            }

            int deathY = GetStandingY(GetActiveWorld(), pos);
            waypoints.Add(new WaypointEntry
            {
                Name = $"{Tr("Death", "Muerte")} {number}",
                X = Mathf.RoundToInt(pos.x),
                Y = deathY,
                Z = Mathf.RoundToInt(pos.z),
                ColorIndex = 3,
                Enabled = true,
                IsDeath = true
            });
            SaveWaypoints();
            Log.LogInfo($"[Wayfinder] Deathpoint {number} at {pos.x:F0},{pos.y:F0},{pos.z:F0}.");
        }

        private void DrawMapInfoCard(Rect canvas, Vector3 playerPos, IWorld world)
        {
            if (ShowMapInfo == null || !ShowMapInfo.Value) return;
            EnsureWayfinderCraftStyles();
            Rect info = new Rect(canvas.xMin + 8f, canvas.yMin + 8f, 214f, 40f);
            NanoSurface.FillRounded(info, new Color(0.055f, 0.044f, 0.035f, 0.86f), 7f);
            NanoSurface.StrokeRounded(info, NanoBrand.Line, 1f, 7f);
            string line1 = $"{LiveFps:F0} FPS · {LiveFrameTimeMs:F1} ms";
            string line2 = $"{GetMapClockLabel()} · {GetMapDayLabel()} · {GetMapLightLabel(world, playerPos)}";
            Color fpsColor = LiveFps >= 45f ? NanoBrand.Ok
                : (LiveFps >= 25f ? NanoBrand.Warn : NanoBrand.Bad);
            NanoSurface.DrawCrispLabel(new Rect(info.x + 8f, info.y + 3f, info.width - 16f, 16f), line1, _wfMonoSmallStyle, fpsColor, 0.35f);
            NanoSurface.DrawCrispLabel(new Rect(info.x + 8f, info.y + 20f, info.width - 16f, 16f), line2, _wfMonoSmallStyle, NanoBrand.Mute, 0.3f);
        }

        /// <summary>
        /// True when the player is inside an enclosed structure that is actually at the surface:
        /// the explored surface at the player's column must be within a few blocks of the player.
        /// This keeps Auto/Cave from showing the overworld above a shallow cave under a mountain.
        /// The manual Interior layer bypasses this check on purpose.
        /// </summary>
        private const int SurfaceCutawayMaxDepth = 8;

        private static bool IsSurfaceStructureContext(Vector3 playerPos, bool indoors)
        {
            if (!indoors || SurfaceMapStore == null) return false;

            int px = Mathf.FloorToInt(playerPos.x);
            int pz = Mathf.FloorToInt(playerPos.z);
            if (!SurfaceMapStore.TryGetSurface(px, pz, out WayfinderCell cell)) return false;

            return Mathf.Abs(cell.TopY - Mathf.RoundToInt(playerPos.y)) <= SurfaceCutawayMaxDepth;
        }

        internal void DrawCornerMinimap()
        {
            Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
            if (cam == null) return;
            EnsureWayfinderCraftStyles();

            Vector3 playerPos = cam.transform.position;
            IWorld world = GetActiveWorld();

            bool isCircle = (MinimapShape != null && MinimapShape.Value == "Circle");
            string minimapTheme = MinimapTheme != null ? MinimapTheme.Value : "Aether";
            string minimapLayer = MapLayerMode != null ? MapLayerMode.Value : "Auto";
            string minimapRender = MapRenderMode != null ? MapRenderMode.Value : "Day";
            int minimapGrid = GetMapGridStyle();
            // Auto/Cave/Interior follow the player's current Y; only Slice pins an altitude.
            int minimapSliceY = (minimapLayer == "Slice") ? currentSliceY : Mathf.RoundToInt(playerPos.y);
            bool minimapSurfaceStructure = IsSurfaceStructureContext(playerPos, IsIndoors);

            // Show exactly 2 * RadarRange blocks; the chunk atlas draws the terrain.
            float worldRadius = WayfinderMath.ClampRadarRange(MinimapRadarRange != null ? MinimapRadarRange.Value : 48f);

            Rect frameRect = GetMinimapScreenRect();
            float mapSize = frameRect.width;

            Rect innerMap = isCircle
                ? new Rect(frameRect.x + 6, frameRect.y + 6, mapSize - 12, mapSize - 12)
                : new Rect(frameRect.x + 6, frameRect.y + 6, mapSize - 12, mapSize - 12);

            Vector2 center = innerMap.center;
            float pxPerBlock = innerMap.width / (worldRadius * 2f);
            float maxRadarRadius = (innerMap.width * 0.5f) - (isCircle ? 6f : 4f);

            if (isCircle)
            {
                // True round minimap: the visible chunks are composed into a texture and the
                // area outside the bezel's inner circle is made transparent. No square card.
                int cw = Mathf.Max(1, Mathf.RoundToInt(innerMap.width));
                int ch = Mathf.Max(1, Mathf.RoundToInt(innerMap.height));
                if (circleMinimapTex == null || circleMinimapTex.width != cw || circleMinimapTex.height != ch)
                {
                    if (circleMinimapTex != null) UnityEngine.Object.Destroy(circleMinimapTex);
                    circleMinimapTex = new Texture2D(cw, ch, TextureFormat.RGBA32, false)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp
                    };
                }
                try
                {
                    WayfinderChunkAtlas.Shared.DrawView(
                        innerMap, world, playerPos.x, playerPos.z, Mathf.RoundToInt(playerPos.y), minimapSliceY,
                        MapCave, minimapLayer, minimapRender, minimapGrid, IsIndoors, pxPerBlock,
                        minimapSurfaceStructure,
                        circleMinimapTex, 0.890625f * (mapSize * 0.5f));
                }
                catch (Exception ex) { LogMapException("minimap DrawView", ex); }
            }
            else
            {
                DrawWayfinderSurface(frameRect, minimapTheme, 0.96f, 10f);
                GUI.DrawTexture(innerMap, GetColorTexture(new Color(0.045f, 0.055f, 0.072f, 0.96f)));
                // Chunk-grid terrain: stable textures drawn at world positions, so panning is
                // inherently smooth and zoom only rescales the quads.
                try
                {
                    WayfinderChunkAtlas.Shared.DrawView(
                        innerMap, world, playerPos.x, playerPos.z, Mathf.RoundToInt(playerPos.y), minimapSliceY,
                        MapCave, minimapLayer, minimapRender, minimapGrid, IsIndoors, pxPerBlock,
                        minimapSurfaceStructure);
                }
                catch (Exception ex) { LogMapException("minimap DrawView", ex); }
                NanoSurface.StrokeRounded(innerMap, new Color(0.16f, 0.13f, 0.10f, 0.72f), 1f, 3f);
            }

            if (isCircle)
            {
                Color bezelTint = new Color(0.92f, 0.86f, 0.76f, 1f);
                if (minimapTheme == "RoyalGold") bezelTint = new Color(1.0f, 0.82f, 0.25f, 1f);
                else if (minimapTheme == "DarkObsidian") bezelTint = new Color(0.42f, 0.46f, 0.54f, 1f);
                else if (minimapTheme == "MinimalistGlass") bezelTint = new Color(0.70f, 0.85f, 1.0f, 0.8f);

                Color oldC = GUI.color;
                GUI.color = bezelTint;
                GUI.DrawTexture(frameRect, GetCircleBezelTexture());
                GUI.color = oldC;
            }

            // Radar Entities (Health dots + native radar POIs)
            RefreshMobCache(playerPos, worldRadius);
            RefreshRadarPois(playerPos, worldRadius);
            for (int i = 0; i < MobCache.Count; i++)
            {
                var mob = MobCache[i];
                if (mob == null || mob.IsDead()) continue;

                Vector3 diff = mob.transform.position - playerPos;
                float dotX = center.x + diff.x * pxPerBlock;
                float dotY = center.y - diff.z * pxPerBlock;
                Vector2 dotPos = new Vector2(dotX, dotY);

                if (isCircle)
                {
                    Vector2 off = dotPos - center;
                    if (off.magnitude > maxRadarRadius) continue;
                }
                else
                {
                    if (!innerMap.Contains(dotPos)) continue;
                }

                if (!WayfinderChunkAtlas.Shared.IsRenderedExplored(
                        Mathf.RoundToInt(mob.transform.position.x), Mathf.RoundToInt(mob.transform.position.z),
                        minimapLayer, MapCave, IsIndoors, minimapSurfaceStructure, minimapRender, minimapGrid, minimapSliceY))
                    continue;

                Color mobColor = new Color(0.98f, 0.22f, 0.22f);
                string mobName = mob.gameObject.name.ToLower();
                if (mobName.Contains("pet") || mobName.Contains("dragon"))
                    mobColor = new Color(0.25f, 0.75f, 1.0f);
                else if (mobName.Contains("cow") || mobName.Contains("sheep") || mobName.Contains("pig") || mobName.Contains("deer") || mobName.Contains("bird") || mobName.Contains("fish"))
                    mobColor = new Color(0.30f, 0.95f, 0.35f);

                GUI.DrawTexture(new Rect(dotPos.x - 3f, dotPos.y - 3f, 6f, 6f), GetColorTexture(Color.black));
                GUI.DrawTexture(new Rect(dotPos.x - 2f, dotPos.y - 2f, 4f, 4f), GetColorTexture(mobColor));
            }

            // Waypoints
            bool minimapWaypointLabels = ShowWaypointLabelsOnMinimap != null && ShowWaypointLabelsOnMinimap.Value;
            int minimapLabelsDrawn = 0;
            for (int i = 0; i < waypoints.Count; i++)
            {
                var wp = waypoints[i];
                if (!wp.Enabled) continue;

                float dx = wp.X - playerPos.x;
                float dz = wp.Z - playerPos.z;
                float wx = center.x + dx * pxPerBlock;
                float wy = center.y - dz * pxPerBlock;
                Vector2 wpPos = new Vector2(wx, wy);

                if (isCircle)
                {
                    Vector2 off = wpPos - center;
                    if (off.magnitude > maxRadarRadius)
                        wpPos = center + off.normalized * maxRadarRadius;
                }
                else
                {
                    float half = innerMap.width * 0.5f - 5f;
                    wpPos.x = Mathf.Clamp(wpPos.x, center.x - half, center.x + half);
                    wpPos.y = Mathf.Clamp(wpPos.y, center.y - half, center.y + half);
                }

                Color wpCol = GetWaypointColor(wp.ColorIndex);
                DrawWaypointMapMarker(wpPos.x, wpPos.y, wpCol, wp.IsDeath, 6f);

                if (minimapWaypointLabels && minimapLabelsDrawn < 8 && innerMap.Contains(wpPos))
                {
                    float labelW = Mathf.Min(110f, innerMap.width * 0.7f);
                    float lx = Mathf.Clamp(wpPos.x + 5f, innerMap.xMin + 2f, innerMap.xMax - labelW - 2f);
                    float ly = Mathf.Clamp(wpPos.y - 9f, innerMap.yMin + 2f, innerMap.yMax - 16f);
                    DrawShadowLabel(new Rect(lx, ly, labelW, 16f), wp.Name, MinimapLabelStyle, wpCol, Color.black);
                    minimapLabelsDrawn++;
                }
            }

            // Native radar POIs: small diamonds so they read differently from waypoint squares.
            for (int i = 0; i < radarPois.Count; i++)
            {
                RadarPoi poi = radarPois[i];
                Vector3 diff = poi.Position - playerPos;
                float dotX = center.x + diff.x * pxPerBlock;
                float dotY = center.y - diff.z * pxPerBlock;
                Vector2 dotPos = new Vector2(dotX, dotY);

                if (isCircle)
                {
                    if ((dotPos - center).magnitude > maxRadarRadius) continue;
                }
                else if (!innerMap.Contains(dotPos))
                {
                    continue;
                }

                if (!WayfinderChunkAtlas.Shared.IsRenderedExplored(
                        Mathf.RoundToInt(poi.Position.x), Mathf.RoundToInt(poi.Position.z),
                        minimapLayer, MapCave, IsIndoors, minimapSurfaceStructure, minimapRender, minimapGrid, minimapSliceY))
                    continue;

                Color poiColor = GetPoiColor(poi.Icon);
                GUI.DrawTexture(new Rect(dotPos.x - 4f, dotPos.y - 4f, 8f, 8f), GetColorTexture(Color.black));
                GUI.DrawTexture(new Rect(dotPos.x - 3f, dotPos.y - 3f, 6f, 6f), GetColorTexture(poiColor));
            }

            // Directional Player Chevron (clean alpha blend, zero cyan box artifact!)
            float playerYaw = cam.transform.eulerAngles.y;
            Matrix4x4 matBackup = GUI.matrix;
            GUIUtility.RotateAroundPivot(playerYaw, center);
            Color oldCol = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(center.x - 12f, center.y - 12f, 24f, 24f), GetPlayerChevronTexture(), ScaleMode.StretchToFill, true);
            GUI.color = oldCol;
            GUI.matrix = matBackup;

            // Cardinal letters (Ember N, crisp Paper S/E/W over subtle ink pills for legibility on any biome)
            DrawCardinalMark(new Rect(center.x - 8f, innerMap.yMin + 2f, 16f, 14f), "N", NanoBrand.Ember);
            DrawCardinalMark(new Rect(center.x - 8f, innerMap.yMax - 16f, 16f, 14f), "S", NanoBrand.Paper);
            DrawCardinalMark(new Rect(innerMap.xMax - 16f, center.y - 7f, 14f, 14f), "E", NanoBrand.Paper);
            DrawCardinalMark(new Rect(innerMap.xMin + 2f, center.y - 7f, 14f, 14f), "W", NanoBrand.Paper);

            // Unified cartographic footer card directly below the minimap (replaces 4 stacked legacy slabs)
            bool showCoords = MinimapShowCoords != null && MinimapShowCoords.Value;
            bool showInfo = ShowMapInfo != null && ShowMapInfo.Value && mapSize >= 200f;
            float footerY = frameRect.y + frameRect.height + 5f;
            float footerH = 6f + (showCoords ? 18f : 0f) + (showInfo ? 16f : 0f) + 22f + 4f;
            Rect footerRect = new Rect(frameRect.x, footerY, mapSize, footerH);
            DrawWayfinderSurface(footerRect, minimapTheme, 0.94f, 9f);

            float rowY = footerY + 5f;
            if (showCoords)
            {
                string coordsText;
                if (mapSize >= 236f)
                {
                    string biomeName = world != null
                        ? BiomeHelper.GetBiomeName(world, Mathf.RoundToInt(playerPos.x), Mathf.RoundToInt(playerPos.y), Mathf.RoundToInt(playerPos.z), IsSpanish())
                        : "?";
                    string layerBadge = MapCave ? "▼ " : "▲ ";
                    coordsText = $"X {playerPos.x:F0} · Y {playerPos.y:F0} · Z {playerPos.z:F0}  ·  {layerBadge}{biomeName}";
                }
                else if (mapSize >= 156f)
                {
                    coordsText = $"X {playerPos.x:F0} · Y {playerPos.y:F0} · Z {playerPos.z:F0}";
                }
                else
                {
                    coordsText = $"{playerPos.x:F0},{playerPos.y:F0},{playerPos.z:F0}";
                }
                NanoSurface.DrawCrispLabel(new Rect(footerRect.x + 6f, rowY, mapSize - 12f, 16f),
                    coordsText, _wfMonoCenterStyle, NanoBrand.PaperLight, 0.35f);
                rowY += 18f;
            }

            if (showInfo)
            {
                Color fpsCol = LiveFps >= 45f ? NanoBrand.Ok : (LiveFps >= 25f ? NanoBrand.Warn : NanoBrand.Bad);
                string mapKeyLabel = WayfinderKey != null ? WayfinderKey.Value.ToString() : "M";
                string infoLeft = $"{LiveFps:F0} FPS · {GetMapClockLabel()}";
                string infoRight = $"[{mapKeyLabel}] " + Tr("Map", "Mapa");
                NanoSurface.DrawCrispLabel(new Rect(footerRect.x + 8f, rowY, mapSize * 0.60f, 14f),
                    infoLeft, _wfMonoSmallStyle, fpsCol, 0.25f);
                _wfMeasure.text = infoRight;
                float rw = _wfMonoSmallStyle.CalcSize(_wfMeasure).x + 2f;
                NanoSurface.DrawCrispLabel(new Rect(footerRect.xMax - rw - 8f, rowY, rw, 14f),
                    infoRight, _wfMonoSmallStyle, NanoBrand.Mute, 0.25f);
                rowY += 16f;
            }

            // Compact toolbar row: [▲Auto] [- Sz +] [- Z +] [M]
            float bx = footerRect.x + 5f;
            float by = rowY + 1f;
            float bh = 20f;
            float remW = mapSize - 10f;
            string layerMode = MapLayerMode != null ? MapLayerMode.Value : "Auto";
            bool caveActive = layerMode == "Cave" || layerMode == "Interior" || layerMode == "Slice" || (layerMode == "Auto" && MapCave);
            float sBtnW = 16f;

            string modeIcon = caveActive ? "▼" : "▲";
            string modeText = $"{modeIcon}{layerMode}";
            float modeW = Mathf.Clamp(remW * 0.30f, 42f, 68f);
            if (DrawCompactMinimapButton(new Rect(bx, by, modeW, bh), modeText, caveActive))
                CycleMapLayerMode();
            bx += modeW + 3f;

            float rightEdge = footerRect.xMax - 5f;
            float afterMode = rightEdge - bx;
            bool showZoomControls = afterMode >= 64f;
            bool showSizeControls = afterMode - (showZoomControls ? 48f : 0f) >= 78f;

            if (showSizeControls)
            {
                if (DrawCompactMinimapButton(new Rect(bx, by, sBtnW, bh), "−"))
                {
                    MinimapSize.Value = Mathf.Clamp(MinimapSize.Value - 15f, 110f, 340f);
                    Config.Save();
                }
                bx += sBtnW + 1f;

                float szLabelW = Mathf.Clamp(remW * 0.13f, 22f, 28f);
                NanoSurface.DrawCrispLabel(new Rect(bx, by, szLabelW, bh), $"{MinimapSize.Value:F0}", _wfMonoCenterStyle, NanoBrand.Paper, 0.3f);
                bx += szLabelW + 1f;

                if (DrawCompactMinimapButton(new Rect(bx, by, sBtnW, bh), "+"))
                {
                    MinimapSize.Value = Mathf.Clamp(MinimapSize.Value + 15f, 110f, 340f);
                    Config.Save();
                }
                bx += sBtnW + 3f;
            }

            if (showZoomControls)
            {
                if (DrawCompactMinimapButton(new Rect(bx, by, sBtnW, bh), "−"))
                {
                    MinimapRadarRange.Value = WayfinderMath.ClampRadarRange(MinimapRadarRange.Value + 12f);
                    Config.Save();
                }
                bx += sBtnW + 1f;

                float zmLabelW = 12f;
                NanoSurface.DrawCrispLabel(new Rect(bx, by, zmLabelW, bh), "Z", _wfMonoCenterStyle, NanoBrand.Mute, 0.3f);
                bx += zmLabelW + 1f;

                if (DrawCompactMinimapButton(new Rect(bx, by, sBtnW, bh), "+"))
                {
                    MinimapRadarRange.Value = WayfinderMath.ClampRadarRange(MinimapRadarRange.Value - 12f);
                    Config.Save();
                }
                bx += sBtnW + 3f;
            }

            // Fullscreen map button showing the real bound hotkey (e.g. "M")
            float mapBtnW = rightEdge - bx;
            if (mapBtnW >= 18f)
            {
                string keyStr = WayfinderKey != null ? WayfinderKey.Value.ToString() : "M";
                if (DrawCompactMinimapButton(new Rect(bx, by, mapBtnW, bh), keyStr, false, NanoBrand.Ember))
                    SetWayfinderOpen(true);
            }
        }

        private static void DrawCardinalMark(Rect r, string letter, Color ink)
        {
            if (Event.current.type != EventType.Repaint) return;
            NanoSurface.FillRounded(r, new Color(0.055f, 0.044f, 0.035f, 0.72f), 4f);
            NanoSurface.DrawCrispLabel(r, letter, _wfCardinalStyle, ink, 0.45f);
        }

        private bool DrawCompactMinimapButton(Rect rect, string text, bool isSelected = false, Color? accentTint = null)
        {
            Vector2 mousePos = Event.current.mousePosition;
            bool hover = rect.Contains(mousePos);
            if (Event.current.type == EventType.Repaint)
            {
                Color accent = accentTint ?? NanoBrand.Ember;
                Color bg;
                Color border;
                Color textCol;
                if (isSelected)
                {
                    bg = new Color(0.20f, 0.11f, 0.06f, 0.96f);
                    border = new Color(accent.r, accent.g, accent.b, 0.72f);
                    textCol = NanoBrand.PaperLight;
                }
                else if (accentTint.HasValue)
                {
                    bg = hover ? new Color(0.28f, 0.14f, 0.06f, 0.96f) : new Color(0.18f, 0.10f, 0.05f, 0.92f);
                    border = new Color(accent.r, accent.g, accent.b, hover ? 0.85f : 0.55f);
                    textCol = hover ? NanoBrand.PaperLight : NanoBrand.EmberSoft;
                }
                else
                {
                    bg = hover ? new Color(0.15f, 0.12f, 0.095f, 0.96f) : new Color(0.085f, 0.068f, 0.054f, 0.92f);
                    border = hover ? new Color(NanoBrand.Ember.r, NanoBrand.Ember.g, NanoBrand.Ember.b, 0.55f) : NanoBrand.Line;
                    textCol = hover ? NanoBrand.PaperLight : NanoBrand.Paper;
                }

                NanoSurface.FillRounded(rect, bg, 5f);
                NanoSurface.StrokeRounded(rect, border, 1f, 5f);
                NanoSurface.DrawCrispLabel(rect, text, _wfMonoCenterStyle, textCol, 0.3f);
            }

            if (Event.current.type == EventType.MouseDown && hover && Event.current.button == 0)
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        // =====================================================================
        //        3D IN-WORLD WAYPOINTS
        // =====================================================================
        internal void DrawWorldWaypoints3D()
        {
            Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
            if (cam == null) return;
            EnsureWayfinderCraftStyles();

            Vector3 camPos = cam.transform.position;

            for (int i = 0; i < waypoints.Count; i++)
            {
                var wp = waypoints[i];
                if (!wp.Enabled) continue;

                Vector3 wpPos = new Vector3(wp.X + 0.5f, wp.Y + 1.5f, wp.Z + 0.5f);
                Vector3 screenPt = cam.WorldToScreenPoint(wpPos);
                if (screenPt.z <= 0.5f) continue;

                float dist = Vector3.Distance(camPos, wpPos);
                float sx = screenPt.x;
                float sy = Screen.height - screenPt.y;

                sx = Mathf.Clamp(sx, 60f, Screen.width - 60f);
                sy = Mathf.Clamp(sy, 40f, Screen.height - 40f);

                Color wpCol = GetWaypointColor(wp.ColorIndex);
                string label = $"{wp.Name}  {dist:F0}m";
                _wfMeasure.text = label;
                float textW = _wfMonoSmallStyle.CalcSize(_wfMeasure).x;
                float iconW = wp.IsDeath ? 18f : 12f;
                float pillW = Mathf.Max(80f, textW + iconW + 16f);
                Rect pillRect = new Rect(sx - pillW * 0.5f, sy - 11f, pillW, 22f);

                NanoSurface.FillRounded(pillRect, new Color(0.055f, 0.044f, 0.035f, 0.88f), 6f);
                NanoSurface.StrokeRounded(pillRect, new Color(wpCol.r, wpCol.g, wpCol.b, 0.72f), 1f, 6f);

                float tx = pillRect.x + 6f;
                if (wp.IsDeath)
                {
                    Texture2D skull = GetWaypointDeathSkullTexture();
                    if (skull != null)
                    {
                        GUI.DrawTexture(new Rect(pillRect.x + 5f, pillRect.y + 4f, 14f, 14f), skull);
                        tx += 17f;
                    }
                }
                else
                {
                    NanoSurface.DrawRhombus(new Rect(pillRect.x + 6f, pillRect.y + 7.5f, 7f, 7f), wpCol, true);
                    tx += 11f;
                }
                NanoSurface.DrawCrispLabel(new Rect(tx, pillRect.y, pillRect.xMax - tx - 5f, pillRect.height),
                    label, _wfMonoSmallStyle, NanoBrand.PaperLight, 0.4f);
            }
        }

        /// <summary>
        /// Shared square waypoint dot. Death waypoints get the procedural skull over the
        /// colour chip so they read as deaths on the minimap and the fullscreen map.
        /// </summary>
        private void DrawWaypointMapMarker(float x, float y, Color color, bool isDeath, float size)
        {
            float half = size * 0.5f;
            GUI.DrawTexture(new Rect(x - half - 1f, y - half - 1f, size + 2f, size + 2f),
                GetColorTexture(Color.black));
            GUI.DrawTexture(new Rect(x - half, y - half, size, size),
                GetColorTexture(color));

            if (!isDeath) return;
            Texture2D skull = GetWaypointDeathSkullTexture();
            if (skull != null)
                GUI.DrawTexture(new Rect(x - half - 1f, y - half - 1f, size + 2f, size + 2f), skull);
        }

        /// <summary>
        /// Edge marker for a waypoint outside the fullscreen map viewport. The position is the
        /// first intersection of the centre→waypoint ray with the map rectangle, so the dot
        /// points in the real direction instead of sticking to a corner.
        /// </summary>
        private void DrawOffscreenWaypointMarker(
            Rect mapRect, Vector2 center, Vector2 target, Color color, string label, bool isDeath,
            int stackSlot)
        {
            float halfW = mapRect.width * 0.5f - 16f;
            float halfH = mapRect.height * 0.5f - 16f;
            float dx = target.x - center.x;
            float dy = target.y - center.y;
            if (halfW <= 1f || halfH <= 1f) return;
            if (Mathf.Abs(dx) < 0.001f && Mathf.Abs(dy) < 0.001f) return;

            float t = float.PositiveInfinity;
            if (Mathf.Abs(dx) > 0.0001f) t = Mathf.Min(t, halfW / Mathf.Abs(dx));
            if (Mathf.Abs(dy) > 0.0001f) t = Mathf.Min(t, halfH / Mathf.Abs(dy));
            if (float.IsInfinity(t)) return;

            t = Mathf.Min(t, 1f);
            Vector2 edge = center + new Vector2(dx, dy) * t;
            DrawWaypointMapMarker(edge.x, edge.y, color, isDeath, 10f);

            float iconW = isDeath ? 15f : 0f;
            float labelW = Mathf.Min(
                mapRect.width - 8f,
                Mathf.Max(72f, label.Length * 6.8f + 22f + iconW));
            bool onRight = dx >= 0f;
            bool onBottom = dy >= 0f;
            float lx = onRight ? edge.x - labelW - 10f : edge.x + 10f;
            // Stack multiple edge labels inward instead of painting them on top of each other.
            float stack = Mathf.Min(stackSlot, 5) * 20f;
            float ly = onBottom ? edge.y - 22f - stack : edge.y + 6f + stack;
            lx = Mathf.Clamp(lx, mapRect.xMin + 4f, mapRect.xMax - labelW - 4f);
            ly = Mathf.Clamp(ly, mapRect.yMin + 4f, mapRect.yMax - 22f);

            Rect pill = new Rect(lx, ly, labelW, 19f);
            NanoSurface.FillRounded(pill, new Color(0.055f, 0.044f, 0.035f, 0.90f), 5f);
            NanoSurface.StrokeRounded(pill, new Color(color.r, color.g, color.b, 0.72f), 1f, 5f);

            float textX = pill.x + 5f;
            if (isDeath)
            {
                Texture2D skull = GetWaypointDeathSkullTexture();
                if (skull != null)
                {
                    GUI.DrawTexture(new Rect(pill.x + 4f, pill.y + 2.5f, 14f, 14f), skull);
                    textX += 16f;
                }
            }

            NanoSurface.DrawCrispLabel(new Rect(textX, pill.y, pill.xMax - textX - 4f, pill.height),
                label, _wfMonoSmallStyle, color, 0.35f);
        }

        // =====================================================================
        //        EXPANDABLE FULLSCREEN WAYFINDER & WAYPOINTS
        // =====================================================================
        internal void DrawFullscreenWayfinderGUI(float anim = 1f)
        {
            EnsureWayfinderCraftStyles();
            bool es = IsSpanish();

            Rect fullScreen = new Rect(0, 0, Screen.width, Screen.height);
            GUI.DrawTexture(fullScreen, GetColorTexture(new Color(0.02f, 0.015f, 0.012f, 0.74f * anim)));

            float scale = FullscreenMapScale != null ? FullscreenMapScale.Value : 0.90f;
            float winWidth = Mathf.Clamp(Screen.width * scale, 760f, Screen.width - 20f);
            float winHeight = Mathf.Clamp(Screen.height * scale, 540f, Screen.height - 20f);
            float winX = (Screen.width - winWidth) * 0.5f;
            float winY = (Screen.height - winHeight) * 0.5f;
            Rect winRect = new Rect(winX, winY, winWidth, winHeight);

            using (NanoMotion.BeginModal(winRect, anim))
            {
            string activeTheme = MinimapTheme != null ? MinimapTheme.Value : "Aether";
            DrawWayfinderSurface(winRect, activeTheme, 0.98f, 14f);

            // Integrated Top Bar (open cube + Gloock wordmark + live world metadata + Export PNG + Close)
            float topBarH = 50f;
            float hx = winX + 16f;
            if (NanoBrand.Cube != null)
                hx += NanoBrand.DrawCube(hx, winY + 14f, 21f) + 10f;
            NanoSurface.DrawCrispLabel(new Rect(hx, winY + 9f, 118f, 32f), "wayfinder", _wfTitleStyle, NanoBrand.Paper, 0.45f);
            hx += 112f;
            NanoSurface.FillRounded(new Rect(hx, winY + 17f, 1f, 16f), NanoBrand.Line, 0.5f);
            hx += 12f;

            string worldLabel = string.IsNullOrEmpty(WorldManager.loadedWorldName) ? "?" : WorldManager.loadedWorldName;
            string mapKey = WayfinderKey != null ? WayfinderKey.Value.ToString() : "M";
            string headerMeta = $"{worldLabel}  ·  {waypoints.Count} " + (es ? "marcadores" : "waypoints") + $"  ·  {mapKey}";
            NanoSurface.DrawCrispLabel(new Rect(hx, winY + 15f, Mathf.Max(80f, winX + winWidth - hx - 160f), 20f),
                headerMeta, _wfMetaStyle, NanoBrand.Mute, 0.3f);

            DrawExportWayfinderButton(new Rect(winX + winWidth - 144f, winY + 10f, 92f, 28f));

            Rect closeBtnRect = new Rect(winX + winWidth - 44f, winY + 10f, 30f, 28f);
            if (NanoSurface.DrawActionButton(closeBtnRect, "×", false))
            {
                SetWayfinderOpen(false);
                return;
            }

            NanoSurface.FillRounded(new Rect(winX + 14f, winY + topBarH, winWidth - 28f, 1f), NanoBrand.Line, 0.5f);

            Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
            Vector3 playerPos = cam != null ? cam.transform.position : Vector3.zero;
            IWorld world = GetActiveWorld();

            Vector3 renderCenter = new Vector3(playerPos.x + mapPanBlocksX, playerPos.y, playerPos.z + mapPanBlocksZ);

            // Layout dimensions: Left Map & Right Waypoints
            float rightW = Mathf.Clamp(winWidth * 0.38f, 328f, 430f);
            float mapX = winX + 16f;
            float rightX = winX + winWidth - rightW - 16f;
            float mapAvailableW = rightX - mapX - 14f;

            // Dedicated Layer & Render Toolbar strips ABOVE map canvas
            float toolbarY = winY + topBarH + 10f;
            float toolbarH = 66f;

            float mapY = toolbarY + toolbarH + 8f;
            float bottomBarH = 32f;
            float mapAvailableH = winHeight - (mapY - winY) - bottomBarH - 22f;
            float mapCanvasSize = Mathf.Min(mapAvailableW, mapAvailableH);
            Rect mapCanvasRect = new Rect(mapX, mapY, mapCanvasSize, mapCanvasSize);

            // =================================================================
            // 1. LAYER & RENDER TOOLBARS (NanoSurface Segmented Bars)
            // =================================================================
            string layerMode = MapLayerMode != null ? MapLayerMode.Value : "Auto";
            string renderMode = MapRenderMode != null ? MapRenderMode.Value : "Day";

            bool inCaveSliceMode = layerMode == "Cave" || layerMode == "Interior" || layerMode == "Slice"
                || (layerMode == "Auto" && MapCave);

            bool showLayerChip = mapCanvasSize >= 460f;
            float layerSegW = showLayerChip ? Mathf.Min(356f, mapCanvasSize - 130f) : mapCanvasSize;
            if (NanoSurface.DrawSegmented(new Rect(mapX, toolbarY, layerSegW, 28f),
                    es ? LayerSegmentsEs : LayerSegmentsEn, layerMode, out string nextLayer))
            {
                MapLayerMode.Value = nextLayer;
                if (nextLayer == "Slice") SetSliceY(MapSliceY.Value);
            }

            if (showLayerChip)
            {
                string badgeText = layerMode == "Slice"
                    ? $"▼ Y:{currentSliceY}"
                    : layerMode == "Interior"
                        ? $"▼ INT Y:{Mathf.RoundToInt(playerPos.y)}"
                        : (layerMode == "Cave" ? (es ? "▼ CUEVA" : "▼ CAVE")
                            : (layerMode == "Auto" && MapCave ? (IsIndoors ? "▼ INTERIOR" : (es ? "▼ CUEVA" : "▼ CAVE"))
                                : (es ? "▲ SUPERFICIE" : "▲ SURFACE")));
                Rect badgeR = new Rect(mapX + mapCanvasSize - 118f, toolbarY + 3f, 118f, 22f);
                Color chipFill = inCaveSliceMode ? new Color(0.22f, 0.11f, 0.06f, 0.94f) : new Color(0.07f, 0.16f, 0.11f, 0.94f);
                Color chipBorder = inCaveSliceMode ? new Color(0.76f, 0.34f, 0.10f, 0.85f) : new Color(0.24f, 0.52f, 0.36f, 0.85f);
                Color chipInk = inCaveSliceMode ? NanoBrand.EmberSoft : NanoBrand.Ok;
                NanoSurface.DrawChip(badgeR, badgeText, _wfChipStyle, chipFill, chipBorder, chipInk, 6f);
            }

            // Row 2: Render Modes [Day | Night | Topo | Biome] + [# Grid] + [- 100% +]
            float renderBarY = toolbarY + 34f;
            float renderSegW = Mathf.Min(256f, mapCanvasSize - 90f);
            if (NanoSurface.DrawSegmented(new Rect(mapX, renderBarY, renderSegW, 26f),
                    es ? RenderSegmentsEs : RenderSegmentsEn, renderMode, out string nextRender))
            {
                MapRenderMode.Value = nextRender;
            }

            int gridOn = GetMapGridStyle();
            string gridLabel = gridOn == 1 ? "# Chunk"
                : gridOn == 2 ? "# Region"
                : gridOn == 3 ? (es ? "# Ambos" : "# Both")
                : "# Off";
            if (NanoSurface.DrawActionButton(new Rect(mapX + renderSegW + 8f, renderBarY, 78f, 26f), gridLabel, gridOn != 0))
            {
                SetMapGridStyle((gridOn + 1) & 3);
            }

            if (mapCanvasSize >= 460f)
            {
                float zx = mapX + mapCanvasSize - 106f;
                if (NanoSurface.DrawActionButton(new Rect(zx, renderBarY, 26f, 26f), "−", false))
                    fullscreenZoom = WayfinderMath.ClampZoom(fullscreenZoom * 0.80f);
                NanoSurface.DrawCrispLabel(new Rect(zx + 28f, renderBarY, 50f, 26f),
                    $"{Mathf.RoundToInt(fullscreenZoom * 100f)}%", _wfMonoCenterStyle, NanoBrand.Paper, 0.3f);
                if (NanoSurface.DrawActionButton(new Rect(zx + 80f, renderBarY, 26f, 26f), "+", false))
                    fullscreenZoom = WayfinderMath.ClampZoom(fullscreenZoom * 1.25f);
            }

            // =================================================================
            // 2/3. TERRAIN VIA CHUNK ATLAS: draw the visible chunk grid. Zoom only changes
            // pxPerBlock, so there is no re-render, no partial upload and no sweep.
            // =================================================================
            NanoSurface.DrawDebossedSlot(new Rect(mapX - 4f, mapY - 4f, mapCanvasSize + 8f, mapCanvasSize + 8f),
                new Color(0.036f, 0.028f, 0.022f, 0.96f), NanoBrand.Line, 9f);
            GUI.DrawTexture(mapCanvasRect, GetColorTexture(new Color(0.04f, 0.052f, 0.068f, 0.98f)));

            float viewWorldSpan = 128f / fullscreenZoom;
            float pxPerBlock = mapCanvasSize / viewWorldSpan;
            // Auto/Cave/Interior follow the player's current Y; only Slice pins an altitude.
            int activeSliceY = layerMode == "Slice" ? currentSliceY : Mathf.RoundToInt(playerPos.y);
            bool fullSurfaceStructure = IsSurfaceStructureContext(playerPos, IsIndoors);

            try
            {
                WayfinderChunkAtlas.Shared.DrawView(
                    mapCanvasRect, world, renderCenter.x, renderCenter.z,
                    Mathf.RoundToInt(playerPos.y), activeSliceY, MapCave,
                    layerMode, renderMode, gridOn, IsIndoors, pxPerBlock, fullSurfaceStructure);
            }
            catch (Exception ex) { LogMapException("fullscreen DrawView", ex); }

            Vector2 mapCenter = mapCanvasRect.center;

            Vector2 mousePos = Event.current.mousePosition;
            bool hoveringMap = mapCanvasRect.Contains(mousePos);

            float relX = mousePos.x - mapCenter.x;
            float relY = mousePos.y - mapCenter.y;
            int hoverWorldX = Mathf.RoundToInt(renderCenter.x + relX / pxPerBlock);
            int hoverWorldZ = Mathf.RoundToInt(renderCenter.z - relY / pxPerBlock);
            int sampledY = GetHoverSurfaceY(world, hoverWorldX, hoverWorldZ,
                Mathf.RoundToInt(playerPos.y), !inCaveSliceMode);

            // =================================================================
            // 4. MOUSE DRAG & ZOOM HANDLING (ONLY ON MAP CANVAS!)
            // =================================================================
            if (hoveringMap && Event.current.type == EventType.ScrollWheel)
            {
                float scrollDelta = -Event.current.delta.y;
                float oldZoom = fullscreenZoom;
                float newZoom = WayfinderMath.ClampZoom(oldZoom * (scrollDelta > 0 ? 1.25f : 0.80f));
                if (Mathf.Abs(newZoom - oldZoom) > 0.01f)
                {
                    // Keep the block under the cursor fixed instead of zooming toward the centre.
                    float anchorX = renderCenter.x + relX / pxPerBlock;
                    float anchorZ = renderCenter.z - relY / pxPerBlock;
                    float newPxPerBlock = mapCanvasSize * newZoom / 128f;
                    mapPanBlocksX = WayfinderMath.ClampPan(
                        WayfinderMath.PanXForAnchor(anchorX, playerPos.x, relX, newPxPerBlock));
                    mapPanBlocksZ = WayfinderMath.ClampPan(
                        WayfinderMath.PanZForAnchor(anchorZ, playerPos.z, relY, newPxPerBlock));
                    fullscreenZoom = newZoom;
                    Event.current.Use();
                }
            }

            if (Event.current.type == EventType.MouseDown && hoveringMap)
            {
                if (Event.current.button == 0)
                {
                    if (Event.current.clickCount == 2)
                    {
                        var created = new WaypointEntry
                        {
                            Name = $"Waypoint {waypoints.Count + 1}",
                            X = hoverWorldX,
                            Y = sampledY + 1,
                            Z = hoverWorldZ,
                            ColorIndex = newWpColorIndex,
                            Enabled = true
                        };
                        waypoints.Add(created);
                        SaveWaypoints();
                        BeginEditWaypoint(created, true);
                        isDraggingMap = false;
                        Event.current.Use();
                    }
                    else
                    {
                        isDraggingMap = true;
                        mapDragStartMouse = mousePos;
                        mapDragStartPanX = mapPanBlocksX;
                        mapDragStartPanZ = mapPanBlocksZ;
                        Event.current.Use();
                    }
                }
                else if (Event.current.button == 1)
                {
                    mapPanBlocksX = 0f;
                    mapPanBlocksZ = 0f;
                    Event.current.Use();
                }
            }
            else if (Event.current.type == EventType.MouseDrag && isDraggingMap)
            {
                Vector2 delta = mousePos - mapDragStartMouse;
                mapPanBlocksX = WayfinderMath.ClampPan(mapDragStartPanX - (delta.x / pxPerBlock));
                mapPanBlocksZ = WayfinderMath.ClampPan(mapDragStartPanZ + (delta.y / pxPerBlock));
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp && isDraggingMap)
            {
                isDraggingMap = false;
                Event.current.Use();
            }

            // Draw Waypoints on Fullscreen Map
            int offscreenSlot = 0;
            for (int i = 0; i < waypoints.Count; i++)
            {
                var wp = waypoints[i];
                if (!wp.Enabled) continue;

                float dx = wp.X - renderCenter.x;
                float dz = wp.Z - renderCenter.z;
                float wx = mapCenter.x + dx * pxPerBlock;
                float wy = mapCenter.y - dz * pxPerBlock;
                Color wpCol = GetWaypointColor(wp.ColorIndex);
                Vector2 wpScreen = new Vector2(wx, wy);

                if (mapCanvasRect.Contains(wpScreen))
                {
                    DrawWaypointMapMarker(wx, wy, wpCol, wp.IsDeath, 10f);

                    const float labelW = 130f;
                    float lx = Mathf.Clamp(wx + 8f,
                        mapCanvasRect.xMin + 2f, mapCanvasRect.xMax - labelW - 2f);
                    float ly = Mathf.Clamp(wy - 9f,
                        mapCanvasRect.yMin + 2f, mapCanvasRect.yMax - 18f);
                    DrawShadowLabel(new Rect(lx, ly, labelW, 18f), wp.Name, ToolStyle, wpCol, Color.black);
                }
                else
                {
                    // Waypoint is outside the panned/zoomed view: clamp it to the map edge in
                    // its true direction, with name and map-plane distance. The slot stacks
                    // multiple labels on the same edge so they do not paint over each other.
                    float mapDistance = Mathf.Sqrt(dx * dx + dz * dz);
                    string edgeLabel = $"{wp.Name} [{mapDistance:F0}m]";
                    DrawOffscreenWaypointMarker(
                        mapCanvasRect, mapCenter, wpScreen, wpCol, edgeLabel, wp.IsDeath,
                        offscreenSlot++);
                }
            }

            // Native radar POIs (beds, vehicles, teleporters, NPCs, spawn).
            RefreshRadarPois(playerPos, Mathf.Max(128f, viewWorldSpan));
            for (int i = 0; i < radarPois.Count; i++)
            {
                RadarPoi poi = radarPois[i];
                float dx = poi.Position.x - renderCenter.x;
                float dz = poi.Position.z - renderCenter.z;
                float wx = mapCenter.x + dx * pxPerBlock;
                float wy = mapCenter.y - dz * pxPerBlock;
                Vector2 poiPos = new Vector2(wx, wy);
                if (!mapCanvasRect.Contains(poiPos)) continue;

                // Never reveal an entity inside a black/unexplored room: the marker is only
                // drawn when the terrain pixel under it is actually rendered.
                if (!WayfinderChunkAtlas.Shared.IsRenderedExplored(
                        Mathf.RoundToInt(poi.Position.x), Mathf.RoundToInt(poi.Position.z),
                        layerMode, MapCave, IsIndoors, fullSurfaceStructure, renderMode, gridOn, activeSliceY))
                    continue;

                Color poiColor = GetPoiColor(poi.Icon);
                GUI.DrawTexture(new Rect(wx - 5f, wy - 5f, 10f, 10f), GetColorTexture(Color.black));
                GUI.DrawTexture(new Rect(wx - 4f, wy - 4f, 8f, 8f), GetColorTexture(poiColor));
                // Dozens of "NPC" labels hide the terrain; keep icons, label the unusual POIs.
                if (poi.Icon != RadarPoint.Icon.NPC || radarPois.Count <= 10)
                    DrawShadowLabel(new Rect(wx + 6f, wy - 9f, 120f, 18f), poi.Label, ToolStyle, poiColor, Color.black);
            }

            // Draw Player Position Chevron on Fullscreen Map (clean alpha blend, zero cyan box artifact!)
            float playerScreenX = mapCenter.x + (playerPos.x - renderCenter.x) * pxPerBlock;
            float playerScreenY = mapCenter.y - (playerPos.z - renderCenter.z) * pxPerBlock;
            bool isPlayerOnCanvas = mapCanvasRect.Contains(new Vector2(playerScreenX, playerScreenY));

            Vector2 playerDrawPos = new Vector2(
                Mathf.Clamp(playerScreenX, mapCanvasRect.xMin + 12f, mapCanvasRect.xMax - 12f),
                Mathf.Clamp(playerScreenY, mapCanvasRect.yMin + 12f, mapCanvasRect.yMax - 12f)
            );

            float playerYaw = cam != null ? cam.transform.eulerAngles.y : 0f;
            Matrix4x4 matBkp = GUI.matrix;
            GUIUtility.RotateAroundPivot(playerYaw, playerDrawPos);
            Color oldC = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(playerDrawPos.x - 12f, playerDrawPos.y - 12f, 24f, 24f), GetPlayerChevronTexture(), ScaleMode.StretchToFill, true);
            GUI.color = oldC;
            GUI.matrix = matBkp;

            if (!isPlayerOnCanvas)
            {
                GUI.DrawTexture(new Rect(playerDrawPos.x - 14f, playerDrawPos.y - 14f, 28f, 28f), GetColorTexture(new Color(0.25f, 0.85f, 1.0f, 0.45f)));
            }

            DrawMapInfoCard(mapCanvasRect, playerPos, world);

            // =================================================================
            // 5. BOTTOM MAP STATUS TOOLBAR
            // =================================================================
            float barY = mapY + mapCanvasSize + 8f;
            if (NanoSurface.DrawActionButton(new Rect(mapX, barY, 64f, 28f), "Zoom +", false))
                fullscreenZoom = WayfinderMath.ClampZoom(fullscreenZoom * 1.35f);
            if (NanoSurface.DrawActionButton(new Rect(mapX + 68f, barY, 64f, 28f), "Zoom −", false))
                fullscreenZoom = WayfinderMath.ClampZoom(fullscreenZoom * 0.75f);
            if (NanoSurface.DrawActionButton(new Rect(mapX + 136f, barY, 82f, 28f), es ? "Centrar" : "Center", false))
            {
                mapPanBlocksX = 0f;
                mapPanBlocksZ = 0f;
            }

            string biomeAtLoc = hoveringMap
                ? BiomeHelper.GetBiomeName(world, hoverWorldX, sampledY, hoverWorldZ, es)
                : BiomeHelper.GetBiomeName(world, Mathf.RoundToInt(playerPos.x), Mathf.RoundToInt(playerPos.y), Mathf.RoundToInt(playerPos.z), es);

            string coordInfo;
            if (hoveringMap)
            {
                string blockName = Tr("Air", "Aire");
                if (world != null)
                {
                    try
                    {
                        Block hb = WayfinderEngine.GetBlockSafe(world, hoverWorldX, sampledY, hoverWorldZ);
                        if (hb.id > 0)
                        {
                            BlockType bt = (hb.id < BlockTypes.blocks.Length) ? BlockTypes.blocks[hb.id] : null;
                            string rawName = bt != null ? bt.name : $"Block #{hb.id}";
                            blockName = GetLocalizedBlockName(rawName, ResolveBlockItem(rawName));
                        }
                    }
                    catch { }
                }
                int chunkX = hoverWorldX >> 5;
                int chunkZ = hoverWorldZ >> 5;
                coordInfo = $"{blockName}  ·  X:{hoverWorldX} Y:{sampledY} Z:{hoverWorldZ}  ·  [{chunkX},{chunkZ}]  ·  {biomeAtLoc}";
            }
            else
            {
                int pChunkX = Mathf.FloorToInt(playerPos.x) >> 5;
                int pChunkZ = Mathf.FloorToInt(playerPos.z) >> 5;
                coordInfo = $"{(es ? "Jugador" : "Player")}: X:{playerPos.x:F0} Y:{playerPos.y:F0} Z:{playerPos.z:F0}  ·  [{pChunkX},{pChunkZ}]  ·  {biomeAtLoc}";
            }

            Rect coordBg = new Rect(mapX + 224f, barY, Mathf.Max(80f, mapCanvasSize - 224f), 28f);
            NanoSurface.DrawDebossedSlot(coordBg, new Color(0.038f, 0.030f, 0.024f, 0.94f), NanoBrand.Line, 7f);
            NanoSurface.DrawCrispLabel(new Rect(coordBg.x + 8f, coordBg.y, coordBg.width - 16f, coordBg.height),
                coordInfo, _wfMonoSmallStyle, NanoBrand.Paper, 0.3f);

            // =================================================================
            // 6. RIGHT PANEL: WAYPOINTS & DISPLAY CONTROLS (Aether Ink Recessed)
            // =================================================================
            float rightH = winHeight - topBarH - 24f;
            Rect rightBg = new Rect(rightX, toolbarY, rightW, rightH);
            NanoSurface.DrawDebossedSlot(rightBg, new Color(0.040f, 0.032f, 0.026f, 0.92f), NanoBrand.Line, 10f);

            float rx = rightX + 12f;
            float rw = rightW - 24f;
            float ry = toolbarY + 10f;

            bool isEditing = editingWaypoint != null;
            string formTitle = isEditing
                ? (es ? "Editar waypoint" : "Edit waypoint")
                : (es ? "Nuevo waypoint" : "New waypoint");
            float headerRightX = isEditing ? (rx + rw - 122f) : (rx + rw - 58f);
            NanoSurface.DrawCrispLabel(new Rect(rx, ry, Mathf.Max(80f, headerRightX - rx - 6f), 22f),
                formTitle.ToUpperInvariant(), _wfMonoSmallStyle, isEditing ? NanoBrand.EmberSoft : NanoBrand.Mute, 0.25f);
            if (isEditing)
            {
                if (NanoSurface.DrawActionButton(new Rect(rx + rw - 122f, ry - 1f, 58f, 22f), es ? "Nuevo" : "New", false))
                    CancelWaypointEdit();
                if (NanoSurface.DrawActionButton(new Rect(rx + rw - 60f, ry - 1f, 60f, 22f), es ? "Cancelar" : "Cancel", false))
                    CancelWaypointEdit();
            }
            else if (NanoSurface.DrawActionButton(new Rect(rx + rw - 58f, ry - 1f, 58f, 22f), es ? "Limpiar" : "Clear", false))
            {
                CancelWaypointEdit();
            }
            ry += 26f;

            NanoSurface.DrawCrispLabel(new Rect(rx, ry, 48f, 24f), es ? "Nombre" : "Name", _wfLabelStyle, NanoBrand.Mute, 0.25f);
            newWpName = DrawWayfinderTextField(new Rect(rx + 50f, ry, rw - 146f, 24f), newWpName ?? "", 28);
            if (NanoSurface.DrawActionButton(new Rect(rx + rw - 92f, ry, 92f, 24f), es ? "Mi posición" : "My Pos", false))
            {
                newWpX = Mathf.RoundToInt(playerPos.x).ToString();
                newWpY = GetStandingY(world, playerPos).ToString();
                newWpZ = Mathf.RoundToInt(playerPos.z).ToString();
            }
            ry += 30f;

            float fieldW = (rw - 72f) / 3f;
            NanoSurface.DrawCrispLabel(new Rect(rx, ry, 16f, 24f), "X", _wfMonoSmallStyle, NanoBrand.Bad, 0.25f);
            newWpX = DrawWayfinderTextField(new Rect(rx + 16f, ry, fieldW, 24f), newWpX ?? "0", 8);

            float yStart = rx + 24f + fieldW;
            NanoSurface.DrawCrispLabel(new Rect(yStart, ry, 16f, 24f), "Y", _wfMonoSmallStyle, NanoBrand.Ok, 0.25f);
            newWpY = DrawWayfinderTextField(new Rect(yStart + 16f, ry, fieldW, 24f), newWpY ?? "64", 8);

            float zStart = yStart + 24f + fieldW;
            NanoSurface.DrawCrispLabel(new Rect(zStart, ry, 16f, 24f), "Z", _wfMonoSmallStyle, new Color(0.56f, 0.78f, 0.94f), 0.25f);
            newWpZ = DrawWayfinderTextField(new Rect(zStart + 16f, ry, fieldW, 24f), newWpZ ?? "0", 8);
            ry += 30f;

            // Squircle color swatches + Save button
            for (int c = 0; c < WaypointColors.Length; c++)
            {
                Rect colRect = new Rect(rx + c * 26f, ry + 3f, 20f, 20f);
                bool sel = (c == newWpColorIndex);
                if (Event.current.type == EventType.Repaint)
                {
                    NanoSurface.FillRounded(colRect, WaypointColors[c], 5f);
                    if (sel)
                        NanoSurface.StrokeRounded(new Rect(colRect.x - 2f, colRect.y - 2f, colRect.width + 4f, colRect.height + 4f), NanoBrand.PaperLight, 1.5f, 7f);
                    else
                        NanoSurface.StrokeRounded(colRect, new Color(0f, 0f, 0f, 0.55f), 1f, 5f);
                }
                if (GUI.Button(colRect, GUIContent.none, GUIStyle.none))
                    newWpColorIndex = c;
            }

            string saveLabel = isEditing
                ? (es ? "Aplicar cambios" : "Apply Changes")
                : (es ? "+ Guardar" : "+ Save");
            if (NanoSurface.DrawActionButton(new Rect(rx + 162f, ry, rw - 162f, 26f), saveLabel, true))
            {
                bool okX = int.TryParse(newWpX, out int px);
                bool okY = int.TryParse(newWpY, out int py);
                bool okZ = int.TryParse(newWpZ, out int pz);
                if (!(okX && okY && okZ))
                {
                    Log.LogWarning("[Wayfinder] Waypoint coords must be whole numbers; using the player's position instead.");
                    px = Mathf.RoundToInt(playerPos.x);
                    py = GetStandingY(world, playerPos);
                    pz = Mathf.RoundToInt(playerPos.z);
                }
                string finalName = string.IsNullOrWhiteSpace(newWpName) ? $"Waypoint {waypoints.Count + 1}" : newWpName.Trim();

                WaypointEntry target = isEditing && waypoints.Contains(editingWaypoint) ? editingWaypoint : null;
                if (target == null)
                {
                    target = new WaypointEntry { Enabled = true };
                    waypoints.Add(target);
                }

                target.Name = finalName;
                target.X = px;
                target.Y = py;
                target.Z = pz;
                target.ColorIndex = newWpColorIndex;
                SaveWaypoints();

                editingWaypoint = null;
                editingWaypointIsNew = false;
                newWpName = $"Waypoint {waypoints.Count + 1}";
                newWpColorIndex = (newWpColorIndex + 1) % WaypointColors.Length;
            }
            ry += 34f;

            // 3D Markers & Beams toggles side by side
            float halfRw = (rw - 6f) * 0.5f;
            ShowWorldWaypoints.Value = NanoSurface.DrawToggleRow(
                new Rect(rx, ry, halfRw, 28f), es ? "Marcadores 3D" : "3D Markers", ShowWorldWaypoints.Value);
            if (ShowWaypointBeacons != null)
            {
                ShowWaypointBeacons.Value = NanoSurface.DrawToggleRow(
                    new Rect(rx + halfRw + 6f, ry, halfRw, 28f), es ? "Haces 3D" : "3D Beams", ShowWaypointBeacons.Value);
            }
            ry += 34f;

            // Custom NanoSurface Display Sliders (zero ugly GUI.HorizontalSlider)
            float newMiniSize = NanoSurface.DrawSlider(new Rect(rx, ry, rw, 22f),
                es ? "Minimapa" : "Minimap", MinimapSize.Value, 110f, 340f, $"{MinimapSize.Value:F0} px", 2f, 76f);
            if (Mathf.Abs(newMiniSize - MinimapSize.Value) > 0.5f) MinimapSize.Value = newMiniSize;
            ry += 26f;

            float newScale = NanoSurface.DrawSlider(new Rect(rx, ry, rw, 22f),
                es ? "Ventana" : "Window", FullscreenMapScale.Value, 0.65f, 1.0f, $"{FullscreenMapScale.Value * 100f:F0}%", 0.01f, 76f);
            if (Mathf.Abs(newScale - FullscreenMapScale.Value) > 0.005f) FullscreenMapScale.Value = newScale;
            ry += 28f;

            if (layerMode == "Slice")
            {
                NanoSurface.DrawCrispLabel(new Rect(rx, ry, 24f, 22f), "Y:", _wfMonoSmallStyle, NanoBrand.Ember, 0.25f);
                if (NanoSurface.DrawActionButton(new Rect(rx + 24f, ry, 42f, 22f), "−10", false)) SetSliceY(currentSliceY - 10);
                NanoSurface.DrawCrispLabel(new Rect(rx + 68f, ry, 52f, 22f), currentSliceY.ToString(), _wfMonoCenterStyle, NanoBrand.PaperLight, 0.3f);
                if (NanoSurface.DrawActionButton(new Rect(rx + 122f, ry, 42f, 22f), "+10", false)) SetSliceY(currentSliceY + 10);
                if (NanoSurface.DrawActionButton(new Rect(rx + 170f, ry, 68f, 22f), es ? "Mi Y" : "My Y", false))
                    SetSliceY(Mathf.RoundToInt(playerPos.y));
                ry += 28f;
            }

            // Persistence status bar
            int knownRegions = SurfaceMapStore != null ? SurfaceMapStore.KnownRegionCount : 0;
            long surfaceCells = SurfaceMapStore != null ? SurfaceMapStore.KnownCellCount : 0;
            long caveCells = CaveMapStore != null ? CaveMapStore.KnownCellCount : 0;
            bool anyPersistence = SurfaceMapStore != null || CaveMapStore != null;
            string mapData = anyPersistence
                ? $"{knownRegions} reg · {surfaceCells:N0} sup · {caveCells:N0} cue"
                : (es ? "Persistencia desactivada" : "Map persistence off");
            NanoSurface.DrawCrispLabel(new Rect(rx, ry, rw - (anyPersistence ? 82f : 0f), 20f), mapData, _wfMonoSmallStyle, NanoBrand.Mute, 0.25f);
            if (anyPersistence && NanoSurface.DrawActionButton(new Rect(rx + rw - 78f, ry - 1f, 78f, 22f), es ? "Guardar" : "Save map", false))
            {
                try { SurfaceMapStore?.FlushAll(); } catch { }
                try { CaveMapStore?.FlushAll(); } catch { }
            }
            ry += 24f;

            NanoSurface.FillRounded(new Rect(rx, ry, rw, 1f), NanoBrand.Line, 0.5f);
            ry += 8f;

            // Saved Waypoints Header
            NanoSurface.DrawCrispLabel(new Rect(rx, ry, rw - 156f, 22f),
                (es ? $"WAYPOINTS ({waypoints.Count})" : $"WAYPOINTS ({waypoints.Count})"),
                _wfMonoSmallStyle, NanoBrand.Mute, 0.25f);

            bool allEnabled = waypoints.Count > 0;
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (!waypoints[i].Enabled) { allEnabled = false; break; }
            }

            string sortLabel = waypointSortByDistance ? (es ? "Dist" : "Dist") : (es ? "Nombre" : "Name");
            if (NanoSurface.DrawActionButton(new Rect(rx + rw - 148f, ry - 1f, 68f, 22f), sortLabel, false))
            {
                waypointSortByDistance = !waypointSortByDistance;
                SortWaypoints(playerPos);
            }
            string allLabel = allEnabled ? (es ? "Todo NO" : "All OFF") : (es ? "Todo SÍ" : "All ON");
            if (NanoSurface.DrawActionButton(new Rect(rx + rw - 76f, ry - 1f, 76f, 22f), allLabel, allEnabled))
                ToggleAllWaypoints();

            float wpListY = ry + 26f;
            float wpListH = Mathf.Max(60f, rightBg.yMax - wpListY - 10f);
            Rect wpListRect = new Rect(rx, wpListY, rw, wpListH);
            float wpTotalH = waypoints.Count * 52f;

            wpScrollPos = NanoSurface.BeginScrollView(wpListRect, wpScrollPos, wpTotalH, out float wpRowW);

            for (int i = 0; i < waypoints.Count; i++)
            {
                var wp = waypoints[i];
                Rect rowRect = new Rect(0, i * 52f, wpRowW, 46f);
                bool isSel = (wp == editingWaypoint);
                bool hov = rowRect.Contains(Event.current.mousePosition);
                Color wpCol = GetWaypointColor(wp.ColorIndex);

                if (Event.current.type == EventType.Repaint)
                {
                    Color rBg = isSel
                        ? new Color(0.15f, 0.115f, 0.088f, 0.96f)
                        : (hov ? new Color(0.105f, 0.082f, 0.064f, 0.92f) : new Color(0.068f, 0.054f, 0.044f, 0.86f));
                    Color rBd = isSel ? new Color(NanoBrand.Ember.r, NanoBrand.Ember.g, NanoBrand.Ember.b, 0.62f) : NanoBrand.Line;
                    NanoSurface.FillRounded(rowRect, rBg, 7f);
                    NanoSurface.StrokeRounded(rowRect, rBd, 1f, 7f);

                    // Clean rhombus / death skull sigil (zero decorative vertical side stripes!)
                    if (wp.IsDeath)
                    {
                        Texture2D skull = GetWaypointDeathSkullTexture();
                        if (skull != null)
                            GUI.DrawTexture(new Rect(rowRect.x + 8f, rowRect.y + 15f, 15f, 15f), skull);
                        else
                            NanoSurface.DrawRhombus(new Rect(rowRect.x + 10f, rowRect.y + 18f, 9f, 9f), wpCol, wp.Enabled);
                    }
                    else
                    {
                        NanoSurface.DrawRhombus(new Rect(rowRect.x + 10f, rowRect.y + 18f, 9f, 9f), wpCol, wp.Enabled);
                    }
                }

                float xX = rowRect.width - 28f;
                float xE = xX - 30f;
                float xOn = xE - 38f;
                float textX = rowRect.x + 28f;
                float textW = Mathf.Max(50f, xOn - textX - 4f);

                // Clicking the name or coordinates pans/centers on this waypoint
                if (GUI.Button(new Rect(textX, rowRect.y + 3f, textW, 40f), GUIContent.none, GUIStyle.none))
                    FindWaypointOnMap(wp);

                float dist = Vector3.Distance(playerPos, new Vector3(wp.X, wp.Y, wp.Z));
                NanoSurface.DrawCrispLabel(new Rect(textX, rowRect.y + 5f, textW, 18f),
                    $"{wp.Name}  ·  {dist:F0}m", _wfRowTitleStyle, wp.Enabled ? NanoBrand.PaperLight : NanoBrand.Mute, 0.35f);
                NanoSurface.DrawCrispLabel(new Rect(textX, rowRect.y + 24f, textW, 16f),
                    $"X {wp.X}  Y {wp.Y}  Z {wp.Z}", _wfRowSubStyle, NanoBrand.Mute, 0.25f);

                string visTxt = wp.Enabled ? (es ? "SÍ" : "ON") : (es ? "NO" : "OFF");
                if (NanoSurface.DrawActionButton(new Rect(xOn, rowRect.y + 10f, 34f, 26f), visTxt, wp.Enabled))
                {
                    wp.Enabled = !wp.Enabled;
                    SaveWaypoints();
                }

                if (NanoSurface.DrawActionButton(new Rect(xE, rowRect.y + 10f, 26f, 26f), "✎", isSel))
                    BeginEditWaypoint(wp);

                if (NanoSurface.DrawActionButton(new Rect(xX, rowRect.y + 10f, 24f, 26f), "×", false))
                {
                    if (editingWaypoint == wp)
                    {
                        editingWaypoint = null;
                        editingWaypointIsNew = false;
                    }
                    waypoints.RemoveAt(i);
                    SaveWaypoints();
                    break;
                }
            }

            NanoSurface.EndScrollView();

            if (isWayfinderOpen && Event.current.type == EventType.MouseDown && (Time.unscaledTime - ModalOpenedTimestamp > 0.35f) && !winRect.Contains(Event.current.mousePosition))
            {
                SetWayfinderOpen(false);
                Event.current.Use();
            }
            }
        }

        public static bool IsAirOrPassable(ushort id)
        {
            if (id <= 2 || id == 68) return true;
            BlockType bt = (id < BlockTypes.blocks.Length) ? BlockTypes.blocks[id] : null;
            return bt == null || bt.air || !bt.visible;
        }

        public static bool IsSolidOpaqueCeiling(ushort id)
        {
            if (id <= 2 || id == 68) return false;
            switch (id)
            {
                case 4:   // Blue Flower
                case 5:   // Yellow Flower
                case 6:   // Red Flower
                case 15:  // Leaves
                case 16:  // Palm Leaves
                case 23:  // Red Leaves
                case 27:  // Cactus
                case 29:  // Torch
                case 31:  // Glass
                case 40:  // Yellow Coral
                case 41:  // Green Coral
                case 42:  // Sea Grass
                case 43:  // Birch Leaves
                case 45:  // Baobab Leaves
                case 46:  // Pine Leaves
                case 47:  // Oak Leaves
                case 53:  // Jungle Leaves
                case 56:  // Swamp Grass
                case 163: // Moss
                case 164: // Space Leafs
                case 245: // Lost Leaves
                case 246: // Autumn Leaves
                case 249: // Sakura Leaves
                    return false;
            }

            BlockType bt = (id < BlockTypes.blocks.Length) ? BlockTypes.blocks[id] : null;
            if (bt == null || bt.air || bt.liquid || !bt.visible)
                return false;

            return true;
        }

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
    }

    // =========================================================================
    //       WAYFINDER TOPOGRAPHICAL TERRAIN SAMPLER & COLOR PALETTE
    // =========================================================================
    public struct TerrainSample
    {
        public ushort BlockId;
        public int TopY;
        public bool IsLiquid;
        public ushort LiquidId;
        public int WaterDepth;
        public ushort BedBlockId;
        public ushort PaintHue;
        public bool IsPainted;
        public bool IsCaveWall;
        public bool IsOre;
        /// <summary>Column has open walkable space at the sampled slice (room, corridor, cave).</summary>
        public bool IsOpenRoom;
        public bool FromStore;
        /// <summary>Pixel came from the tile cache: no live block id, but a real colour/height.</summary>
        public bool FromCache;
        public bool IsSurfaceFallback;
        public uint StoredBiomeTint;
    }

    // =========================================================================
    //                 REAL 69-BIOME + VOLCANO LOOKUP HELPER
    // =========================================================================
    public static class BiomeHelper
    {
        private static readonly Dictionary<Biome, (string En, string Es)> BiomeNames = new Dictionary<Biome, (string, string)>
        {
            { Biome.Greens, ("Greens", "Praderas") },
            { Biome.Desert, ("Desert", "Desierto") },
            { Biome.DesertMountain, ("Desert Mountain", "Montaña Desértica") },
            { Biome.Sinkhole, ("Sinkhole", "Sumidero") },
            { Biome.Mountain, ("Mountain", "Montaña") },
            { Biome.Rock, ("Rock Barren", "Paredón Rocoso") },
            { Biome.Thundra, ("Tundra", "Tundra") },
            { Biome.Beach, ("Beach", "Playa") },
            { Biome.OakForest, ("Oak Forest", "Bosque de Robles") },
            { Biome.PlainsForest, ("Plains Forest", "Bosque Llano") },
            { Biome.Jungle, ("Jungle", "Jungla") },
            { Biome.River, ("River", "Río") },
            { Biome.ShallowSea, ("Shallow Sea", "Mar Poco Profundo") },
            { Biome.DeepSea, ("Deep Sea", "Mar Profundo") },
            { Biome.HumanTown, ("Human Town", "Aldea Humana") },
            { Biome.HobbitTown, ("Hobbit Town", "Aldea Hobbit") },
            { Biome.OrkTown, ("Orc Town", "Aldea Orco") },
            { Biome.EskimoTown, ("Eskimo Village", "Aldea Esquimal") },
            { Biome.Atlantis, ("Atlantis", "Atlántida") },
            { Biome.Stratosphere, ("Stratosphere", "Estratósfera") },
            { Biome.Bridge, ("Sky Bridge", "Puente Celeste") },
            { Biome.Cave, ("Cave System", "Sistema de Cuevas") },
            { Biome.WaterSurface, ("Water Surface", "Superficie de Agua") },
            { Biome.MediumSea, ("Ocean", "Océano") },
            { Biome.Hades, ("Hades", "Inframundo (Hades)") },
            { Biome.LavaRiver, ("Lava River", "Río de Lava") },
            { Biome.LavaSea, ("Lava Sea", "Mar de Lava") },
            { Biome.Shore, ("Shore", "Costa") },
            { Biome.SequoiaForest, ("Sequoia Forest", "Bosque de Secuoyas") },
            { Biome.SkyTown, ("Sky Island Town", "Aldea Flotante") },
            { Biome.Exosphere, ("Exosphere", "Exósfera") },
            { Biome.ExosphereSpaceShips, ("Alien Fleet", "Flota Espacial") },
            { Biome.SpaceUrban, ("Space Metropolis", "Metrópolis Espacial") },
            { Biome.SpaceVisitors, ("Alien Outpost", "Puesto Alienígena") },
            { Biome.SpaceVisitorsTown, ("Alien Colony", "Colonia Alienígena") },
            { Biome.SpaceAcidRiver, ("Acid River", "Río de Ácido") },
            { Biome.SpaceAcidSea, ("Acid Ocean", "Océano de Ácido") },
            { Biome.SpacePoisonLands, ("Poison Wastes", "Tierras Tóxicas") },
            { Biome.SpaceMushroomTown, ("Fungal Village", "Aldea Fúngica") },
            { Biome.SpaceSwampRiver, ("Toxic Swamp River", "Río de Pantano Tóxico") },
            { Biome.SpaceSwampSea, ("Toxic Swamp Sea", "Mar de Pantano Tóxico") },
            { Biome.SpaceDeepSwampSea, ("Abyssal Toxic Sea", "Mar Tóxico Abisal") },
            { Biome.SpaceGreens, ("Alien Plains", "Llanuras Alienígenas") },
            { Biome.SpaceForest, ("Alien Forest", "Bosque Alienígena") },
            { Biome.SpaceRiver, ("Space River", "Río Espacial") },
            { Biome.SpaceSea, ("Cosmic Sea", "Mar Cósmico") },
            { Biome.SpaceDeepSea, ("Abyssal Void Sea", "Mar Abisal Cósmico") },
            { Biome.SpaceLightBlueMushrooms, ("Cyan Mushroom Forest", "Bosque Fúngico Cian") },
            { Biome.SpaceBlueMushrooms, ("Cobalt Mushroom Forest", "Bosque Fúngico Azul") },
            { Biome.SpacePurpleMushrooms, ("Amethyst Mushroom Forest", "Bosque Fúngico Violeta") },
            { Biome.SpaceGiantMushrooms, ("Giant Mushroom Canopy", "Canopia de Hongos Gigantes") },
            { Biome.SpaceSwampSeaSurface, ("Toxic Sea Surface", "Superficie Marina Tóxica") },
            { Biome.SpaceJungleVillage, ("Space Jungle Village", "Aldea Selva Espacial") },
            { Biome.SpaceAlienVillage, ("Alien Settlement", "Asentamiento Alienígena") },
            { Biome.SpaceShallowAcid, ("Shallow Acid Pool", "Charca de Ácido") },
            { Biome.IceDeepSea, ("Abyssal Frozen Sea", "Mar Helado Profundo") },
            { Biome.WaterTown, ("Ocean Settlement", "Asentamiento Marino") },
            { Biome.IceWaterTown, ("Glacial Settlement", "Asentamiento Glaciar") },
            { Biome.IceShallowSea, ("Shallow Ice Sea", "Mar Helado Costero") },
            { Biome.IceMediumSea, ("Glacial Ocean", "Océano Glaciar") },
            { Biome.IceWaterSurface, ("Glacial Waters", "Aguas Glaciares") },
            { Biome.SpaceSwampSeaTown, ("Toxic Marsh Town", "Pueblo Pantano Tóxico") },
            { Biome.Moria, ("Moria Depths", "Profundidades de Moria") },
            { Biome.Old, ("Ancient Lands", "Tierras Antiguas") },
            { Biome.Nebula, ("Nebula Sector", "Sector Nébula") },
            { Biome.DeepVoid, ("Deep Void", "Vacío Profundo") },
            { Biome.Realm, ("The Realm", "El Reino") },
            { Biome.CherryGrove, ("Cherry Grove", "Arboleda de Cerezos") },
            { Biome.AutumnForest, ("Autumn Forest", "Bosque de Otoño") }
        };

        public static string GetBiomeName(IWorld world, int x, int y, int z, bool isSpanish)
        {
            if (world == null) return isSpanish ? "Desconocido" : "Unknown";
            try
            {
                Vector3i voxel = new Vector3i(x, y, z);
                SurfaceProperties env = world.GetEnvironment(voxel, out int yadj);
                if (BiomeSpawner.IsVolcano(ref env, voxel, yadj, 40))
                {
                    return isSpanish ? "Volcán" : "Volcano";
                }

                if (BiomeNames.TryGetValue(env.biome, out var pair))
                {
                    return isSpanish ? pair.Es : pair.En;
                }

                return env.biome.ToString();
            }
            catch
            {
                return isSpanish ? "Tierras Salvajes" : "Wilderness";
            }
        }

        public static Color GetBiomeTint(IWorld world, int x, int y, int z)
        {
            if (world == null) return Color.white;
            try
            {
                Vector3i voxel = new Vector3i(x, y, z);
                SurfaceProperties env = world.GetEnvironment(voxel, out int yadj);
                if (BiomeSpawner.IsVolcano(ref env, voxel, yadj, 40))
                {
                    return new Color(0.95f, 0.28f, 0.12f);
                }
                return GetBiomeTint(env.biome);
            }
            catch
            {
                return Color.white;
            }
        }

        public static Color GetBiomeTint(Biome biome)
        {
            switch (biome)
            {
                case Biome.Greens: return new Color(0.35f, 0.85f, 0.30f);
                case Biome.Desert:
                case Biome.DesertMountain: return new Color(0.92f, 0.82f, 0.42f);
                case Biome.Mountain:
                case Biome.Rock: return new Color(0.68f, 0.70f, 0.75f);
                case Biome.Thundra:
                case Biome.IceDeepSea:
                case Biome.IceShallowSea:
                case Biome.IceMediumSea:
                case Biome.IceWaterSurface:
                case Biome.IceWaterTown:
                case Biome.EskimoTown: return new Color(0.75f, 0.90f, 1.0f);
                case Biome.OakForest:
                case Biome.PlainsForest:
                case Biome.SequoiaForest: return new Color(0.18f, 0.60f, 0.22f);
                case Biome.Jungle: return new Color(0.12f, 0.80f, 0.28f);
                case Biome.River:
                case Biome.ShallowSea:
                case Biome.MediumSea:
                case Biome.DeepSea:
                case Biome.WaterSurface:
                case Biome.WaterTown:
                case Biome.Atlantis: return new Color(0.20f, 0.55f, 0.95f);
                case Biome.Hades:
                case Biome.LavaRiver:
                case Biome.LavaSea: return new Color(0.95f, 0.32f, 0.15f);
                case Biome.Cave:
                case Biome.Moria: return new Color(0.48f, 0.38f, 0.58f);
                case Biome.SpaceUrban:
                case Biome.SpaceVisitors:
                case Biome.SpaceVisitorsTown:
                case Biome.Exosphere:
                case Biome.ExosphereSpaceShips:
                case Biome.Nebula:
                case Biome.DeepVoid:
                case Biome.Realm: return new Color(0.60f, 0.30f, 0.85f);
                case Biome.SpaceAcidRiver:
                case Biome.SpaceAcidSea:
                case Biome.SpaceShallowAcid: return new Color(0.45f, 0.95f, 0.20f);
                case Biome.SpacePoisonLands:
                case Biome.SpaceSwampRiver:
                case Biome.SpaceSwampSea:
                case Biome.SpaceDeepSwampSea:
                case Biome.SpaceSwampSeaSurface:
                case Biome.SpaceSwampSeaTown: return new Color(0.40f, 0.75f, 0.35f);
                case Biome.SpaceLightBlueMushrooms: return new Color(0.30f, 0.85f, 0.95f);
                case Biome.SpaceBlueMushrooms: return new Color(0.25f, 0.45f, 0.95f);
                case Biome.SpacePurpleMushrooms: return new Color(0.75f, 0.30f, 0.90f);
                case Biome.SpaceGiantMushrooms:
                case Biome.SpaceMushroomTown: return new Color(0.85f, 0.40f, 0.65f);
                case Biome.CherryGrove: return new Color(0.96f, 0.65f, 0.80f);
                case Biome.AutumnForest: return new Color(0.92f, 0.52f, 0.18f);
                case Biome.Beach:
                case Biome.Shore: return new Color(0.95f, 0.88f, 0.60f);
                default: return Color.white;
            }
        }
    }

    // =========================================================================
    //        2D WORLD TILE CACHE (512x512, ZERO ALLOCATION, O(1) LOOKUP)
    // =========================================================================
    public static class WayfinderTileCache
    {
        public const int CacheSize = 512;
        public const int CacheMask = 511;
        public const int TotalEntries = CacheSize * CacheSize;

        /// <summary>Cached pixel is a cave wall (needed by the wall-edge probe when the
        /// neighbour pixel comes from the cache instead of a live sample).</summary>
        public const byte FlagCaveWall = 1;
        /// <summary>Cached pixel is open walkable room/corridor at the sampled slice.</summary>
        public const byte FlagOpenRoom = 2;

        private static readonly Color32[] colors = new Color32[TotalEntries];
        private static readonly short[] heights = new short[TotalEntries];
        private static readonly int[] tagX = new int[TotalEntries];
        private static readonly int[] tagZ = new int[TotalEntries];
        private static readonly short[] tagSliceY = new short[TotalEntries];
        private static readonly byte[] tagMode = new byte[TotalEntries];
        private static readonly byte[] tagFlags = new byte[TotalEntries];
        private static readonly int[] tagEpoch = new int[TotalEntries];

        private static int currentEpoch = 1;

        static WayfinderTileCache()
        {
            Invalidate();
        }

        public static void Invalidate()
        {
            currentEpoch++;
            if (currentEpoch == int.MaxValue)
            {
                currentEpoch = 1;
                Array.Clear(tagEpoch, 0, tagEpoch.Length);
            }
        }

        public static bool TryGet(int wx, int wz, byte mode, int sliceY, out Color32 color, out short height)
        {
            return TryGet(wx, wz, mode, sliceY, out color, out height, out _);
        }

        public static bool TryGet(int wx, int wz, byte mode, int sliceY, out Color32 color, out short height, out byte flags)
        {
            int idx = (wx & CacheMask) + ((wz & CacheMask) << 9);
            if (tagEpoch[idx] == currentEpoch &&
                tagX[idx] == wx &&
                tagZ[idx] == wz &&
                tagMode[idx] == mode &&
                (mode == 0 || tagSliceY[idx] == (short)sliceY))
            {
                color = colors[idx];
                height = heights[idx];
                flags = tagFlags[idx];
                return true;
            }
            color = default;
            height = 0;
            flags = 0;
            return false;
        }

        public static void Put(int wx, int wz, byte mode, int sliceY, Color32 color, short height, byte flags = 0)
        {
            int idx = (wx & CacheMask) + ((wz & CacheMask) << 9);
            tagEpoch[idx] = currentEpoch;
            tagX[idx] = wx;
            tagZ[idx] = wz;
            tagMode[idx] = mode;
            tagSliceY[idx] = (short)sliceY;
            tagFlags[idx] = flags;
            colors[idx] = color;
            heights[idx] = height;
        }
    }

    // =========================================================================
    //                 WAYFINDER MULTI-LAYER TERRAIN ENGINE
    // =========================================================================
    public class WayfinderEngine
    {
        private readonly int width;
        private readonly int height;
        private readonly Color32[] pixels;
        private readonly short[] heights;
        private readonly TerrainSample[] samples;
        private readonly List<(int x, int z, Color color)> lightSources = new List<(int x, int z, Color color)>(64);
        private readonly Texture2D texture;

        private int lastCenterX;
        private int lastCenterZ;
        private int lastStep = 1;
        private bool lastCircleMask;
        private float lastMaskSpan = -1f;
        private bool hasUploaded;
        private byte lastModeByte = 255;
        private int lastSliceYForUpload = int.MinValue;
        private static float lastSlowMapLog;
        private bool renderComplete = true;

        // Unloaded/empty columns are retried later, not every frame. Without this, a cave
        // layer with no data kept HasPendingWork true forever and re-rendered every Repaint.
        private const float MissRetrySeconds = 1.0f;
        private readonly int[] negativeKey;
        private readonly float[] negativeExpiry;

        private static readonly FieldInfo pwChunksField = typeof(PriorityWorld).GetField("chunks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo pwC0xField = typeof(PriorityWorld).GetField("c0x", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo pwC0yField = typeof(PriorityWorld).GetField("c0y", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo pwC0zField = typeof(PriorityWorld).GetField("c0z", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo chunkBlocksField = typeof(Chunk).GetField("blocks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public Texture2D Texture => texture;
        public int Width => width;
        public int Height => height;
        /// <summary>True while a progressive pass still has unsampled columns.</summary>
        public bool HasPendingWork => !renderComplete;
        public int LastCenterX => lastCenterX;
        public int LastCenterZ => lastCenterZ;
        public int LastStep => lastStep;

        public WayfinderEngine(int w, int h)
        {
            width = w;
            height = h;
            pixels = new Color32[w * h];
            heights = new short[w * h];
            samples = new TerrainSample[w * h];
            negativeKey = new int[w * h];
            negativeExpiry = new float[w * h];
            texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        public int GetSampledHeightAtWorld(int wx, int wz, int fallbackY, bool allowStore = true)
        {
            int halfW = width / 2;
            int halfH = height / 2;
            int gx = (wx - lastCenterX) / Mathf.Max(1, lastStep) + halfW;
            int gz = (wz - lastCenterZ) / Mathf.Max(1, lastStep) + halfH;
            if (gx >= 0 && gx < width && gz >= 0 && gz < height)
            {
                short h = heights[gz * width + gx];
                if (h > -500) return h;
            }
            if (WayfinderTileCache.TryGet(wx, wz, 0, 0, out _, out short cachedH) && cachedH > -500)
            {
                return cachedH;
            }
            WayfinderStore store = WayfinderModule.SurfaceMapStore;
            if (allowStore && store != null && store.TryGetSurface(wx, wz, out WayfinderCell cell) && cell.TopY > -500)
            {
                return cell.TopY;
            }
            return fallbackY;
        }

        public void RenderTerrain(
            IWorld world,
            Vector3 centerPos,
            int step = 1,
            string layerMode = "Auto",
            int sliceY = 64,
            bool isPlayerUnderground = false,
            bool isCircleMask = false,
            bool isDragging = false,
            float maskSpan = 1f,
            int columnBudget = int.MaxValue)
        {
            if (world == null) return;
            long renderStart = Stopwatch.GetTimestamp();

            int px = Mathf.RoundToInt(centerPos.x);
            int py = Mathf.RoundToInt(centerPos.y);
            int pz = Mathf.RoundToInt(centerPos.z);

            int requestedStep = Mathf.Max(1, step);
            bool stepChanged = lastStep != requestedStep;
            bool centerChanged = px != lastCenterX || pz != lastCenterZ;
            lastCenterX = px;
            lastCenterZ = pz;
            lastStep = requestedStep;

            string renderType = (WayfinderModule.MapRenderMode != null && !string.IsNullOrEmpty(WayfinderModule.MapRenderMode.Value)) ? WayfinderModule.MapRenderMode.Value : "Day";
            bool showGrid = (WayfinderModule.ShowChunkGrid != null && WayfinderModule.ShowChunkGrid.Value) || renderType == "Biome";

            bool effectiveCave = (layerMode == "Cave" || layerMode == "Slice" || (layerMode == "Auto" && isPlayerUnderground));
            int effectiveSliceY = (layerMode == "Slice") ? sliceY : py;

            byte renderTypeIdx = (byte)(renderType == "Night" ? 1 : (renderType == "Topo" ? 2 : (renderType == "Biome" ? 3 : 0)));
            byte modeByte = (byte)((effectiveCave ? 1 : 0) + (renderTypeIdx << 2) + (showGrid ? 16 : 0));
            bool modeChanged = modeByte != lastModeByte || effectiveSliceY != lastSliceYForUpload;

            Chunk[,,] pwChunks = null;
            int c0x = 0, c0y = 0, c0z = 0;

            if (world is PriorityWorld pw && pwChunksField != null)
            {
                try
                {
                    pwChunks = pwChunksField.GetValue(pw) as Chunk[,,];
                    c0x = (int)pwC0xField.GetValue(pw);
                    c0y = (int)pwC0yField.GetValue(pw);
                    c0z = (int)pwC0zField.GetValue(pw);
                }
                catch { pwChunks = null; }
            }

            int halfW = width / 2;
            int halfH = height / 2;

            Color32 gridDark = effectiveCave ? new Color32(14, 14, 18, 255) : new Color32(18, 22, 28, 255);
            Color32 gridLine = effectiveCave ? new Color32(22, 22, 28, 255) : new Color32(26, 32, 42, 255);

            // Every pass scans the whole grid and fills it from the tile cache. Only uncached
            // columns are budgeted; a shifted view therefore reuses almost everything and only
            // the new edge appears dark for a frame or two instead of clearing the whole map.
            bool textureDirty = centerChanged || modeChanged || stepChanged || !hasUploaded;
            int budget = isDragging ? 64 : (columnBudget <= 0 ? int.MaxValue : columnBudget);
            int uncachedSampled = 0;
            int surfaceRecords = 0;
            bool anyUnsampled = false;
            WayfinderStore surfaceStore = WayfinderModule.SurfaceMapStore;
            float now = Time.unscaledTime;

            for (int z = 0; z < height; z++)
            {
                int wz = pz + (z - halfH) * lastStep;
                int rowOffset = z * width;

                for (int x = 0; x < width; x++)
                {
                    int wx = px + (x - halfW) * lastStep;
                    int idx = rowOffset + x;

                    if (WayfinderTileCache.TryGet(wx, wz, modeByte, effectiveSliceY, out Color32 cachedCol, out short cachedH))
                    {
                        pixels[idx] = cachedCol;
                        heights[idx] = cachedH;
                        samples[idx].BlockId = 1; // Sentinel for cached
                        samples[idx].TopY = cachedH;
                        continue;
                    }

                    int missKey = NegativeKey(wx, wz, modeByte, effectiveSliceY);
                    if (negativeExpiry[idx] > now && negativeKey[idx] == missKey)
                    {
                        // Known miss from the last second: show dark but do not consume the
                        // sampling budget or keep the map permanently "pending".
                        pixels[idx] = (wx % 16 == 0 || wz % 16 == 0) ? gridLine : gridDark;
                        heights[idx] = (short)(py - 1000);
                        samples[idx].BlockId = 0;
                        continue;
                    }

                    if (uncachedSampled >= budget)
                    {
                        pixels[idx] = (wx % 16 == 0 || wz % 16 == 0) ? gridLine : gridDark;
                        heights[idx] = (short)(py - 1000);
                        samples[idx].BlockId = 0;
                        anyUnsampled = true;
                        continue;
                    }

                    uncachedSampled++;
                    bool sampled;
                    TerrainSample sample;
                    if (effectiveCave)
                    {
                        sampled = SampleColumnLayer(world, pwChunks, c0x, c0y, c0z, wx, wz, effectiveSliceY, out sample);
                        if (!sampled || sample.BlockId == 0)
                        {
                            bool showSurface = WayfinderModule.ShowSurfaceAboveCaves == null
                                || WayfinderModule.ShowSurfaceAboveCaves.Value;
                            if (showSurface && surfaceStore != null
                                && surfaceStore.TryGetSurface(wx, wz, out WayfinderCell surfaceCell))
                            {
                                // Wayfinder-style "surface above caves": show explored terrain dimmed
                                // instead of an unknown black tile while underground.
                                sample = FromCell(surfaceCell);
                                sample.IsSurfaceFallback = true;
                                sampled = true;
                            }
                        }

                        // Keep the surface layer filling while the player travels underground.
                        if (surfaceStore != null
                            && (WayfinderModule.AlwaysMapSurface == null || WayfinderModule.AlwaysMapSurface.Value)
                            && (x & 3) == 0 && (z & 3) == 0 && surfaceRecords < 128)
                        {
                            surfaceRecords++;
                            if (SampleColumnFast(world, pwChunks, c0x, c0y, c0z, wx, wz, py, 96, 52, out TerrainSample surfaceSample)
                                && surfaceSample.BlockId != 0)
                            {
                                uint biomeTint = 0;
                                if (renderType == "Biome")
                                    biomeTint = PackColor(BiomeHelper.GetBiomeTint(world, wx, surfaceSample.TopY, wz));
                                surfaceStore.RecordSurface(wx, wz, ToCell(surfaceSample, biomeTint));
                            }
                        }
                    }
                    else
                    {
                        sampled = SampleColumnFast(world, pwChunks, c0x, c0y, c0z, wx, wz, py, 44, 52, out sample);

                        if (sampled && sample.BlockId != 0)
                        {
                            if (surfaceStore != null)
                            {
                                uint biomeTint = 0;
                                if (renderType == "Biome")
                                    biomeTint = PackColor(BiomeHelper.GetBiomeTint(world, wx, sample.TopY, wz));
                                surfaceStore.RecordSurface(wx, wz, ToCell(sample, biomeTint));
                            }
                        }
                        else if (surfaceStore != null && surfaceStore.TryGetSurface(wx, wz, out WayfinderCell storedCell))
                        {
                            // Explored in a previous session: show it even while the chunk is unloaded.
                            sample = FromCell(storedCell);
                            sampled = true;
                        }
                    }

                    if (!sampled || sample.BlockId == 0)
                    {
                        pixels[idx] = (wx % 16 == 0 || wz % 16 == 0) ? gridLine : gridDark;
                        heights[idx] = (short)(py - 1000);
                        samples[idx].BlockId = 0;
                        // Retry after a short TTL instead of resampling this column every
                        // frame. This is what stops a dataless cave layer from sweeping.
                        negativeKey[idx] = missKey;
                        negativeExpiry[idx] = now + MissRetrySeconds;
                    }
                    else
                    {
                        samples[idx] = sample;
                        negativeExpiry[idx] = 0f;
                    }
                    textureDirty = true;
                }
            }

            renderComplete = !anyUnsampled;

            // Rebuild light sources from every valid sample so progressive passes do not lose
            // lamps that were sampled in an earlier frame.
            lightSources.Clear();
            if (renderType == "Night")
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    TerrainSample s = samples[i];
                    if (s.BlockId > 1 && NanoPalette.IsLightSource(s.BlockId))
                    {
                        int sx = i % width;
                        int sz = i / width;
                        lightSources.Add((sx, sz, NanoPalette.GetLightColor(s.BlockId)));
                    }
                }
            }

            // Pass 2: Shading, Render Modes, Wall Edges & Chunk Grid
            for (int z = 0; z < height; z++)
            {
                int wz = pz + (z - halfH) * lastStep;
                int rowOffset = z * width;

                for (int x = 0; x < width; x++)
                {
                    int wx = px + (x - halfW) * lastStep;
                    int idx = rowOffset + x;

                    // Skip already cached entries
                    if (samples[idx].BlockId == 1 && pixels[idx].a > 0)
                        continue;

                    TerrainSample sample = samples[idx];
                    if (sample.BlockId == 0)
                        continue;

                    Color32 baseColor;
                    bool isEdgeWall = false;

                    if (effectiveCave && sample.IsCaveWall)
                    {
                        // Check if adjacent to open cave floor
                        if (x > 0 && !samples[idx - 1].IsCaveWall && samples[idx - 1].BlockId != 0) isEdgeWall = true;
                        else if (x < width - 1 && !samples[idx + 1].IsCaveWall && samples[idx + 1].BlockId != 0) isEdgeWall = true;
                        else if (z > 0 && !samples[idx - width].IsCaveWall && samples[idx - width].BlockId != 0) isEdgeWall = true;
                        else if (z < height - 1 && !samples[idx + width].IsCaveWall && samples[idx + width].BlockId != 0) isEdgeWall = true;

                        if (sample.IsOre)
                        {
                            baseColor = NanoPalette.GetBlockColor(sample.BlockId);
                            baseColor = new Color32(
                                (byte)Mathf.Min(255, (int)(baseColor.r * 1.55f)),
                                (byte)Mathf.Min(255, (int)(baseColor.g * 1.55f)),
                                (byte)Mathf.Min(255, (int)(baseColor.b * 1.55f)),
                                255
                            );
                        }
                        else
                        {
                            Color32 rockCol = NanoPalette.GetBlockColor(sample.BlockId);
                            float wallFactor = isEdgeWall ? 0.28f : 0.42f;
                            baseColor = new Color32(
                                (byte)Mathf.Clamp(rockCol.r * wallFactor, 0, 255),
                                (byte)Mathf.Clamp(rockCol.g * wallFactor, 0, 255),
                                (byte)Mathf.Clamp(rockCol.b * (wallFactor * 1.05f), 0, 255),
                                255
                            );
                        }
                    }
                    else if (sample.IsOre)
                    {
                        baseColor = NanoPalette.GetBlockColor(sample.BlockId);
                        baseColor = new Color32(
                            (byte)Mathf.Min(255, (int)(baseColor.r * 1.45f)),
                            (byte)Mathf.Min(255, (int)(baseColor.g * 1.45f)),
                            (byte)Mathf.Min(255, (int)(baseColor.b * 1.45f)),
                            255
                        );
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
                            (byte)((baseColor.b * paint.b) / 255),
                            255
                        );
                    }

                    // Liquids
                    if (sample.IsLiquid)
                    {
                        if (sample.LiquidId == 22) // Lava
                        {
                            baseColor = new Color32(255, 120, 20, 255);
                        }
                        else if (sample.LiquidId == 108) // Acid
                        {
                            baseColor = new Color32(75, 255, 35, 255);
                        }
                        else // Water
                        {
                            Color32 bedColor = NanoPalette.GetBlockColor(sample.BedBlockId);
                            float depthFactor = Mathf.Clamp01(sample.WaterDepth / 12f);
                            Color shallow = new Color(0.24f, 0.68f, 0.92f);
                            Color deep = new Color(0.06f, 0.18f, 0.52f);
                            Color waterCol = Color.Lerp(shallow, deep, depthFactor);
                            baseColor = Color.Lerp(bedColor, waterCol, 0.55f + depthFactor * 0.40f);
                        }
                    }

                    // Hillshading & Relief
                    int curY = sample.TopY;
                    int northH = curY;
                    if (WayfinderTileCache.TryGet(wx, wz + lastStep, modeByte, effectiveSliceY, out _, out short nh) && nh > -500)
                    {
                        northH = nh;
                    }
                    else if (z < height - 1 && samples[idx + width].BlockId != 0)
                    {
                        northH = samples[idx + width].TopY;
                    }

                    int dy = curY - northH;
                    float slopeShade = 1.0f + Mathf.Clamp(dy * 0.14f, -0.32f, 0.32f);

                    float elevShade;
                    if (effectiveCave)
                    {
                        float depthUnderSlice = Mathf.Clamp((effectiveSliceY - curY) * 0.04f, -0.25f, 0.45f);
                        elevShade = 1.0f - depthUnderSlice;
                    }
                    else
                    {
                        elevShade = 1.0f + Mathf.Clamp((curY - py) * 0.002f, -0.15f, 0.15f);
                    }

                    float shade = slopeShade * elevShade;

                    // Render Mode Customizations
                    if (renderType == "Night")
                    {
                        float ambient = 0.30f;
                        float totalLight = 0f;
                        Color lightSum = Color.black;

                        for (int li = 0; li < lightSources.Count; li++)
                        {
                            var ls = lightSources[li];
                            float ldx = x - ls.x;
                            float ldz = z - ls.z;
                            float distSq = ldx * ldx + ldz * ldz;
                            if (distSq <= 64f)
                            {
                                float dist = Mathf.Sqrt(distSq);
                                float atten = 1.0f - (dist / 8.0f);
                                atten *= atten;
                                totalLight += atten;
                                lightSum += ls.color * atten;
                            }
                        }

                        if (NanoPalette.IsLightSource(sample.BlockId))
                        {
                            baseColor = Color.Lerp(baseColor, Color.white, 0.35f);
                            shade = 1.35f;
                        }
                        else
                        {
                            float lightFactor = Mathf.Clamp01(ambient + totalLight * 0.95f);
                            shade *= lightFactor;
                            if (totalLight > 0.05f)
                            {
                                Color warmLight = lightSum / totalLight;
                                baseColor = Color.Lerp(baseColor, warmLight, Mathf.Clamp01(totalLight * 0.40f));
                            }
                        }
                    }
                    else if (renderType == "Topo")
                    {
                        if (Mathf.Abs(curY) % 4 == 0) shade *= 0.70f;
                        if (Mathf.Abs(curY) % 20 == 0) shade *= 0.50f;

                        Color topoTint;
                        if (curY < 60)
                            topoTint = Color.Lerp(new Color(0.30f, 0.70f, 0.35f), new Color(0.55f, 0.80f, 0.40f), (curY + 20f) / 80f);
                        else if (curY < 95)
                            topoTint = Color.Lerp(new Color(0.85f, 0.80f, 0.45f), new Color(0.75f, 0.55f, 0.35f), (curY - 60f) / 35f);
                        else if (curY < 130)
                            topoTint = Color.Lerp(new Color(0.65f, 0.55f, 0.45f), new Color(0.60f, 0.62f, 0.68f), (curY - 95f) / 35f);
                        else
                            topoTint = Color.Lerp(new Color(0.75f, 0.80f, 0.88f), new Color(0.95f, 0.98f, 1.0f), Mathf.Clamp01((curY - 130f) / 50f));

                        baseColor = Color.Lerp(baseColor, topoTint, 0.35f);
                    }
                    else if (renderType == "Biome")
                    {
                        Color biomeTint;
                        if (sample.FromStore)
                        {
                            // Never call GetEnvironment for an unloaded chunk; use the tint
                            // captured when the cell was explored, or a neutral fallback.
                            biomeTint = sample.StoredBiomeTint != 0
                                ? (Color)UnpackColor(sample.StoredBiomeTint)
                                : Color.white;
                        }
                        else
                        {
                            biomeTint = BiomeHelper.GetBiomeTint(world, wx, curY, wz);
                        }
                        baseColor = Color.Lerp(baseColor, biomeTint, 0.45f);
                    }

                    Color32 finalCol = new Color32(
                        (byte)Mathf.Clamp(baseColor.r * shade, 0f, 255f),
                        (byte)Mathf.Clamp(baseColor.g * shade, 0f, 255f),
                        (byte)Mathf.Clamp(baseColor.b * shade, 0f, 255f),
                        255
                    );

                    // Chunk Grid Line Overlay
                    if (showGrid && ((wx & 31) == 0 || (wz & 31) == 0))
                    {
                        Color32 chunkGridCol = new Color32(50, 210, 255, 255);
                        finalCol = Color32.Lerp(finalCol, chunkGridCol, 0.65f);
                    }

                    if (sample.IsSurfaceFallback)
                    {
                        // Dim the remembered surface so live cave detail still dominates.
                        finalCol = new Color32(
                            (byte)(finalCol.r * 0.42f),
                            (byte)(finalCol.g * 0.42f),
                            (byte)(finalCol.b * 0.50f),
                            255);
                    }

                    pixels[idx] = finalCol;
                    heights[idx] = (short)curY;
                    // Persisted cells are not cached: when the chunk loads, the live sample
                    // must be allowed to replace the stored (possibly stale) color.
                    if (!sample.FromStore)
                        WayfinderTileCache.Put(wx, wz, modeByte, effectiveSliceY, finalCol, (short)curY);
                }
            }

            // Circular Masking for Minimap. maskSpan is the cropped UV fraction, so the mask
            // stays a circle when the minimap shows a sub-window of the sampled texture.
            bool maskChanged = isCircleMask != lastCircleMask
                || (isCircleMask && Mathf.Abs(maskSpan - lastMaskSpan) > 0.001f);

            if (isCircleMask && (textureDirty || maskChanged))
            {
                float halfRadius = ((width * 0.5f) - 1f) * Mathf.Clamp01(maskSpan <= 0f ? 1f : maskSpan);
                float rSq = halfRadius * halfRadius;
                for (int z = 0; z < height; z++)
                {
                    float dz = (z + 0.5f) - halfH;
                    int rowOffset = z * width;
                    for (int x = 0; x < width; x++)
                    {
                        float dx = (x + 0.5f) - halfW;
                        if (dx * dx + dz * dz > rSq)
                        {
                            pixels[rowOffset + x] = new Color32(0, 0, 0, 0);
                        }
                    }
                }
                textureDirty = true;
            }

            lastCircleMask = isCircleMask;
            lastMaskSpan = maskSpan;

            if (textureDirty || maskChanged)
            {
                texture.SetPixels32(pixels);
                texture.Apply(false);
                hasUploaded = true;
            }

            lastModeByte = modeByte;
            lastSliceYForUpload = effectiveSliceY;

            long elapsedMs = (Stopwatch.GetTimestamp() - renderStart) * 1000L / Stopwatch.Frequency;
            if (elapsedMs >= 20 && Time.unscaledTime - lastSlowMapLog > 2f)
            {
                lastSlowMapLog = Time.unscaledTime;
                WayfinderModule.Log?.LogWarning(
                    $"[Wayfinder] Slow map pass {elapsedMs} ms ({width}x{height}, step {lastStep}, upload={textureDirty || maskChanged})");
            }
        }

        public static bool IsOreBlock(ushort id)
        {
            switch (id)
            {
                case 12: // Gold Ore
                case 17: // Green Fluorite
                case 18: // Blue Crystals
                case 19: // Red Crystals
                case 20: // Crystal Block Blue
                case 21: // Coal Ore
                case 30: // Sulphur Ore
                case 57: // Iron Ore
                case 64: // Unobtainium
                case 66: // Cryptonite
                case 67: // Klorium
                case 202: // Amber
                case 275: // Cobalt Ore
                case 303: // Amethyst
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Live surface sample for hover/waypoint Y when no store cell exists.</summary>
        public static bool TrySampleSurface(IWorld world, int wx, int wz, int playerY, out TerrainSample sample)
        {
            sample = default;
            if (world is PriorityWorld pw && PwChunksFieldStatic != null)
            {
                try
                {
                    Chunk[,,] chunks = PwChunksFieldStatic.GetValue(pw) as Chunk[,,];
                    if (chunks != null)
                    {
                        int c0x = (int)PwC0xFieldStatic.GetValue(pw);
                        int c0y = (int)PwC0yFieldStatic.GetValue(pw);
                        int c0z = (int)PwC0zFieldStatic.GetValue(pw);
                        return SampleColumnFast(world, chunks, c0x, c0y, c0z, wx, wz, playerY, 44, 52, out sample);
                    }
                }
                catch { }
            }
            return false;
        }

        private static readonly FieldInfo PwChunksFieldStatic = typeof(PriorityWorld).GetField("chunks",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo PwC0xFieldStatic = typeof(PriorityWorld).GetField("c0x",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo PwC0yFieldStatic = typeof(PriorityWorld).GetField("c0y",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo PwC0zFieldStatic = typeof(PriorityWorld).GetField("c0z",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static Block GetBlockSafe(IWorld world, int wx, int y, int wz)
        {
            if (world is PriorityWorld pw && pwChunksField != null)
            {
                try
                {
                    Chunk[,,] pwChunks = pwChunksField.GetValue(pw) as Chunk[,,];
                    if (pwChunks != null)
                    {
                        int c0x = (int)pwC0xField.GetValue(pw);
                        int c0y = (int)pwC0yField.GetValue(pw);
                        int c0z = (int)pwC0zField.GetValue(pw);
                        return GetBlockFast(world, pwChunks, c0x, c0y, c0z, wx, y, wz);
                    }
                }
                catch { }
            }
            return default;
        }

        public static Block GetBlockFast(
            IWorld world,
            Chunk[,,] pwChunks,
            int c0x, int c0y, int c0z,
            int wx, int y, int wz)
        {
            if (pwChunks != null)
            {
                int cx = (wx >> 5) - c0x;
                int cy = (y >> 5) - c0y;
                int cz = (wz >> 5) - c0z;
                if (cx >= 0 && cy >= 0 && cz >= 0 &&
                    cx < pwChunks.GetLength(0) && cy < pwChunks.GetLength(1) && cz < pwChunks.GetLength(2))
                {
                    Chunk c = pwChunks[cx, cy, cz];
                    if (c != null && !c.empty && chunkBlocksField != null)
                    {
                        Block[,,] blocks = chunkBlocksField.GetValue(c) as Block[,,];
                        if (blocks != null)
                        {
                            return blocks[wx & 0x1F, y & 0x1F, wz & 0x1F];
                        }
                    }
                }
            }
            return default;
        }

        private static int NegativeKey(int wx, int wz, byte mode, int sliceY)
        {
            unchecked
            {
                return wx * 73856093 ^ wz * 19349663 ^ (mode << 8) ^ sliceY;
            }
        }

        public static uint PackColor(Color32 color)
        {
            return (uint)color.r | ((uint)color.g << 8) | ((uint)color.b << 16) | ((uint)color.a << 24);
        }

        public static Color32 UnpackColor(uint packed)
        {
            return new Color32(
                (byte)(packed & 0xFF),
                (byte)((packed >> 8) & 0xFF),
                (byte)((packed >> 16) & 0xFF),
                (byte)((packed >> 24) & 0xFF));
        }

        public static WayfinderCell ToCell(TerrainSample sample, uint biomeTint)
        {
            WayfinderCellFlags flags = WayfinderCellFlags.None;
            if (sample.IsLiquid) flags |= WayfinderCellFlags.Liquid;
            if (sample.IsPainted) flags |= WayfinderCellFlags.Painted;
            if (sample.IsCaveWall) flags |= WayfinderCellFlags.CaveWall;
            if (sample.IsOre) flags |= WayfinderCellFlags.Ore;

            return new WayfinderCell
            {
                BlockId = sample.BlockId,
                TopY = (short)sample.TopY,
                LiquidId = sample.LiquidId,
                BedBlockId = sample.BedBlockId,
                WaterDepth = (byte)Mathf.Clamp(sample.WaterDepth, 0, 255),
                PaintHue = sample.PaintHue,
                Flags = (byte)flags,
                BiomeTint = biomeTint
            };
        }

        public static TerrainSample FromCell(WayfinderCell cell)
        {
            return new TerrainSample
            {
                BlockId = cell.BlockId,
                TopY = cell.TopY,
                IsLiquid = cell.IsLiquid,
                LiquidId = cell.LiquidId,
                WaterDepth = cell.WaterDepth,
                BedBlockId = cell.BedBlockId,
                PaintHue = cell.PaintHue,
                IsPainted = cell.IsPainted,
                IsCaveWall = cell.IsCaveWall,
                IsOre = cell.IsOre,
                FromStore = true,
                StoredBiomeTint = cell.BiomeTint
            };
        }

        /// <summary>Converts a live cave/slice sample to the 4x4 persisted cave cell.</summary>
        public static WayfinderCaveCell ToCaveCell(TerrainSample sample, uint tint)
        {
            WayfinderCaveCellFlags flags = WayfinderCaveCellFlags.None;
            if (sample.IsLiquid) flags |= WayfinderCaveCellFlags.Liquid;
            if (sample.IsPainted) flags |= WayfinderCaveCellFlags.Painted;
            if (sample.IsCaveWall) flags |= WayfinderCaveCellFlags.CaveWall;
            if (sample.IsOre) flags |= WayfinderCaveCellFlags.Ore;
            if (sample.IsOpenRoom) flags |= WayfinderCaveCellFlags.OpenRoom;

            return new WayfinderCaveCell
            {
                BlockId = sample.BlockId,
                TopY = (short)Mathf.Clamp(sample.TopY, short.MinValue, short.MaxValue),
                LiquidId = sample.LiquidId,
                LiquidDepth = (byte)Mathf.Clamp(sample.WaterDepth, 0, 255),
                PaintHue = sample.PaintHue,
                Flags = (byte)flags,
                Tint = tint
            };
        }

        /// <summary>Restores a persisted cave/slice cell as a renderable sample.</summary>
        public static TerrainSample FromCaveCell(WayfinderCaveCell cell)
        {
            return new TerrainSample
            {
                BlockId = cell.BlockId,
                TopY = cell.TopY,
                IsLiquid = cell.IsLiquid,
                LiquidId = cell.LiquidId,
                WaterDepth = cell.LiquidDepth,
                // The cave record has no bed id; stone is a better approximation than the
                // liquid itself (which would blend water over water after a reload).
                BedBlockId = cell.IsLiquid ? (ushort)8 : cell.BlockId,
                PaintHue = cell.PaintHue,
                IsPainted = cell.IsPainted,
                IsCaveWall = cell.IsCaveWall,
                IsOre = cell.IsOre,
                IsOpenRoom = cell.IsOpenRoom,
                FromStore = true,
                StoredBiomeTint = cell.Tint
            };
        }

        /// <summary>Public wrapper for the chunk atlas' cave/slice sampler.</summary>
        public static bool SampleColumnLayerPublic(
            IWorld world, Chunk[,,] pwChunks, int c0x, int c0y, int c0z, int wx, int wz, int sliceY,
            out TerrainSample result)
        {
            return SampleColumnLayer(world, pwChunks, c0x, c0y, c0z, wx, wz, sliceY, out result);
        }

        /// <summary>Public wrapper for the chunk atlas' surface sampler.</summary>
        public static bool SampleColumnFastPublic(
            IWorld world, Chunk[,,] pwChunks, int c0x, int c0y, int c0z, int wx, int wz, int playerY,
            int scanAbove, int scanBelow, out TerrainSample result)
        {
            return SampleColumnFast(world, pwChunks, c0x, c0y, c0z, wx, wz, playerY, scanAbove, scanBelow, out result);
        }

        /// <summary>Cell conversion used by the chunk atlas.</summary>
        

        /// <summary>Cell conversion used by the chunk atlas.</summary>
        

        /// <summary>Packs a color for the per-cell biome tint.</summary>
        

        /// <summary>Unpacks a stored biome tint.</summary>
        

        private static bool SampleColumnLayer(
            IWorld world,
            Chunk[,,] pwChunks,
            int c0x, int c0y, int c0z,
            int wx, int wz, int sliceY,
            out TerrainSample result)
        {
            result = default;

            if (pwChunks != null)
            {
                int cx = (wx >> 5) - c0x;
                int cz = (wz >> 5) - c0z;
                if (cx < 0 || cz < 0 || cx >= pwChunks.GetLength(0) || cz >= pwChunks.GetLength(2))
                {
                    result.BlockId = 0;
                    result.TopY = sliceY - 1000;
                    return false;
                }
            }

            // 2. Start scanning around sliceY (player height)
            Block bAbove = GetBlockFast(world, pwChunks, c0x, c0y, c0z, wx, sliceY + 1, wz);
            Block bSlice = GetBlockFast(world, pwChunks, c0x, c0y, c0z, wx, sliceY, wz);
            bool isAirAbove = WayfinderModule.IsAirOrPassable(bAbove.id);
            bool isAirSlice = WayfinderModule.IsAirOrPassable(bSlice.id);

            // Case A: Room/Corridor open space at player height
            if (isAirAbove || isAirSlice)
            {
                bool foundLiquid = false;
                int liquidY = 0;
                ushort liquidId = 0;

                int startY = isAirSlice ? sliceY : (sliceY + 1);
                int bottomY = sliceY - 24;

                for (int y = startY; y >= bottomY; y--)
                {
                    Block b = GetBlockFast(world, pwChunks, c0x, c0y, c0z, wx, y, wz);
                    ushort id = b.id;
                    if (WayfinderModule.IsAirOrPassable(id))
                        continue;

                    BlockType bt = (id < BlockTypes.blocks.Length) ? BlockTypes.blocks[id] : null;
                    if (bt != null && bt.liquid)
                    {
                        if (!foundLiquid)
                        {
                            foundLiquid = true;
                            liquidY = y;
                            liquidId = id;
                        }
                        continue;
                    }

                    if (foundLiquid)
                    {
                        result.BlockId = liquidId;
                        result.TopY = liquidY;
                        result.IsLiquid = true;
                        result.LiquidId = liquidId;
                        result.WaterDepth = liquidY - y;
                        result.BedBlockId = id;
                        result.IsCaveWall = false;
                        result.IsOpenRoom = true;
                        return true;
                    }

                    // True Cave / Dungeon Floor
                    result.BlockId = id;
                    result.TopY = y;
                    result.IsLiquid = false;
                    result.BedBlockId = id;
                    result.IsPainted = b.IsPainted;
                    result.PaintHue = b.PaintHue;
                    result.IsOre = IsOreBlock(id);
                    result.IsCaveWall = false;
                    result.IsOpenRoom = true;
                    return true;
                }

                if (foundLiquid)
                {
                    result.BlockId = liquidId;
                    result.TopY = liquidY;
                    result.IsLiquid = true;
                    result.LiquidId = liquidId;
                    result.WaterDepth = liquidY - bottomY;
                    result.BedBlockId = 8;
                    result.IsCaveWall = false;
                    result.IsOpenRoom = true;
                    return true;
                }

                // Deep cave void/chasm
                result.BlockId = 0;
                result.TopY = bottomY;
                return false;
            }

            // Case B: sliceY + 1 is SOLID (wall or sloped tunnel going up)
            // Check for an enclosed air pocket anywhere in the rest of the 16-block band,
            // not just 5 blocks above: a room/house floor can sit 6–15 blocks above the
            // band floor, and the interior cutaway needs to find it.
            int upwardAirY = -1;
            for (int ay = sliceY + 2; ay <= sliceY + 15; ay++)
            {
                Block ab = GetBlockFast(world, pwChunks, c0x, c0y, c0z, wx, ay, wz);
                if (WayfinderModule.IsAirOrPassable(ab.id))
                {
                    upwardAirY = ay;
                    break;
                }
            }

            if (upwardAirY > 0)
            {
                // Verify there is STILL a solid ceiling above that air pocket at upwardAirY + 1 .. upwardAirY + 15
                bool upwardHasCeiling = false;
                for (int cy = upwardAirY + 1; cy <= upwardAirY + 15; cy++)
                {
                    Block ucb = GetBlockFast(world, pwChunks, c0x, c0y, c0z, wx, cy, wz);
                    if (WayfinderModule.IsSolidOpaqueCeiling(ucb.id))
                    {
                        upwardHasCeiling = true;
                        break;
                    }
                }

                if (upwardHasCeiling)
                {
                    // Enclosed upward-sloping cave floor
                    int floorY = upwardAirY - 1;
                    Block floorBlock = GetBlockFast(world, pwChunks, c0x, c0y, c0z, wx, floorY, wz);
                    ushort id = floorBlock.id;
                    result.BlockId = id;
                    result.TopY = floorY;
                    result.IsLiquid = false;
                    result.BedBlockId = id;
                    result.IsPainted = floorBlock.IsPainted;
                    result.PaintHue = floorBlock.PaintHue;
                    result.IsOre = IsOreBlock(id);
                    result.IsCaveWall = false;
                    result.IsOpenRoom = true;
                    return true;
                }
            }

            // Cave / Dungeon Wall
            ushort wallId = bAbove.id != 0 ? bAbove.id : (bSlice.id != 0 ? bSlice.id : (ushort)8);
            result.BlockId = wallId;
            result.TopY = sliceY + 1;
            result.IsCaveWall = true;
            result.IsOre = IsOreBlock(wallId);
            result.IsPainted = bAbove.IsPainted;
            result.PaintHue = bAbove.PaintHue;
            return true;
        }

        private static bool SampleColumnFast(
            IWorld world,
            Chunk[,,] pwChunks,
            int c0x, int c0y, int c0z,
            int wx, int wz, int playerY,
            int scanAbove, int scanBelow,
            out TerrainSample result)
        {
            result = default;

            if (pwChunks != null)
            {
                int ix = (wx >> 5) - c0x;
                int iz = (wz >> 5) - c0z;
                if (ix < 0 || iz < 0 || ix >= pwChunks.GetLength(0) || iz >= pwChunks.GetLength(2))
                {
                    result.BlockId = 0;
                    result.TopY = playerY - 1000;
                    return false;
                }
            }

            int topY = playerY + scanAbove;
            int bottomY = playerY - scanBelow;

            int cx = wx >> 5;
            int cz = wz >> 5;
            int lx = wx & 0x1F;
            int lz = wz & 0x1F;

            int currentCy = int.MinValue;
            Block[,,] currentBlocks = null;
            bool skipChunk = false;

            bool foundLiquid = false;
            int liquidSurfaceY = 0;
            ushort liquidId = 0;

            for (int y = topY; y >= bottomY; y--)
            {
                int cy = y >> 5;
                if (cy != currentCy)
                {
                    currentCy = cy;
                    currentBlocks = null;
                    skipChunk = false;

                    if (pwChunks != null)
                    {
                        int ix = cx - c0x;
                        int iy = cy - c0y;
                        int iz = cz - c0z;
                        if (ix >= 0 && iy >= 0 && iz >= 0 &&
                            ix < pwChunks.GetLength(0) && iy < pwChunks.GetLength(1) && iz < pwChunks.GetLength(2))
                        {
                            Chunk c = pwChunks[ix, iy, iz];
                            if (c == null || c.empty)
                            {
                                skipChunk = true;
                            }
                            else if (chunkBlocksField != null)
                            {
                                currentBlocks = chunkBlocksField.GetValue(c) as Block[,,];
                            }
                        }
                        else
                        {
                            skipChunk = true;
                        }
                    }
                    else
                    {
                        skipChunk = true;
                    }
                }

                if (skipChunk || currentBlocks == null)
                {
                    y = currentCy << 5;
                    continue;
                }

                Block block = currentBlocks[lx, y & 0x1F, lz];
                ushort id = block.id;
                if (id <= 2 || id == 68)
                    continue;

                BlockType bType = (id < BlockTypes.blocks.Length) ? BlockTypes.blocks[id] : null;
                if (bType == null || bType.air || !bType.visible)
                    continue;

                if (bType.liquid)
                {
                    if (!foundLiquid)
                    {
                        foundLiquid = true;
                        liquidSurfaceY = y;
                        liquidId = id;
                    }
                    continue;
                }

                if (foundLiquid)
                {
                    result.BlockId = liquidId;
                    result.TopY = liquidSurfaceY;
                    result.IsLiquid = true;
                    result.LiquidId = liquidId;
                    result.WaterDepth = liquidSurfaceY - y;
                    result.BedBlockId = id;
                    return true;
                }

                result.BlockId = id;
                result.TopY = y;
                result.IsLiquid = false;
                result.BedBlockId = id;
                result.IsPainted = block.IsPainted;
                result.PaintHue = block.PaintHue;
                result.IsOre = IsOreBlock(id);
                return true;
            }

            if (foundLiquid)
            {
                result.BlockId = liquidId;
                result.TopY = liquidSurfaceY;
                result.IsLiquid = true;
                result.LiquidId = liquidId;
                result.WaterDepth = liquidSurfaceY - bottomY;
                result.BedBlockId = 8;
                return true;
            }

            return false;
        }
    }
}
