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
