using System;
using Aether.Core.Diagnostics;
using Aether.UI.Theme;
using UnityEngine;

namespace Aether.UI.Crash
{
    /// <summary>
    /// Atelier In-Game Native Crash Overlay.
    /// Intercepts fatal exceptions before silent game crashes, displays suspected mod culprits,
    /// and allows players to open the log path, copy info, or exit cleanly.
    /// </summary>
    public static class AetherCrashOverlay
    {
        private static Vector2 _stackScroll = Vector2.zero;
        private static string _statusNotice = "";
        private static float _statusNoticeTime = 0f;
        private static GUIStyle? _cardTitleStyle;
        private static GUIStyle? _normalTextStyle;
        private static GUIStyle? _headerStyle;
        private static GUIStyle? _subtextStyle;
        private static GUIStyle? _culpritBadgeStyle;
        private static GUIStyle? _pathBoxStyle;
        private static GUIStyle? _buttonPrimaryStyle;
        private static GUIStyle? _buttonDangerStyle;
        private static GUIStyle? _buttonSecondaryStyle;
        private static Texture2D? _crimsonTexture;

        public static void DrawGUI()
        {
            if (!CrashReporter.IsOverlayRequested || CrashReporter.ActiveCrashReport == null)
            {
                return;
            }

            var report = CrashReporter.ActiveCrashReport;

            // Ensure cursor is visible and freely movable
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;

            InitStyles();

            int prevDepth = GUI.depth;
            GUI.depth = -9999; // Absolute topmost UI layer

            // 1. Full-screen dim backdrop
            var fullScreenRect = new Rect(0, 0, Screen.width, Screen.height);
            var prevColor = GUI.color;
            GUI.color = new Color(9f / 255f, 3f / 255f, 20f / 255f, 0.94f); // Deep Void dim
            GUI.DrawTexture(fullScreenRect, Texture2D.whiteTexture);

            // 2. Centered Atelier Squircle Crash Card
            float cardWidth = Math.Min(780f, Screen.width - 40f);
            float cardHeight = Math.Min(560f, Screen.height - 40f);
            float cardX = (Screen.width - cardWidth) / 2f;
            float cardY = (Screen.height - cardHeight) / 2f;
            var cardRect = new Rect(cardX, cardY, cardWidth, cardHeight);

            // Card body (Obsidian Glass)
            GUI.color = AtelierTheme.ObsidianGlass;
            GUI.DrawTexture(cardRect, AtelierTheme.ObsidianTexture, ScaleMode.StretchToFill, true, 0);

            // Card top alert accent (Crimson)
            GUI.color = new Color(0.94f, 0.27f, 0.27f, 1f); // #EF4444
            GUI.DrawTexture(new Rect(cardX, cardY, cardWidth, 4f), _crimsonTexture!);

            // Content Area
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(cardX + 24, cardY + 20, cardWidth - 48, cardHeight - 36));

            // Header
            GUILayout.Label("✦ AETHER CRASH GUARD", _cardTitleStyle);
            GUILayout.Label("Se ha interceptado una excepción fatal antes de que el motor de juego termine abruptamente.", _normalTextStyle!);
            GUILayout.Space(12);

            // Culprit / Blame Box
            GUILayout.BeginHorizontal();
            GUILayout.Label("Módulo Involucrado:", GUILayout.Width(140));
            string blamedText = string.IsNullOrWhiteSpace(report.BlamedMod) ? "Desconocido (Motor nativo o frame externo)" : report.BlamedMod;
            GUILayout.Label($"  {blamedText}  ", _culpritBadgeStyle);
            if (!string.IsNullOrWhiteSpace(report.HookName))
            {
                GUILayout.Space(8);
                GUILayout.Label($"Hook: {report.HookName}", _subtextStyle!);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Exception & Message
            GUILayout.Label($"<b>{report.ExceptionType}</b>: {report.Message}", _headerStyle!);
            GUILayout.Space(6);

            // Stack trace scroll box
            GUILayout.Label("Pila de llamadas (Stack Trace):", _subtextStyle!);
            _stackScroll = GUILayout.BeginScrollView(_stackScroll, GUILayout.Height(180));
            GUILayout.TextArea(report.StackTrace, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();

            GUILayout.Space(12);

            // Saved File Path Display
            GUILayout.Label("Registro detallado generado y guardado en:", _subtextStyle!);
            string displayPath = report.SavedFilePath ?? "Guardado en carpeta crash-reports/";
            GUILayout.TextField(displayPath, _pathBoxStyle!);

            GUILayout.Space(8);

            // Status notice (e.g. "¡Ruta copiada al portapapeles!")
            if (Time.unscaledTime - _statusNoticeTime < 3.0f && !string.IsNullOrEmpty(_statusNotice))
            {
                var prevNoticeColor = GUI.color;
                GUI.color = AtelierTheme.StarlightCyan;
                GUILayout.Label(_statusNotice);
                GUI.color = prevNoticeColor;
            }
            else
            {
                GUILayout.Space(20);
            }

            GUILayout.FlexibleSpace();

            // Action Buttons Bar
            GUILayout.BeginHorizontal();

            // 1. Copy the report path (Aether never launches other programs)
            if (GUILayout.Button("📋 Copiar Ruta", _buttonPrimaryStyle!, GUILayout.Height(36)))
            {
                GUIUtility.systemCopyBuffer = report.SavedFilePath ?? "";
                _statusNotice = "✓ ¡Ruta copiada al portapapeles con éxito!";
                _statusNoticeTime = Time.unscaledTime;
            }

            GUILayout.Space(10);

            // 3. Continuar de todos modos (opcional para no forzar salida inmediata)
            if (GUILayout.Button("⟳ Continuar", _buttonSecondaryStyle!, GUILayout.Height(36)))
            {
                CrashReporter.DismissActiveCrash();
            }

            GUILayout.FlexibleSpace();

            // 4. Cerrar Juego
            if (GUILayout.Button("✕ Cerrar Juego", _buttonDangerStyle!, GUILayout.Height(36), GUILayout.Width(140)))
            {
                CrashReporter.TerminateGame();
            }

            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            GUI.color = prevColor;
            GUI.depth = prevDepth;
        }

        private static void InitStyles()
        {
            if (_crimsonTexture == null)
            {
                _crimsonTexture = new Texture2D(1, 1);
                _crimsonTexture.SetPixel(0, 0, new Color(0.94f, 0.27f, 0.27f, 1f));
                _crimsonTexture.Apply();
            }

            if (_cardTitleStyle == null)
            {
                _cardTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold
                };
                _cardTitleStyle.normal.textColor = new Color(0.97f, 0.44f, 0.44f, 1f); // #F87171
            }

            if (_normalTextStyle == null)
            {
                _normalTextStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12
                };
                _normalTextStyle.normal.textColor = AtelierTheme.PureCrystal;
            }

            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold
                };
                _headerStyle.normal.textColor = AtelierTheme.PureCrystal;
            }

            if (_subtextStyle == null)
            {
                _subtextStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11
                };
                _subtextStyle.normal.textColor = AtelierTheme.SubduedText;
            }

            if (_culpritBadgeStyle == null)
            {
                _culpritBadgeStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _culpritBadgeStyle.normal.textColor = AtelierTheme.StarlightCyan;
            }

            if (_pathBoxStyle == null)
            {
                _pathBoxStyle = new GUIStyle(GUI.skin.textField)
                {
                    fontSize = 12
                };
            }

            if (_buttonPrimaryStyle == null)
            {
                _buttonPrimaryStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold
                };
            }

            if (_buttonSecondaryStyle == null)
            {
                _buttonSecondaryStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13
                };
            }

            if (_buttonDangerStyle == null)
            {
                _buttonDangerStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold
                };
            }
        }
    }
}
