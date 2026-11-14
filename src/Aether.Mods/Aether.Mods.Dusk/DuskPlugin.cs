using System.IO;
using Aether.Nano;
using BepInEx;
using HarmonyLib;

namespace Aether.Mods.Dusk
{
    /// <summary>Aether Dusk as a BepInEx plugin on Aether Nano: no Aether needed.</summary>
    [BepInPlugin(Guid, "Aether Dusk", "1.1.1")]
    [BepInDependency(Aether.Nano.Nano.Guid)]
    public sealed class DuskPlugin : NanoMod
    {
        public const string Guid = "dev.ultimaibrahim.aether.dusk";

        private readonly DuskModule _module = new DuskModule();

        protected override void OnReady()
        {
            // Settings used to live in the UI suite's file; carry them over once.
            NanoConfig.AdoptSection(Config, Path.Combine(Paths.ConfigPath, "com.ultimaibrahim.blockstory.suite.cfg"), "Dusk");
            DuskModule.Config = Config;
            DuskModule.Log = Logger;
            _module.Initialize();

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(DuskPlugin).Assembly);
            int patched = 0;
            foreach (var _ in harmony.GetPatchedMethods()) patched++;
            Logger.LogInfo($"Aether Dusk {Info.Metadata.Version} ready on Aether Nano ({patched} game methods patched).");

            NanoPages.Register(new NanoPage
            {
                Id = Guid,
                Name = "Aether Dusk",
                Author = "Ibrahim García",
                Version = Info.Metadata.Version.ToString(),
                Description = Aether.Nano.Nano.Tr("Matte terrain, cave darkness, underground sun suppression and handheld torchlight.",
                                         "Terreno mate, cuevas oscuras, sin sol bajo tierra y luz de antorcha en mano."),
                Draw = (rect, y) => _module.DrawDuskModMenuSettings(rect, y),
                ContentHeight = 620f,
            });
        }

        protected override void OnTick() => _module.UpdateDusk();
        protected override void OnLateTick() => _module.LateUpdateDusk();
    }
}
