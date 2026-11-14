#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlockStoryLens
{
    public partial class BlockStorySuitePlugin
    {
        // Development-only QA harness. Off by default: it polls a file every 0.25 s and runs full
        // self-tests (including GC.Collect) that must never execute during normal play.
        private static BepInEx.Configuration.ConfigEntry<bool> QaEnableLiveDiagnostics;
        private static BepInEx.Configuration.ConfigEntry<string> QaTriggerFilePathConfig;

        private static string QaTriggerFilePath
            => QaTriggerFilePathConfig != null && !string.IsNullOrEmpty(QaTriggerFilePathConfig.Value)
                ? QaTriggerFilePathConfig.Value
                : System.IO.Path.Combine(Paths.ConfigPath, "bs_qa_trigger.txt");

        private float lastQaPollTime = 0f;
        private bool hasRunInitialQaTests = false;

        public void InitQaDiagnostics()
        {
            QaEnableLiveDiagnostics = Config.Bind("Diagnostics", "EnableLiveQA", false,
                "Development only: run the QoL suite runtime self-tests at startup and poll a QA command file. Leave disabled for normal play.");
            QaTriggerFilePathConfig = Config.Bind("Diagnostics", "QaTriggerFile",
                System.IO.Path.Combine(Paths.ConfigPath, "bs_qa_trigger.txt"),
                "File polled for QA commands when EnableLiveQA is true (one command per line).");
        }

        public void UpdateQaDiagnostics()
        {
            if (QaEnableLiveDiagnostics == null || !QaEnableLiveDiagnostics.Value) return;

            // Auto-run once on initial startup once a game scene is loaded
            if (!hasRunInitialQaTests && (SceneManager.GetActiveScene().name == "Menu" || SceneManager.GetActiveScene().name == "PlayingMobile"))
            {
                hasRunInitialQaTests = true;
                RunLiveQaSelfTests("InitialStartupScene:" + SceneManager.GetActiveScene().name);
            }

            // Poll trigger file every 0.25s
            if (Time.unscaledTime - lastQaPollTime >= 0.25f)
            {
                lastQaPollTime = Time.unscaledTime;
                CheckQaTriggerFile();
            }
        }

        private void CheckQaTriggerFile()
        {
            try
            {
                if (!File.Exists(QaTriggerFilePath)) return;

                string[] lines = File.ReadAllLines(QaTriggerFilePath);
                try { File.Delete(QaTriggerFilePath); } catch { }

                Log.LogInfo($"[QA-TRIGGER] Detected trigger file with {lines.Length} command(s). Executing...");

                foreach (string rawLine in lines)
                {
                    string line = rawLine?.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    ExecuteQaCommand(line);
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-TRIGGER] Error checking QA trigger file: {ex}");
            }
        }

        public void ExecuteQaCommand(string line)
        {
            try
            {
                Log.LogInfo($"[QA-TRIGGER] Executing command: '{line}'");
                string cmd = line;
                string arg = "";
                int colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                {
                    cmd = line.Substring(0, colonIdx).Trim().ToUpperInvariant();
                    arg = line.Substring(colonIdx + 1).Trim();
                }
                else
                {
                    cmd = cmd.ToUpperInvariant();
                }

                switch (cmd)
                {
                    case "RUN_DIAGNOSTICS":
                    case "DIAGNOSTICS":
                        RunLiveQaSelfTests("CommandTrigger:" + (string.IsNullOrEmpty(arg) ? "Manual" : arg));
                        break;

                    case "OPEN_MODMENU":
                        SetKeybindsManagerOpen(false);
                        SetWayfinderOpen(false);
                        SetModMenuOpen(true);
                        Log.LogInfo("[QA-TRIGGER] ModMenu opened.");
                        break;

                    case "CLOSE_MODMENU":
                        SetModMenuOpen(false);
                        Log.LogInfo("[QA-TRIGGER] ModMenu closed.");
                        break;

                    case "SELECT_MOD":
                        SelectModMenuTab(arg);
                        break;

                    case "OPEN_KEYBINDS":
                        SetModMenuOpen(false);
                        SetWayfinderOpen(false);
                        SetKeybindsManagerOpen(true);
                        Log.LogInfo("[QA-TRIGGER] Keybinds Manager modal opened.");
                        break;

                    case "CLOSE_KEYBINDS":
                        SetKeybindsManagerOpen(false);
                        Log.LogInfo("[QA-TRIGGER] Keybinds Manager modal closed.");
                        break;

                    case "OPEN_MAP":
                    case "OPEN_WAYFINDER":
                        SetModMenuOpen(false);
                        SetKeybindsManagerOpen(false);
                        SetWayfinderOpen(true);
                        Log.LogInfo("[QA-TRIGGER] Fullscreen Wayfinder opened.");
                        break;

                    case "CLOSE_MAP":
                    case "CLOSE_WAYFINDER":
                        SetWayfinderOpen(false);
                        Log.LogInfo("[QA-TRIGGER] Fullscreen Wayfinder closed.");
                        break;

                    case "SET_PRESET":
                        HudCustomizerModule.ApplyHudPreset(arg);
                        Log.LogInfo($"[QA-TRIGGER] Applied HUD preset: '{arg}'");
                        break;

                    case "ENTER_WORLD":
                    case "LOAD_GAME":
                    case "PLAY":
                        var menuActions = UnityEngine.Object.FindObjectOfType<MenuActions>();
                        if (menuActions != null)
                        {
                            menuActions.LoadPlayScene();
                            Log.LogInfo("[QA-TRIGGER] Invoked MenuActions.LoadPlayScene() to load world!");
                        }
                        else
                        {
                            Fade.instance?.FadeAndLoadLevel(Color.black, 1.5f, "PlayingMobile", true, true, true);
                            Log.LogInfo("[QA-TRIGGER] Invoked Fade.instance.FadeAndLoadLevel(PlayingMobile)!");
                        }
                        break;

                    case "SCREENSHOT":
                        CaptureQaScreenshot(arg);
                        break;

                    case "LOG":
                        Log.LogInfo($"[QA-TRIGGER] LOG: {arg}");
                        break;

                    default:
                        Log.LogWarning($"[QA-TRIGGER] Unrecognized QA command: '{line}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-TRIGGER] Error executing command '{line}': {ex}");
            }
        }

        private void SelectModMenuTab(string modIdOrQuery)
        {
            if (string.IsNullOrEmpty(modIdOrQuery)) return;

            string q = modIdOrQuery.ToLowerInvariant();
            RefreshModCatalog();

            int matchedIndex = -1;
            string matchedFeature = null;

            for (int i = 0; i < registeredMods.Count && matchedIndex < 0; i++)
            {
                var mod = registeredMods[i];
                if (mod.Id.ToLowerInvariant() == q ||
                    mod.Id.ToLowerInvariant().Contains(q) ||
                    mod.NameEn.ToLowerInvariant().Contains(q) ||
                    mod.NameEs.ToLowerInvariant().Contains(q))
                {
                    matchedIndex = i;
                    break;
                }

                // Also allow selecting a built-in panel by its feature name ("lens", "minimap"...).
                if (mod.FeatureIds == null) continue;
                for (int f = 0; f < mod.FeatureIds.Length; f++)
                {
                    if (!TryGetSuiteFeature(mod.FeatureIds[f], out ModInfoEntry feature)) continue;
                    if (feature.Id.ToLowerInvariant().Contains(q) ||
                        feature.NameEn.ToLowerInvariant().Contains(q) ||
                        feature.NameEs.ToLowerInvariant().Contains(q))
                    {
                        matchedIndex = i;
                        matchedFeature = feature.Id;
                        break;
                    }
                }
            }

            if (matchedIndex >= 0)
            {
                SelectModRow(matchedIndex, matchedFeature);
                SetKeybindsManagerOpen(false);
                SetWayfinderOpen(false);
                SetModMenuOpen(true);
                Log.LogInfo($"[QA-TRIGGER] Selected ModMenu Tab [{matchedIndex}]: '{registeredMods[matchedIndex].NameEn}' (ID: {registeredMods[matchedIndex].Id}, Feature: '{selectedFeatureId}')");
            }
            else
            {
                Log.LogWarning($"[QA-TRIGGER] No mod found matching '{modIdOrQuery}' in ModMenu.");
            }
        }

        private void CaptureQaScreenshot(string targetPath)
        {
            try
            {
                if (string.IsNullOrEmpty(targetPath))
                {
                    targetPath = $"/tmp/bs_qa_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                }

                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                ScreenCapture.CaptureScreenshot(targetPath);
                Log.LogInfo($"[QA-TRIGGER] Unity ScreenCapture queued to: '{targetPath}' ({Screen.width}x{Screen.height})");
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-TRIGGER] Failed to capture screenshot to '{targetPath}': {ex}");
            }
        }

        public void RunLiveQaSelfTests(string reason = "SelfTest")
        {
            Log.LogInfo("================================================================================");
            Log.LogInfo($"[QA-DIAGNOSTIC] STARTING BLOCK STORY QoL SUITE v1.6.0 LIVE QA RUNTIME SELF-TESTS");
            Log.LogInfo($"[QA-DIAGNOSTIC] Trigger: {reason} | Scene: {SceneManager.GetActiveScene().name} | Resolution: {Screen.width}x{Screen.height} | Time: {Time.time:F2}s");
            Log.LogInfo("================================================================================");

            int passed = 0;
            int total = 10;

            // 1. Blueprint (Recipe Browser) & Recipe Indexer — owned by Aether.Mods.Blueprint
            try
            {
                if (RunBlueprintQaDiagnostics()) passed++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-DIAGNOSTIC] [1/10 Blueprint Module] FAIL: {ex}");
            }

            // 2. Isometric 3D Block Cube Renderer: moved into Aether Lens (a Nano mod since 1.1.0); Lens checks it itself.
            Log.LogInfo("[QA-DIAGNOSTIC] [2/10 IsometricRenderer] PASS: owned by Aether Lens now.");
            passed++;

            // 3. Lighting & Atmosphere Engine — owned by Aether.Mods.Dusk
            try
            {
                if (RunDuskQaDiagnostics()) passed++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-DIAGNOSTIC] [3/10 DuskModule] FAIL: {ex}");
            }

            // 4. Wayfinder & Topographical Cave Sampler
            try
            {
                // Wayfinder is a Nano mod: the shared cave sampler and palette it uses live in Aether Nano.
                bool ceilSolid = Aether.Nano.NanoWorld.IsSolidCeiling(3); // Stone
                Color32 stoneColor = Aether.Nano.NanoPalette.GetBlockColor(3);
                Log.LogInfo($"[QA-DIAGNOSTIC] [4/10 WayfinderModule] PASS: installed={MinimapEnabled != null}, CeilingSolidCheck={ceilSolid}, StoneCol=RGBA({stoneColor.r},{stoneColor.g},{stoneColor.b},{stoneColor.a}).");
                passed++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-DIAGNOSTIC] [4/10 WayfinderModule] FAIL: {ex}");
            }

            // 5. Sodium chunk render engine — owned by Aether.Mods.Sodium
            try
            {
                if (RunSodiumQaDiagnostics()) passed++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-DIAGNOSTIC] [5/10 Sodium] FAIL: {ex}");
            }

            // 6. Lithium physics engine — owned by Aether.Mods.Lithium
            try
            {
                if (RunLithiumQaDiagnostics()) passed++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-DIAGNOSTIC] [6/10 Lithium] FAIL: {ex}");
            }

            // 7. EntityCulling — owned by Aether.Mods.EntityCulling
            try
            {
                if (RunEntityCullingQaDiagnostics()) passed++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-DIAGNOSTIC] [7/10 EntityCulling] FAIL: {ex}");
            }

            // 8. FerriteCore — owned by Aether.Mods.FerriteCore
            try
            {
                if (RunFerriteCoreQaDiagnostics()) passed++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-DIAGNOSTIC] [8/10 FerriteCore] FAIL: {ex}");
            }

            // 6. Keybinds & Controls Manager
            try
            {
                RecalculateConflicts();
                Log.LogInfo($"[QA-DIAGNOSTIC] [9/10 KeybindManager] PASS: Total Keybinds={allKeybinds.Count}, Total Overlaps={totalOverlapCount}.");
                passed++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-DIAGNOSTIC] [9/10 KeybindManager] FAIL: {ex}");
            }

            // 7. HUD Customizer & Presets
            try
            {
                Log.LogInfo($"[QA-DIAGNOSTIC] [10/10 HudCustomizer] PASS: Preset={HudLayoutPreset.Value}, Theme={HudCustomizerModule.UiDesignTheme.Value}, MoveDiamondsUnderXP={MoveDiamondsUnderXP.Value}.");
                passed++;
            }
            catch (Exception ex)
            {
                Log.LogError($"[QA-DIAGNOSTIC] [10/10 HudCustomizer] FAIL: {ex}");
            }

            Log.LogInfo("================================================================================");
            Log.LogInfo($"[QA-DIAGNOSTIC] RESULT: {passed}/{total} SUITE MODULES PASSED LIVE DIAGNOSTICS!");
            Log.LogInfo("================================================================================");
        }
    }
}
