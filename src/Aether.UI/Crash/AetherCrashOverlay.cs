using System;
using Aether.Core.Diagnostics;
using Aether.Nano;
using Aether.UI.Diagnostics;
using UnityEngine;
using CrashReport = Aether.Core.Diagnostics.CrashReport;

namespace Aether.UI.Crash
{
    /// <summary>
    /// Aether In-Game Native Crash Guard Overlay (Aether Ink &amp; Squircle).
    /// Intercepts fatal exceptions before silent game crashes, displays suspected mod culprits,
    /// and allows players to copy the log path, send a problem report, continue, or exit cleanly.
    /// </summary>
    public static class AetherCrashOverlay
    {
        private static Vector2 _stackScroll = Vector2.zero;
        private static string _statusNotice = "";
        private static float _statusNoticeTime = 0f;
        private static GUIStyle? _titleStyle;
        private static GUIStyle? _normalTextStyle;
        private static GUIStyle? _headerStyle;
        private static GUIStyle? _subtextStyle;
        private static GUIStyle? _chipStyle;
        private static GUIStyle? _stackStyle;
        private static GUIStyle? _pathStyle;
        private static GUIStyle? _dangerBtnStyle;
        private static readonly GUIContent _measure = new GUIContent();

        public static void DrawGUI()
        {
            bool active = CrashReporter.IsOverlayRequested && CrashReporter.ActiveCrashReport != null;
            float anim = NanoMotion.Sample("aether:crash_guard", active, 0.20f, 0.12f);
            if (!active || anim <= 0.005f)
            {
                return;
            }

            var report = CrashReporter.ActiveCrashReport!;

            // Ensure cursor is visible and freely movable
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;

            InitStyles();

            int prevDepth = GUI.depth;
            GUI.depth = -9999; // Absolute topmost UI layer
            var prevColor = GUI.color;

            // 1. Warm Aether Ink full-screen dim backdrop (no legacy purple)
            NanoSurface.FillRounded(new Rect(0, 0, Screen.width, Screen.height), new Color(0.025f, 0.016f, 0.014f, 0.90f * anim), 0f);

            // 2. Centered Aether Ink Squircle Crash Card (no decorative top stripe)
            float cardWidth = Mathf.Min(800f, Screen.width - 40f);
            float cardHeight = Mathf.Min(580f, Screen.height - 40f);
            float cardX = (Screen.width - cardWidth) * 0.5f;
            float cardY = (Screen.height - cardHeight) * 0.5f;
            var cardRect = new Rect(cardX, cardY, cardWidth, cardHeight);

            using (NanoMotion.BeginModal(cardRect, anim))
            {
                if (Event.current.type == EventType.Repaint)
                {
                    NanoSurface.DrawDropShadow(cardRect, 14f, 0.95f * anim);
                    NanoSurface.FillRoundedGradient(cardRect,
                        new Color(0.095f, 0.072f, 0.062f, 0.98f),
                        new Color(0.056f, 0.044f, 0.038f, 0.99f), 14f);
                    NanoSurface.FillRounded(new Rect(cardRect.x + 14f, cardRect.y + 1f, cardRect.width - 28f, 1f),
                        new Color(1f, 0.92f, 0.88f, 0.12f), 0.5f);
                    NanoSurface.StrokeRounded(cardRect, new Color(NanoBrand.Bad.r, NanoBrand.Bad.g, NanoBrand.Bad.b, 0.65f), 1.2f, 14f);
                }

                float padX = 24f;
                float innerX = cardX + padX;
                float innerW = cardWidth - padX * 2f;
                float y = cardY + 18f;

                // Header: Cube + Gloock Title + Alert Chip
                float hx = innerX;
                const float cubeH = 22f;
                if (NanoBrand.Cube != null)
                    hx += NanoBrand.DrawCube(hx, y + 4f, cubeH) + 10f;

                NanoSurface.DrawCrispLabel(new Rect(hx, y, innerW - 140f, 30f),
                    Tr("Aether Crash Guard", "Aether Crash Guard"), _titleStyle!, NanoBrand.PaperLight, 0.4f);

                NanoSurface.DrawChip(new Rect(innerX + innerW - 112f, y + 4f, 112f, 22f),
                    Tr("INTERCEPTED", "INTERCEPTADO"), _chipStyle!,
                    new Color(NanoBrand.Bad.r * 0.26f, NanoBrand.Bad.g * 0.18f, NanoBrand.Bad.b * 0.18f, 0.94f),
                    new Color(NanoBrand.Bad.r, NanoBrand.Bad.g, NanoBrand.Bad.b, 0.72f),
                    new Color(1f, 0.78f, 0.76f, 1f), 6f);
                y += 32f;

                NanoSurface.DrawCrispLabel(new Rect(innerX, y, innerW, 18f),
                    Tr("A fatal exception was intercepted before the game engine terminated abruptly.",
                       "Se interceptó una excepción fatal antes de que el motor de juego cerrara abruptamente."),
                    _normalTextStyle!, NanoBrand.Mute, 0.25f);
                y += 24f;

                NanoSurface.FillRounded(new Rect(innerX, y, innerW, 1f), NanoBrand.Line, 0.5f);
                y += 12f;

                // Culprit / Hook row
                string blamedText = string.IsNullOrWhiteSpace(report.BlamedMod)
                    ? Tr("Unknown (native engine or external frame)", "Desconocido (motor nativo o frame externo)")
                    : report.BlamedMod;
                NanoSurface.DrawCrispLabel(new Rect(innerX, y + 2f, 132f, 20f),
                    Tr("SUSPECTED MOD", "MÓDULO INVOLUCRADO"), _subtextStyle!, NanoBrand.Mute, 0.2f);

                _measure.text = blamedText;
                float culpritW = Mathf.Clamp(_chipStyle!.CalcSize(_measure).x + 22f, 110f, innerW - 142f);
                NanoSurface.DrawChip(new Rect(innerX + 138f, y, culpritW, 22f), blamedText, _chipStyle!,
                    new Color(0.24f, 0.14f, 0.09f, 0.94f), NanoBrand.Ember, NanoBrand.EmberSoft, 6f);

                if (!string.IsNullOrWhiteSpace(report.HookName))
                {
                    float hookX = innerX + 146f + culpritW;
                    float hookAvail = (innerX + innerW) - hookX;
                    if (hookAvail > 90f)
                    {
                        NanoSurface.DrawCrispLabel(new Rect(hookX, y + 2f, hookAvail, 20f),
                            $"Hook: {report.HookName}", _subtextStyle!, NanoBrand.Mute, 0.2f);
                    }
                }
                y += 30f;

                // Exception headline
                string exHeadline = $"{report.ExceptionType}: {report.Message}";
                NanoSurface.DrawCrispLabel(new Rect(innerX, y, innerW, 22f), exHeadline, _headerStyle!, NanoBrand.PaperLight, 0.35f);
                y += 26f;

                // Stack trace inside debossed slot with custom Aether scrollbar
                NanoSurface.DrawCrispLabel(new Rect(innerX, y, innerW, 16f),
                    Tr("CALL STACK", "PILA DE LLAMADAS (STACK TRACE)"), _subtextStyle!, NanoBrand.Mute, 0.2f);
                y += 18f;

                float stackH = 166f;
                Rect stackBox = new Rect(innerX, y, innerW, stackH);
                NanoSurface.DrawDebossedSlot(stackBox, new Color(0.036f, 0.028f, 0.023f, 0.94f), NanoBrand.Line, 9f);

                Rect stackInner = new Rect(stackBox.x + 8f, stackBox.y + 6f, stackBox.width - 16f, stackBox.height - 12f);
                string stackText = string.IsNullOrEmpty(report.StackTrace) ? "(no stack trace)" : report.StackTrace;
                _measure.text = stackText;
                float stackContentH = Mathf.Max(stackInner.height, _stackStyle!.CalcHeight(_measure, Mathf.Max(120f, stackInner.width - 16f)) + 10f);
                _stackScroll = NanoSurface.BeginScrollView(stackInner, _stackScroll, stackContentH, out float stackTextW);
                try
                {
                    GUI.TextArea(new Rect(0f, 0f, stackTextW, stackContentH), stackText, _stackStyle!);
                }
                finally
                {
                    NanoSurface.EndScrollView();
                }
                y += stackH + 12f;

                // Saved report path in debossed slot
                NanoSurface.DrawCrispLabel(new Rect(innerX, y, innerW, 16f),
                    Tr("SAVED REPORT", "REGISTRO DETALLADO GUARDADO EN"), _subtextStyle!, NanoBrand.Mute, 0.2f);
                y += 18f;

                string displayPath = report.SavedFilePath ?? "crash-reports/";
                Rect pathBox = new Rect(innerX, y, innerW, 28f);
                NanoSurface.DrawDebossedSlot(pathBox, new Color(0.042f, 0.034f, 0.028f, 0.94f), NanoBrand.Line, 7f);
                GUI.TextField(new Rect(pathBox.x + 6f, pathBox.y + 3f, pathBox.width - 12f, pathBox.height - 6f), displayPath, _pathStyle!);
                y += 36f;

                // Problem report send row + auto-send preference
                y = DrawReportRow(report, innerX, y, innerW);

                // Transient status toast
                if (Time.unscaledTime - _statusNoticeTime < 3.0f && !string.IsNullOrEmpty(_statusNotice))
                {
                    NanoSurface.DrawCrispLabel(new Rect(innerX, y + 2f, innerW, 18f),
                        _statusNotice, _subtextStyle!, NanoBrand.Ok, 0.25f);
                }

                // Bottom action bar
                float btnH = 34f;
                float btnY = cardY + cardHeight - btnH - 18f;
                NanoSurface.FillRounded(new Rect(innerX, btnY - 10f, innerW, 1f), NanoBrand.Line, 0.5f);

                if (NanoSurface.DrawActionButton(new Rect(innerX, btnY, 152f, btnH), Tr("Copy path", "Copiar ruta"), true))
                {
                    GUIUtility.systemCopyBuffer = report.SavedFilePath ?? "";
                    _statusNotice = Tr("Path copied to clipboard.", "Ruta copiada al portapapeles.");
                    _statusNoticeTime = Time.unscaledTime;
                }

                if (NanoSurface.DrawActionButton(new Rect(innerX + 162f, btnY, 140f, btnH), Tr("Continue", "Continuar"), false))
                {
                    CrashReporter.DismissActiveCrash();
                }

                Rect exitRect = new Rect(innerX + innerW - 152f, btnY, 152f, btnH);
                if (DrawDangerButton(exitRect, Tr("Close game", "Cerrar juego")))
                {
                    CrashReporter.TerminateGame();
                }
            }

            GUI.color = prevColor;
            GUI.depth = prevDepth;
        }

        private static bool DrawDangerButton(Rect rect, string label)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                Color top = hover ? new Color(0.74f, 0.22f, 0.20f, 0.98f) : new Color(0.56f, 0.16f, 0.15f, 0.96f);
                Color bot = hover ? new Color(0.54f, 0.14f, 0.13f, 0.98f) : new Color(0.38f, 0.10f, 0.10f, 0.96f);
                NanoSurface.FillRoundedGradient(rect, top, bot, 8f);
                NanoSurface.StrokeRounded(rect, hover ? new Color(1f, 0.62f, 0.58f, 0.85f) : new Color(NanoBrand.Bad.r, NanoBrand.Bad.g, NanoBrand.Bad.b, 0.75f), 1f, 8f);
                NanoSurface.DrawCrispLabel(rect, label, _dangerBtnStyle!, NanoBrand.PaperLight, 0.35f);
            }
            return clicked;
        }

        private static string Tr(string en, string es) => BlockStoryLens.BlockStorySuitePlugin.Tr(en, es);

        private static float DrawReportRow(CrashReport report, float x, float y, float width)
        {
            var mode = ProblemReports.AutoSend?.Value ?? ProblemReports.AutoSendMode.Ask;
            bool busy = ProblemReports.Status == ProblemReports.State.Sending;
            bool sent = ProblemReports.Status == ProblemReports.State.Sent && ProblemReports.AlreadySent(report);

            float sendW = 176f;
            bool clickedSend = NanoSurface.DrawActionButton(new Rect(x, y, sendW, 30f),
                sent ? Tr("Report sent", "Reporte enviado") : (busy ? Tr("Sending…", "Enviando…") : Tr("Send report", "Enviar reporte")),
                !sent && !busy);
            if (clickedSend && !busy && !sent)
                ProblemReports.SendCrash(report);

            float copyW = sent ? 124f : 0f;
            float lineX = x + sendW + 10f;
            float lineW = Mathf.Max(80f, width - sendW - 10f - (sent ? copyW + 8f : 0f));
            string line = ProblemReports.StatusText(Tr);
            if (string.IsNullOrEmpty(line))
                line = Tr("Sends the error, your mod list and the end of the log to the mod's author. No personal paths, user names or IP.",
                          "Manda el error, tu lista de mods y el final del log al autor del mod. Sin rutas personales, nombres de usuario ni IP.");
            NanoSurface.DrawCrispLabel(new Rect(lineX, y + 1f, lineW, 30f), line, _subtextStyle!, sent ? NanoBrand.Ok : NanoBrand.Mute, 0.2f);

            if (sent && NanoSurface.DrawActionButton(new Rect(x + width - copyW, y, copyW, 30f), Tr("Copy code", "Copiar código"), false))
            {
                GUIUtility.systemCopyBuffer = ProblemReports.LastCode;
                _statusNotice = Tr("Code copied.", "Código copiado.");
                _statusNoticeTime = Time.unscaledTime;
            }
            y += 36f;

            if (mode == ProblemReports.AutoSendMode.Ask && ProblemReports.AutoSend != null)
            {
                NanoSurface.DrawCrispLabel(new Rect(x, y + 4f, width - 294f, 20f),
                    Tr("Send them by itself when something fails?", "¿Enviarlos solos cuando algo falle?"),
                    _subtextStyle!, NanoBrand.Mute, 0.2f);

                if (NanoSurface.DrawActionButton(new Rect(x + width - 286f, y, 108f, 26f), Tr("Always", "Siempre"), false))
                {
                    ProblemReports.AutoSend.Value = ProblemReports.AutoSendMode.Always;
                    if (!ProblemReports.AlreadySent(report)) ProblemReports.SendCrash(report, auto: true);
                }
                if (NanoSurface.DrawActionButton(new Rect(x + width - 170f, y, 170f, 26f), Tr("Only with the button", "Solo con el botón"), false))
                    ProblemReports.AutoSend.Value = ProblemReports.AutoSendMode.Never;

                y += 32f;
            }

            return y;
        }

        private static void InitStyles()
        {
            if (_titleStyle != null) return;

            _titleStyle = NanoBrand.Style(NanoBrand.Display, 22, NanoBrand.PaperLight);
            _normalTextStyle = NanoBrand.Style(NanoBrand.Body, 12, NanoBrand.Paper);
            _headerStyle = NanoBrand.Style(NanoBrand.BodySemi, 13, NanoBrand.PaperLight);
            _subtextStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.Mute);
            _subtextStyle.wordWrap = true;
            _chipStyle = NanoBrand.Style(NanoBrand.Mono, 10, NanoBrand.PaperLight, TextAnchor.MiddleCenter);
            _dangerBtnStyle = NanoBrand.Style(NanoBrand.BodySemi, 12, NanoBrand.PaperLight, TextAnchor.MiddleCenter);

            _stackStyle = new GUIStyle(GUI.skin.textArea)
            {
                font = NanoBrand.Mono,
                fontSize = 11,
                wordWrap = true,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(4, 4, 2, 2)
            };
            _stackStyle.normal.background = null;
            _stackStyle.focused.background = null;
            _stackStyle.hover.background = null;
            _stackStyle.active.background = null;
            _stackStyle.normal.textColor = NanoBrand.Paper;
            _stackStyle.focused.textColor = NanoBrand.PaperLight;

            _pathStyle = new GUIStyle(GUI.skin.textField)
            {
                font = NanoBrand.Mono,
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(4, 4, 2, 2)
            };
            _pathStyle.normal.background = null;
            _pathStyle.focused.background = null;
            _pathStyle.hover.background = null;
            _pathStyle.active.background = null;
            _pathStyle.normal.textColor = NanoBrand.EmberSoft;
            _pathStyle.focused.textColor = NanoBrand.PaperLight;
        }
    }
}
