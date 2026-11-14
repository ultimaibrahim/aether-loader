using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;

namespace AetherNano
{
    /// <summary>Finds out, without referencing any Aether assembly, whether Aether is loaded in this game.</summary>
    internal static class AetherLink
    {
        /// <summary>Plugin GUID of Aether.Loader. A test keeps it equal to <c>AetherBootstrap.PluginGuid</c>.</summary>
        internal const string AetherGuid = "dev.ultimaibrahim.aether";

        internal static bool IsPresent
        {
            get
            {
                try { return Chainloader.PluginInfos.ContainsKey(AetherGuid); }
                catch { return false; }
            }
        }

        /// <summary>A type from an assembly Aether loaded, found by name. Null when Aether is absent.</summary>
        internal static Type FindType(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("Aether.", StringComparison.Ordinal)))
            {
                try
                {
                    Type t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
