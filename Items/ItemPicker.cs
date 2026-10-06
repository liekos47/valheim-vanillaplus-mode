using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Item list editing: a menu button opens the game's "Enter text" box holding the list (item
    // names with commas between), and while that box is open a grid of item icons is shown beside
    // it. Clicking an icon adds that item's name to the text, clicking a framed one takes it out
    // again; OK saves the list, Cancel leaves it alone. The items are read from the game's own item
    // database when you are in a world, so whatever the installed game version (and other mods)
    // add is there. Used by the auto-pickup filter's whitelist and blacklist and the never-store list.
    internal static class ItemPicker
    {
        internal class Item { public string Prefab, Shown; public Sprite Icon; }

        private const int PerRow = 11, MaxShown = 132;
        private static readonly AccessTools.FieldRef<TextInput, TextReceiver> Queued =
            AccessTools.FieldRefAccess<TextInput, TextReceiver>("m_queuedSign");

        private static readonly List<Item> Items = new List<Item>();
        private static ObjectDB _builtFor;

        // The box opened by Open(), and how its list is read.
        private static Receiver _open, _placedFor;
        private static Color _frame;
        private static bool _wildcards;
        private static string _status = "";

        private static string _find = "";
        private static bool _onlyListed;
        private static Vector2 _scroll;
        private static readonly List<Item> Shown = new List<Item>();
        private static readonly List<bool> ShownListed = new List<bool>();
        private static bool _more;

        // The text in the box, taken apart (redone only when the text changes).
        private static string _parsedText;
        private static readonly List<string> Entries = new List<string>();
        private static readonly List<Regex> Patterns = new List<Regex>();

        // frame: color of the frame around items that are in the list. wildcards: entries may use *.
        public static void Open(string title, ConfigEntry<string> entry, Color frame, bool wildcards)
        {
            if (TextInput.instance == null) return;
            MenuWindow.IsOpen = false;
            _open = new Receiver(entry);
            _frame = frame; _wildcards = wildcards; _status = ""; _parsedText = null;
            TextInput.instance.RequestText(_open, title, 500);
        }

        private class Receiver : TextReceiver
        {
            private readonly ConfigEntry<string> _entry;
            public Receiver(ConfigEntry<string> entry) { _entry = entry; }
            public string GetText() => _entry.Value ?? "";
            public void SetText(string text) => _entry.Value = (text ?? "").Trim();
        }

        // Every item that has an icon, by shown name.
        private static List<Item> All()
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

        // Drawn every OnGUI: shows only while the box opened by Open() is up.
        public static void DrawPanel()
        {
            var ti = TextInput.instance;
            if (_open == null || ti == null || !TextInput.IsVisible() || Queued(ti) != _open) return;
            var f = ti.m_inputField;
            if (f == null) return;
            Parse(f.text ?? "");

            // Beside the game's text box, never over it; with less room the grid gets fewer columns.
            const float chrome = 34f; // box padding + scrollbar
            var area = PanelPlace.Beside(ti.m_panel, PerRow * 38f + chrome, 6 * 38f + chrome, 450f);
            int perRow = Mathf.Clamp(Mathf.FloorToInt((area.width - chrome) / 38f), 1, PerRow);
            if (_placedFor != _open)
            {
                _placedFor = _open; // once per opening, so a misplaced panel can be traced in the log
                VanillaPlusPlugin.Log.LogInfo($"Item grid at {area} ({perRow} columns) on a {Screen.width}x{Screen.height} screen");
            }
            GUILayout.BeginArea(area, GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Find:", GUILayout.Width(40f));
            _find = GUILayout.TextField(_find ?? "", 30);
            if (GUILayout.Button("✕", GUILayout.Width(28f))) { _find = ""; GUI.FocusControl(null); }
            GUILayout.EndHorizontal();
            _onlyListed = GUILayout.Toggle(_onlyListed, " only items in the list");

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
                    bool on = Listed(it);
                    if (_onlyListed && !on) continue;
                    if (Shown.Count == MaxShown) { _more = true; break; }
                    Shown.Add(it); ShownListed.Add(on);
                }
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            int col = 0;
            for (int i = 0; i < Shown.Count; i++)
            {
                if (col == 0) GUILayout.BeginHorizontal();
                var it = Shown[i];
                var r = GUILayoutUtility.GetRect(38f, 38f, GUILayout.Width(38f), GUILayout.Height(38f));
                if (GUI.Button(r, new GUIContent("", it.Shown == it.Prefab ? it.Shown : $"{it.Shown}  ({it.Prefab})"))) Click(f, it);
                if (Event.current.type == EventType.Repaint)
                {
                    DrawSprite(new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f), it.Icon);
                    if (ShownListed[i]) Frame(r, _frame);
                }
                if (++col == perRow) { GUILayout.EndHorizontal(); col = 0; }
            }
            if (col != 0) GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            string tip = GUI.tooltip;
            GUILayout.Label(!string.IsNullOrEmpty(tip) ? tip
                : !string.IsNullOrEmpty(_status) ? _status
                : Items.Count == 0 ? "The item list is available once you are in a world."
                : Shown.Count == 0 ? "Nothing matches."
                : _more ? $"Showing the first {MaxShown} - type in Find to narrow down."
                : "Click an icon to add it to the list; click a framed one to take it out. OK saves.", GUILayout.Height(22f));
            GUILayout.EndArea();
        }

        private static void Parse(string text)
        {
            if (text == _parsedText) return;
            _parsedText = text;
            Entries.Clear(); Patterns.Clear();
            foreach (string part in text.Split(','))
            {
                string entry = part.Trim();
                if (entry.Length == 0) continue;
                Entries.Add(entry);
                string pattern = _wildcards ? Regex.Escape(entry).Replace("\\*", ".*") : Regex.Escape(entry);
                Patterns.Add(new Regex("^" + pattern + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
        }

        // Is the item named by the text in the box (by prefab or shown name; * entries count)?
        private static bool Listed(Item it)
        {
            foreach (var p in Patterns)
                if (p.IsMatch(it.Prefab) || p.IsMatch(it.Shown)) return true;
            return false;
        }

        // Adds the item's prefab name to the text in the box, or takes its entry out.
        private static void Click(TMPro.TMP_InputField f, Item it)
        {
            var entries = new List<string>(Entries);
            int removed = entries.RemoveAll(e => string.Equals(e, it.Prefab, System.StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(e, it.Shown, System.StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                if (Listed(it)) { _status = $"{it.Shown} is covered by a * entry; edit the text to change that"; return; }
                entries.Add(it.Prefab);
            }
            string text = string.Join(", ", entries);
            if (f.characterLimit > 0 && text.Length > f.characterLimit) { _status = $"The list is full ({f.characterLimit} characters)"; return; }
            f.text = text;
            _status = removed > 0 ? $"Removed {it.Shown}" : $"Added {it.Shown}";
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
