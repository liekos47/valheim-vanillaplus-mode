using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Storage window (F2): every item in your chests within [Storage] Range as ONE list — search bar,
    // category filter, sorting, scrolling — with Take 1 / Take stack / Take all per item, and a
    // "My inventory" page to Store a stack / all of an item / everything. Storing goes only to chests
    // that already hold that item (ones holding nothing else first, then mixed ones) and stops with
    // "chest full" when those are full; an item no chest holds yet is not stored ("no chest assigned").
    // Uses the same chests as Craft from chests: player-built chests, carts and ship storage you could
    // open, not open by someone else. Moving items works like dragging them in a chest.
    internal static class Storage
    {
        private static readonly System.Reflection.MethodInfo CheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly System.Reflection.FieldInfo CurrentContainer = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
        private static readonly List<Container> Cache = new List<Container>();
        private static float _cacheUntil;

        // Usable chests within range, closest first (cached for 0.5 s).
        public static List<Container> Chests(Player p)
        {
            if (Time.unscaledTime < _cacheUntil) { Cache.RemoveAll(c => c == null); return Cache; }
            _cacheUntil = Time.unscaledTime + 0.5f;
            Cache.Clear();
            if (p == null) return Cache;
            Vector3 pos = p.transform.position;
            float range = VanillaPlusPlugin.StorageRange.Value;
            long id = p.GetPlayerID();
            Container open = InventoryGui.instance != null ? CurrentContainer?.GetValue(InventoryGui.instance) as Container : null;
            foreach (var kv in ChestTracker.All())
            {
                var c = kv.Key;
                if (c == null || kv.Value.Kind != ChestTracker.Kind.PlayerBuilt) continue;
                var view = kv.Value.View;
                if (view == null || !view.IsValid() || Vector3.Distance(pos, c.transform.position) > range) continue;
                if (c.IsInUse() && c != open) continue;
                if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) continue;
                if (CheckAccess != null && !(bool)CheckAccess.Invoke(c, new object[] { id })) continue;
                Cache.Add(c);
            }
            Cache.Sort((a, b) => (a.transform.position - pos).sqrMagnitude.CompareTo((b.transform.position - pos).sqrMagnitude));
            return Cache;
        }

        public static void Invalidate() => _cacheUntil = 0f;

        // Only the owner's changes to a chest are saved, so take ownership before changing it.
        public static void Own(Container c)
        {
            var view = c.GetComponentInParent<ZNetView>();
            if (view != null && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
        }

        public static string Key(ItemDrop.ItemData it) => $"{it.m_shared.m_name}|{it.m_quality}|{it.m_variant}";

        // Moves up to `amount` of `item` from one inventory into another (stacking first). Returns moved.
        public static int Move(Inventory from, Inventory to, ItemDrop.ItemData item, int amount)
        {
            int moved = 0;
            int guard = 0;
            while (amount > 0 && item.m_stack > 0 && from.ContainsItem(item) && guard++ < 64)
            {
                if (!FindSlot(to, item, out int x, out int y, out int room)) break;
                int n = Mathf.Min(amount, Mathf.Min(room, item.m_stack));
                int before = item.m_stack;
                bool gone = n >= before;
                to.MoveItemToThis(from, item, n, x, y);
                int done = gone ? before : before - item.m_stack;
                if (done <= 0) break;
                moved += done; amount -= done;
                if (gone) break;
            }
            return moved;
        }

        private static bool FindSlot(Inventory inv, ItemDrop.ItemData item, out int x, out int y, out int room)
        {
            int max = item.m_shared.m_maxStackSize;
            if (max > 1)
                foreach (var it in inv.GetAllItems())
                    if (it.m_shared.m_name == item.m_shared.m_name && it.m_quality == item.m_quality && it.m_variant == item.m_variant
                        && it.m_stack < max && it.m_worldLevel == item.m_worldLevel)
                    { x = it.m_gridPos.x; y = it.m_gridPos.y; room = max - it.m_stack; return true; }
            for (int yy = 0; yy < inv.GetHeight(); yy++)
                for (int xx = 0; xx < inv.GetWidth(); xx++)
                    if (inv.GetItemAt(xx, yy) == null) { x = xx; y = yy; room = max; return true; }
            x = y = room = 0;
            return false;
        }

        private static readonly HashSet<ItemDrop.ItemData.ItemType> Weapons = new HashSet<ItemDrop.ItemData.ItemType>
        {
            ItemDrop.ItemData.ItemType.OneHandedWeapon, ItemDrop.ItemData.ItemType.TwoHandedWeapon,
            ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft, ItemDrop.ItemData.ItemType.Bow,
            ItemDrop.ItemData.ItemType.Shield, ItemDrop.ItemData.ItemType.Attach_Atgeir,
        };
        private static readonly HashSet<ItemDrop.ItemData.ItemType> Armor = new HashSet<ItemDrop.ItemData.ItemType>
        {
            ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs,
            ItemDrop.ItemData.ItemType.Hands, ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility,
            ItemDrop.ItemData.ItemType.Trinket,
        };
        private static readonly HashSet<ItemDrop.ItemData.ItemType> Tools = new HashSet<ItemDrop.ItemData.ItemType>
        {
            ItemDrop.ItemData.ItemType.Tool, ItemDrop.ItemData.ItemType.Torch,
        };
        private static readonly HashSet<ItemDrop.ItemData.ItemType> Ammo = new HashSet<ItemDrop.ItemData.ItemType>
        {
            ItemDrop.ItemData.ItemType.Ammo, ItemDrop.ItemData.ItemType.AmmoNonEquipable,
        };

        // Items the "keep" rules protect when storing everything: ticked categories and the never-store list.
        public static bool Kept(ItemDrop.ItemData item)
        {
            var t = item.m_shared.m_itemType;
            if (VanillaPlusPlugin.StorageKeepArmor.Value && Armor.Contains(t)) return true;
            if (VanillaPlusPlugin.StorageKeepWeapons.Value && Weapons.Contains(t)) return true;
            if (VanillaPlusPlugin.StorageKeepTools.Value && Tools.Contains(t)) return true;
            if (VanillaPlusPlugin.StorageKeepFood.Value && t == ItemDrop.ItemData.ItemType.Consumable) return true;
            if (VanillaPlusPlugin.StorageKeepAmmo.Value && Ammo.Contains(t)) return true;
            if (VanillaPlusPlugin.StorageKeepTrophies.Value && t == ItemDrop.ItemData.ItemType.Trophy) return true;
            return NeverStored(item);
        }

        // The never-store list: prefab or shown names, * wildcard.
        private static bool NeverStored(ItemDrop.ItemData item)
        {
            string list = VanillaPlusPlugin.StorageNeverStore.Value ?? "";
            if (list.Trim().Length == 0) return false;
            string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
            string shown = Localization.instance.Localize(item.m_shared.m_name);
            foreach (string part in list.Split(','))
            {
                string entry = part.Trim();
                if (entry.Length == 0) continue;
                var m = new Regex("^" + Regex.Escape(entry).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);
                if (m.IsMatch(prefab) || m.IsMatch(shown)) return true;
            }
            return false;
        }
    }

    internal static class StorageWindow
    {
        public static bool IsOpen;
        public static bool Typing; // search box focused: game keys blocked
        private const int WindowId = 0x7A92;
        private const string SearchControl = "vanillaplus_storagesearch";
        private static Rect _rect = new Rect(600f, 80f, 600f, 640f);
        private static bool _placed, _moved;
        private static Vector2 _scroll;
        private static string _search = "";
        private static int _page;      // 0 = chests, 1 = my inventory
        private static int _category;  // see Categories
        private static int _sort;      // 0 = name, 1 = count
        private static string _status = "";
        private static float _statusUntil;
        private static GUIStyle _rich, _count;
        private static GUISkin _styleSkin;

        private static readonly string[] Pages = { "Chests (take)", "My inventory (store)" };
        private static readonly string[] Categories = { "All", "Materials", "Food & meads", "Weapons & tools", "Armor", "Other" };
        private static readonly string[] Sorts = { "Name", "Count" };

        private class Row
        {
            public string Key, Name;
            public ItemDrop.ItemData Sample;
            public int Total, Where;
            public List<(Container c, ItemDrop.ItemData item)> Sources = new List<(Container, ItemDrop.ItemData)>();
        }

        public static void Toggle()
        {
            IsOpen = !IsOpen;
            if (IsOpen) { MenuWindow.IsOpen = false; Storage.Invalidate(); }
        }

        public static void Draw()
        {
            if (!IsOpen) { Typing = false; SavePosition(); return; }
            var p = Player.m_localPlayer;
            if (p == null || !VanillaPlusPlugin.StorageOn) { IsOpen = false; return; }
            if (!_placed)
            {
                _placed = true;
                _rect.x = Mathf.Clamp(VanillaPlusPlugin.StorageX.Value, 0f, Mathf.Max(0f, Screen.width - _rect.width));
                _rect.y = Mathf.Clamp(VanillaPlusPlugin.StorageY.Value, 0f, Mathf.Max(0f, Screen.height - 200f));
            }
            var savedSkin = GUI.skin;
            var theme = MenuTheme.Current(savedSkin);
            if (theme != null) GUI.skin = theme;
            if (_rich == null || _styleSkin != GUI.skin)
            {
                _styleSkin = GUI.skin;
                _rich = new GUIStyle(GUI.skin.label) { richText = true };
                _count = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
            }
            _rect.height = Mathf.Min(680f, Screen.height - _rect.y - 20f);
            var before = _rect.position;
            _rect = GUILayout.Window(WindowId, _rect, Contents, "Storage — all nearby chests");
            if (_rect.position != before) _moved = true;
            if (_moved && Event.current.rawType == EventType.MouseUp) SavePosition();
            GUI.skin = savedSkin;
        }

        private static void SavePosition()
        {
            if (!_moved) return;
            _moved = false;
            VanillaPlusPlugin.StorageX.Value = Mathf.Round(_rect.x);
            VanillaPlusPlugin.StorageY.Value = Mathf.Round(_rect.y);
        }

        private static void Contents(int id)
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            var chests = Storage.Chests(p);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{chests.Count} chest{(chests.Count == 1 ? "" : "s")} within", GUILayout.Width(110f));
            float r = GUILayout.HorizontalSlider(VanillaPlusPlugin.StorageRange.Value, 5f, 100f, GUILayout.Width(150f));
            r = Mathf.Round(r);
            if (!Mathf.Approximately(r, VanillaPlusPlugin.StorageRange.Value)) { VanillaPlusPlugin.StorageRange.Value = r; Storage.Invalidate(); }
            GUILayout.Label($"{r:0} m", GUILayout.Width(45f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button($"Close ({VanillaPlusPlugin.StorageKey.Value})", GUILayout.Width(90f))) IsOpen = false;
            GUILayout.EndHorizontal();

            int page = GUILayout.Toolbar(_page, Pages);
            if (page != _page) { _page = page; _scroll = Vector2.zero; }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(55f));
            GUI.SetNextControlName(SearchControl);
            _search = GUILayout.TextField(_search ?? "", 40);
            if (GUILayout.Button("x", GUILayout.Width(26f))) { _search = ""; GUI.FocusControl(null); }
            GUILayout.Label("Sort:", GUILayout.Width(35f));
            _sort = GUILayout.Toolbar(_sort, Sorts, GUILayout.Width(120f));
            GUILayout.EndHorizontal();
            if (Event.current.type == EventType.Repaint) Typing = GUI.GetNameOfFocusedControl() == SearchControl;
            _category = GUILayout.Toolbar(_category, Categories);

            if (_page == 1)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Store everything (keeps equipped items and the keep rules in the menu)"))
                    StoreEverything(p, chests);
                GUILayout.EndHorizontal();
            }

            var rows = Filter(_page == 0 ? ChestRows(chests) : InventoryRows(p));

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (rows.Count == 0)
                GUILayout.Label(_page == 0 ? (chests.Count == 0 ? "No usable chests in range." : "Nothing matches.") : "Nothing to store.");
            foreach (var row in rows) DrawRow(p, chests, row);
            GUILayout.EndScrollView();

            GUILayout.Label(Time.unscaledTime < _statusUntil ? _status : $"{rows.Count} item type{(rows.Count == 1 ? "" : "s")}");
            GUI.DragWindow();
        }

        private static List<Row> ChestRows(List<Container> chests)
        {
            var map = new Dictionary<string, Row>();
            foreach (var c in chests)
            {
                var seen = new HashSet<string>();
                foreach (var it in c.GetInventory().GetAllItems())
                {
                    string k = Storage.Key(it);
                    if (!map.TryGetValue(k, out var row)) map[k] = row = new Row { Key = k, Name = NameOf(it), Sample = it };
                    row.Total += it.m_stack;
                    row.Sources.Add((c, it));
                    if (seen.Add(k)) row.Where++;
                }
            }
            return map.Values.ToList();
        }

        private static List<Row> InventoryRows(Player p)
        {
            var map = new Dictionary<string, Row>();
            foreach (var it in p.GetInventory().GetAllItems())
            {
                if (it.m_equipped) continue;
                string k = Storage.Key(it);
                if (!map.TryGetValue(k, out var row)) map[k] = row = new Row { Key = k, Name = NameOf(it), Sample = it };
                row.Total += it.m_stack;
                row.Sources.Add((null, it));
            }
            return map.Values.ToList();
        }

        private static List<Row> Filter(List<Row> rows)
        {
            string s = (_search ?? "").Trim();
            IEnumerable<Row> q = rows;
            if (s.Length > 0)
                q = q.Where(r => r.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0
                              || (r.Sample.m_dropPrefab != null && r.Sample.m_dropPrefab.name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0));
            if (_category > 0) q = q.Where(r => CategoryOf(r.Sample) == _category);
            q = _sort == 1 ? q.OrderByDescending(r => r.Total).ThenBy(r => r.Name) : q.OrderBy(r => r.Name);
            return q.ToList();
        }

        private static int CategoryOf(ItemDrop.ItemData it)
        {
            var t = it.m_shared.m_itemType;
            switch (t)
            {
                case ItemDrop.ItemData.ItemType.Material: return 1;
                case ItemDrop.ItemData.ItemType.Consumable: return 2;
                case ItemDrop.ItemData.ItemType.OneHandedWeapon: case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft: case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield: case ItemDrop.ItemData.ItemType.Tool: case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Ammo: case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                    return 3;
                case ItemDrop.ItemData.ItemType.Helmet: case ItemDrop.ItemData.ItemType.Chest: case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Hands: case ItemDrop.ItemData.ItemType.Shoulder: case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return 4;
                default: return 5;
            }
        }

        private static string NameOf(ItemDrop.ItemData it)
        {
            string n = Localization.instance.Localize(it.m_shared.m_name);
            return it.m_shared.m_maxQuality > 1 ? $"{n} (lvl {it.m_quality})" : n;
        }

        private static void DrawRow(Player p, List<Container> chests, Row row)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            var icon = row.Sample.GetIcon();
            Rect ir = GUILayoutUtility.GetRect(28f, 28f, GUILayout.Width(28f), GUILayout.Height(28f));
            if (icon != null && icon.texture != null && Event.current.type == EventType.Repaint)
            {
                var tr = icon.textureRect;
                var tex = icon.texture;
                GUI.DrawTextureWithTexCoords(ir, tex, new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
            }
            string where = _page == 0 ? $"  <color=#888888>in {row.Where} chest{(row.Where == 1 ? "" : "s")}</color>" : "";
            GUILayout.Label(row.Name + where, _rich, GUILayout.Height(28f));
            GUILayout.Label(row.Total.ToString(), _count, GUILayout.Width(55f), GUILayout.Height(28f));

            int stack = Mathf.Max(1, row.Sample.m_shared.m_maxStackSize);
            if (_page == 0)
            {
                if (GUILayout.Button("1", GUILayout.Width(34f), GUILayout.Height(26f))) Take(p, row, 1);
                if (stack > 1 && GUILayout.Button("Stack", GUILayout.Width(52f), GUILayout.Height(26f))) Take(p, row, stack);
                if (GUILayout.Button("All", GUILayout.Width(40f), GUILayout.Height(26f))) Take(p, row, row.Total);
            }
            else
            {
                if (GUILayout.Button("Store stack", GUILayout.Width(85f), GUILayout.Height(26f))) Store(p, chests, row, stack);
                if (GUILayout.Button("Store all", GUILayout.Width(75f), GUILayout.Height(26f))) Store(p, chests, row, row.Total);
            }
            GUILayout.EndHorizontal();
        }

        private static void Take(Player p, Row row, int amount)
        {
            int moved = 0;
            var inv = p.GetInventory();
            foreach (var (c, item) in row.Sources)
            {
                if (amount <= 0) break;
                if (c == null) continue;
                Storage.Own(c);
                int m = Storage.Move(c.GetInventory(), inv, item, amount);
                moved += m; amount -= m;
                if (m == 0 && !c.GetInventory().ContainsItem(item)) continue;
                if (m == 0) break; // inventory full
            }
            Status(moved > 0 ? $"Took {moved} {row.Name}" + (amount > 0 ? " (inventory full)" : "") : "Inventory full");
        }

        // Returns how many were stored.
        private static int Store(Player p, List<Container> chests, Row row, int amount)
        {
            if (chests.Count == 0) { Status("No usable chests in range"); return 0; }
            var inv = p.GetInventory();
            int moved = 0;
            // Only into chests that already hold this item: ones holding nothing else first, then mixed
            // ones (closest first within each). When they are full it stops there, so items never end up
            // in a chest of something else. An item no chest holds yet is not stored at all: put one in
            // a chest by hand to assign that chest to it.
            string name = row.Sample.m_shared.m_name;
            var holding = Holding(chests, row);
            if (holding.Count == 0) { Status($"{row.Name}: no chest assigned"); return 0; }
            var order = holding.OrderByDescending(c => c.GetInventory().GetAllItems().All(i => i.m_shared.m_name == name)).ToList();
            foreach (var item in row.Sources.Select(s => s.item).ToList())
            {
                foreach (var c in order)
                {
                    if (amount <= 0 || !inv.ContainsItem(item)) break;
                    Storage.Own(c);
                    int m = Storage.Move(inv, c.GetInventory(), item, Mathf.Min(amount, item.m_stack));
                    moved += m; amount -= m;
                }
                if (amount <= 0) break;
            }
            Status(moved > 0 ? $"Stored {moved} {row.Name}" + (amount > 0 ? " (chest full)" : "") : $"{row.Name}: chest full");
            return moved;
        }

        // The chests that already hold this item (same name, quality and variant), closest first.
        private static List<Container> Holding(List<Container> chests, Row row) =>
            chests.Where(c => c.GetInventory().GetAllItems().Any(i => Storage.Key(i) == row.Key)).ToList();

        private static void StoreEverything(Player p, List<Container> chests)
        {
            bool keepHotbar = VanillaPlusPlugin.StorageKeepHotbar.Value;
            int stacks = 0, full = 0, unassigned = 0;
            foreach (var row in InventoryRows(p))
            {
                if (Storage.Kept(row.Sample)) continue;
                var sources = row.Sources.Where(s => !(keepHotbar && s.item.m_gridPos.y == 0)).ToList();
                if (sources.Count == 0) continue;
                row.Sources = sources;
                int total = sources.Sum(s => s.item.m_stack);
                if (Holding(chests, row).Count == 0) { unassigned++; continue; }
                int moved = Store(p, chests, row, total);
                if (moved > 0) stacks++;
                if (moved < total) full++;
            }
            Status($"Stored {stacks} item type{(stacks == 1 ? "" : "s")}"
                + (full > 0 ? $", {full} left (chest full)" : "")
                + (unassigned > 0 ? $", {unassigned} left (no chest assigned)" : ""));
        }

        private static void Status(string s) { _status = s; _statusUntil = Time.unscaledTime + 4f; Storage.Invalidate(); }
    }
}
