using System;
using Aether.API.UI;
using Aether.Core.Diagnostics;
using Aether.Core.SPI;
using Aether.Loader.Discovery;
using Aether.Loader.Entrypoints;
using Aether.UI.Overlays;
using BlockStoryLens;
using UnityEngine;

[assembly: AetherMod("aether_ui", "1.2.0-beta",
    Name = "Aether UI & Native Suite",
    Author = "Ibrahim García",
    Description = "Shared native Block Story UI shell: theme, widgets, HUD editor, keybind manager and Mod Health. The official mods are separate assemblies.")]

namespace Aether.UI
{
    /// <summary>
    /// Core UI service implementation for Aether.
    /// Hosts the full 540 KB Native Block Story QoL Suite UI (WoodPanel/UIAtlas, AcquireModalIsolation,
    /// WayfinderTileCache, Blueprint, Sodium/Lithium/FerriteCore, Lighting, HUD Customizer, Keybind Manager)
    /// alongside Aether's native Crash Guard overlay.
    /// </summary>
    public sealed class AetherUIService : IUIService, IModInitializer, IClientModInitializer
    {
        public static AetherUIService Instance { get; private set; } = null!;
        private static GameObject? _runnerGo;

        public void OnInitialize()
        {
            Instance = this;
            AetherServiceLocator.Register<IUIService>(this);
        }

        public void OnInitializeClient()
        {
            Instance = this;
            AetherServiceLocator.Register<IUIService>(this);
            EnsureRunnerComponent();
        }

        public static void EnsureRunnerComponent()
        {
            if (_runnerGo == null)
            {
                _runnerGo = new GameObject("Aether_NativeBlockStorySuite");
                UnityEngine.Object.DontDestroyOnLoad(_runnerGo);
                _runnerGo.AddComponent<BlockStorySuitePlugin>();
            }
        }

        public void RenderGUI()
        {
            // Only render the full-screen Aether Crash Guard dialog when a fatal exception is intercepted.
            // All in-game and menu UI is rendered natively by BlockStorySuitePlugin using Block Story's UIAtlas.
            if (CrashReporter.IsOverlayRequested)
            {
                Crash.AetherCrashOverlay.DrawGUI();
                return;
            }

            // "Aether loaded + version" legend. Menus only, so it never clutters gameplay.
            TitleWatermarkOverlay.DrawGUI();
        }
    }
}
