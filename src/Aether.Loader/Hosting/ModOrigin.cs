using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Aether.Loader.Hosting
{
    /// <summary>Where a hosted BepInEx plugin comes from, for the badge in Aether's menu.</summary>
    public enum ModOriginKind
    {
        /// <summary>Any other BepInEx plugin.</summary>
        Other,
        /// <summary>Built on Aether Nano (references <c>Aether.Nano</c>), or Nano itself.</summary>
        Nano,
        /// <summary>One of Aether's own mods: strong-named with Aether's key.</summary>
        ByAether,
    }

    /// <summary>
    /// Tells Aether's own mods apart from the rest without trusting names: a mod is "by Aether" only when its assembly
    /// carries Aether's public key token, which nobody can produce without Aether's private key. A GUID such as
    /// <c>dev.ultimaibrahim.aether.*</c> or a name like "Aether Lens" proves nothing. Nano mods are recognised by their
    /// reference to <c>Aether.Nano</c>, which is a fact about the code and claims nothing about the author.
    /// </summary>
    public static class ModOrigin
    {
        /// <summary>Public key token of Aether's strong-name key (the private key lives outside the repo).</summary>
        public const string AetherPublicKeyToken = "1706426d92902c72";

        private static readonly Dictionary<string, ModOriginKind> Cache = new Dictionary<string, ModOriginKind>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The origin of the loaded assembly with this simple name (Other when it is not loaded).</summary>
        public static ModOriginKind Of(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return ModOriginKind.Other;
            if (Cache.TryGetValue(assemblyName, out ModOriginKind kind)) return kind;
            Assembly asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(SafeName(a), assemblyName, StringComparison.OrdinalIgnoreCase));
            kind = asm == null ? ModOriginKind.Other : Of(asm);
            if (asm != null) Cache[assemblyName] = kind;
            return kind;
        }

        public static ModOriginKind Of(Assembly assembly)
        {
            if (assembly == null) return ModOriginKind.Other;
            try
            {
                AssemblyName name = assembly.GetName();
                if (IsAetherToken(name.GetPublicKeyToken())) return ModOriginKind.ByAether;
                if (string.Equals(name.Name, "Aether.Nano", StringComparison.OrdinalIgnoreCase)) return ModOriginKind.Nano;
                if (assembly.GetReferencedAssemblies().Any(r => string.Equals(r.Name, "Aether.Nano", StringComparison.OrdinalIgnoreCase)))
                    return ModOriginKind.Nano;
            }
            catch (Exception) { }
            return ModOriginKind.Other;
        }

        public static bool IsAetherToken(byte[]? token)
        {
            if (token == null || token.Length != 8) return false;
            return string.Equals(BitConverter.ToString(token).Replace("-", string.Empty), AetherPublicKeyToken, StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeName(Assembly a)
        {
            try { return a.GetName().Name ?? string.Empty; } catch (Exception) { return string.Empty; }
        }
    }
}
