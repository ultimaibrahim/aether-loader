using System;
using System.Collections.Generic;
using com.mindblocks.i18n;
using UnityEngine;

namespace Aether.Nano
{
    /// <summary>The game's items: lookup by name, the item behind a block type, its icon and its localized name.</summary>
    public static class NanoItems
    {
        private static readonly Dictionary<string, InvBaseItem> BlockItems = new Dictionary<string, InvBaseItem>(StringComparer.Ordinal);

        private static readonly System.Reflection.FieldInfo CreativeItemField =
            typeof(DisplayCreativeItem).GetField("item", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        private static readonly System.Reflection.FieldInfo RecipeItemField =
            typeof(RecipeItemClick).GetField("item", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        /// <summary>
        /// The item under the cursor in the open inventory, creative list or recipe list; null while dragging, when no
        /// inventory is open, or when a host overlay or a mod panel is on top.
        /// </summary>
        public static InvGameItem HoveredSlotItem()
        {
            if (UIItemSlot.draggedItem != null || NanoHud.OverlayOpen || NanoPanels.AnyModal || NanoPanels.MouseOverAny) return null;
            Inventory inv = Nano.Inventory;
            if (inv == null || inv.inventoryGameObject == null || !inv.inventoryGameObject.activeInHierarchy) return null;

            GameObject hovered = UICamera.hoveredObject;
            if (hovered == null)
            {
                Vector2 cursor = UICustomCursor.current != null ? UICustomCursor.current.Position : (Vector2)Input.mousePosition;
                RaycastHit hit = default;
                if (UICamera.Raycast(cursor, ref hit) && hit.collider != null) hovered = hit.collider.gameObject;
            }
            if (hovered == null) return null;

            // A paused UICamera can leave a stale hovered object: check the ray really hits it.
            Collider col = hovered.GetComponent<Collider>();
            Camera uiCam = UICamera.currentCamera;
            if (col != null && uiCam != null && !col.Raycast(uiCam.ScreenPointToRay(Input.mousePosition), out _, 1000f)) return null;

            UIItemSlot slot = hovered.GetComponentInParent<UIItemSlot>();
            if (slot != null && slot.observedItem != null) return slot.observedItem;
            DisplayCreativeItem creative = hovered.GetComponentInParent<DisplayCreativeItem>();
            if (creative != null && CreativeItemField?.GetValue(creative) is InvGameItem c) return c;
            RecipeItemClick recipe = hovered.GetComponentInParent<RecipeItemClick>();
            if (recipe != null && RecipeItemField?.GetValue(recipe) is InvGameItem r) return r;
            return null;
        }

        /// <summary>Forgets the block-to-item map (for example after mods add items).</summary>
        public static void ClearCache() => BlockItems.Clear();

        public static InvBaseItem FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try { return InvDatabase.FindByName(name); }
            catch { return null; }
        }

        /// <summary>The inventory item of a block type ("Rock" is Stone, "Sod" is Grass...). Only hits are cached: a miss while loading is retried.</summary>
        public static InvBaseItem ResolveBlockItem(string blockTypeName)
        {
            if (string.IsNullOrEmpty(blockTypeName)) return null;
            if (BlockItems.TryGetValue(blockTypeName, out InvBaseItem cached)) return cached;
            InvBaseItem item = FindByName(blockTypeName);
            if (item == null)
            {
                switch (blockTypeName)
                {
                    case "Rock": item = FindByName("Stone"); break;
                    case "Sod": item = FindByName("Grass"); break;
                    case "Wood": item = FindByName("Bark") ?? FindByName("Plank"); break;
                    case "Tree": item = FindByName("Bark"); break;
                    case "Water": item = FindByName("Bucket Water") ?? FindByName("Water"); break;
                }
            }
            if (item != null) BlockItems[blockTypeName] = item;
            return item;
        }

        /// <summary>Draws the item's icon from its atlas. False when it has none.</summary>
        public static bool DrawIcon(Rect rect, InvBaseItem item, Color? tint = null)
        {
            if (item == null || item.iconAtlas == null || string.IsNullOrEmpty(item.iconName)) return false;
            var sprite = item.iconAtlas.GetSprite(item.iconName);
            Texture tex = item.iconAtlas.texture;
            if (sprite == null || tex == null) return false;
            Rect uv = sprite.outer;
            if (item.iconAtlas.coordinates == UIAtlas.Coordinates.Pixels) uv = NGUIMath.ConvertToTexCoords(uv, tex.width, tex.height);
            Color old = GUI.color;
            try
            {
                Color c = tint ?? item.color;
                if (c.a > 0.01f) GUI.color = c;
                GUI.DrawTextureWithTexCoords(rect, tex, uv);
            }
            finally { GUI.color = old; }
            return true;
        }

        /// <summary>The name in the player's language. Format strings ("{0} Antique Spawner") fall back to the item's own name.</summary>
        public static string LocalizedName(string rawName, InvBaseItem item)
        {
            if (string.IsNullOrEmpty(rawName)) return "Block";
            try
            {
                string key = item != null && !string.IsNullOrEmpty(item.name) ? item.name : rawName;
                string translated = T.ItemNames(key);
                if (!string.IsNullOrEmpty(translated))
                {
                    if (translated.IndexOf("{0}", StringComparison.Ordinal) >= 0)
                        return item != null && !string.IsNullOrEmpty(item.name) ? item.name : translated.Replace("{0}", "").Trim();
                    return translated;
                }
            }
            catch { }
            return rawName;
        }
    }
}
