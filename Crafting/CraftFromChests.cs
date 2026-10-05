using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Craft / build from nearby chests: while Valheim checks, shows or consumes crafting and building
    // requirements, your inventory also counts the items in player-built chests (and carts / ship
    // storage) within [Crafting] ChestRange. Whatever your inventory lacks is taken from the closest
    // chests first. Taking works like opening the chest and removing the items by hand: the chest must
    // be one you could open (private chests / wards respected) and not open by another player.
    internal static class CraftFromChests
    {
        // > 0 while a requirement check / display / consume of the local player is running.
        internal static int Scope;
        private static bool _inside; // our own CountItems calls on the player's inventory must not recurse

        private static readonly MethodInfo CheckAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly FieldInfo CurrentContainer = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");

        private static readonly List<Container> Nearby = new List<Container>();
        private static float _nextRefresh;

        // Another craft-from-chests mod (e.g. UnderHeiz CraftFromContainers) counting chests too makes the
        // game's craft list disagree with itself.
        internal static bool OtherModLoaded => _other ?? (_other = CheckOtherMod()).Value;
        private static bool? _other;
        private static bool CheckOtherMod()
        {
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                string id = (info.Metadata.GUID + " " + info.Metadata.Name).ToLowerInvariant();
                if (id.Contains("craftfromcontainers") || id.Contains("craftfromchests") || id.Contains("craftyboxes")) return true;
            }
            return false;
        }

        // While a crafting station is open: when the chests' contents change, rebuild the game's recipe
        // list so "can craft" (white / grey) matches the materials shown.
        private static readonly MethodInfo UpdateCraftingPanel = AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel");
        private static float _nextListCheck;
        private static int _lastSignature;

        internal static void Update(Player p)
        {
            if (!VanillaPlusPlugin.CraftFromChestsOn || Time.unscaledTime < _nextListCheck) return;
            _nextListCheck = Time.unscaledTime + 1f;
            var gui = InventoryGui.instance;
            if (gui == null || !InventoryGui.IsVisible() || gui.m_crafting == null || !gui.m_crafting.gameObject.activeInHierarchy) { _lastSignature = 0; return; }

            int sig = 17;
            foreach (var c in Chests(p))
                foreach (var it in c.GetInventory().GetAllItems())
                    sig = unchecked(sig * 31 + it.m_shared.m_name.GetHashCode() * 7 + it.m_stack);
            if (_lastSignature != 0 && sig != _lastSignature) UpdateCraftingPanel?.Invoke(gui, new object[] { false });
            _lastSignature = sig;
        }

        internal static bool Active(Inventory inv)
        {
            if (Scope <= 0 || _inside || !VanillaPlusPlugin.CraftFromChestsOn) return false;
            var p = Player.m_localPlayer;
            return p != null && inv == p.GetInventory();
        }

        // Usable chests in range, closest first. Rebuilt at most twice a second (the recipe list asks a lot).
        internal static List<Container> Chests(Player p)
        {
            if (Time.unscaledTime < _nextRefresh) { Nearby.RemoveAll(c => c == null); return Nearby; }
            _nextRefresh = Time.unscaledTime + 0.5f;
            Nearby.Clear();

            Vector3 pos = p.transform.position;
            float range = VanillaPlusPlugin.CraftChestRange.Value;
            long id = p.GetPlayerID();
            Container open = InventoryGui.instance != null ? CurrentContainer?.GetValue(InventoryGui.instance) as Container : null;

            foreach (var kv in ChestTracker.All())
            {
                var c = kv.Key;
                if (c == null || kv.Value.Kind != ChestTracker.Kind.PlayerBuilt) continue;
                var view = kv.Value.View;
                if (view == null || !view.IsValid()) continue;
                if (Vector3.Distance(pos, c.transform.position) > range) continue;
                if (c.IsInUse() && c != open) continue; // someone else has it open
                if (c.m_checkGuardStone && !PrivateArea.CheckAccess(c.transform.position, 0f, false)) continue;
                if (CheckAccess != null && !(bool)CheckAccess.Invoke(c, new object[] { id })) continue;
                Nearby.Add(c);
            }
            Nearby.Sort((a, b) => Vector3.SqrMagnitude(a.transform.position - pos)
                .CompareTo(Vector3.SqrMagnitude(b.transform.position - pos)));
            return Nearby;
        }

        internal static int CountInChests(string name, int quality, bool matchWorldLevel)
        {
            int n = 0;
            foreach (var c in Chests(Player.m_localPlayer))
                n += c.GetInventory().CountItems(name, quality, matchWorldLevel);
            return n;
        }

        // Takes what the player's inventory can't cover from the chests. Returns the amount the
        // player's inventory still has to give.
        internal static int TakeShortfall(Inventory playerInv, string name, int amount, int quality, bool worldLevelBased)
        {
            int have;
            _inside = true;
            try { have = playerInv.CountItems(name, quality, worldLevelBased); }
            finally { _inside = false; }
            int missing = amount - have;
            if (missing <= 0) return amount;

            var p = Player.m_localPlayer;
            foreach (var c in Chests(p))
            {
                var inv = c.GetInventory();
                int inChest = inv.CountItems(name, quality, worldLevelBased);
                if (inChest <= 0) continue;
                var view = c.GetComponentInParent<ZNetView>();
                if (view != null && !view.IsOwner()) view.ClaimOwnership(); // only the owner's changes are saved
                int take = Mathf.Min(inChest, missing);
                inv.RemoveItem(name, take, quality, worldLevelBased);
                VanillaPlusPlugin.Log.LogInfo($"Craft from chests: took {take} {name} from chest at {c.transform.position}");
                missing -= take;
                if (missing <= 0) break;
            }
            _nextRefresh = 0f;
            return have; // anything still missing simply isn't there (vanilla removes what it can too)
        }
    }

    // ----- where the extra counting applies -----

    [HarmonyPatch(typeof(Player), "HaveRequirementItems")]
    internal static class CraftFromChests_HaveItems
    {
        // "Only one ingredient" recipes pick the item straight from your inventory, so they stay vanilla.
        private static void Prefix(Player __instance, Recipe piece, bool discover, ref bool __state)
        {
            __state = !discover && !piece.m_requireOnlyOneIngredient && VanillaPlusPlugin.IsLocalPlayer(__instance);
            if (__state) CraftFromChests.Scope++;
        }
        private static void Finalizer(bool __state) { if (__state) CraftFromChests.Scope--; }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
    internal static class CraftFromChests_HavePiece
    {
        private static void Prefix(Player __instance, ref bool __state)
        {
            __state = VanillaPlusPlugin.IsLocalPlayer(__instance);
            if (__state) CraftFromChests.Scope++;
        }
        private static void Finalizer(bool __state) { if (__state) CraftFromChests.Scope--; }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class CraftFromChests_Consume
    {
        private static void Prefix(Player __instance, ref bool __state)
        {
            __state = VanillaPlusPlugin.IsLocalPlayer(__instance);
            if (__state) CraftFromChests.Scope++;
        }
        private static void Finalizer(bool __state) { if (__state) CraftFromChests.Scope--; }
    }

    // Requirement amounts in the crafting panel and the hammer's piece info (no red flashing).
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    internal static class CraftFromChests_Display
    {
        private static void Prefix(Player player, ref bool __state)
        {
            __state = VanillaPlusPlugin.IsLocalPlayer(player);
            if (__state) CraftFromChests.Scope++;
        }
        private static void Finalizer(bool __state) { if (__state) CraftFromChests.Scope--; }
    }

    // ----- the counting / taking itself -----

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.CountItems))]
    internal static class CraftFromChests_Count
    {
        private static void Postfix(Inventory __instance, string name, int quality, bool matchWorldLevel, ref int __result)
        {
            if (name != null && CraftFromChests.Active(__instance))
                __result += CraftFromChests.CountInChests(name, quality, matchWorldLevel);
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.HaveItem), typeof(string), typeof(bool))]
    internal static class CraftFromChests_HaveItem
    {
        private static void Postfix(Inventory __instance, string name, bool matchWorldLevel, ref bool __result)
        {
            if (!__result && CraftFromChests.Active(__instance))
                __result = CraftFromChests.CountInChests(name, -1, matchWorldLevel) > 0;
        }
    }

    [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), typeof(string), typeof(int), typeof(int), typeof(bool))]
    internal static class CraftFromChests_Remove
    {
        private static void Prefix(Inventory __instance, string name, ref int amount, int itemQuality, bool worldLevelBased)
        {
            if (CraftFromChests.Active(__instance))
                amount = CraftFromChests.TakeShortfall(__instance, name, amount, itemQuality, worldLevelBased);
        }
    }
}
