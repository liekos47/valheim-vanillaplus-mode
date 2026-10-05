using System.Collections.Generic;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Repair alert: warns on screen when an equipped weapon, tool, shield or armor piece drops below
    // [RepairAlert] BelowPercent durability, and again when it breaks (0 %). Each item warns once per
    // drop below the line; repairing it above the line re-arms the warning. Purely local.
    internal static class DurabilityAlert
    {
        private static readonly Dictionary<ItemDrop.ItemData, int> Warned = new Dictionary<ItemDrop.ItemData, int>(); // 1 = low, 2 = broken
        private static float _next, _nextReminder;

        public static void Update(Player p)
        {
            if (!VanillaPlusPlugin.RepairAlertOn) { Warned.Clear(); return; }
            if (Time.time < _next) return;
            _next = Time.time + 1f;

            float threshold = VanillaPlusPlugin.RepairAlertPercent.Value / 100f;
            var low = new List<string>();
            var equipped = p.GetInventory().GetEquippedItems();
            foreach (var it in equipped)
            {
                if (!it.m_shared.m_useDurability) continue;
                float max = it.GetMaxDurability();
                if (max <= 0f) continue;
                float f = it.m_durability / max;
                string name = Localization.instance.Localize(it.m_shared.m_name);
                Warned.TryGetValue(it, out int state);

                if (f <= 0f)
                {
                    if (state < 2) Alert(p, $"<color=#ff4040>{name} is BROKEN</color> - repair it at a workbench / forge", name, f);
                    Warned[it] = 2;
                    low.Add($"{name} broken");
                }
                else if (f <= threshold)
                {
                    if (state < 1) Alert(p, $"<color=#ffb030>{name} is at {f * 100f:0}%</color> - repair soon", name, f);
                    Warned[it] = 1;
                    low.Add($"{name} {f * 100f:0}%");
                }
                else Warned.Remove(it);
            }

            // Forget unequipped / gone items.
            var gone = new List<ItemDrop.ItemData>();
            foreach (var k in Warned.Keys) if (!equipped.Contains(k)) gone.Add(k);
            foreach (var k in gone) Warned.Remove(k);

            // Optional periodic reminder while something is still low.
            float every = VanillaPlusPlugin.RepairAlertRepeatMinutes.Value * 60f;
            if (low.Count == 0) _nextReminder = 0f;
            else if (every > 0f && Time.time >= _nextReminder)
            {
                if (_nextReminder > 0f) p.Message(MessageHud.MessageType.TopLeft, "Needs repair: " + string.Join(", ", low));
                _nextReminder = Time.time + every;
            }
        }

        private static void Alert(Player p, string msg, string name, float f)
        {
            p.Message(MessageHud.MessageType.Center, msg);
            VanillaPlusPlugin.Log.LogInfo($"Repair alert: {name} at {f * 100f:0}%");
        }
    }
}
