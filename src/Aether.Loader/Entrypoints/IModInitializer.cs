namespace Aether.Loader.Entrypoints
{
    /// <summary>
    /// Executes at the earliest possible stage (BepInEx preloader / awake).
    /// Used for low-level byte-patching, harmony patches, and assembly resolve hooks.
    /// </summary>
    public interface IPreLaunchEntrypoint
    {
        void OnPreLaunch();
    }

    /// <summary>
    /// Executes during common mod loading.
    /// Used for registering custom blocks, items, crafting recipes, and entities.
    /// </summary>
    public interface IModInitializer
    {
        void OnInitialize();
    }

    /// <summary>
    /// Executes when the graphics / client environment is ready.
    /// Used for initializing UI canvases, HUD overlays, shaders, and keybinds.
    /// </summary>
    public interface IClientModInitializer
    {
        void OnInitializeClient();
    }

    /// <summary>
    /// Executes when a specific Unity scene finishes loading.
    /// </summary>
    public interface ISceneModInitializer
    {
        void OnSceneLoaded(string sceneName, int sceneBuildIndex);
    }
}
