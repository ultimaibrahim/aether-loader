using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace Aether.Loader
{
    /// <summary>
    /// Aether's own on-disk layout, deliberately outside the generic BepInEx/plugins folder for
    /// user-facing mods.
    ///
    ///   &lt;game&gt;/Aether/Mods/        third-party and flagship Aether mods (drag and drop)
    ///   &lt;game&gt;/BepInEx/plugins/Aether/  only the core runtime (Loader/Core/UI/...)
    ///
    /// The loader scans both locations for backward compatibility; the core directory is searched
    /// first for assembly dependencies so a mod can live in Aether/Mods and still reference UI/Core.
    /// </summary>
    public static class AetherPaths
    {
        /// <summary>
        /// User-facing mod drop folder: &lt;game&gt;/Aether/Mods by default.
        /// Set the <c>AETHER_MODS_PATH</c> environment variable to use an absolute custom folder.
        /// </summary>
        public static string ModsDirectory
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("AETHER_MODS_PATH");
                if (!string.IsNullOrWhiteSpace(custom))
                {
                    try { return Path.GetFullPath(custom); } catch { }
                }
                return Path.Combine(Paths.GameRootPath, "Aether", "Mods");
            }
        }

        /// <summary>Core runtime folder: BepInEx/plugins/Aether.</summary>
        public static string CoreDirectory => Path.Combine(Paths.PluginPath, "Aether");

        public static void EnsureModsDirectory()
        {
            try { Directory.CreateDirectory(ModsDirectory); } catch { }
        }

        /// <summary>Directories searched for assembly dependencies, most specific first.</summary>
        public static IEnumerable<string> AssemblySearchDirectories
        {
            get
            {
                yield return CoreDirectory;
                yield return ModsDirectory;
                yield return Paths.PluginPath;
            }
        }
    }
}
