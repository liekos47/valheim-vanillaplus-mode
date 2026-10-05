using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Search box above the crafting panel: filters the recipe list by the item made OR any ingredient
    // (prefab or shown name, case-insensitive), e.g. "bronze" or "copper". Applied in the recipe-list
    // hook below. While the box has focus, game keys are blocked (SearchInputBlock). Only recipes the
    // game already lists are shown; nothing is unlocked.
    internal static class CraftSearch
    {
        public static string Text = "";
        public static bool Typing;
        private const string Control = "vanillaplus_craftsearch";
        private static readonly MethodInfo UpdateCraftingPanel = AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel");
        private static readonly Vector3[] Corners = new Vector3[4];
        private static string _applied = "";

        public static bool Active => !string.IsNullOrEmpty(Text?.Trim());

        public static void Draw()
        {
            var gui = InventoryGui.instance;
            bool show = VanillaPlusPlugin.CraftSearchOn && gui != null && InventoryGui.IsVisible()
                        && gui.m_crafting != null && gui.m_crafting.gameObject.activeInHierarchy;
            if (!show)
            {
                Typing = false;
                if (Active && !InventoryGui.IsVisible()) { Text = ""; _applied = ""; } // clear when inventory closes
                return;
            }

            gui.m_crafting.GetWorldCorners(Corners);                 // 1 = top-left
            float x = Corners[1].x, y = Screen.height - Corners[1].y - 30f;
            GUI.Label(new Rect(x, y + 4f, 60f, 22f), "Search:");
            GUI.SetNextControlName(Control);
            Text = GUI.TextField(new Rect(x + 58f, y, 240f, 24f), Text ?? "", 40);
            if (GUI.Button(new Rect(x + 302f, y, 26f, 24f), "✕")) { Text = ""; GUI.FocusControl(null); }
            if (Event.current.type == EventType.Repaint) Typing = GUI.GetNameOfFocusedControl() == Control;

            if ((Text ?? "") != _applied)
            {
                _applied = Text ?? "";
                UpdateCraftingPanel?.Invoke(gui, new object[] { false }); // rebuild the recipe list
            }
        }

        // Called from the recipe-list hook: keep only recipes matching the search.
        public static void Filter(List<Recipe> recipes)
        {
            if (!Active || !VanillaPlusPlugin.CraftSearchOn) return;
            string q = Text.Trim();
            recipes.RemoveAll(r => r == null || !Matches(r, q));
        }

        private static bool Matches(Recipe r, string q)
        {
            if (r.m_item != null && Has(r.m_item, q)) return true;
            if (r.m_resources != null)
                foreach (var req in r.m_resources)
                    if (req?.m_resItem != null && Has(req.m_resItem, q)) return true;
            return false;
        }

        private static bool Has(ItemDrop item, string q) =>
            Contains(item.gameObject.name, q)
            || Contains(Localization.instance.Localize(item.m_itemData.m_shared.m_name), q);

        private static bool Contains(string s, string q) =>
            s != null && s.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.GetAvailableRecipes))]
    internal static class CraftSearch_Recipes
    {
        private static void Postfix(Player __instance, ref List<Recipe> available)
        {
            if (available == null || __instance != Player.m_localPlayer) return;
            CraftSearch.Filter(available);
        }
    }
}
