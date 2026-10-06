using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Death log: what killed you. The last hits your character takes are remembered (source, damage
    // by type after armor / block / resistances, health left); when you die the killing hit is turned
    // into a cause ("Killed by Greydwarf brute", "Died from poison (from a Blob)", "Drowned", ...),
    // shown on screen, written to the log and added to a history kept in a small file on your PC
    // (BepInEx/config/liekos47.valheimvanillaplus.deaths.txt). Damage to your own character is worked
    // out in your own game, so nothing is asked of the server; nothing in the game is changed.
    internal static class DeathLog
    {
        private const int KeepHits = 8, KeepDeaths = 30, ShownDeaths = 10;
        internal static string FilePath => Path.Combine(Paths.ConfigPath, "liekos47.valheimvanillaplus.deaths.txt");
        private static bool On => VanillaPlusPlugin.DeathLogOn;

        private class Hit
        {
            public float At, HealthAfter, MaxHealth;
            public string From;      // who dealt it, or null
            public string Kind;      // the game's hit type: Fall, Drowning, Burning, ...
            public string Element;   // fire / poison / ... when the hit was only that
            public string Effect;    // the status effect that dealt it ("Puking", "Poison", ...), or null
            public string Damage;    // "42 blunt, 10 fire"
        }

        internal class Death
        {
            public DateTime When;
            public string WorldKey, World, Biome, Cause;
            public Vector3 Pos;
            public List<string> Hits = new List<string>();
        }

        private static readonly List<Hit> Recent = new List<Hit>();
        public static readonly List<Death> Deaths = new List<Death>(); // newest first
        // Who last put fire / poison / ... on you directly, for the ticks that follow without an attacker.
        private static readonly Dictionary<string, KeyValuePair<string, float>> ElementFrom = new Dictionary<string, KeyValuePair<string, float>>();
        private static string _announce;
        private static float _announceAt;

        // ----- recording -----

        // The status effect whose update is running right now, if any: a hit taken inside it was dealt
        // by that effect (bukeperry puking, poison, burning, smoke, ...). Such hits carry no attacker
        // and often no hit type, so this is the only way to name them.
        private static StatusEffect _effect;
        private static int _effectDepth;

        internal static void EnterEffect(StatusEffect effect) { _effectDepth++; _effect = effect; }
        internal static void LeaveEffect() { if (--_effectDepth <= 0) { _effectDepth = 0; _effect = null; } }

        private static string EffectName(StatusEffect se)
        {
            if (se == null) return null;
            string name = string.IsNullOrEmpty(se.m_name) ? "" : Localization.instance.Localize(se.m_name);
            return string.IsNullOrEmpty(name) || name.StartsWith("$") || name.StartsWith("[") ? se.name : name;
        }

        // Called when a hit arrives, before the game takes its poison / fire / spirit part off to turn
        // it into a lingering effect: remembers who that part came from, since the ticks that follow
        // carry no attacker.
        public static void NoteSource(Character target, HitData hit)
        {
            if (!On || hit == null || target == null || target != Player.m_localPlayer) return;
            var attacker = hit.GetAttacker();
            if (attacker == null || attacker == target) return;
            var d = hit.m_damage;
            string from = Name(attacker);
            void Note(string element, float amount) { if (amount > 0f) ElementFrom[element] = new KeyValuePair<string, float>(from, Time.time); }
            Note("poison", d.m_poison); Note("fire", d.m_fire); Note("spirit", d.m_spirit); Note("frost", d.m_frost); Note("lightning", d.m_lightning);
        }

        // Called just before a hit's damage is taken off a character's health.
        public static void Record(Character target, HitData hit)
        {
            if (!On || hit == null || target == null || target != Player.m_localPlayer) return;
            float total = hit.GetTotalDamage();
            if (total <= 0f) return;

            var d = hit.m_damage;
            var attacker = hit.GetAttacker();
            string from = attacker != null && attacker != target ? Name(attacker) : null;

            var elements = new[]
            {
                new KeyValuePair<string, float>("fire", d.m_fire), new KeyValuePair<string, float>("frost", d.m_frost),
                new KeyValuePair<string, float>("lightning", d.m_lightning), new KeyValuePair<string, float>("poison", d.m_poison),
                new KeyValuePair<string, float>("spirit", d.m_spirit),
            };
            float physical = d.m_damage + d.m_blunt + d.m_slash + d.m_pierce + d.m_chop + d.m_pickaxe;
            var biggest = elements.OrderByDescending(e => e.Value).First();
            if (from != null)
                foreach (var e in elements)
                    if (e.Value > 0f) ElementFrom[e.Key] = new KeyValuePair<string, float>(from, Time.time);

            Recent.Add(new Hit
            {
                At = Time.time, From = from, Kind = hit.m_hitType.ToString(),
                Element = physical <= 0f && biggest.Value > 0f ? biggest.Key : null,
                Effect = EffectName(_effect),
                Damage = Breakdown(d), HealthAfter = Mathf.Max(0f, target.GetHealth() - total), MaxHealth = target.GetMaxHealth(),
            });
            if (Recent.Count > KeepHits) Recent.RemoveAt(0);
        }

        private static string Name(Character c)
        {
            string name = Localization.instance.Localize(c.GetHoverName());
            if (c.IsPlayer()) return "player " + name;
            return c.GetLevel() > 1 ? $"{name} ★{c.GetLevel() - 1}" : name;
        }

        private static string Breakdown(HitData.DamageTypes d)
        {
            var parts = new List<string>();
            void Add(string n, float v) { if (v >= 0.05f) parts.Add($"{v:0.#} {n}"); }
            Add("damage", d.m_damage); Add("blunt", d.m_blunt); Add("slash", d.m_slash); Add("pierce", d.m_pierce);
            Add("chop", d.m_chop); Add("pickaxe", d.m_pickaxe); Add("fire", d.m_fire); Add("frost", d.m_frost);
            Add("lightning", d.m_lightning); Add("poison", d.m_poison); Add("spirit", d.m_spirit);
            return parts.Count > 0 ? string.Join(", ", parts) : "no damage";
        }

        private static string Cause(Hit h)
        {
            // Lingering fire / poison has no attacker of its own: name who put it on you, if that was recent.
            string fromNote = "";
            if (h.From == null && h.Element != null && ElementFrom.TryGetValue(h.Element, out var src) && Time.time - src.Value < 90f)
                fromNote = $" (from {src.Key})";

            switch (h.Kind)
            {
                case "Fall": return "Fell to your death";
                case "Drowning": return "Drowned";
                case "Burning": case "CinderFire": return "Burned to death" + fromNote;
                case "Freezing": return "Froze to death";
                case "Poisoned": return "Died from poison" + fromNote;
                case "Smoke": return "Suffocated in smoke";
                case "Tree": return "Crushed by a falling tree";
                case "Cart": return "Run over by a cart";
                case "Boat": return "Hit by a ship";
                case "Stalagtite": return "Hit by a falling stalactite";
                case "Structural": return "Crushed by a collapsing structure";
                case "Impact": return "Killed by an impact";
                case "Turret": return "Shot by a ballista";
                case "Catapult": return "Hit by a catapult";
                case "EdgeOfWorld": return "Fell off the edge of the world";
                case "AshlandsOcean": return "Boiled in the Ashlands sea";
                case "AshlandsLava": return "Burned in lava";
                case "Incinerator": return "Struck by the obliterator";
                case "DrawBridge": return "Crushed by a drawbridge";
                case "Water": return "Killed by water";
                case "Self": return "Killed by your own attack";
            }
            if (h.From != null) return h.Element != null ? $"Killed by {h.From} ({h.Element})" : $"Killed by {h.From}";
            // No attacker and no telling hit type, but it came from a status effect: name the effect.
            if (h.Effect != null && h.Element == null) return $"Died from the \"{h.Effect}\" effect";
            switch (h.Element)
            {
                case "fire": return "Burned to death" + fromNote;
                case "poison": return "Died from poison" + fromNote;
                case "frost": return "Froze to death" + fromNote;
                case "lightning": return "Killed by lightning" + fromNote;
                case "spirit": return "Killed by spirit damage" + fromNote;
            }
            return h.Effect != null ? $"Died from the \"{h.Effect}\" effect" : "Killed by damage with no known source";
        }

        internal static void OnDeath(Player p)
        {
            if (!On || p == null || p != Player.m_localPlayer) return;
            float now = Time.time;
            var last = Recent.Count > 0 ? Recent[Recent.Count - 1] : null;
            bool seen = last != null && now - last.At < 3f;

            var death = new Death
            {
                When = DateTime.Now, WorldKey = WorldInfo.WorldKey(), World = ZNet.instance != null ? ZNet.instance.GetWorldName() : "",
                Biome = p.GetCurrentBiome().ToString(), Pos = p.transform.position,
                Cause = seen ? $"{Cause(last)} — last hit: {last.Damage}" : "Died with no damage seen just before (a command, or a hit this game did not process)",
            };
            foreach (var h in Recent)
                if (now - h.At < 30f)
                    death.Hits.Add($"{now - h.At:0.0} s before: {h.From ?? KindWords(h)}, {h.Damage} → {h.HealthAfter:0}/{h.MaxHealth:0} HP");
            Recent.Clear(); ElementFrom.Clear();

            Deaths.Insert(0, death);
            if (Deaths.Count > KeepDeaths) Deaths.RemoveRange(KeepDeaths, Deaths.Count - KeepDeaths);
            Save();

            p.Message(MessageHud.MessageType.Center, $"<color=#ff6060>{death.Cause}</color>");
            _announce = death.Cause; _announceAt = Time.unscaledTime + 6f; // said again once you are back on your feet
            VanillaPlusPlugin.Log.LogInfo($"Death log: {death.Cause} in {death.World} ({death.Biome}) at {death.Pos}" + (death.Hits.Count > 0 ? "\n  " + string.Join("\n  ", death.Hits) : ""));
        }

        private static string KindWords(Hit h)
        {
            if (h.Effect != null) return $"\"{h.Effect}\" effect";
            if (h.Element != null) return h.Element;
            switch (h.Kind)
            {
                case "Fall": return "fall";
                case "Drowning": return "drowning";
                case "Burning": case "CinderFire": return "fire";
                case "AshlandsLava": return "lava";
                case "AshlandsOcean": return "boiling sea";
                case "Tree": return "falling tree";
                case "Freezing": return "freezing";
                case "Poisoned": return "poison";
                case "Smoke": return "smoke";
                default: return "unknown source";
            }
        }

        // Every frame while in a world: repeats the cause top-left once you have respawned.
        public static void Update(Player p)
        {
            if (_announce == null || p == null || p.IsDead() || Time.unscaledTime < _announceAt) return;
            if (On) p.Message(MessageHud.MessageType.TopLeft, "Last death: " + _announce);
            _announce = null;
        }

        // ----- file -----

        private const string HitSeparator = " || ";

        public static void Load()
        {
            Deaths.Clear();
            if (!File.Exists(FilePath)) return;
            foreach (string line in File.ReadAllLines(FilePath))
            {
                var f = line.Split('\t');
                if (f.Length < 8) continue;
                try
                {
                    var d = new Death
                    {
                        When = DateTime.Parse(f[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), WorldKey = f[1], World = f[2], Biome = f[3],
                        Pos = new Vector3(P(f[4]), P(f[5]), P(f[6])), Cause = f[7],
                    };
                    if (f.Length > 8 && f[8].Length > 0) d.Hits.AddRange(f[8].Split(new[] { HitSeparator }, StringSplitOptions.RemoveEmptyEntries));
                    Deaths.Add(d);
                }
                catch (FormatException) { }
            }
        }

        private static void Save() =>
            File.WriteAllLines(FilePath, Deaths.Select(d => string.Join("\t", d.When.ToString("o", CultureInfo.InvariantCulture), T(d.WorldKey), T(d.World), T(d.Biome),
                S(d.Pos.x), S(d.Pos.y), S(d.Pos.z), T(d.Cause), string.Join(HitSeparator, d.Hits.Select(T)))));

        private static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static string S(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string T(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ');

        // ----- menu -----

        private static GUIStyle _rich;
        private static GUISkin _styleSkin;
        private static Death _open, _openWanted;   // the death whose hits are shown
        private static bool _clearArmed, _clearNow;
        private static float _clearArmedAt;

        public static void DrawMenu()
        {
            if (_rich == null || _styleSkin != GUI.skin) { _styleSkin = GUI.skin; _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true }; }
            // Changes to what is listed take effect on the layout pass, so one frame's passes draw the same rows.
            if (Event.current.type == EventType.Layout)
            {
                _open = _openWanted;
                if (_clearNow) { _clearNow = false; Deaths.Clear(); _open = _openWanted = null; Save(); }
            }

            GUILayout.Space(6f);
            GUILayout.Label("<b>Death log</b>", _rich);
            var enabled = VanillaPlusPlugin.DeathLogEnabled;
            bool on = GUILayout.Toggle(enabled.Value, "   Record how I die (cause, last hits, history)");
            if (on != enabled.Value) enabled.Value = on;

            if (Deaths.Count == 0) { GUILayout.Label("   <color=#999999>No deaths recorded.</color>", _rich); return; }

            string here = WorldInfo.WorldKey();
            for (int i = 0; i < Deaths.Count && i < ShownDeaths; i++)
            {
                var d = Deaths[i];
                GUILayout.Label($"   <b>{d.When:yyyy-MM-dd HH:mm}</b>  <color=#999999>{d.World}, {d.Biome}</color>\n   {d.Cause}", _rich);
                GUILayout.BeginHorizontal();
                GUILayout.Space(14f);
                if (GUILayout.Button(_open == d ? "Hide hits" : $"Last hits ({d.Hits.Count})", GUILayout.Width(120f))) _openWanted = _open == d ? null : d;
                bool sameWorld = d.WorldKey == here && Minimap.instance != null;
                bool was = GUI.enabled;
                GUI.enabled = was && sameWorld;
                if (GUILayout.Button("Map", GUILayout.Width(60f)) && sameWorld) { MenuWindow.IsOpen = false; Minimap.instance.ShowPointOnMap(d.Pos); }
                GUI.enabled = was;
                if (GUILayout.Button("Copy", GUILayout.Width(60f)))
                {
                    GUIUtility.systemCopyBuffer = Text(d);
                    Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, "Death details copied");
                }
                GUILayout.EndHorizontal();
                if (_open == d)
                {
                    if (d.Hits.Count == 0) GUILayout.Label("      <color=#999999>No hits were seen in the 30 s before.</color>", _rich);
                    foreach (string h in d.Hits) GUILayout.Label("      " + h, _rich);
                }
            }
            if (Deaths.Count > ShownDeaths) GUILayout.Label($"   <color=#999999>{Deaths.Count - ShownDeaths} older ones are in the file.</color>", _rich);

            // Clearing asks once more: the second click within 4 s empties the history.
            bool armed = _clearArmed && Time.unscaledTime - _clearArmedAt < 4f;
            if (GUILayout.Button(armed ? "Really clear the death history?" : "Clear death history", GUILayout.Width(240f)))
            {
                if (armed) { _clearNow = true; _clearArmed = false; }
                else { _clearArmed = true; _clearArmedAt = Time.unscaledTime; }
            }
        }

        private static string Text(Death d) =>
            $"{d.When:yyyy-MM-dd HH:mm}  {d.World}, {d.Biome}  ({d.Pos.x:0}, {d.Pos.y:0}, {d.Pos.z:0})\n{d.Cause}"
            + (d.Hits.Count > 0 ? "\n" + string.Join("\n", d.Hits) : "");
    }

    // ApplyDamage receives the final hit (after armor, block and resistances) just before health drops.
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class DeathLog_Hit
    {
        private static void Prefix(Character __instance, HitData hit) => DeathLog.Record(__instance, hit);
    }

    // RPC_Damage is where a hit lands on the client that owns the target, with its poison / fire part
    // still on it (it is split off into a lingering effect before ApplyDamage).
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class DeathLog_Source
    {
        private static void Prefix(Character __instance, HitData hit) => DeathLog.NoteSource(__instance, hit);
    }

    // The status effects that deal damage of their own: while one updates, hits are credited to it.
    // (Bukeperry puking is SE_Puke, which runs its damage through SE_Stats.)
    [HarmonyPatch(typeof(SE_Stats), nameof(SE_Stats.UpdateStatusEffect))]
    internal static class DeathLog_EffectStats
    {
        private static void Prefix(StatusEffect __instance) => DeathLog.EnterEffect(__instance);
        private static void Finalizer() => DeathLog.LeaveEffect();
    }

    [HarmonyPatch(typeof(SE_Burning), nameof(SE_Burning.UpdateStatusEffect))]
    internal static class DeathLog_EffectBurning
    {
        private static void Prefix(StatusEffect __instance) => DeathLog.EnterEffect(__instance);
        private static void Finalizer() => DeathLog.LeaveEffect();
    }

    [HarmonyPatch(typeof(SE_Poison), nameof(SE_Poison.UpdateStatusEffect))]
    internal static class DeathLog_EffectPoison
    {
        private static void Prefix(StatusEffect __instance) => DeathLog.EnterEffect(__instance);
        private static void Finalizer() => DeathLog.LeaveEffect();
    }

    [HarmonyPatch(typeof(SE_Smoke), nameof(SE_Smoke.UpdateStatusEffect))]
    internal static class DeathLog_EffectSmoke
    {
        private static void Prefix(StatusEffect __instance) => DeathLog.EnterEffect(__instance);
        private static void Finalizer() => DeathLog.LeaveEffect();
    }

    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class DeathLog_Death
    {
        private static void Prefix(Player __instance) => DeathLog.OnDeath(__instance);
    }
}
