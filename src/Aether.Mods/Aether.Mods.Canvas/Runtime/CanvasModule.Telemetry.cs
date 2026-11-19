using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Aether.Nano;
using BepInEx;
using Blocksters.Terrain.chunk;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Aether.Mods.Canvas
{
    public sealed partial class CanvasModule
    {
        private bool _pendingOpenSnapshot;

        /// <summary>
        /// Zero-gameplay-overhead diagnostic snapshot of the entire live HUD (native NGUI hierarchy +
        /// all NanoHud mod elements + automatic overlap checks). Runs strictly on-demand (once on Canvas open,
        /// once on close, or when the user triggers a snapshot — never inside Update/OnGUI loops).
        /// </summary>
        private void WriteLayoutSnapshot(
            string trigger,
            WorldMode mode,
            HudElementVisual stats,
            HudElementVisual dia,
            HudElementVisual btns,
            HudElementVisual acts,
            IReadOnlyList<NanoHudElement> mods)
        {
            try
            {
                var sb = new StringBuilder(2048);
                string scene = SceneManager.GetActiveScene().name;
                Inventory inv = Aether.Nano.Nano.Inventory;
                Camera guiCam = ResolveHudCamera(_statsObj ?? _diamondObj ?? _buttonsObj ?? _actionsObj);

                sb.AppendLine("=== Aether Canvas HUD Telemetry Snapshot ===");
                sb.AppendLine($"Timestamp : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                sb.AppendLine($"Trigger   : {trigger}");
                sb.AppendLine($"Scene     : {scene} | Mode: {mode} | Screen: {Screen.width}x{Screen.height}");
                if (guiCam != null)
                    sb.AppendLine($"GuiCamera : {guiCam.name} enabled={guiCam.enabled} active={guiCam.gameObject.activeInHierarchy} orthoSize={guiCam.orthographicSize.ToString("F3", CultureInfo.InvariantCulture)} depth={guiCam.depth.ToString("F1", CultureInfo.InvariantCulture)}");
                else
                    sb.AppendLine("GuiCamera : <null>");

                sb.AppendLine($"Preset    : {HudLayoutPreset?.Value ?? "?"} | MoveDiamonds={MoveDiamondsUnderXP?.Value} | MoveButtons={MoveTopRightButtonsToBottomRight?.Value}");
                sb.AppendLine();

                // 1. Tracked Native HUD groups
                sb.AppendLine("-- Native HUD Groups (4) --");
                AppendNativeGroup(sb, "HUD/Stats", _statsObj, _statsAnchor, StatsOffsetX.Value, StatsOffsetY.Value, StatsScale.Value, stats);
                AppendNativeGroup(sb, "HUD/Diamond", _diamondObj, _diamondAnchor, DiamondsOffsetX.Value, DiamondsOffsetY.Value, DiamondsScale.Value, dia);
                AppendNativeGroup(sb, "HUD/Buttons", _buttonsObj, _buttonsAnchor, MenuButtonsOffsetX.Value, MenuButtonsOffsetY.Value, MenuButtonsScale.Value, btns);
                AppendNativeGroup(sb, "HUD/Actions", _actionsObj, _actionsAnchor, ActionsOffsetX.Value, ActionsOffsetY.Value, ActionsScale.Value, acts);
                if (_statsObj != null)
                {
                    sb.Append("  Stats sprites: ");
                    var sprites = _statsObj.GetComponentsInChildren<UISprite>(false);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < sprites.Length; i++)
                        if (sprites[i] != null && !string.IsNullOrEmpty(sprites[i].spriteName)) seen.Add(sprites[i].spriteName);
                    sb.AppendLine(string.Join(", ", seen));
                    if (_barsAnchorObj != null)
                        sb.AppendLine($"  Stats/BarsAnchor localPos={FormatVec3(_barsAnchorObj.transform.localPosition)}");
                }
                sb.AppendLine();

                // 2. Full top-level children of Inventory.hud (discovers any extra native HUD groups or buttons!)
                sb.AppendLine("-- Live Inventory.hud Children --");
                if (inv != null && inv.hud != null)
                {
                    Transform root = inv.hud.transform;
                    sb.AppendLine($"HUD root '{inv.hud.name}' active={inv.hud.activeSelf} children={root.childCount} scale={FormatVec3(root.localScale)}");
                    var scratchParts = new List<Rect>(8);
                    for (int i = 0; i < root.childCount; i++)
                    {
                        Transform ch = root.GetChild(i);
                        bool hasVis = ProjectVisibleWidgets(ch.gameObject, scratchParts, out Rect cb);
                        UIAnchor anc = ch.GetComponent<UIAnchor>();
                        string ancStr = anc != null
                            ? $"anchor={anc.side}({anc.relativeOffset.x.ToString("F3", CultureInfo.InvariantCulture)},{anc.relativeOffset.y.ToString("F3", CultureInfo.InvariantCulture)})"
                            : "no-anchor";
                        sb.AppendLine($"  [{i}] {ch.name} active={ch.gameObject.activeSelf} {ancStr} scale={FormatVec3(ch.localScale)} visible={hasVis} rect={FormatRect(cb)} parts={scratchParts.Count}");
                    }
                }
                else
                {
                    sb.AppendLine("  <Inventory.hud unavailable>");
                }
                sb.AppendLine();

                // 3. Registered NanoHud mod elements
                sb.AppendLine($"-- NanoHud Mod Elements ({mods.Count}) --");
                var visibleBoxes = new List<(string Name, Rect Rect)>(8);
                if (!stats.HiddenByMode) visibleBoxes.Add(("Native:Stats", stats.Bounds));
                if (!dia.HiddenByMode) visibleBoxes.Add(("Native:Diamonds", dia.Bounds));
                if (!btns.HiddenByMode) visibleBoxes.Add(("Native:Buttons", btns.Bounds));
                if (!acts.HiddenByMode) visibleBoxes.Add(("Native:Actions", acts.Bounds));

                for (int i = 0; i < mods.Count; i++)
                {
                    var el = mods[i];
                    if (el == null) continue;
                    Rect r = el.GetRect != null ? el.GetRect() : Rect.zero;
                    sb.AppendLine($"  {el.Id} ({el.Name}) anchor={el.Anchor?.Value ?? "-"} off=({el.OffsetX?.Value.ToString("F1", CultureInfo.InvariantCulture) ?? "-"},{el.OffsetY?.Value.ToString("F1", CultureInfo.InvariantCulture) ?? "-"}) readout={FormatModReadout(el)} preview={el.DrawPreview != null} rect={FormatRect(r)}");
                    if (r.width > 4f && r.height > 4f)
                        visibleBoxes.Add((el.Name, r));
                }
                sb.AppendLine();

                // 4. Overlap detector across visible elements
                int overlaps = 0;
                sb.AppendLine("-- Overlap Check --");
                for (int i = 0; i < visibleBoxes.Count; i++)
                {
                    for (int j = i + 1; j < visibleBoxes.Count; j++)
                    {
                        if (visibleBoxes[i].Rect.Overlaps(visibleBoxes[j].Rect))
                        {
                            overlaps++;
                            sb.AppendLine($"  OVERLAP: {visibleBoxes[i].Name} {FormatRect(visibleBoxes[i].Rect)} <-> {visibleBoxes[j].Name} {FormatRect(visibleBoxes[j].Rect)}");
                        }
                    }
                }
                if (overlaps == 0) sb.AppendLine("  None (all visible elements clear).");

                string logDir = Path.Combine(Paths.GameRootPath, "Aether", "logs");
                Directory.CreateDirectory(logDir);
                string path = Path.Combine(logDir, "canvas-snapshot.txt");
                File.WriteAllText(path, sb.ToString());

                int hiddenCount = (stats.HiddenByMode ? 1 : 0) + (dia.HiddenByMode ? 1 : 0) + (btns.HiddenByMode ? 1 : 0) + (acts.HiddenByMode ? 1 : 0);
                Log?.LogInfo($"[Canvas] Snapshot ({trigger}): {mode} {Screen.width}x{Screen.height} | native=4 ({hiddenCount} hidden) | mods={mods.Count} | overlaps={overlaps} -> Aether/logs/canvas-snapshot.txt");
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"[Canvas] Snapshot failed: {ex.Message}");
            }
        }

        private static void AppendNativeGroup(
            StringBuilder sb,
            string label,
            GameObject go,
            UIAnchor anchor,
            float ox,
            float oy,
            float scale,
            HudElementVisual vis)
        {
            string state = vis.HiddenByMode ? $"HIDDEN ({vis.ShortBadgeEn})" : "VISIBLE";
            string objInfo = go != null ? $"obj=alive active={go.activeInHierarchy}" : "obj=destroyed/null";
            string ancInfo = anchor != null ? $"anchor={anchor.side}({anchor.relativeOffset.x.ToString("F3", CultureInfo.InvariantCulture)},{anchor.relativeOffset.y.ToString("F3", CultureInfo.InvariantCulture)})" : "anchor=null";
            sb.AppendLine($"  {label,-12} [{state,-18}] {objInfo} | {ancInfo} | cfg=({ox.ToString("F3", CultureInfo.InvariantCulture)},{oy.ToString("F3", CultureInfo.InvariantCulture)}) scale={scale.ToString("F2", CultureInfo.InvariantCulture)} | rect={FormatRect(vis.Bounds)} parts={vis.Parts?.Count ?? 0}");
        }

        private static string FormatRect(Rect r) =>
            string.Format(CultureInfo.InvariantCulture, "({0:F0},{1:F0} {2:F0}x{3:F0})", r.x, r.y, r.width, r.height);

        private static string FormatVec3(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
    }
}
