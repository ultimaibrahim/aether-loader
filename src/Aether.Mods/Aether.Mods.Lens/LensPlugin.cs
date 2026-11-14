using System.IO;
using Aether.Nano;
using BepInEx;
using UnityEngine;

namespace Aether.Mods.Lens
{
    /// <summary>Aether Lens as a BepInEx plugin on Aether Nano: no Aether needed.</summary>
    [BepInPlugin(Guid, "Aether Lens", "1.1.1")]
    [BepInDependency(Aether.Nano.Nano.Guid)]
    public sealed class LensPlugin : NanoMod
    {
        public const string Guid = "dev.ultimaibrahim.aether.lens";

        private readonly LensModule _module = new LensModule();

        protected override void OnReady()
        {
            // Settings used to live in the UI suite's file (including [Lens] Scale); carry them over once.
            NanoConfig.AdoptSection(Config, Path.Combine(Paths.ConfigPath, "com.ultimaibrahim.blockstory.suite.cfg"), "Lens");
            LensModule.Config = Config;
            LensModule.Log = Logger;
            _module.Initialize();
            NanoKeys.Register(Guid, "Aether Lens", LensModule.LensToggleKey, "Show Lens", "Mostrar Lens");
            Logger.LogInfo($"Aether Lens {Info.Metadata.Version} ready on Aether Nano.");

            NanoPages.Register(new NanoPage
            {
                Id = Guid,
                Name = "Aether Lens",
                Author = "Ibrahim García",
                Version = Info.Metadata.Version.ToString(),
                Description = Aether.Nano.Nano.Tr("Card for the block or creature you aim at: name, owner mod, health, the tool that breaks it.",
                                                  "Tarjeta del bloque o criatura que miras: nombre, mod de origen, vida y la herramienta que lo rompe."),
                Draw = (rect, y) => _module.DrawSettings(rect, y),
                Config = Config,
                ContentHeight = 560f,
            });
            NanoHud.Register(new NanoHudElement
            {
                Id = Guid,
                Name = "Lens",
                GetRect = LensModule.CurrentCardRect,
                OffsetX = LensModule.LensOffsetX,
                OffsetY = LensModule.LensOffsetY,
                Scale = LensModule.LensScale,
                Anchor = LensModule.LensAnchor,
            });
        }

        protected override void OnTick()
        {
            if (!NanoHud.KeyboardCaptured && !NanoHud.OverlayOpen && Input.GetKeyDown(LensModule.LensToggleKey.Value))
                LensModule.LensEnabled.Value = !LensModule.LensEnabled.Value;
            _module.OnUpdate();
        }

        protected override void OnSceneChanged(string sceneName) => _module.OnSceneChanged(sceneName);

        protected override void OnDraw()
        {
            if (LensModule.LensEnabled.Value && NanoHud.CanDrawGameplayHud) _module.DrawHud();
        }
    }
}
