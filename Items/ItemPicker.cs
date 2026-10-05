using System.Collections.Generic;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // A grid of item icons to click, for building item lists without typing names. The items are read
    // from the game's own item database when you are in a world, so whatever the installed game
    // version (and other mods) add is there. Used by the auto-pickup filter's lists.
    internal static class ItemPicker
    {
        internal class Item { public string Prefab, Shown; public Sprite Icon; }

        public const string Control = "vanillaplus_itempicker"; // the Find box: game keys are blocked while it has focus
        private const int PerRow = 11, MaxShown = 132;

        private static readonly List<Item> Items = new List<Item>();
        private static ObjectDB _builtFor;

        private static string _find = "";
        private static bool _onlyListed;
        private static readonly List<Item> Shown = new List<Item>();
        private static readonly List<bool> ShownListed = new List<bool>();
        private static bool _more;

        // Every item that has an icon, by shown name.
        public static List<Item> All()
        {
            var db = ObjectDB.instance;
            if (db == null || db.m_items == null || db.m_items.Count == 0) return Items;
            if (_builtFor == db) return Items;
            _builtFor = db;
            Items.Clear();
            var seen = new HashSet<string>();
            foreach (var go in db.m_items)
            {
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                var shared = drop?.m_itemData?.m_shared;
                var icons = shared?.m_icons;
                if (icons == null || icons.Length == 0 || icons[0] == null || !seen.Add(go.name)) continue;
                string shown = Localization.instance.Localize(shared.m_name);
                Items.Add(new Item { Prefab = go.name, Shown = string.IsNullOrEmpty(shown) || shown.StartsWith("$") || shown.StartsWith("[") ? go.name : shown, Icon = icons[0] });
            }
            Items.Sort((a, b) => string.Compare(a.Shown, b.Shown, System.StringComparison.OrdinalIgnoreCase));
            return Items;
        }

        // Draws the Find box, the grid and a line of text under it. Items for which `listed` is true get
        // a frame in `frame`; a click calls `onClick`. `status` is shown under the grid when the mouse
        // isn't over an icon (e.g. "Added Wood").
        public static void Draw(System.Func<Item, bool> listed, System.Action<Item> onClick, Color frame, string status)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Find:", GUILayout.Width(40f));
            GUI.SetNextControlName(Control);
            _find = GUILayout.TextField(_find ?? "", 30);
            if (GUILayout.Button("✕", GUILayout.Width(28f))) { _find = ""; GUI.FocusControl(null); }
            _onlyListed = GUILayout.Toggle(_onlyListed, " only items in the list", GUILayout.Width(170f));
            GUILayout.EndHorizontal();

            // What's shown is worked out once per frame (on the layout pass), so the layout and drawing
            // passes of that frame agree even when a click changes the list in between.
            if (Event.current.type == EventType.Layout)
            {
                Shown.Clear(); ShownListed.Clear(); _more = false;
                string q = (_find ?? "").Trim();
                foreach (var it in All())
                {
                    if (q.Length > 0 && it.Shown.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) < 0
                                     && it.Prefab.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool on = listed(it);
                    if (_onlyListed && !on) continue;
                    if (Shown.Count == MaxShown) { _more = true; break; }
                    Shown.Add(it); ShownListed.Add(on);
                }
            }

            int col = 0;
            for (int i = 0; i < Shown.Count; i++)
            {
                if (col == 0) GUILayout.BeginHorizontal();
                var it = Shown[i];
                var r = GUILayoutUtility.GetRect(38f, 38f, GUILayout.Width(38f), GUILayout.Height(38f));
                if (GUI.Button(r, new GUIContent("", it.Shown == it.Prefab ? it.Shown : $"{it.Shown}  ({it.Prefab})"))) onClick(it);
                if (Event.current.type == EventType.Repaint)
                {
                    DrawSprite(new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f), it.Icon);
                    if (ShownListed[i]) Frame(r, frame);
                }
                if (++col == PerRow) { GUILayout.EndHorizontal(); col = 0; }
            }
            if (col != 0) GUILayout.EndHorizontal();

            string tip = GUI.tooltip;
            GUILayout.Label(!string.IsNullOrEmpty(tip) ? tip
                : !string.IsNullOrEmpty(status) ? status
                : Items.Count == 0 ? "The item list is available once you are in a world."
                : Shown.Count == 0 ? "Nothing matches."
                : _more ? $"Showing the first {MaxShown} - type in Find to narrow down."
                : "Click an icon to add it to the list; click a framed one to take it out.", GUILayout.Height(22f));
        }

        private static void DrawSprite(Rect r, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return;
            var tr = sprite.textureRect; var tex = sprite.texture;
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
        }

        private static void Frame(Rect r, Color color)
        {
            const float w = 3f;
            var old = GUI.color; GUI.color = color;
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, r.width, w), tex);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - w, r.width, w), tex);
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, w, r.height), tex);
            GUI.DrawTexture(new Rect(r.xMax - w, r.yMin, w, r.height), tex);
            GUI.color = old;
        }
    }
}
