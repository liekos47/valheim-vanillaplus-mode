using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Batch moves in the inventory screen, for every stack of the clicked item (same item name):
    //  - Left Alt + left click with a chest open: move all of them to the other side (inventory <-> chest).
    //  - Left Alt + Shift + left click (inventory or chest): drop all of them on the ground.
    // Plain Ctrl + click is the game's own (moves just that one stack). The Alt key is configurable
    // ([Inventory] BatchMoveKey). Items move / drop the same way vanilla does. Equipped copies are left
    // alone unless they are the one you clicked; quest items are never touched.
    internal static class BatchTransfer
    {
        private static readonly FieldInfo CurrentContainer = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
        private static readonly FieldInfo DragGo = AccessTools.Field(typeof(InventoryGui), "m_dragGo");
        private static readonly FieldInfo MoveEffects = AccessTools.Field(typeof(InventoryGui), "m_moveItemEffects");

        // Returns true when handled (the game's own click handling is skipped).
        // drop = Alt + Shift held (throw all out); otherwise it's the Alt batch move.
        internal static bool Handle(InventoryGui gui, InventoryGrid grid, ItemDrop.ItemData item, bool drop)
        {
            if (!VanillaPlusPlugin.BatchTransferOn) return false;
            var p = Player.m_localPlayer;
            if (p == null || item == null || item.m_shared.m_questItem) return false;
            if (DragGo?.GetValue(gui) as GameObject != null) return false; // holding a dragged item: vanilla

            var from = grid.GetInventory();
            var chest = CurrentContainer?.GetValue(gui) as Container;
            if (!drop && chest == null) return false; // nothing to move to without a chest open

            var same = SameItems(p, from, item);
            if (drop)
            {
                int dropped = 0;
                foreach (var it in same)
                    if (p.DropItem(from, it, it.m_stack)) dropped++;
                Done(gui, p, dropped, $"Dropped {dropped} stack{S(dropped)} of {Name(item)}");
                return true;
            }

            var to = from == chest.GetInventory() ? p.GetInventory() : chest.GetInventory();
            int moved = 0;
            foreach (var it in same)
            {
                int before = it.m_stack;
                to.MoveItemToThis(from, it);
                if (!from.ContainsItem(it) || it.m_stack < before) moved++;
            }
            Done(gui, p, moved, moved > 0 ? $"Moved {moved} stack{S(moved)} of {Name(item)}" : "No room");
            return true;
        }

        private static List<ItemDrop.ItemData> SameItems(Player p, Inventory inv, ItemDrop.ItemData clicked)
        {
            var list = new List<ItemDrop.ItemData>();
            foreach (var it in inv.GetAllItems())
            {
                if (it.m_shared.m_name != clicked.m_shared.m_name || it.m_shared.m_questItem) continue;
                if (it.m_equipped && it != clicked) continue;
                list.Add(it);
            }
            if (clicked.m_equipped)
            {
                p.RemoveEquipAction(clicked);
                p.UnequipItem(clicked);
            }
            return list;
        }

        private static void Done(InventoryGui gui, Player p, int count, string msg)
        {
            if (count > 0 && MoveEffects?.GetValue(gui) is EffectList fx) fx.Create(gui.transform.position, Quaternion.identity);
            p.Message(MessageHud.MessageType.TopLeft, msg);
        }

        private static string Name(ItemDrop.ItemData item) => Localization.instance.Localize(item.m_shared.m_name);
        private static string S(int n) => n == 1 ? "" : "s";
    }

    // Ctrl + click arrives as Modifier.Move; Alt + click as Modifier.Select.
    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    internal static class BatchTransfer_Click
    {
        private static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
        {
            if (ZInput.IsExclusiveGamepadActive()) return true;
            // Move key (Left Alt) + Shift + click: throw out every stack. (With Shift held the game reports
            // the click as Split, so the keys are checked directly, and before the plain Alt move.)
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift && Input.GetKey(VanillaPlusPlugin.BatchMoveKey.Value))
                return !BatchTransfer.Handle(__instance, grid, item, drop: true);
            // Move key (Left Alt) + click (arrives as a plain Select): move every stack to the other side.
            if (mod == InventoryGrid.Modifier.Select && Input.GetKey(VanillaPlusPlugin.BatchMoveKey.Value))
                return !BatchTransfer.Handle(__instance, grid, item, drop: false);
            return true; // plain Ctrl+click etc.: the game's own behavior
        }
    }
}
