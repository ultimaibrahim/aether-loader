using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aether.Nano
{
    /// <summary>A mod's settings page. Nano shows it wherever there is a mod menu (see <see cref="NanoPages"/>).</summary>
    public sealed class NanoPage
    {
        /// <summary>The mod's BepInEx GUID.</summary>
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public string Author = string.Empty;
        public string Version = string.Empty;
        /// <summary>Draws the settings. The rect is the page area in local coordinates; the float is the first free y.</summary>
        public Action<Rect, float> Draw;
        /// <summary>The mod's config file, so a host can read or edit its settings by section and key.</summary>
        public BepInEx.Configuration.ConfigFile Config;
        /// <summary>Height the page needs, for scrolling.</summary>
        public float ContentHeight = 600f;
    }

    /// <summary>
    /// Settings pages of Nano mods. One registration, two places: Aether's mod menu draws them inline, and without Aether
    /// Nano adds its own "Mods" button.
    /// </summary>
    public static class NanoPages
    {
        private static readonly List<NanoPage> Pages = new List<NanoPage>();
        public static IReadOnlyList<NanoPage> All => Pages;

        public static void Register(NanoPage page)
        {
            if (page == null || string.IsNullOrEmpty(page.Id) || page.Draw == null) return;
            Pages.RemoveAll(p => string.Equals(p.Id, page.Id, StringComparison.OrdinalIgnoreCase));
            Pages.Add(page);
        }

        /// <summary>The page with this GUID, or else this display name; null when none.</summary>
        public static NanoPage Find(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;
            foreach (NanoPage p in Pages) if (string.Equals(p.Id, idOrName, StringComparison.OrdinalIgnoreCase)) return p;
            foreach (NanoPage p in Pages) if (string.Equals(p.Name, idOrName, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>A setting of a registered mod by GUID, section and key, or null (the mod or the key is not there).</summary>
        public static BepInEx.Configuration.ConfigEntry<T> Setting<T>(string id, string section, string key)
        {
            NanoPage page = Find(id);
            if (page?.Config == null) return null;
            return page.Config.TryGetEntry(section, key, out BepInEx.Configuration.ConfigEntry<T> entry) ? entry : null;
        }

        /// <summary>Draws a page in a scroll view filling <paramref name="area"/> (used by Nano window and by Aether's menu).</summary>
        public static Vector2 DrawScrolled(NanoPage page, Rect area, Vector2 scroll, float firstY = 10f)
        {
            float w = area.width - 18f;
            scroll = GUI.BeginScrollView(area, scroll, new Rect(0, 0, w, Mathf.Max(page.ContentHeight, area.height - 4f)));
            try { page.Draw(new Rect(0, 0, w, page.ContentHeight), firstY); }
            catch (Exception ex) { Nano.Log.LogError($"Settings page '{page.Name}' failed: {ex.Message}"); }
            finally { GUI.EndScrollView(); }
            return scroll;
        }
    }
}
