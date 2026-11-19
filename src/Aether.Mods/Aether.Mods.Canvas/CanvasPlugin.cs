using System.IO;
using Aether.Nano;
using BepInEx;
using UnityEngine;

namespace Aether.Mods.Canvas
{
    /// <summary>Aether Canvas as a standalone BepInEx plugin on Aether Nano: no Aether required.</summary>
    [BepInPlugin(Guid, "Aether Canvas", "1.1.3")]
    [BepInDependency(Aether.Nano.Nano.Guid)]
    public sealed class CanvasPlugin : NanoMod
    {
        public const string Guid = "dev.ultimaibrahim.aether.canvas";

        private readonly CanvasModule _module = new CanvasModule();

        protected override void OnReady()
        {
            string legacySuiteCfg = Path.Combine(Paths.ConfigPath, "com.ultimaibrahim.blockstory.suite.cfg");
            NanoConfig.AdoptSection(Config, legacySuiteCfg, "HUDLayout");
            NanoConfig.AdoptSection(Config, legacySuiteCfg, "HUD_Stats");
            NanoConfig.AdoptSection(Config, legacySuiteCfg, "HUD_Diamonds");
            NanoConfig.AdoptSection(Config, legacySuiteCfg, "HUD_Buttons");
            NanoConfig.AdoptSection(Config, legacySuiteCfg, "HUD_Actions");

            CanvasModule.Config = Config;
            CanvasModule.Log = Logger;
            _module.Initialize();

            NanoKeys.Register(Guid, "Aether Canvas", CanvasModule.OpenEditorKey, "Open HUD Editor", "Abrir Editor de HUD");

            NanoHud.ExternalLayoutActive = true;
            NanoHud.OpenCanvasEditor = () => _module.OpenEditor();
            if (NanoHud.OpenHostEditor == null)
                NanoHud.OpenHostEditor = () => _module.OpenEditor();

            NanoHud.DiamondsUnderXp = () => _module.AreDiamondsMoved();
            NanoHud.TopRightButtonsMoved = () => _module.AreTopButtonsMoved();
            NanoHud.TopLeftInset = () => _module.AreDiamondsMoved() ? 160f : 24f;

            NanoPages.Register(new NanoPage
            {
                Id = Guid,
                Name = "Aether Canvas",
                Author = "Ibrahim García",
                Version = Info.Metadata.Version.ToString(),
                Description = Aether.Nano.Nano.Tr(
                    "WYSIWYG HUD layout editor: move and scale Block Story's native HUD and every mod registered on Aether Nano.",
                    "Editor visual de HUD en vivo: mueve y escala el HUD nativo de Block Story y cualquier mod registrado en Aether Nano."),
                Draw = (rect, y) => _module.DrawSettings(rect, y),
                Config = Config,
                ContentHeight = 460f,
            });

            NanoPanels.Register(new NanoPanel
            {
                Id = Guid,
                IsOpen = () => _module.IsEditorOpen,
                IsModal = () => _module.IsEditorOpen,
                MouseOver = () => _module.IsEditorOpen,
                Close = () => _module.CloseEditor(),
            });

            Logger.LogInfo($"Aether Canvas {Info.Metadata.Version} ready on Aether Nano.");
        }

        protected override void OnTick()
        {
            if (_module.IsEditorOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(CanvasModule.OpenEditorKey.Value))
                {
                    _module.CloseEditor();
                    return;
                }
                _module.TickEditor();
            }
            else if (Aether.Nano.Nano.InGame
                     && !NanoHud.KeyboardCaptured
                     && !NanoHud.OverlayOpen
                     && !Aether.Nano.Nano.NativeWindowOpen
                     && Input.GetKeyDown(CanvasModule.OpenEditorKey.Value))
            {
                _module.OpenEditor();
                return;
            }

            _module.OnUpdate();
        }

        protected override void OnSceneChanged(string sceneName) => _module.OnSceneChanged(sceneName);

        protected override void OnDraw()
        {
            if (_module.IsEditorOpen)
            {
                _module.DrawEditorGUI();
                NanoInput.DrawGameCursor();
            }
        }
    }
}
