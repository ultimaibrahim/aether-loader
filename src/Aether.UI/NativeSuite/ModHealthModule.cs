using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Aether.Core.Diagnostics;
using Aether.Loader;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlockStoryLens
{
    /// <summary>
    /// "Mod Health" system panel: one screen that proves the live state of the loader, the suite,
    /// Harmony and every loaded mod manifest (assembly path, version, order, modules).
    /// Read-only by design; it never mutates mod state. Menus only, so a 0.5 s sample is enough.
    /// </summary>
    public partial class BlockStorySuitePlugin
    {
        private struct ModHealthRow
        {
            public string Name;
            public string Id;
            public string Version;
            public string Author;
            public string Path;
            public string StatusText;
            public long SizeKb;
            public int LoadOrder;
            public int Modules;
            public bool PathExists;
            public bool MetadataOk;
        }

        private readonly List<ModHealthRow> healthRows = new List<ModHealthRow>();
        private float healthFps = 60f;
        private float healthFrameMs = 16.6f;
        private long healthRamMb;
        private int healthSampleCount;
        private float lastHealthSampleTime = -10f;
        private float lastHealthRowsRefresh = -10f;
        private string lastHealthReportPath = string.Empty;

        /// <summary>Called every frame from the suite update; samples at most twice per second.</summary>
        public void UpdateModHealth()
        {
            if (Time.unscaledTime - lastHealthSampleTime < 0.5f) return;
            lastHealthSampleTime = Time.unscaledTime;

            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            float instantFps = 1f / dt;

            if (healthSampleCount < 4)
            {
                healthSampleCount++;
                healthFps = instantFps;
                healthFrameMs = dt * 1000f;
            }
            else
            {
                healthFps = Mathf.Lerp(healthFps, instantFps, 0.25f);
                healthFrameMs = Mathf.Lerp(healthFrameMs, dt * 1000f, 0.25f);
            }

            healthRamMb = GC.GetTotalMemory(false) / (1024 * 1024);
        }

        private void RefreshModHealthRows()
        {
            healthRows.Clear();

            var loaded = AetherBootstrap.LoadedMods;

            for (int i = 0; loaded != null && i < loaded.Count; i++)
            {
                var meta = loaded[i];
                if (meta == null) continue;

                string path = meta.SourcePath ?? string.Empty;
                bool pathExists = !string.IsNullOrEmpty(path) && File.Exists(path);
                long sizeKb = 0;
                if (pathExists)
                {
                    try { sizeKb = new FileInfo(path).Length / 1024; }
                    catch { pathExists = false; }
                }

                int modules = 0;
                for (int r = 0; r < registeredMods.Count; r++)
                {
                    var entry = registeredMods[r];
                    if (entry.IsFeatureRow) continue;
                    if (!string.Equals(entry.Id, meta.Id, StringComparison.OrdinalIgnoreCase)) continue;
                    modules = entry.FeatureIds != null ? entry.FeatureIds.Length : 0;
                    break;
                }

                bool metadataOk = !string.IsNullOrWhiteSpace(meta.Name) &&
                                  !string.IsNullOrWhiteSpace(meta.Author);

                healthRows.Add(new ModHealthRow
                {
                    Name = string.IsNullOrWhiteSpace(meta.Name) ? meta.Id : meta.Name,
                    Id = meta.Id,
                    Version = meta.Version != null ? meta.Version.ToString() : "1.0.0",
                    Author = string.IsNullOrWhiteSpace(meta.Author) ? Tr("Unknown", "Desconocido") : meta.Author,
                    Path = path,
                    SizeKb = sizeKb,
                    LoadOrder = i + 1,
                    Modules = modules,
                    PathExists = pathExists,
                    MetadataOk = metadataOk,
                    StatusText = !pathExists
                        ? Tr("MISSING", "FALTA")
                        : (metadataOk ? Tr("OK", "OK") : Tr("WARN", "AVISO"))
                });
            }

            // Third-party mods Aether does not load through [AetherMod]: hosted BepInEx plugins and the ones BepInEx started.
            int order = healthRows.Count;
            foreach (var ext in Aether.Loader.Hosting.ExternalModCatalog.Snapshot())
            {
                bool pathExists = !string.IsNullOrEmpty(ext.Location) && File.Exists(ext.Location);
                long sizeKb = 0;
                if (pathExists)
                {
                    try { sizeKb = new FileInfo(ext.Location).Length / 1024; }
                    catch { pathExists = false; }
                }

                healthRows.Add(new ModHealthRow
                {
                    Name = ext.Name,
                    Id = ext.Id,
                    Version = string.IsNullOrEmpty(ext.Version) ? "?" : ext.Version,
                    Author = string.IsNullOrWhiteSpace(ext.Author) ? ext.Source : ext.Author + " • " + ext.Source,
                    Path = ext.Location,
                    SizeKb = sizeKb,
                    LoadOrder = ++order,
                    Modules = 0,
                    PathExists = pathExists,
                    MetadataOk = true,
                    StatusText = ext.Source == "BepInEx" ? "BEPINEX" : "HOSTED"
                });
            }
        }

        private void DrawModHealthPanel(Rect rightRect, float baseY)
        {
            if (Time.unscaledTime - lastHealthRowsRefresh > 1f)
            {
                RefreshModHealthRows();
                lastHealthRowsRefresh = Time.unscaledTime;
            }

            float sy = baseY;
            DrawShadowLabel(new Rect(10, sy, rightRect.width - 24, 22),
                Tr("LIVE RUNTIME HEALTH", "SALUD DEL RUNTIME EN VIVO"),
                sectionTitleStyle, new Color(1.0f, 0.85f, 0.25f), Color.black);
            sy += 26f;

            float cardW = (rightRect.width - 40f) / 3f;
            float cardH = 54f;
            DrawHealthCard(new Rect(10, sy, cardW, cardH),
                Tr("FPS", "FPS"), $"{healthFps:F0}", $"{healthFrameMs:F1} ms");
            DrawHealthCard(new Rect(10 + cardW + 10f, sy, cardW, cardH),
                Tr("MEMORY", "MEMORIA"), $"{healthRamMb} MB", Tr("managed GC", "GC gestionado"));
            DrawHealthCard(new Rect(10 + (cardW + 10f) * 2f, sy, cardW, cardH),
                Tr("LOADED MODS", "MODS CARGADOS"), $"{(AetherBootstrap.LoadedMods?.Count ?? 0) + Aether.Loader.Hosting.ExternalModCatalog.Snapshot().Count}",
                Tr("Aether + third-party", "Aether + terceros"));
            sy += cardH + 8f;

            DrawHealthCard(new Rect(10, sy, cardW, cardH),
                "HARMONY", HarmonyPatchesApplied ? "OK" : "MISSING",
                Tr("patches applied", "parches aplicados"));
            DrawHealthCard(new Rect(10 + cardW + 10f, sy, cardW, cardH),
                Tr("HOSTED", "EXTERNOS"), $"{Aether.Loader.Hosting.ExternalModCatalog.Snapshot().Count}",
                Tr("BepInEx plugins in Aether/Mods", "plugins BepInEx en Aether/Mods"));
            DrawHealthCard(new Rect(10 + (cardW + 10f) * 2f, sy, cardW, cardH),
                Tr("CRASH GUARD", "CRASH GUARD"), CrashReporter.HasActiveCrash ? "ACTIVE" : "NONE",
                Tr("active report", "informe activo"));
            sy += cardH + 14f;

            DrawShadowLabel(new Rect(10, sy, rightRect.width - 24, 22),
                Tr($"LOADED MOD MANIFESTS ({healthRows.Count})", $"MANIFIESTOS CARGADOS ({healthRows.Count})"),
                sectionTitleStyle, new Color(1.0f, 0.85f, 0.25f), Color.black);
            sy += 24f;

            DrawShadowLabel(new Rect(12, sy, rightRect.width - 24, 14),
                Tr("STATUS     MOD / ID / VERSION                     ORDER   MODULES   STATUS",
                   "ESTADO     MOD / ID / VERSIÓN                     ORDEN   MÓDULOS   ESTADO"),
                sourceStyle, new Color(0.60f, 0.72f, 0.85f), Color.black);
            sy += 18f;

            if (healthRows.Count == 0)
            {
                DrawShadowLabel(new Rect(12, sy, rightRect.width - 28, 40),
                    Tr("No manifests reported by AetherBootstrap.LoadedMods.",
                       "AetherBootstrap.LoadedMods no reporta ningún manifiesto."),
                    descStyle, new Color(0.95f, 0.70f, 0.45f), Color.black);
                sy += 46f;
            }
            else
            {
                for (int i = 0; i < healthRows.Count; i++)
                {
                    DrawHealthRow(new Rect(10, sy, rightRect.width - 20, 46f), healthRows[i]);
                    sy += 48f;
                }
            }
            DrawShadowLabel(new Rect(10, sy, rightRect.width - 24, 22),
                Tr("SUITE & ENVIRONMENT", "SUITE Y ENTORNO"),
                sectionTitleStyle, new Color(1.0f, 0.85f, 0.25f), Color.black);
            sy += 24f;

            string scene = SceneManager.GetActiveScene().name;
            string suite = Instance != null ? "OK" : "MISSING";
            string line = $"Scene: {scene}   |   Unity {Application.unityVersion}   |   Aether {AetherBootstrap.PluginVersion}   |   Suite: {suite}";
            DrawShadowLabel(new Rect(10, sy, rightRect.width - 24, 18), line, authorStyle,
                new Color(0.85f, 0.90f, 0.98f), Color.black);
            sy += 20f;

            string configPath = Config != null ? Config.ConfigFilePath : "?";
            string configLabel = string.IsNullOrEmpty(configPath) ? "?" : Path.GetFileName(configPath);
            DrawShadowLabel(new Rect(10, sy, rightRect.width - 24, 16), $"Config: {configLabel}", sourceStyle,
                new Color(0.72f, 0.76f, 0.82f), Color.black);
            sy += 24f;


            sy += 8f;
            if (DrawNativeWoodButton(new Rect(10, sy, 150, 30), Tr("↻ Refresh", "↻ Refrescar"), false))
            {
                RefreshModHealthRows();
                lastHealthRowsRefresh = Time.unscaledTime;
            }

            if (DrawNativeWoodButton(new Rect(170, sy, 190, 30), Tr("▤ Write report", "▤ Escribir informe"), true))
            {
                WriteModHealthReport();
            }

            if (!string.IsNullOrEmpty(lastHealthReportPath) &&
                DrawNativeWoodButton(new Rect(370, sy, 150, 30), Tr("▣ Copy path", "▣ Copiar ruta"), false))
            {
                GUIUtility.systemCopyBuffer = lastHealthReportPath;
            }

            sy += 36f;
            if (!string.IsNullOrEmpty(lastHealthReportPath))
            {
                DrawShadowLabel(new Rect(10, sy, rightRect.width - 24, 16), lastHealthReportPath, sourceStyle,
                    new Color(0.55f, 0.90f, 1.0f), Color.black);
            }
        }

        private void DrawHealthCard(Rect rect, string title, string value, string subtitle)
        {
            DrawDarkSlotBox(rect);
            DrawShadowLabel(new Rect(rect.x + 8, rect.y + 3, rect.width - 16, 14), title, smallStyle,
                new Color(0.68f, 0.86f, 1.0f), Color.black);
            DrawShadowLabel(new Rect(rect.x + 8, rect.y + 16, rect.width - 16, 22), value, toolStyle,
                Color.white, Color.black);
            DrawShadowLabel(new Rect(rect.x + 8, rect.y + 37, rect.width - 16, 14), subtitle, authorStyle,
                new Color(0.80f, 0.82f, 0.86f), Color.black);
        }

        private void DrawHealthRow(Rect rect, ModHealthRow row)
        {
            DrawDarkSlotBox(rect);

            Color statusColor = !row.PathExists
                ? new Color(0.95f, 0.35f, 0.30f)
                : (row.MetadataOk ? new Color(0.25f, 0.90f, 0.40f) : new Color(1.0f, 0.80f, 0.20f));

            GUI.DrawTexture(new Rect(rect.x + 8, rect.y + 7, 10, 10), GetColorTexture(statusColor));

            DrawShadowLabel(new Rect(rect.x + 24, rect.y + 3, rect.width - 220, 18), row.Name, modNameStyle,
                Color.white, Color.black);

            string meta = $"{row.Id}  •  v{row.Version}  •  {row.Author}";
            DrawShadowLabel(new Rect(rect.x + 24, rect.y + 21, rect.width - 220, 15), meta, authorStyle,
                new Color(0.90f, 0.84f, 0.72f), Color.black);

            float rightEdge = rect.x + rect.width;
            DrawShadowLabel(new Rect(rightEdge - 186, rect.y + 5, 54, 16), $"#{row.LoadOrder}", authorStyle,
                new Color(0.80f, 0.88f, 1.0f), Color.black);
            DrawShadowLabel(new Rect(rightEdge - 128, rect.y + 5, 58, 16), $"{row.Modules}", authorStyle,
                new Color(0.80f, 0.88f, 1.0f), Color.black);
            DrawShadowLabel(new Rect(rightEdge - 68, rect.y + 5, 60, 16), row.StatusText, authorStyle,
                statusColor, Color.black);

            string path = row.PathExists
                ? $"{Path.GetFileName(row.Path)}  ({row.SizeKb} KB)"
                : (string.IsNullOrEmpty(row.Path) ? Tr("no source path", "sin ruta de origen") : row.Path);
            DrawShadowLabel(new Rect(rect.x + 24, rect.y + 34, rect.width - 34, 12), path, sourceStyle,
                row.PathExists ? new Color(0.65f, 0.72f, 0.80f) : new Color(0.95f, 0.45f, 0.40f), Color.black);
        }

        private void WriteModHealthReport()
        {
            try
            {
                RefreshModHealthRows();
                lastHealthRowsRefresh = Time.unscaledTime;

                string path = Path.Combine(Paths.ConfigPath, "aether_mod_health.txt");
                var sb = new StringBuilder();
                sb.AppendLine("Aether Mod Health Report");
                sb.AppendLine($"Generated: {DateTime.Now:O}");
                sb.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
                sb.AppendLine($"Unity: {Application.unityVersion}");
                sb.AppendLine($"Aether: {AetherBootstrap.PluginVersion}");
                sb.AppendLine($"FPS: {healthFps:F1} ({healthFrameMs:F2} ms)");
                sb.AppendLine($"RAM: {healthRamMb} MB");
                sb.AppendLine($"Loaded mods: {AetherBootstrap.LoadedMods?.Count ?? 0} Aether + {Aether.Loader.Hosting.ExternalModCatalog.Snapshot().Count} third-party");
                sb.AppendLine($"Suite: {(Instance != null ? "OK" : "MISSING")} | Harmony: {(HarmonyPatchesApplied ? "OK" : "MISSING")} | Crash: {(CrashReporter.HasActiveCrash ? "ACTIVE" : "NONE")}");
                sb.AppendLine($"Config: {(Config != null ? Config.ConfigFilePath : "?")}");
                sb.AppendLine();
                sb.AppendLine("ID | NAME | VERSION | AUTHOR | ORDER | MODULES | STATUS | ASSEMBLY");

                for (int i = 0; i < healthRows.Count; i++)
                {
                    var row = healthRows[i];
                    sb.AppendLine($"{row.Id} | {row.Name} | v{row.Version} | {row.Author} | #{row.LoadOrder} | {row.Modules} | {row.StatusText} | {row.Path}");
                }

                File.WriteAllText(path, sb.ToString());
                lastHealthReportPath = path;
                Log.LogInfo($"[Aether] Mod health report written: {path}");
            }
            catch (Exception ex)
            {
                Log.LogError($"[Aether] Failed to write mod health report: {ex}");
            }
        }
    }
}
