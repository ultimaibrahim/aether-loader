using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Aether.Core.Diagnostics;
using Aether.Loader;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;
using CrashReport = Aether.Core.Diagnostics.CrashReport;

namespace Aether.UI.Diagnostics
{
    /// <summary>
    /// The "Send report" button: gathers what a mod author needs to fix a problem (versions, loaded mods, the error and the end of
    /// the BepInEx log), removes what could point at the player (home folders, user names, Steam ids, e-mail and IP addresses) and
    /// sends it to Lattice, which answers with a short code (AE-XXXXX) the player can quote. Nothing is sent by itself unless the
    /// player said "Always" once; the choice lives in Aether.UI's config and in F10 → Mod Health.
    /// </summary>
    public static class ProblemReports
    {
        public const string Endpoint = "https://lattice.ultimaibrahim.dev/api/crash-reports";
        public enum AutoSendMode { Ask, Always, Never }
        public enum State { Idle, Sending, Sent, Failed }

        public static ConfigEntry<AutoSendMode>? AutoSend { get; private set; }
        public static State Status { get; private set; } = State.Idle;
        public static string LastCode { get; private set; } = string.Empty;
        public static string LastError { get; private set; } = string.Empty;

        private static readonly HashSet<string> SentCrashIds = new HashSet<string>(StringComparer.Ordinal);
        private static float _lastSendAt = -999f;

        public static void Bind(ConfigFile config)
        {
            AutoSend = config.Bind("Reports", "AutoSend", AutoSendMode.Ask,
                "When something fails: Ask (show the Send report button and ask once), Always (send it by itself), Never (only the button). " +
                "Reports go to Lattice without personal paths, user names or IP, and are deleted after 90 days.");
            CrashReporter.OnCrashReportCreated += OnCrash;
        }

        private static void OnCrash(CrashReport report)
        {
            if (AutoSend?.Value == AutoSendMode.Always) SendCrash(report, auto: true);
        }

        public static bool AlreadySent(CrashReport report) => SentCrashIds.Contains(report.Id);

        public static void SendCrash(CrashReport report, bool auto = false, string? note = null)
        {
            if (!SentCrashIds.Add(report.Id) && auto) return;     // the automatic send goes once per crash; the button can retry
            Send(BuildJson("crash", auto, report.BlamedMod, report.ExceptionType, report.Message, report.StackTrace, note));
        }

        public static void SendHealth(string? note)
        {
            Send(BuildJson("health", false, null, null, null, null, string.IsNullOrWhiteSpace(note) ? "Sent from Mod Health (F10)." : note));
        }

        // ───────────────────────── payload ─────────────────────────

        internal static string BuildJson(string source, bool auto, string? blamed, string? type, string? message, string? stack, string? note)
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"v\":1,\"source\":").Append(Q(source)).Append(",\"auto\":").Append(auto ? "true" : "false");
            sb.Append(",\"client\":").Append(Q("aether " + AetherBootstrap.PluginVersion));
            sb.Append(",\"game\":").Append(Q("Block Story " + Application.version + " · Unity " + Application.unityVersion));
            sb.Append(",\"os\":").Append(Q(SystemInfo.operatingSystem));
            sb.Append(",\"blamed\":").Append(Q(blamed));
            sb.Append(",\"error\":{\"type\":").Append(Q(type)).Append(",\"message\":").Append(Q(message)).Append(",\"stack\":").Append(Q(Clip(stack, 16000))).Append('}');
            sb.Append(",\"mods\":[");
            bool first = true;
            void Mod(string id, string version)
            {
                if (string.IsNullOrWhiteSpace(id)) return;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":").Append(Q(id)).Append(",\"version\":").Append(Q(version)).Append('}');
            }
            foreach (var m in AetherBootstrap.LoadedMods) Mod(m.Id, m.Version.ToString());
            foreach (var m in Aether.Loader.Hosting.ExternalModCatalog.Snapshot()) Mod(m.Id, m.Version);
            sb.Append(']');
            sb.Append(",\"log\":").Append(Q(LogTail(24000)));
            sb.Append(",\"note\":").Append(Q(Clip(note, 1000)));
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>The end of BepInEx's log. BepInEx keeps it open for writing, so it is read with shared access.</summary>
        private static string? LogTail(int maxChars)
        {
            try
            {
                string path = Path.Combine(Paths.BepInExRootPath, "LogOutput.log");
                if (!File.Exists(path)) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                long start = Math.Max(0, fs.Length - maxChars * 2L);
                fs.Seek(start, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                string text = reader.ReadToEnd();
                if (start > 0) { int nl = text.IndexOf('\n'); if (nl >= 0) text = text.Substring(nl + 1); }
                return text.Length > maxChars ? text.Substring(text.Length - maxChars) : text;
            }
            catch { return null; }
        }

        // ───────────────────────── privacy ─────────────────────────

        /// <summary>Same rules as Lattice applies on arrival: done here too so nothing personal leaves the PC.</summary>
        public static string Scrub(string s) => ReportPrivacy.Scrub(s);

        private static string? Clip(string? s, int n) => s == null ? null : (s.Length > n ? s.Substring(0, n) : s);

        private static string Q(string? s)
        {
            if (s == null) return "null";
            s = Scrub(s);
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ───────────────────────── sending ─────────────────────────

        private static void Send(string json)
        {
            if (Status == State.Sending) return;
            if (Time.unscaledTime - _lastSendAt < 10f && Status == State.Sent) return;     // a double click sends once
            _lastSendAt = Time.unscaledTime;
            Status = State.Sending;
            LastError = string.Empty;
            try
            {
                var req = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = 20,
                };
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("X-Lattice", "1");
                req.SendWebRequest().completed += _ =>
                {
                    try
                    {
                        string body = req.downloadHandler?.text ?? string.Empty;
                        var m = Regex.Match(body, "\"code\"\\s*:\\s*\"(AE-[0-9A-Z]{5})\"");
                        if (req.responseCode == 201 && m.Success) { LastCode = m.Groups[1].Value; Status = State.Sent; }
                        else
                        {
                            Status = State.Failed;
                            LastError = req.responseCode == 429 ? "too_many" : (req.responseCode == 0 ? "offline" : "http_" + req.responseCode);
                        }
                    }
                    finally { req.Dispose(); }
                };
            }
            catch (Exception ex)
            {
                Status = State.Failed;
                LastError = "offline";
                Debug.LogWarning("[Aether] Could not send the report: " + ex.Message);
            }
        }

        /// <summary>One line for the UI, in the player's language.</summary>
        public static string StatusText(Func<string, string, string> tr)
        {
            switch (Status)
            {
                case State.Sending: return tr("Sending…", "Enviando…");
                case State.Sent: return tr($"Sent. Your report code: {LastCode}", $"Enviado. Tu código de reporte: {LastCode}");
                case State.Failed:
                    return LastError == "too_many" ? tr("Too many reports in a short time. Try again in a while.", "Demasiados reportes en poco tiempo. Prueba más tarde.")
                        : LastError == "offline" ? tr("Could not reach Lattice. Check your connection and try again.", "No se pudo llegar a Lattice. Revisa tu conexión y vuelve a intentar.")
                        : tr("Lattice did not accept the report (" + LastError + ").", "Lattice no aceptó el reporte (" + LastError + ").");
                default: return string.Empty;
            }
        }
    }
}
