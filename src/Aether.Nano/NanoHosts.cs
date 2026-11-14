using System;
using System.Reflection;

namespace Aether.Nano
{
    /// <summary>What else is loaded next to Nano. Nano shows its pages in Aether's menu or, without Aether, in its own window.</summary>
    public static class NanoHosts
    {
        public static bool AssemblyLoaded(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
