using System.Collections;
using Aether.Nano;
using BepInEx.Configuration;
using BepInEx.Logging;
using Blocksters.Terrain.chunk;
using UnityEngine;

namespace Aether.Mods.Wayfinder
{
    /// <summary>
    /// Wayfinder's lifecycle and what it takes from Aether Nano: its config and logger (set by <see cref="WayfinderPlugin"/>),
    /// styles, input isolation for the fullscreen map, HUD hints from the host, and frame-rate metrics. Everything the UI
    /// shell used to provide now comes from Nano or from Wayfinder itself (caves, indoors and nearby mobs: WayfinderCaves).
    /// </summary>
    public sealed partial class WayfinderModule
    {
        public static WayfinderModule Current { get; private set; }
        public static ConfigFile Config { get; internal set; }
        public static ManualLogSource Log { get; internal set; }
        public static ConfigEntry<KeyCode> MapLayerModeKey { get; private set; }
        public static ConfigEntry<KeyCode> QuickWaypointKey { get; private set; }

        public string Id => "aether_wayfinder";
        public bool IsInitialized { get; private set; }

        public void Initialize()
        {
            if (IsInitialized) return;
            Current = this;
            InitializeWayfinder();
            // These two used to be bound by Aether's keybind manager in the same [Wayfinder] section.
            MapLayerModeKey = Config.Bind("Wayfinder", "MapLayerModeKey", KeyCode.L, "Cycle map terrain layer: Auto -> Surface -> Cave -> Slice.");
            QuickWaypointKey = Config.Bind("Wayfinder", "QuickWaypointKey", KeyCode.B, "Instantly create a waypoint at player position.");
            IsInitialized = true;
            Log?.LogInfo("[Wayfinder] Module initialized (own assembly, per-mod config section).");
        }

        public bool IsOpen => isWayfinderOpen;
        public bool HasWaypoints => waypoints.Count > 0;
        public int WaypointCount => waypoints.Count;
        public bool HasSurfaceStore => SurfaceMapStore != null;

        /// <summary>M opens/closes the map, N toggles the minimap, L cycles the layer, B drops a waypoint, Esc closes the map.</summary>
        public void HandleKeys()
        {
            if (isWayfinderOpen && Input.GetKeyDown(KeyCode.Escape) && GUIUtility.keyboardControl == 0) { SetWayfinderOpen(false); return; }
            if (NanoHud.KeyboardCaptured || NanoHud.OverlayOpen) return;

            if (WayfinderKey != null && Input.GetKeyDown(WayfinderKey.Value))
            {
                if (isWayfinderOpen) { if (GUIUtility.keyboardControl == 0) SetWayfinderOpen(false); }
                else if (!Aether.Nano.Nano.NativeWindowOpen && Aether.Nano.Nano.InGame) SetWayfinderOpen(true);
                return;
            }
            if (isWayfinderOpen) return;
            if (MinimapToggleKey != null && MinimapEnabled != null && Input.GetKeyDown(MinimapToggleKey.Value)) MinimapEnabled.Value = !MinimapEnabled.Value;
            if (!Aether.Nano.Nano.InGame || Aether.Nano.Nano.NativeWindowOpen) return;
            if (MapLayerModeKey != null && Input.GetKeyDown(MapLayerModeKey.Value)) CycleMapLayerMode();
            if (QuickWaypointKey != null && Input.GetKeyDown(QuickWaypointKey.Value)) CreateQuickWaypoint();
        }

        public void MarkCaveVisited(int x, int z, int y)
        {
            int caveFogY = MapLayerMode != null && MapLayerMode.Value == "Slice" ? currentSliceY : y;
            WayfinderChunkAtlas.Shared.MarkCaveVisited(x, z, caveFogY);
        }

        public void SaveMapNow()
        {
            try { SurfaceMapStore?.FlushAll(); } catch { }
            try { CaveMapStore?.FlushAll(); } catch { }
        }

        // ---- What the UI shell used to provide, now from Nano ----
        private static Camera CachedCamera => Aether.Nano.Nano.CachedCamera;
        private static bool IsSpanish() => Aether.Nano.Nano.IsSpanish();
        private static string Tr(string en, string es) => Aether.Nano.Nano.Tr(en, es);
        private static IWorld GetActiveWorld() => NanoWorld.Active();
        private static InvBaseItem ResolveBlockItem(string name) => NanoItems.ResolveBlockItem(name);
        private static string GetLocalizedBlockName(string raw, InvBaseItem item) => NanoItems.LocalizedName(raw, item);
        private static Coroutine StartCoroutine(IEnumerator routine) => WayfinderPlugin.Instance.StartCoroutine(routine);

        private static bool MapCave => WayfinderCaves.MapCave;
        private static bool IsIndoors => WayfinderCaves.IsIndoors;
        private static System.Collections.Generic.List<Health> MobCache => WayfinderCaves.Mobs;
        private static void RefreshMobCache(Vector3 playerPos, float maxDist) => WayfinderCaves.RefreshMobs(playerPos, maxDist);

        // Host menus (Aether's F10, keybind manager, HUD editor) give way to the fullscreen map.
        private static bool IsModMenuOpen => NanoHud.OverlayOpen;
        private static bool IsHudDragOpen => false;
        private static bool IsKeybindManagerOpen => false;
        private static void CloseModMenu() => NanoHud.CloseHostMenus?.Invoke();
        private static void CloseHudDrag() { }
        private static void CloseKeybinds() { }
        private static void AcquireModalIsolation() => NanoInput.AcquireIsolation();
        private static void ReleaseModalIsolation() => NanoInput.ReleaseIsolation();
        private static float ModalOpenedTimestamp { get => NanoPanels.LastOpened; set => NanoPanels.LastOpened = value; }
        private static float ModalClosedTimestamp { get => NanoPanels.LastClosed; set => NanoPanels.LastClosed = value; }

        private static bool MoveDiamondsUnderXP => NanoHud.DiamondsUnderXp();
        private static bool MoveTopRightButtonsToBottomRight => NanoHud.TopRightButtonsMoved();
        private static float LiveFps => NanoPerf.LiveFps;
        private static float LiveFrameTimeMs => NanoPerf.FrameTimeMs;

        private static void DrawThemedPanel(Rect rect, string theme, float opacity = 1f) => NanoUi.DrawThemedFrame(rect, theme, opacity);
        private static GUIStyle AuthorStyle => NanoStyles.Author;
        private static GUIStyle BadgeStyle => NanoStyles.Badge;
        private static GUIStyle MenuHeaderStyle => NanoStyles.MenuHeader;
        private static GUIStyle MinimapLabelStyle => NanoStyles.MinimapLabel;
        private static GUIStyle ModNameStyle => NanoStyles.ModName;
        private static GUIStyle SectionTitleStyle => NanoStyles.SectionTitle;
        private static GUIStyle SmallStyle => NanoStyles.Small;
        private static GUIStyle TextFieldStyle => NanoStyles.TextField;
        private static GUIStyle ToolStyle => NanoStyles.Tool;
        private static GUIStyle DescStyle => NanoStyles.Desc;

        /// <summary>Quick waypoint at the player's feet, including the native notification.</summary>
        public void CreateQuickWaypoint()
        {
            Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
            if (cam == null) return;

            Vector3 p = cam.transform.position;
            int x = Mathf.RoundToInt(p.x);
            int y = GetStandingY(GetActiveWorld(), p);
            int z = Mathf.RoundToInt(p.z);

            string wpName = $"Point {waypoints.Count + 1}";
            waypoints.Add(new WaypointEntry
            {
                Name = wpName,
                X = x,
                Y = y,
                Z = z,
                ColorIndex = waypoints.Count % 6,
                Enabled = true
            });

            SaveWaypoints();

            string msg = Tr($"✦ Waypoint '{wpName}' created at ({x}, {y}, {z})!",
                            $"✦ ¡Waypoint '{wpName}' creado en ({x}, {y}, {z})!");
            new Notify.TimedMessage(msg, new Color(0.40f, 1.0f, 0.40f)).Send();
        }

        /// <summary>Cycles Auto → Surface → Cave → Interior → Slice (L by default).</summary>
        private void CycleMapLayerMode()
        {
            string cur = MapLayerMode.Value;
            string next = "Auto";
            if (cur == "Auto") next = "Surface";
            else if (cur == "Surface") next = "Cave";
            else if (cur == "Cave") next = "Interior";
            else if (cur == "Interior") next = "Slice";
            else next = "Auto";

            MapLayerMode.Value = next;
            if (next == "Slice") SetSliceY(MapSliceY.Value);
            Config.Save();

            string title = Tr($"Map Layer Mode: {next}", $"Modo de Capa: {next}");
            new Notify.TimedMessage(title, new Color(0.35f, 0.90f, 1.0f)).Send();
        }
    }
}
