using System;
using BepInEx.Configuration;
using UnityEngine;
using Aether.Nano;
using static Aether.Nano.NanoUi;

namespace Aether.Mods.Wayfinder
{
    /// <summary>
    /// Vertical "light column" beacons over enabled waypoints.
    ///
    /// Drawn as rotated screen-space quads from a single cached gradient texture, which keeps
    /// the feature independent of Materials/Meshes/LineRenderers and makes z-fighting with the
    /// voxel terrain impossible. This mirrors how the existing waypoint pills are drawn:
    /// project with <see cref="Camera.WorldToScreenPoint"/>, then paint with IMGUI.
    ///
    /// The beam is an intentional waypoint-location x-ray: IMGUI is composited after the world
    /// and has no depth buffer, so a column is visible through rock/water/structures. That is
    /// why the feature is opt-in (ShowWaypointBeacons defaults to false). True occlusion would
    /// need a throttled coarse voxel walk through loaded chunks (e.g. one short raycast per
    /// waypoint every 0.25-0.5 s), never Physics.Linecast: Block Story terrain colliders may
    /// not exist for unloaded/streaming chunks.
    ///
    /// Integration: main calls <see cref="EnsureBeaconConfig"/> from InitializeWayfinder(),
    /// then <see cref="DrawWaypointBeacons3D"/> from OnGUI immediately before
    /// <c>DrawWorldWaypoints3D()</c> so the labels stay on top of the beams.
    /// </summary>
    public sealed partial class WayfinderModule
    {
        // =====================================================================
        //        WAYPOINT BEACON CONFIGURATION (LAZY, OPT-IN)
        // =====================================================================

        private static ConfigEntry<bool> showWaypointBeacons;
        private static bool showWaypointBeaconsBindFailed;

        /// <summary>
        /// Lazily bound so this file can be integrated additively. The main module should call
        /// <see cref="EnsureBeaconConfig"/> from InitializeWayfinder() (where Config exists);
        /// this property remains a safe fallback for any later first read. Defaults to false:
        /// the beams are an x-ray through terrain and should be opt-in.
        /// </summary>
        public static ConfigEntry<bool> ShowWaypointBeacons
        {
            get
            {
                EnsureBeaconConfig();
                return showWaypointBeacons;
            }
        }

        /// <summary>
        /// Binds [Wayfinder] ShowWaypointBeacons once, if the plugin and its ConfigFile are
        /// already available. Safe to call repeatedly and from InitializeWayfinder. A missing
        /// plugin instance is not an error: the lazy read later retries.
        /// </summary>
        public static void EnsureBeaconConfig()
        {
            if (showWaypointBeacons != null || showWaypointBeaconsBindFailed) return;

            try
            {
                ConfigFile config = Config;
                if (config == null) return;

                showWaypointBeacons = config.Bind(
                    "Wayfinder",
                    "ShowWaypointBeacons",
                    false,
                    "Draw a vertical light beam over every enabled waypoint in the world. " +
                    "Independent of the 3D marker master. Off by default: beams show through terrain.");
            }
            catch (Exception ex)
            {
                // Bind failures are permanent; retrying every frame would only spam the log.
                showWaypointBeaconsBindFailed = true;
                Log?.LogWarning("[Wayfinder] Could not bind ShowWaypointBeacons: " +
                                ex.GetType().Name + ": " + ex.Message);
            }
        }

        // =====================================================================
        //        CACHED BEAM TEXTURE
        // =====================================================================

        private static Texture2D waypointBeaconTex;

        /// <summary>
        /// 64x8 white RGBA gradient. The U axis runs from the base (bright, u=0) to the top
        /// (transparent, u=1), so the bright end is always anchored at the projected base even
        /// when the camera rolls or the top projects "below" the base on screen. The V axis
        /// has a soft centre falloff so the column has no hard pixel edges.
        /// </summary>
        private static Texture2D GetWaypointBeaconTexture()
        {
            if (waypointBeaconTex != null) return waypointBeaconTex;

            const int texWidth = 64;
            const int texHeight = 8;

            Texture2D tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false)
            {
                name = "AetherWaypointBeacon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[texWidth * texHeight];
            for (int y = 0; y < texHeight; y++)
            {
                // Soft falloff across the beam width (texture V).
                float across = Mathf.Abs((y + 0.5f) / texHeight - 0.5f) * 2f;
                float horizontal = 1f - across * across;

                for (int x = 0; x < texWidth; x++)
                {
                    // Texture U: 0 = base (bright), 1 = tip (transparent).
                    float along = x / (float)(texWidth - 1);
                    float vertical = Mathf.Pow(1f - along, 1.35f);
                    byte alpha = (byte)Mathf.Clamp(
                        Mathf.RoundToInt(vertical * horizontal * 210f), 0, 255);
                    pixels[y * texWidth + x] = new Color32(255, 255, 255, alpha);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            waypointBeaconTex = tex;
            return tex;
        }

        // =====================================================================
        //        WORLD-SPACE WAYPOINT BEAMS (ROTATED SCREEN-SPACE QUADS)
        // =====================================================================

        private const float BeaconBeamMaxDistance = 96f;
        private const float BeaconLabelMaxDistance = 384f;
        private const float BeaconNearHideDistance = 4f;
        private const float BeaconNearFadeDistance = 10f;
        private const float BeaconHeightBlocks = 48f;
        private const float BeaconWidthBlocks = 1.35f;
        private const float BeaconMinPixelWidth = 2f;
        private const float BeaconMaxPixelWidth = 26f;
        private const int BeaconMaxVisible = 24;
        private const int BeaconMaxFarLabels = 8;
        private const float BeaconCrosshairGuardPixels = 12f;
        private const float BeaconCrosshairAlphaScale = 0.25f;
        private const int BeaconMaxLabels = 12;

        /// <summary>One projected beam candidate; values are screen-space GUI pixels.</summary>
        private struct BeaconCandidate
        {
            public float SqrDistance;
            public float BaseX;
            public float BaseY;
            public float TopX;
            public float TopY;
            public float Width;
            public float Alpha;
            public int ColorIndex;
            /// <summary>Already-owned waypoint name; no per-frame string allocation.</summary>
            public string Name;
            public bool IsDeath;
        }

        // Fixed-size nearest-first buffer: insertion-sort candidates as they are projected, so
        // the 24 nearest beams win without allocating or sorting the waypoint list itself.
        private static readonly BeaconCandidate[] beaconCandidates =
            new BeaconCandidate[BeaconMaxVisible];

        /// <summary>
        /// A far waypoint that gets a name pill but no beam. Kept separate from the beam buffer
        /// so the 24-beam cap never hides the labels of distant, still-relevant waypoints.
        /// </summary>
        private struct BeaconFarLabel
        {
            public float SqrDistance;
            public float ScreenX;
            public float ScreenY;
            public int ColorIndex;
            public string Name;
            public bool IsDeath;
        }

        private static readonly BeaconFarLabel[] beaconFarLabels =
            new BeaconFarLabel[BeaconMaxFarLabels];

        private static bool waypointBeaconErrorLogged;

        /// <summary>
        /// Paints a rotated vertical beam over the nearest enabled waypoints within
        /// min(192 blocks, camera far clip). Call from OnGUI (Repaint); other event types return
        /// immediately. No per-frame allocations: one cached texture, one GUI.DrawTexture per
        /// visible waypoint, fixed candidate buffer capped at 24.
        /// </summary>
        internal void DrawWaypointBeacons3D()
        {
            // Beams are independent of the 3D marker master (ShowWorldWaypoints); the user
            // may want a clean HUD but still see navigation beams.

            EnsureBeaconConfig();
            if (showWaypointBeacons == null || !showWaypointBeacons.Value) return;

            if (waypoints == null || waypoints.Count == 0) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            Camera cam = CachedCamera != null ? CachedCamera : Camera.main;
            if (cam == null) return;

            Texture2D tex = GetWaypointBeaconTexture();
            if (tex == null) return;

            float beamMaxDistance = Mathf.Min(
                BeaconBeamMaxDistance,
                cam.farClipPlane > 0f ? cam.farClipPlane - 1f : BeaconBeamMaxDistance);
            if (beamMaxDistance <= BeaconNearHideDistance) return;

            float farFadeStart = Mathf.Max(BeaconNearFadeDistance, beamMaxDistance - 24f);
            if (farFadeStart >= beamMaxDistance)
                farFadeStart = Mathf.Max(BeaconNearHideDistance, beamMaxDistance * 0.75f);

            float beamMaxSqr = beamMaxDistance * beamMaxDistance;
            float labelMaxDistance = Mathf.Min(
                BeaconLabelMaxDistance,
                cam.farClipPlane > 0f ? cam.farClipPlane - 1f : BeaconLabelMaxDistance);
            float labelMaxSqr = labelMaxDistance * labelMaxDistance;
            Color previousGuiColor = GUI.color;
            Matrix4x4 previousGuiMatrix = GUI.matrix;

            try
            {
                Vector3 camPos = cam.transform.position;
                Matrix4x4 worldToCamera = cam.worldToCameraMatrix;
                float nearDepth = Mathf.Max(0.05f, cam.nearClipPlane + 0.05f);
                float fovDegrees = Mathf.Clamp(cam.fieldOfView, 20f, 120f);
                float tanHalfFov = Mathf.Tan(fovDegrees * 0.5f * Mathf.Deg2Rad);
                if (tanHalfFov <= 0.01f) return;

                float screenWidth = Screen.width;
                float screenHeight = Screen.height;
                float crosshairX = screenWidth * 0.5f;
                float crosshairY = screenHeight * 0.5f;

                int candidateCount = 0;
                int farLabelCount = 0;
                for (int i = 0; i < waypoints.Count; i++)
                {
                    WaypointEntry wp = waypoints[i];
                    if (wp == null || !wp.Enabled) continue;

                    // Waypoints used to store the camera/head Y; newer ones store the feet Y.
                    // Resolve the actual floor under the waypoint so both anchor the beam at
                    // the ground. The scan is only done for waypoints inside the label radius.
                    Vector3 rawPos = new Vector3(
                        wp.X + 0.5f, wp.Y + 0.05f, wp.Z + 0.5f);

                    float dx = rawPos.x - camPos.x;
                    float dy = rawPos.y - camPos.y;
                    float dz = rawPos.z - camPos.z;
                    float sqrDist = dx * dx + dy * dy + dz * dz;
                    if (sqrDist > labelMaxSqr) continue;

                    int groundY = ResolveWaypointGroundY(wp);
                    Vector3 basePos = new Vector3(
                        wp.X + 0.5f, groundY + 0.05f, wp.Z + 0.5f);

                    // Far waypoints get a name pill only: a full-height beam at 100+ blocks is
                    // visual noise and reads poorly through terrain. They still have map markers.
                    if (sqrDist > beamMaxSqr)
                    {
                        Vector3 labelWorld = new Vector3(
                            basePos.x, basePos.y + 2.2f, basePos.z);
                        Vector3 labelScreen = cam.WorldToScreenPoint(labelWorld);
                        if (labelScreen.z <= 0f) continue;

                        float lx = labelScreen.x;
                        float ly = screenHeight - labelScreen.y;
                        if (lx < -80f || lx > screenWidth + 80f
                            || ly < -40f || ly > screenHeight + 40f) continue;

                        int farSlot;
                        if (farLabelCount < BeaconMaxFarLabels)
                        {
                            farSlot = farLabelCount;
                            farLabelCount++;
                        }
                        else
                        {
                            if (sqrDist >= beaconFarLabels[BeaconMaxFarLabels - 1].SqrDistance)
                                continue;
                            farSlot = BeaconMaxFarLabels - 1;
                        }

                        while (farSlot > 0
                            && beaconFarLabels[farSlot - 1].SqrDistance > sqrDist)
                        {
                            beaconFarLabels[farSlot] = beaconFarLabels[farSlot - 1];
                            farSlot--;
                        }

                        beaconFarLabels[farSlot] = new BeaconFarLabel
                        {
                            SqrDistance = sqrDist,
                            ScreenX = lx,
                            ScreenY = ly,
                            ColorIndex = wp.ColorIndex,
                            Name = wp.Name ?? "",
                            IsDeath = wp.IsDeath
                        };
                        continue;
                    }

                    float distance = Mathf.Sqrt(Mathf.Max(sqrDist, 0.0001f));
                    if (distance <= BeaconNearHideDistance) continue;

                    // Near fade: a 48-block column at point-blank range would fill the screen.
                    float nearFade = Mathf.Clamp01(
                        (distance - BeaconNearHideDistance) /
                        (BeaconNearFadeDistance - BeaconNearHideDistance));
                    nearFade = Mathf.SmoothStep(0f, 1f, nearFade);

                    // Far fade: smooth the cull at min(96, farClip - 1); a lowered far clip
                    // must never show a beam over terrain that was not rendered.
                    float alpha = nearFade;
                    if (distance > farFadeStart && beamMaxDistance > farFadeStart)
                    {
                        float farFade = 1f - Mathf.InverseLerp(farFadeStart, beamMaxDistance, distance);
                        alpha *= Mathf.SmoothStep(0f, 1f, farFade);
                    }

                    if (alpha < 0.01f) continue;

                    // Clip the vertical segment against the camera near plane. Without this,
                    // a beam you are standing inside would disappear whenever its ground
                    // point falls behind the camera.
                    Vector3 topPos = new Vector3(basePos.x, basePos.y + BeaconHeightBlocks, basePos.z);
                    float baseDepth = -worldToCamera.MultiplyPoint(basePos).z;
                    float topDepth = -worldToCamera.MultiplyPoint(topPos).z;

                    if (baseDepth <= nearDepth && topDepth <= nearDepth) continue;

                    if (baseDepth < nearDepth)
                    {
                        float t = (nearDepth - baseDepth) / (topDepth - baseDepth);
                        basePos = Vector3.Lerp(basePos, topPos, Mathf.Clamp01(t));
                    }
                    else if (topDepth < nearDepth)
                    {
                        float t = (nearDepth - topDepth) / (baseDepth - topDepth);
                        topPos = Vector3.Lerp(topPos, basePos, Mathf.Clamp01(t));
                    }

                    Vector3 baseScreen = cam.WorldToScreenPoint(basePos);
                    Vector3 topScreen = cam.WorldToScreenPoint(topPos);
                    if (baseScreen.z <= 0f || topScreen.z <= 0f) continue;

                    // Recompute depth after clipping: the old base depth is invalid when the
                    // ground point was behind the near plane and moved forward.
                    baseDepth = -worldToCamera.MultiplyPoint(basePos).z;
                    if (baseDepth <= 0f) continue;

                    float baseGuiX = baseScreen.x;
                    float baseGuiY = screenHeight - baseScreen.y;
                    float topGuiX = topScreen.x;
                    float topGuiY = screenHeight - topScreen.y;

                    float segmentDx = topGuiX - baseGuiX;
                    float segmentDy = topGuiY - baseGuiY;
                    if (segmentDx * segmentDx + segmentDy * segmentDy <= 2.25f) continue;

                    // Perspective-correct thickness using the clipped camera-space depth.
                    // A constant world width keeps distant beams readable without making
                    // near ones huge (clamped to 26 px).
                    float depth = Mathf.Max(0.1f, baseDepth);
                    float pixelsPerBlock = screenHeight / (2f * depth * tanHalfFov);
                    float beamWidthPixels = Mathf.Clamp(
                        BeaconWidthBlocks * pixelsPerBlock,
                        BeaconMinPixelWidth,
                        BeaconMaxPixelWidth);

                    // Conservative off-screen cull: the rotated segment plus its half-width.
                    float minX = Mathf.Min(baseGuiX, topGuiX) - beamWidthPixels;
                    float maxX = Mathf.Max(baseGuiX, topGuiX) + beamWidthPixels;
                    float minY = Mathf.Min(baseGuiY, topGuiY) - beamWidthPixels;
                    float maxY = Mathf.Max(baseGuiY, topGuiY) + beamWidthPixels;
                    if (maxX < 0f || minX > screenWidth || maxY < 0f || minY > screenHeight)
                        continue;

                    // Nearest-first insertion into the fixed buffer: keep the 24 closest
                    // candidates regardless of the waypoint list's current sort order.
                    int slot;
                    if (candidateCount < BeaconMaxVisible)
                    {
                        slot = candidateCount;
                        candidateCount++;
                    }
                    else
                    {
                        if (sqrDist >= beaconCandidates[BeaconMaxVisible - 1].SqrDistance)
                            continue;
                        slot = BeaconMaxVisible - 1;
                    }

                    while (slot > 0 && beaconCandidates[slot - 1].SqrDistance > sqrDist)
                    {
                        beaconCandidates[slot] = beaconCandidates[slot - 1];
                        slot--;
                    }

                    beaconCandidates[slot] = new BeaconCandidate
                    {
                        SqrDistance = sqrDist,
                        BaseX = baseGuiX,
                        BaseY = baseGuiY,
                        TopX = topGuiX,
                        TopY = topGuiY,
                        Width = beamWidthPixels,
                        Alpha = alpha,
                        ColorIndex = wp.ColorIndex,
                        Name = wp.Name ?? "",
                        IsDeath = wp.IsDeath
                    };
                }

                // Draw far -> near so the closest beam wins where columns overlap.
                for (int c = candidateCount - 1; c >= 0; c--)
                {
                    BeaconCandidate bc = beaconCandidates[c];

                    float segmentX = bc.TopX - bc.BaseX;
                    float segmentY = bc.TopY - bc.BaseY;
                    float segmentLength = Mathf.Sqrt(
                        segmentX * segmentX + segmentY * segmentY);
                    if (segmentLength <= 1.5f) continue;

                    float angle = Mathf.Atan2(segmentY, segmentX) * Mathf.Rad2Deg;
                    float alpha = bc.Alpha;

                    // Crosshair protection follows the rotated centreline, not an old
                    // axis-aligned rect. Keep the crosshair readable but do not punch a
                    // visible hole in the column.
                    float guardRadius = bc.Width * 0.5f + BeaconCrosshairGuardPixels;
                    float crosshairDistSqr = PointToSegmentDistanceSqr(
                        crosshairX, crosshairY,
                        bc.BaseX, bc.BaseY, bc.TopX, bc.TopY);
                    if (crosshairDistSqr <= guardRadius * guardRadius)
                        alpha *= BeaconCrosshairAlphaScale;

                    if (alpha < 0.01f) continue;

                    Color wpCol = GetWaypointColor(bc.ColorIndex);
                    GUI.color = new Color(wpCol.r, wpCol.g, wpCol.b, alpha * 0.9f);

                    Matrix4x4 beamMatrix = GUI.matrix;
                    try
                    {
                        // Rotate around the projected base and draw +X from base -> top. The
                        // texture's bright end is at U=0 (the base), so roll/pitch can never
                        // invert the gradient; the beam always starts bright at the ground.
                        GUIUtility.RotateAroundPivot(
                            angle, new Vector2(bc.BaseX, bc.BaseY));
                        GUI.DrawTexture(
                            new Rect(bc.BaseX, bc.BaseY - bc.Width * 0.5f,
                                     segmentLength, bc.Width),
                            tex);
                    }
                    finally
                    {
                        GUI.matrix = beamMatrix;
                    }
                }

                // Far labels first, then the near beam labels on top. The nearest name wins
                // when a far label happens to overlap it.
                DrawFarBeaconLabels(farLabelCount, screenWidth, screenHeight);
                DrawBeaconLabels(candidateCount, screenWidth, screenHeight);
            }
            catch (Exception ex)
            {
                // Never let an OnGUI exception escape: Unity's Mono can abort while
                // formatting the stack trace. Log the type and message only, once.
                if (!waypointBeaconErrorLogged)
                {
                    waypointBeaconErrorLogged = true;
                    Log?.LogWarning("[Wayfinder] Waypoint beacons disabled after an error: " +
                                    ex.GetType().Name + ": " + ex.Message);
                }
            }
            finally
            {
                GUI.color = previousGuiColor;
                GUI.matrix = previousGuiMatrix;
            }
        }

        /// <summary>
        /// Finds the first standable Y under a waypoint, starting at its stored Y and scanning
        /// down a few blocks. Old waypoints stored the camera/head Y (two blocks above the
        /// floor); new ones store the feet Y. Both resolve to the same beam base.
        /// </summary>
        private int ResolveWaypointGroundY(WaypointEntry wp)
        {
            var world = GetActiveWorld();
            if (world == null) return wp.Y;

            for (int dy = 0; dy <= 5; dy++)
            {
                int y = wp.Y - dy;
                if (y < 0) break;
                try
                {
                    var b = WayfinderEngine.GetBlockSafe(world, wp.X, y, wp.Z);
                    if (!WayfinderModule.IsAirOrPassable(b.id)) return y + 1;
                }
                catch
                {
                    break;
                }
            }
            return wp.Y;
        }

        /// <summary>
        /// Draws the nearest beam names at the midpoint of the visible column. The anchor is the
        /// point of the projected beam closest to the screen centre, so the label stays readable
        /// even when the top of a 48-block beam is off-screen.
        /// </summary>
        private void DrawBeaconLabels(int candidateCount, float screenWidth, float screenHeight)
        {
            if (candidateCount <= 0 || SmallStyle == null) return;

            int labels = Mathf.Min(candidateCount, BeaconMaxLabels);
            Texture2D skull = GetWaypointDeathSkullTexture();
            GUI.color = Color.white;

            // Farthest label first; the nearest name is painted last and wins overlaps.
            for (int i = labels - 1; i >= 0; i--)
            {
                BeaconCandidate bc = beaconCandidates[i];
                if (string.IsNullOrEmpty(bc.Name)) continue;

                float segX = bc.TopX - bc.BaseX;
                float segY = bc.TopY - bc.BaseY;
                float lenSqr = segX * segX + segY * segY;
                float t = 0.5f;
                if (lenSqr > 1f)
                {
                    t = ((screenWidth * 0.5f - bc.BaseX) * segX
                        + (screenHeight * 0.5f - bc.BaseY) * segY) / lenSqr;
                    t = Mathf.Clamp01(t);
                }

                DrawBeaconLabelPill(
                    bc.BaseX + segX * t,
                    bc.BaseY + segY * t - 10f,
                    bc.Name, bc.ColorIndex, bc.IsDeath, skull, screenWidth, screenHeight);
            }
        }

        /// <summary>
        /// Names for waypoints beyond the beam radius. A beam at 100+ blocks is mostly visual
        /// noise, but the name still tells the player where to head; the fullscreen map keeps the
        /// edge markers for everything farther than <see cref="BeaconLabelMaxDistance"/>.
        /// </summary>
        private void DrawFarBeaconLabels(int farLabelCount, float screenWidth, float screenHeight)
        {
            if (farLabelCount <= 0 || SmallStyle == null) return;

            Texture2D skull = GetWaypointDeathSkullTexture();
            GUI.color = Color.white;

            for (int i = farLabelCount - 1; i >= 0; i--)
            {
                BeaconFarLabel label = beaconFarLabels[i];
                if (string.IsNullOrEmpty(label.Name)) continue;

                // Small ground dot so the floating name still marks the exact waypoint.
                Color wpCol = GetWaypointColor(label.ColorIndex);
                GUI.DrawTexture(
                    new Rect(label.ScreenX - 4f, label.ScreenY - 4f, 8f, 8f),
                    GetColorTexture(Color.black));
                GUI.DrawTexture(
                    new Rect(label.ScreenX - 3f, label.ScreenY - 3f, 6f, 6f),
                    GetColorTexture(wpCol));

                DrawBeaconLabelPill(
                    label.ScreenX, label.ScreenY - 24f,
                    label.Name, label.ColorIndex, label.IsDeath, skull, screenWidth, screenHeight);
            }
        }

        private void DrawBeaconLabelPill(
            float centerX, float pillTopY, string name, int colorIndex, bool isDeath,
            Texture2D skull, float screenWidth, float screenHeight)
        {
            Color wpCol = GetWaypointColor(colorIndex);
            float iconW = isDeath ? 17f : 0f;
            float pillW = Mathf.Min(
                screenWidth - 16f,
                Mathf.Max(76f, name.Length * 7.2f + 18f + iconW));
            centerX = Mathf.Clamp(
                centerX, 8f + pillW * 0.5f, screenWidth - 8f - pillW * 0.5f);
            float pillY = Mathf.Clamp(pillTopY, 8f, screenHeight - 30f);
            Rect pill = new Rect(centerX - pillW * 0.5f, pillY, pillW, 20f);

            GUI.DrawTexture(
                new Rect(pill.x - 1f, pill.y - 1f, pill.width + 2f, pill.height + 2f),
                GetColorTexture(wpCol));
            GUI.DrawTexture(pill, GetColorTexture(new Color(0.05f, 0.05f, 0.07f, 0.86f)));

            float textX = pill.x + 4f;
            if (isDeath && skull != null)
            {
                GUI.DrawTexture(new Rect(pill.x + 4f, pill.y + 3f, 14f, 14f), skull);
                textX += 17f;
            }

            DrawShadowLabel(
                new Rect(textX, pill.y, pill.xMax - textX - 4f, pill.height),
                name, SmallStyle, wpCol, Color.black);
        }

        // =====================================================================
        //        SHARED DEATH SKULL ICON
        // =====================================================================

        private static Texture2D waypointDeathSkullTex;

        /// <summary>
        /// 16x16 pixel-art skull used by beacon labels, fullscreen waypoints and the minimap.
        /// Procedural and cached: the game font has no reliable skull glyph.
        /// </summary>
        private static Texture2D GetWaypointDeathSkullTexture()
        {
            if (waypointDeathSkullTex != null) return waypointDeathSkullTex;

            // '#' bone, '+' shaded bone, 'o' eye/nose/teeth, '.' transparent.
            string[] rows =
            {
                "................",
                ".....######.....",
                "...##########...",
                "..############..",
                ".##############.",
                ".##############.",
                ".###oo####oo###.",
                ".###oo####oo###.",
                ".##############.",
                ".#####oooo#####.",
                "..####oooo####..",
                "...##########...",
                "....##o##o##....",
                "...##########...",
                "....++++++++....",
                "................"
            };

            const int size = 16;
            Color32 bone = new Color32(238, 234, 222, 255);
            Color32 shade = new Color32(188, 184, 174, 255);
            Color32 dark = new Color32(20, 18, 24, 255);
            Color32 clear = new Color32(0, 0, 0, 0);

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "AetherWaypointDeathSkull",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                string row = rows[y];
                int dstRow = size - 1 - y; // strings are top-down, textures are bottom-up
                for (int x = 0; x < size; x++)
                {
                    char c = x < row.Length ? row[x] : '.';
                    pixels[dstRow * size + x] = c == '#'
                        ? bone
                        : c == '+' ? shade : c == 'o' ? dark : clear;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false);
            waypointDeathSkullTex = tex;
            return tex;
        }

        /// <summary>Squared distance from a point to a screen-space segment. No allocation.</summary>
        private static float PointToSegmentDistanceSqr(
            float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax;
            float aby = by - ay;
            float apx = px - ax;
            float apy = py - ay;
            float lengthSqr = abx * abx + aby * aby;
            float t = lengthSqr > 0.0001f
                ? Mathf.Clamp01((apx * abx + apy * aby) / lengthSqr)
                : 0f;
            float dx = apx - abx * t;
            float dy = apy - aby * t;
            return dx * dx + dy * dy;
        }
    }
}
