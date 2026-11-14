namespace Aether.Installer;

/// <summary>The assemblies Aether itself installs, and the ones it used to install.</summary>
public static class AetherAssemblies
{
    /// <summary>The core: BepInEx/plugins/Aether.</summary>
    public static readonly string[] Core =
    [
        "Aether.Core.dll",
        "Aether.API.dll",
        "Aether.Loader.dll",
        "Aether.Registry.dll"
    ];

    /// <summary>
    /// The UI package: the F10 menu, HUD, keybinds, and Aether Nano (the shared library the official mods are moving to). Installed by default next to the
    /// core, but its own package: the loader runs without it.
    /// </summary>
    public static readonly string[] Ui = ["Aether.UI.dll", "Aether.Nano.dll"];

    /// <summary>Every assembly Aether itself installs into BepInEx/plugins/Aether.</summary>
    public static string[] All => [.. Core, .. Ui];

    /// <summary>The four official mods (Aether/Mods).</summary>
    public static readonly string[] OfficialMods =
    [
        "Aether.Mods.Dusk.dll",
        "Aether.Mods.Lens.dll",
        "Aether.Mods.Wayfinder.dll",
        "Aether.Mods.Blueprint.dll"
    ];

    /// <summary>
    /// Mod assemblies Aether used to ship. Renamed on 2026-11-05 (Jade, JourneyMap, REI, Lighting) or removed from releases
    /// (Sodium, Lithium, EntityCulling, FerriteCore). Left in place they would load next to their replacements.
    /// </summary>
    public static readonly string[] RetiredMods =
    [
        "Aether.Mods.Jade.dll", "Aether.Mods.JourneyMap.dll", "Aether.Mods.REI.dll", "Aether.Mods.Lighting.dll",
        "Aether.Mods.Sodium.dll", "Aether.Mods.Lithium.dll", "Aether.Mods.EntityCulling.dll", "Aether.Mods.FerriteCore.dll"
    ];

    /// <summary>Core-folder assemblies Aether used to ship and no longer does (see also <see cref="LegacyFiles"/>).</summary>
    public static readonly string[] RetiredCore = ["Aether.Rendering.dll"];
}
