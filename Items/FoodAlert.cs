using System.Collections.Generic;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Food alert: messages about the food you have eaten. Reads your own food list only; nothing is changed.
    //  - Running low: orange, mid-screen, when a food drops below [FoodAlert] WarnBelowMinutes
    //    ("Cooked boar meat runs out in 2:00"). Once per food each time you eat it.
    //  - Ran out: red, mid-screen, when a food expires.
    //  - Can eat again: a top-left note when the game would let you eat that food again (half its time gone).
    //  - Empty slot: a top-left note when fewer than three foods are active (off by default).
    //  - Reminder: while something is low or a slot is empty, a top-left "Food: ..." list every RepeatMinutes.
    //  - Quiet while safe: nothing is shown while you are resting or indoors at a base; what you missed
    //    is said when you leave, if it still applies.
    internal static class FoodAlert
    {
        private const int Low = 1, EatAgain = 2;
        private static readonly int RestingEffect = "Resting".GetStableHashCode();

        private static readonly Dictionary<string, int> Told = new Dictionary<string, int>();      // per food: what has been said
        private static readonly Dictionary<string, float> LastTime = new Dictionary<string, float>(); // per food: time left a second ago
        private static readonly List<string> RanOut = new List<string>();                           // expired, not announced yet
        private static readonly List<string> Scratch = new List<string>();
        private static Player _player;
        private static float _next, _nextReminder;
        private static int _saidEmpty = -1;

        public static void Update(Player p)
        {
            if (!VanillaPlusPlugin.FoodAlertOn) { Reset(null); return; }
            if (p != _player || p.IsDead()) { Reset(p); return; } // new character / death: start clean, no "ran out" for the lot
            if (Time.time < _next) return;
            _next = Time.time + 1f;

            float threshold = VanillaPlusPlugin.FoodAlertMinutes.Value * 60f;
            bool quiet = VanillaPlusPlugin.FoodAlertQuietWhenSafe.Value && IsSafe(p);
            var foods = p.GetFoods();
            var low = new List<string>();

            foreach (var f in foods)
            {
                string key = f.m_item.m_shared.m_name;
                string name = Localization.instance.Localize(key);
                Told.TryGetValue(key, out int told);
                if (LastTime.TryGetValue(key, out float before) && f.m_time > before + 1f) told = 0; // eaten again
                LastTime[key] = f.m_time;
                RanOut.Remove(key);

                if (f.m_time <= threshold)
                {
                    if ((told & Low) == 0 && !quiet)
                    {
                        Center(p, $"<color=#ffb030>{name} runs out in {Clock(f.m_time)}</color>", $"{name} low ({Clock(f.m_time)} left)");
                        told |= Low;
                    }
                    low.Add($"{name} {Clock(f.m_time)}");
                }
                else if (VanillaPlusPlugin.FoodAlertEatAgain.Value && f.CanEatAgain() && (told & EatAgain) == 0 && !quiet)
                {
                    p.Message(MessageHud.MessageType.TopLeft, $"{name} can be eaten again ({Clock(f.m_time)} left)");
                    VanillaPlusPlugin.Log.LogInfo($"Food alert: {name} can be eaten again");
                    told |= EatAgain;
                }
                Told[key] = told;
            }

            // Foods that were active a second ago and are gone now have run out.
            Scratch.Clear();
            foreach (string key in LastTime.Keys)
                if (!foods.Exists(f => f.m_item.m_shared.m_name == key)) Scratch.Add(key);
            foreach (string key in Scratch)
            {
                LastTime.Remove(key); Told.Remove(key);
                if (!RanOut.Contains(key)) RanOut.Add(key);
            }
            if (RanOut.Count > 0 && !quiet)
            {
                string names = string.Join(", ", RanOut.ConvertAll(k => Localization.instance.Localize(k)));
                Center(p, $"<color=#ff4040>{names} {(RanOut.Count == 1 ? "has" : "have")} run out</color>", $"{names} ran out");
                RanOut.Clear();
            }

            // Empty food slots.
            int empty = Mathf.Max(0, 3 - foods.Count);
            if (VanillaPlusPlugin.FoodAlertEmptySlot.Value && empty > 0)
            {
                string text = empty == 3 ? "no food eaten" : $"{empty} empty food slot{(empty == 1 ? "" : "s")}";
                if (_saidEmpty != empty && !quiet)
                {
                    p.Message(MessageHud.MessageType.TopLeft, char.ToUpper(text[0]) + text.Substring(1));
                    _saidEmpty = empty;
                }
                low.Add(text);
            }
            else _saidEmpty = -1;

            // Optional periodic reminder while something is still low / empty.
            float every = VanillaPlusPlugin.FoodAlertRepeatMinutes.Value * 60f;
            if (low.Count == 0) _nextReminder = 0f;
            else if (every > 0f && Time.time >= _nextReminder && !quiet)
            {
                if (_nextReminder > 0f) p.Message(MessageHud.MessageType.TopLeft, "Food: " + string.Join(", ", low));
                _nextReminder = Time.time + every;
            }
        }

        private static void Reset(Player p)
        {
            _player = p;
            if (Told.Count == 0 && LastTime.Count == 0 && RanOut.Count == 0) return;
            Told.Clear(); LastTime.Clear(); RanOut.Clear();
            _saidEmpty = -1; _nextReminder = 0f;
        }

        // Resting (sitting or sheltered by a fire), or under a roof inside a player base.
        private static bool IsSafe(Player p) =>
            p.GetSEMan().HaveStatusEffect(RestingEffect)
            || (p.InShelter() && EffectArea.IsPointInsideArea(p.transform.position, EffectArea.Type.PlayerBase, 0f) != null);

        private static void Center(Player p, string msg, string log)
        {
            p.Message(MessageHud.MessageType.Center, msg);
            VanillaPlusPlugin.Log.LogInfo($"Food alert: {log}");
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
