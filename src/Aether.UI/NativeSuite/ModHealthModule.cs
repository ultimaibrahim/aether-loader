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

        private static GUIStyle? _healthKickerStyle, _healthValStyle, _healthSubStyle, _healthRowTitle, _healthRowMeta, _healthChipStyle;

        private static void EnsureHealthStyles()
        {
            if (_healthKickerStyle != null) return;
            _healthKickerStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Mute);
            _healthValStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 17, Aether.Nano.NanoBrand.Paper);
            _healthSubStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Body, 11, Aether.Nano.NanoBrand.Mute);
            _healthRowTitle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.BodySemi, 13, Aether.Nano.NanoBrand.Paper);
            _healthRowMeta = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Mute);
            _healthChipStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 9, Aether.Nano.NanoBrand.Paper, TextAnchor.MiddleCenter);
        }

        private void DrawModHealthPanel(Rect rightRect, float baseY)
        {
            EnsureHealthStyles();
            if (Time.unscaledTime - lastHealthRowsRefresh > 1f)
            {
                RefreshModHealthRows();
                lastHealthRowsRefresh = Time.unscaledTime;
            }

            float pad = 12f;
            float colW = Mathf.Max(260f, rightRect.width - 20f - pad * 2f);
            Rect col = new Rect(pad, baseY, colW, rightRect.height);

            float sy = Aether.Nano.NanoSurface.DrawSectionHeader(col, baseY,
                Tr("Live runtime health", "Salud del runtime en vivo"),
                $"{healthFps:F0} FPS");

            float cardW = (colW - 16f) / 3f;
            float cardH = 58f;
            DrawHealthCard(new Rect(pad, sy, cardW, cardH),
                "FPS", $"{healthFps:F0}", $"{healthFrameMs:F1} ms", Aether.Nano.NanoBrand.Paper);
            DrawHealthCard(new Rect(pad + cardW + 8f, sy, cardW, cardH),
                Tr("MEMORY", "MEMORIA"), $"{healthRamMb} MB", Tr("managed GC", "GC gestionado"), Aether.Nano.NanoBrand.Paper);
            DrawHealthCard(new Rect(pad + (cardW + 8f) * 2f, sy, cardW, cardH),
                Tr("LOADED MODS", "MODS CARGADOS"), $"{(AetherBootstrap.LoadedMods?.Count ?? 0) + Aether.Loader.Hosting.ExternalModCatalog.Snapshot().Count}",
                Tr("Aether + external", "Aether + externos"), Aether.Nano.NanoBrand.EmberSoft);
            sy += cardH + 8f;

            DrawHealthCard(new Rect(pad, sy, cardW, cardH),
                "HARMONY", HarmonyPatchesApplied ? "OK" : "MISSING",
                Tr("patches applied", "parches aplicados"),
                HarmonyPatchesApplied ? Aether.Nano.NanoBrand.Ok : Aether.Nano.NanoBrand.Bad);
            DrawHealthCard(new Rect(pad + cardW + 8f, sy, cardW, cardH),
                Tr("HOSTED", "EXTERNOS"), $"{Aether.Loader.Hosting.ExternalModCatalog.Snapshot().Count}",
                Tr("plugins in Aether/Mods", "plugins en Aether/Mods"), Aether.Nano.NanoBrand.Paper);
            DrawHealthCard(new Rect(pad + (cardW + 8f) * 2f, sy, cardW, cardH),
                "CRASH GUARD", CrashReporter.HasActiveCrash ? "ACTIVE" : "NONE",
                Tr("active report", "informe activo"),
                CrashReporter.HasActiveCrash ? Aether.Nano.NanoBrand.Bad : Aether.Nano.NanoBrand.Ok);
            sy += cardH + 16f;

            sy = Aether.Nano.NanoSurface.DrawSectionHeader(col, sy,
                Tr($"Loaded manifests ({healthRows.Count})", $"Manifiestos cargados ({healthRows.Count})"));

            if (healthRows.Count == 0)
            {
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(pad, sy, colW, 36f),
                    Tr("No manifests reported by AetherBootstrap.LoadedMods.",
                       "AetherBootstrap.LoadedMods no reporta ningún manifiesto."),
                    _healthSubStyle!, Aether.Nano.NanoBrand.Warn, 0.3f);
                sy += 42f;
            }
            else
            {
                for (int i = 0; i < healthRows.Count; i++)
                {
                    DrawHealthRow(new Rect(pad, sy, colW, 50f), healthRows[i]);
                    sy += 54f;
                }
            }

            sy += 8f;
            sy = Aether.Nano.NanoSurface.DrawSectionHeader(col, sy, Tr("Suite & environment", "Suite y entorno"));

            string scene = SceneManager.GetActiveScene().name;
            string suite = Instance != null ? "OK" : "MISSING";
            string line = $"Scene: {scene}  ·  Unity {Application.unityVersion}  ·  Aether {AetherBootstrap.PluginVersion}  ·  Suite: {suite}";
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(pad, sy, colW, 18f), line, _healthRowMeta!, Aether.Nano.NanoBrand.Paper, 0.3f);
            sy += 20f;

            string configPath = Config != null ? Config.ConfigFilePath : "?";
            string configLabel = string.IsNullOrEmpty(configPath) ? "?" : Path.GetFileName(configPath);
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(pad, sy, colW, 16f), $"Config: {configLabel}", _healthRowMeta!, Aether.Nano.NanoBrand.Mute, 0.25f);
            sy += 26f;

            if (Aether.Nano.NanoSurface.DrawActionButton(new Rect(pad, sy, 136f, 30f), Tr("Refresh", "Refrescar"), false))
            {
                RefreshModHealthRows();
                lastHealthRowsRefresh = Time.unscaledTime;
            }
            if (Aether.Nano.NanoSurface.DrawActionButton(new Rect(pad + 144f, sy, 168f, 30f), Tr("Write report", "Escribir informe"), true))
            {
                WriteModHealthReport();
            }
            if (!string.IsNullOrEmpty(lastHealthReportPath) &&
                Aether.Nano.NanoSurface.DrawActionButton(new Rect(pad + 320f, sy, 136f, 30f), Tr("Copy path", "Copiar ruta"), false))
            {
                GUIUtility.systemCopyBuffer = lastHealthReportPath;
            }

            sy += 38f;
            if (!string.IsNullOrEmpty(lastHealthReportPath))
            {
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(pad, sy, colW, 16f), lastHealthReportPath, _healthRowMeta!, Aether.Nano.NanoBrand.EmberSoft, 0.3f);
                sy += 22f;
            }

            // Send report: what happened (optional) and one click.
            sy += 4f;
            sy = Aether.Nano.NanoSurface.DrawSectionHeader(col, sy, Tr("Problem report", "Reportar problema"));
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(pad, sy, colW, 16f),
                Tr("Something wrong? Describe it in one line (optional) and send it:", "¿Algo falla? Cuéntalo en una línea (opcional) y envíalo:"),
                _healthSubStyle!, Aether.Nano.NanoBrand.Mute, 0.25f);
            sy += 22f;

            float sendBtnW = 166f;
            float noteW = Mathf.Max(180f, colW - sendBtnW - 8f);
            var noteRect = new Rect(pad, sy, noteW, 30f);
            Aether.Nano.NanoSurface.DrawDebossedSlot(noteRect, new Color(0.055f, 0.044f, 0.036f, 0.94f), Aether.Nano.NanoBrand.Line, 7f);
            healthNote = GUI.TextField(new Rect(noteRect.x + 6f, noteRect.y + 4f, noteRect.width - 12f, noteRect.height - 8f), healthNote ?? string.Empty, 300);

            bool busy = Aether.UI.Diagnostics.ProblemReports.Status == Aether.UI.Diagnostics.ProblemReports.State.Sending;
            if (Aether.Nano.NanoSurface.DrawActionButton(new Rect(pad + noteW + 8f, sy, sendBtnW, 30f),
                    busy ? Tr("Sending…", "Enviando…") : Tr("Send report", "Enviar reporte"), true) && !busy)
            {
                Aether.UI.Diagnostics.ProblemReports.SendHealth(healthNote);
                healthNote = string.Empty;
            }
            sy += 38f;

            string status = Aether.UI.Diagnostics.ProblemReports.StatusText(Tr);
            if (!string.IsNullOrEmpty(status))
            {
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(pad, sy, colW - 140f, 18f), status, _healthRowMeta!, Aether.Nano.NanoBrand.EmberSoft, 0.3f);
                if (Aether.UI.Diagnostics.ProblemReports.Status == Aether.UI.Diagnostics.ProblemReports.State.Sent &&
                    Aether.Nano.NanoSurface.DrawActionButton(new Rect(pad + colW - 130f, sy - 4f, 130f, 26f), Tr("Copy code", "Copiar código"), false))
                    GUIUtility.systemCopyBuffer = Aether.UI.Diagnostics.ProblemReports.LastCode;
                sy += 26f;
            }

            var auto = Aether.UI.Diagnostics.ProblemReports.AutoSend;
            if (auto != null)
            {
                string modeText = auto.Value == Aether.UI.Diagnostics.ProblemReports.AutoSendMode.Always ? Tr("Always", "Siempre")
                    : auto.Value == Aether.UI.Diagnostics.ProblemReports.AutoSendMode.Never ? Tr("Only with the button", "Solo con el botón")
                    : Tr("Ask me", "Preguntarme");
                if (Aether.Nano.NanoSurface.DrawActionButton(new Rect(pad, sy, Mathf.Min(360f, colW), 30f),
                        Tr("Auto-send on crash: ", "Enviar automático al fallar: ") + modeText, false))
                    auto.Value = auto.Value == Aether.UI.Diagnostics.ProblemReports.AutoSendMode.Ask ? Aether.UI.Diagnostics.ProblemReports.AutoSendMode.Always
                        : auto.Value == Aether.UI.Diagnostics.ProblemReports.AutoSendMode.Always ? Aether.UI.Diagnostics.ProblemReports.AutoSendMode.Never
                        : Aether.UI.Diagnostics.ProblemReports.AutoSendMode.Ask;
            }
        }

        private string? healthNote;

        private void DrawHealthCard(Rect rect, string title, string value, string subtitle, Color valueColor)
        {
            Aether.Nano.NanoSurface.FillRoundedGradient(rect,
                new Color(0.105f, 0.084f, 0.066f, 0.94f),
                new Color(0.068f, 0.054f, 0.042f, 0.96f), 8f);
            Aether.Nano.NanoSurface.StrokeRounded(rect, Aether.Nano.NanoBrand.Line, 1f, 8f);

            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(rect.x + 10f, rect.y + 5f, rect.width - 20f, 14f),
                title, _healthKickerStyle!, Aether.Nano.NanoBrand.Mute, 0.25f);
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(rect.x + 10f, rect.y + 19f, rect.width - 20f, 22f),
                value, _healthValStyle!, valueColor, 0.35f);
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(rect.x + 10f, rect.y + 40f, rect.width - 20f, 14f),
                subtitle, _healthSubStyle!, Aether.Nano.NanoBrand.Mute, 0.25f);
        }

        private void DrawHealthRow(Rect rect, ModHealthRow row)
        {
            Aether.Nano.NanoSurface.FillRounded(rect, new Color(0.072f, 0.058f, 0.046f, 0.85f), 8f);
            Aether.Nano.NanoSurface.StrokeRounded(rect, Aether.Nano.NanoBrand.Line, 1f, 8f);

            Color statusColor = !row.PathExists
                ? Aether.Nano.NanoBrand.Bad
                : (row.MetadataOk ? Aether.Nano.NanoBrand.Ok : Aether.Nano.NanoBrand.Warn);

            Aether.Nano.NanoSurface.DrawRhombus(new Rect(rect.x + 10f, rect.y + 11f, 8f, 8f), statusColor, true);

            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(rect.x + 25f, rect.y + 5f, rect.width - 190f, 18f),
                row.Name, _healthRowTitle!, Aether.Nano.NanoBrand.Paper, 0.35f);

            string meta = $"{row.Id}  ·  v{row.Version}  ·  {row.Author}";
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(rect.x + 25f, rect.y + 22f, rect.width - 190f, 14f),
                meta, _healthRowMeta!, Aether.Nano.NanoBrand.Mute, 0.25f);

            string path = row.PathExists
                ? $"{Path.GetFileName(row.Path)}  ({row.SizeKb} KB)"
                : (string.IsNullOrEmpty(row.Path) ? Tr("no source path", "sin ruta de origen") : row.Path);
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(rect.x + 25f, rect.y + 35f, rect.width - 190f, 12f),
                path, _healthRowMeta!, row.PathExists ? new Color(0.55f, 0.50f, 0.43f, 1f) : Aether.Nano.NanoBrand.Bad, 0.2f);

            float rightEdge = rect.xMax - 10f;
            Aether.Nano.NanoSurface.DrawChip(new Rect(rightEdge - 62f, rect.y + 8f, 62f, 18f),
                row.StatusText, _healthChipStyle!,
                new Color(statusColor.r * 0.22f, statusColor.g * 0.22f, statusColor.b * 0.22f, 0.92f),
                new Color(statusColor.r, statusColor.g, statusColor.b, 0.65f), statusColor, 4f);

            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(rightEdge - 140f, rect.y + 9f, 70f, 16f),
                $"#{row.LoadOrder}" + (row.Modules > 0 ? $" · {row.Modules}" : ""),
                _healthRowMeta!, Aether.Nano.NanoBrand.Mute, 0.25f);
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
