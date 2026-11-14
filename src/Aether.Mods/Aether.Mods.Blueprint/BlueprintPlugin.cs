using System.IO;
using Aether.Nano;
using BepInEx;

namespace Aether.Mods.Blueprint
{
    /// <summary>Aether Blueprint as a BepInEx plugin on Aether Nano: no Aether needed.</summary>
    [BepInPlugin(Guid, "Aether Blueprint", "1.1.1")]
    [BepInDependency(Aether.Nano.Nano.Guid)]
    public sealed class BlueprintPlugin : NanoMod
    {
        public const string Guid = "dev.ultimaibrahim.aether.blueprint";

        private readonly BlueprintModule _module = new BlueprintModule();

        protected override void OnReady()
        {
            NanoConfig.AdoptSection(Config, Path.Combine(Paths.ConfigPath, "com.ultimaibrahim.blockstory.suite.cfg"), "Blueprint");
            BlueprintModule.Config = Config;
            BlueprintModule.Log = Logger;
            _module.Initialize();
            const string bp = "Aether Blueprint";
            NanoKeys.Register(Guid, bp, BlueprintModule.BlueprintToggleKey, "Item browser", "Catálogo");
            NanoKeys.Register(Guid, bp, BlueprintModule.BlueprintRecipeKey, "Recipe", "Receta");
            NanoKeys.Register(Guid, bp, BlueprintModule.BlueprintUsageKey, "Uses", "Usos");
            NanoKeys.Register(Guid, bp, BlueprintModule.BlueprintBookmarkKey, "Bookmark", "Marcador");
            Logger.LogInfo($"Aether Blueprint {Info.Metadata.Version} ready on Aether Nano.");

            NanoPages.Register(new NanoPage
            {
                Id = Guid,
                Name = "Aether Blueprint",
                Author = "Ibrahim García",
                Version = Info.Metadata.Version.ToString(),
                Description = Aether.Nano.Nano.Tr("Item catalog and recipe browser inside the inventory: search, recipe card, \"used in\", bookmarks.",
                                                  "Catálogo de objetos y recetas dentro del inventario: búsqueda, tarjeta de receta, «se usa en» y favoritos."),
                Draw = (rect, y) => _module.DrawBlueprintModMenuSettings(rect, y),
                Config = Config,
                ContentHeight = 520f,
            });
            NanoPanels.Register(new NanoPanel
            {
                Id = Guid,
                IsOpen = () => _module.IsVisible,
                IsModal = () => _module.IsRecipeCardOpen,
                MouseOver = () => _module.MouseOverPanel(),
                Close = _module.ForceClose,
            });
        }

        protected override void OnTick() => _module.OnUpdate();
        protected override void OnSceneChanged(string sceneName) => _module.OnSceneChanged(sceneName);
        protected override void OnDraw() => _module.DrawGui();
    }
}
