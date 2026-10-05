using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    internal enum PickupFilterMode { Off, Whitelist, Blacklist }

    // Pickup filter: a whitelist / blacklist for the game's auto-pickup only — pressing E still picks
    // up anything. The auto-pickup range and speed are the game's own; this only makes it pick up less.
    // List entries are item names, prefab ("TrophyDeer") or as shown in game ("Deer trophy"), any
    // case; * stands for any run of characters ("Trophy*", "*Ore", "*mead*").
    internal static class PickupFilter
    {
        private static List<Regex> _whitelist = new List<Regex>(), _blacklist = new List<Regex>();
        // Names already checked against the current lists: the same few items are asked about every frame.
        private static readonly Dictionary<string, bool> Listed = new Dictionary<string, bool>();

        public static bool Active => VanillaPlusPlugin.PickupFilterMode.Value != PickupFilterMode.Off && !VanillaPlusPlugin.GodModeLoaded;

        public static void Init()
        {
            RebuildLists();
            VanillaPlusPlugin.PickupWhitelist.SettingChanged += (_, __) => RebuildLists();
            VanillaPlusPlugin.PickupBlacklist.SettingChanged += (_, __) => RebuildLists();
            VanillaPlusPlugin.PickupFilterMode.SettingChanged += (_, __) => Listed.Clear();
        }

        public static void RebuildLists()
        {
            _whitelist = Parse(VanillaPlusPlugin.PickupWhitelist.Value);
            _blacklist = Parse(VanillaPlusPlugin.PickupBlacklist.Value);
            Listed.Clear();
        }

        private static List<Regex> Parse(string csv)
        {
            var list = new List<Regex>();
            foreach (string part in (csv ?? "").Split(','))
            {
                string entry = part.Trim();
                if (entry.Length == 0) continue;
                list.Add(new Regex("^" + Regex.Escape(entry).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
            }
            return list;
        }

        // May auto-pickup take this item?
        public static bool Allowed(ItemDrop drop)
        {
            var mode = VanillaPlusPlugin.PickupFilterMode.Value;
            if (mode == PickupFilterMode.Off) return true;

            string prefab = drop.m_itemData.m_dropPrefab != null ? drop.m_itemData.m_dropPrefab.name : drop.gameObject.name.Replace("(Clone)", "");
            if (!Listed.TryGetValue(prefab, out bool listed))
            {
                string shown = Localization.instance.Localize(drop.m_itemData.m_shared.m_name);
                var set = mode == PickupFilterMode.Whitelist ? _whitelist : _blacklist;
                listed = false;
                foreach (var entry in set)
                    if (entry.IsMatch(prefab) || entry.IsMatch(shown)) { listed = true; break; }
                Listed[prefab] = listed;
            }
            return mode == PickupFilterMode.Whitelist ? listed : !listed;
        }

        // ----- for the icon picker in the menu -----

        // Is this item on the whitelist (or the blacklist)? Wildcard entries count.
        public static bool InList(bool whitelist, string prefab, string shown)
        {
            foreach (var entry in whitelist ? _whitelist : _blacklist)
                if (entry.IsMatch(prefab) || entry.IsMatch(shown)) return true;
            return false;
        }

        // Adds the item to the list (by prefab name), or takes it out if it's there. Returns what happened.
        public static string Toggle(bool whitelist, string prefab, string shown)
        {
            var setting = whitelist ? VanillaPlusPlugin.PickupWhitelist : VanillaPlusPlugin.PickupBlacklist;
            string which = whitelist ? "whitelist" : "blacklist";
            var entries = new List<string>();
            foreach (string part in (setting.Value ?? "").Split(','))
                if (part.Trim().Length > 0) entries.Add(part.Trim());

            int removed = entries.RemoveAll(e => string.Equals(e, prefab, System.StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(e, shown, System.StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                if (InList(whitelist, prefab, shown))
                    return $"{shown} is covered by a * entry of the {which}; use \"Edit {which}\" to change that";
                entries.Add(prefab);
            }
            setting.Value = string.Join(", ", entries); // SettingChanged rebuilds the lists
            return removed > 0 ? $"Removed {shown} from the {which}" : $"Added {shown} to the {which}";
        }

        public static string Summary() =>
            $"whitelist: {Shown(VanillaPlusPlugin.PickupWhitelist.Value)}\nblacklist: {Shown(VanillaPlusPlugin.PickupBlacklist.Value)}";

        private static string Shown(string csv) => string.IsNullOrWhiteSpace(csv) ? "(empty)" : csv.Trim();
    }

    // Temporarily clear m_autoPickup on filtered items near the player for the duration of the
    // vanilla auto-pickup pass, then restore it.
    [HarmonyPatch(typeof(Player), "AutoPickup")]
    internal static class PickupFilter_AutoPickup
    {
        private static readonly FieldInfo InstancesField = AccessTools.Field(typeof(ItemDrop), "s_instances");
        private static readonly List<ItemDrop> Suppressed = new List<ItemDrop>();

        private static void Prefix(Player __instance)
        {
            Suppressed.Clear();
            if (!PickupFilter.Active || __instance != Player.m_localPlayer) return;
            if (!(InstancesField?.GetValue(null) is List<ItemDrop> drops)) return;

            Vector3 pos = __instance.transform.position;
            float r = __instance.m_autoPickupRange + 2f;
            float rSqr = r * r;

            foreach (var drop in drops)
            {
                if (drop == null || !drop.m_autoPickup) continue;
                if ((drop.transform.position - pos).sqrMagnitude > rSqr) continue;
                if (PickupFilter.Allowed(drop)) continue;
                drop.m_autoPickup = false;
                Suppressed.Add(drop);
            }
        }

        private static void Finalizer()
        {
            foreach (var drop in Suppressed)
                if (drop != null) drop.m_autoPickup = true;
            Suppressed.Clear();
        }
    }
}
