using System;

namespace Aether.Nano
{
    /// <summary>
    /// Which mod a block, item or creature comes from. Nano alone only knows "Block Story"; a host that tracks content
    /// (Aether) plugs its resolvers in. Each returns null when it does not know.
    /// </summary>
    public static class NanoOwners
    {
        public static Func<string, string> ItemOwner { get; set; }
        public static Func<string, string> EntityOwner { get; set; }
        /// <summary>Owner of a block id (custom blocks registered by mods).</summary>
        public static Func<ushort, string> BlockOwner { get; set; }

        public static string OfItem(string itemName) => Ask(ItemOwner, itemName);
        public static string OfEntity(string entityName) => Ask(EntityOwner, entityName);
        public static string OfBlock(ushort id)
        {
            try { return BlockOwner?.Invoke(id); }
            catch { return null; }
        }

        private static string Ask(Func<string, string> f, string key)
        {
            if (f == null || string.IsNullOrEmpty(key)) return null;
            try { return f(key); }
            catch { return null; }
        }
    }
}
