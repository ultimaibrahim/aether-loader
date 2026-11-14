using System;
using System.Collections.Generic;

namespace Aether.Nano
{
    /// <summary>A mod's panel or window, so hosts can respect it: hide their tooltips under it, count it as a modal, close it.</summary>
    public sealed class NanoPanel
    {
        public string Id = string.Empty;
        public Func<bool> IsOpen;
        /// <summary>True while it takes the whole input (a recipe card), not just a side panel.</summary>
        public Func<bool> IsModal;
        public Func<bool> MouseOver;
        public Action Close;
    }

    /// <summary>Panels of Nano mods. Aether's tooltips stay out from under them and "close everything" closes them too.</summary>
    public static class NanoPanels
    {
        private static readonly List<NanoPanel> Panels = new List<NanoPanel>();

        public static void Register(NanoPanel panel)
        {
            if (panel == null || string.IsNullOrEmpty(panel.Id)) return;
            Panels.RemoveAll(p => string.Equals(p.Id, panel.Id, StringComparison.OrdinalIgnoreCase));
            Panels.Add(panel);
        }

        /// <summary>When a mod last closed a modal panel; hosts ignore clicks for a moment after it, so the closing click does not fall through.</summary>
        public static float LastClosed { get; set; } = -10f;
        /// <summary>When a mod last opened a modal panel (hosts ignore the opening click's release).</summary>
        public static float LastOpened { get; set; } = -10f;

        public static NanoPanel Find(string id) => Panels.Find(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        public static bool IsOpen(string id) => Find(id) is NanoPanel p && Ask(p.IsOpen);
        public static bool IsModalOpen(string id) => Find(id) is NanoPanel p && Ask(p.IsModal);

        public static bool AnyModal => Panels.Exists(p => Ask(p.IsModal));
        public static bool MouseOverAny => Panels.Exists(p => Ask(p.IsOpen) && Ask(p.MouseOver));

        public static void CloseModals()
        {
            foreach (NanoPanel p in Panels)
                if (Ask(p.IsModal)) { try { p.Close?.Invoke(); } catch { } }
        }

        private static bool Ask(Func<bool> f)
        {
            try { return f != null && f(); }
            catch { return false; }
        }
    }
}
