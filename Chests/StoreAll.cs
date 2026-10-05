using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // "Store all" button under an open chest: moves everything from your inventory into that chest
    // except what you keep: equipped items, and the storage window's keep rules (hotbar row, ticked
    // categories, never-store list). Items move the same way as dragging them in.
    internal static class StoreAll
    {
        private static readonly FieldInfo CurrentContainer = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
        private static readonly Vector3[] Corners = new Vector3[4];

        public static void DrawButton()
        {
            if (!VanillaPlusPlugin.StoreAllOn) return;
            var gui = InventoryGui.instance;
            var player = Player.m_localPlayer;
            if (gui == null || player == null || !InventoryGui.IsVisible() || !gui.IsContainerOpen()) return;
            if (!(CurrentContainer?.GetValue(gui) is Container chest) || chest == null) return;

            // Under the chest panel, at its left edge.
            gui.m_container.GetWorldCorners(Corners);
            var rect = new Rect(Corners[0].x, Screen.height - Corners[0].y + 6f, 150f, 30f);
            if (GUI.Button(rect, "Store all"))
            {
                int moved = Move(player, chest);
                player.Message(MessageHud.MessageType.Center, $"Stored {moved} stack{(moved == 1 ? "" : "s")}");
            }
        }

        private static int Move(Player p, Container chest)
        {
            var from = p.GetInventory();
            var to = chest.GetInventory();
            int moved = 0;
            foreach (var item in new List<ItemDrop.ItemData>(from.GetAllItems()))
            {
                if (item.m_equipped || Storage.Kept(item)) continue;
                if (VanillaPlusPlugin.StorageKeepHotbar.Value && item.m_gridPos.y == 0) continue;

                int stackBefore = item.m_stack;
                to.MoveItemToThis(from, item);
                if (!from.GetAllItems().Contains(item) || item.m_stack < stackBefore) moved++;
            }
            return moved;
        }
    }
}
