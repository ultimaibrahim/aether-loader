using System;
using System.Collections.Generic;
using Aether.Nano;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using UnityEngine;

namespace Aether.Mods.Wayfinder
{
    /// <summary>
    /// What the map needs to know about where the player is: in a cave (deep roof, underground), inside a building (shallow roof
    /// and walls around), and which mobs are nearby. Both states have 0.45 s of hysteresis so doorways do not flip the map.
    /// Moved here from Aether's UI shell: Wayfinder was its only user.
    /// </summary>
    internal static class WayfinderCaves
    {
        private const int EnclosureRadius = 32;
        private const int EnclosureMinHits = 7; // 7 of 8 horizontal rays must hit a wall

        public static bool MapCave { get; private set; }
        public static bool IsIndoors { get; private set; }
        public static readonly List<Health> Mobs = new List<Health>();

        private static float _nextCheck, _nextMobScan;
        private static bool _indoorPending, _indoorPendingState, _cavePending, _cavePendingState;
        private static float _indoorSince, _caveSince;
        private static string _scene = string.Empty;

        public static void Tick(WayfinderModule module)
        {
            if (Nano.Nano.Scene != _scene) { _scene = Nano.Nano.Scene; MapCave = false; IsIndoors = false; Mobs.Clear(); }
            if (!Nano.Nano.InGame || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.35f;

            Camera cam = Nano.Nano.CachedCamera;
            IWorld world = NanoWorld.Active();
            if (cam == null || world == null) return;
            try
            {
                Vector3 pos = cam.transform.position;
                int px = Mathf.FloorToInt(pos.x), py = Mathf.FloorToInt(pos.y), pz = Mathf.FloorToInt(pos.z);
                int top = TopmostSolidY(world, px, pz);
                int roof = top - py;
                bool shallowRoof = top >= 0 && py >= 0 && roof >= 1 && roof <= 32;
                bool indoors = shallowRoof && EnclosedByWalls(world, px, py, pz);
                bool cave = indoors || (roof > 32 && Nano.Nano.IsUnderground);

                IsIndoors = Settle(IsIndoors, indoors, ref _indoorPending, ref _indoorPendingState, ref _indoorSince);
                MapCave = Settle(MapCave, cave, ref _cavePending, ref _cavePendingState, ref _caveSince);
                module.MarkCaveVisited(px, pz, py);
            }
            catch (Exception ex) { WayfinderModule.Log?.LogWarning("[Wayfinder] indoor check failed: " + ex.GetType().Name + ": " + ex.Message); }
        }

        private static bool Settle(bool current, bool target, ref bool pending, ref bool pendingState, ref float since)
        {
            if (target == current) { pending = false; return current; }
            if (!pending || target != pendingState) { pending = true; pendingState = target; since = Time.unscaledTime; return current; }
            if (Time.unscaledTime - since < 0.45f) return current;
            pending = false;
            return target;
        }

        private static int TopmostSolidY(IWorld world, int x, int z)
        {
            for (int y = 255; y >= 0; y--)
            {
                try { if (!WayfinderModule.IsAirOrPassable(NanoWorld.GetBlockSafe(world, x, y, z).id)) return y; }
                catch { return -1; }
            }
            return -1;
        }

        /// <summary>8 rays at head height: an enclosed room has walls in most directions, a semi-open hall does not.</summary>
        private static bool EnclosedByWalls(IWorld world, int px, int py, int pz)
        {
            int[] dxs = { 0, 1, 1, 1, 0, -1, -1, -1 }, dzs = { 1, 1, 0, -1, -1, -1, 0, 1 };
            int hits = 0;
            for (int dir = 0; dir < 8; dir++)
            {
                bool hit1 = false, hit2 = false;
                for (int d = 1; d <= EnclosureRadius && (!hit1 || !hit2); d++)
                {
                    int wx = px + dxs[dir] * d, wz = pz + dzs[dir] * d;
                    try
                    {
                        if (!hit1 && !WayfinderModule.IsAirOrPassable(NanoWorld.GetBlockSafe(world, wx, py + 1, wz).id)) hit1 = true;
                        if (!hit2 && !WayfinderModule.IsAirOrPassable(NanoWorld.GetBlockSafe(world, wx, py + 2, wz).id)) hit2 = true;
                    }
                    catch { break; }
                }
                if (hit1 && hit2) hits++;
            }
            return hits >= EnclosureMinHits;
        }

        /// <summary>Mobs within <paramref name="maxDist"/> and the vertical range, rescanned at most twice a second.</summary>
        public static void RefreshMobs(Vector3 playerPos, float maxDist)
        {
            if (WayfinderModule.ShowMobsOnMinimap != null && !WayfinderModule.ShowMobsOnMinimap.Value) { Mobs.Clear(); return; }
            if (Time.unscaledTime < _nextMobScan) return;
            _nextMobScan = Time.unscaledTime + 0.5f;
            Mobs.Clear();

            Transform player = TerrainLoader.instance != null ? TerrainLoader.instance.player : null;
            MOBManagement manager = TerrainLoader.instance != null ? TerrainLoader.instance.mobManager : null;
            float maxY = WayfinderModule.EntityVerticalRange != null ? WayfinderModule.EntityVerticalRange.Value : 20f;
            float maxDistSq = maxDist * maxDist;

            void Consider(Health h)
            {
                if (h == null || h.IsDead() || (player != null && h.transform == player)) return;
                Vector3 p = h.transform.position;
                if (Mathf.Abs(p.y - playerPos.y) > maxY || (p - playerPos).sqrMagnitude > maxDistSq) return;
                Mobs.Add(h);
            }

            // Native mobs are direct children of MOBManagement: walking it avoids a whole-scene search every 0.5 s.
            if (manager != null)
            {
                Transform root = manager.transform;
                for (int i = 0; i < root.childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child != null) Consider(child.GetComponent<Health>());
                }
                return;
            }
            foreach (Health h in UnityEngine.Object.FindObjectsByType<Health>()) Consider(h);
        }
    }
}
