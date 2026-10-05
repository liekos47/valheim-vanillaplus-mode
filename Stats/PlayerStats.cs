using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // "Stats" menu tab: your character's numbers, worked out with the game's own formulas from your
    // food, gear, skills and active effects — health / stamina / eitr and their regen per second,
    // armor, resistances, food timers, weapon damage per hit (skill range + buffs), stamina per swing,
    // block power, plus measured combat numbers (swings / hits per second, damage per second) from
    // your actual fighting. Read-only: nothing is changed.
    internal static class PlayerStats
    {
        // ----- measured combat (fed by the patches below) -----
        private static readonly Queue<float> Swings = new Queue<float>();
        private static readonly Queue<(float t, float dmg)> Hits = new Queue<(float, float)>();
        private static float _fightStart = -1f, _lastAction = -99f, _fightDamage, _biggest;
        private static int _fightSwings, _fightHits;
        private const float FightGap = 5f; // no swing / hit for this long = the fight is over

        internal static void OnSwing()
        {
            float now = Time.time;
            if (now - _lastAction > FightGap) ResetFight(now);
            _lastAction = now;
            _fightSwings++;
            Swings.Enqueue(now);
        }

        internal static void OnHit(float damage)
        {
            float now = Time.time;
            if (now - _lastAction > FightGap) ResetFight(now);
            _lastAction = now;
            _fightHits++;
            _fightDamage += damage;
            _biggest = Mathf.Max(_biggest, damage);
            Hits.Enqueue((now, damage));
        }

        private static void ResetFight(float now)
        {
            _fightStart = now; _fightDamage = 0f; _fightSwings = 0; _fightHits = 0; _biggest = 0f;
        }

        // ----- drawing -----
        private static GUIStyle _rich;
        private static GUISkin _styleSkin;

        public static void Draw(Player p)
        {
            if (p == null) { GUILayout.Label("Not in a world"); return; }
            if (_rich == null || _styleSkin != GUI.skin) { _styleSkin = GUI.skin; _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true }; }
            var seman = p.GetSEMan();

            // Vitals
            Section("Vitals");
            float maxStam = p.GetMaxStamina(), maxEitr = p.GetMaxEitr();
            Row("Health", $"{p.GetHealth():0} / {p.GetMaxHealth():0}");
            Row("Stamina", $"{p.GetStamina():0} / {maxStam:0}");
            if (maxEitr > 0f) Row("Eitr", $"{p.GetEitr():0} / {maxEitr:0}");
            Row("Armor", $"{p.GetBodyArmor():0}");
            float move = p.GetEquipmentMovementModifier();
            Row("Movement from gear", Pct(move));
            float weight = p.GetInventory().GetTotalWeight(), maxWeight = p.GetMaxCarryWeight();
            Row("Carry weight", $"{weight:0} / {maxWeight:0}" + (weight > maxWeight ? "  <color=#ff6060>encumbered</color>" : ""));

            // Regen
            Section("Regeneration");
            var foods = p.GetFoods();
            float foodRegen = foods.Sum(f => f.m_item.m_shared.m_foodRegen);
            float hpMult = 1f; seman.ModifyHealthRegen(ref hpMult);
            Row("Health", foodRegen > 0f
                ? $"{foodRegen * hpMult / 10f:0.0} HP/s  <color=#999999>({foodRegen * hpMult:0.#} every 10 s from food{MultNote(hpMult)})</color>"
                : "0 HP/s  <color=#999999>(no food with regen)</color>");
            float stMult = 1f; seman.ModifyStaminaRegen(ref stMult);
            float stRate = Game.m_staminaRegenRate;
            float stFull = p.m_staminaRegen * stMult * stRate;
            float stEmpty = (p.m_staminaRegen + p.m_staminaRegen * p.m_staminaRegenTimeMultiplier) * stMult * stRate;
            float stNow = (p.m_staminaRegen + (1f - p.GetStamina() / Mathf.Max(1f, maxStam)) * p.m_staminaRegen * p.m_staminaRegenTimeMultiplier) * stMult * stRate;
            Row("Stamina", $"{stNow:0.0}/s now  <color=#999999>({stFull:0.0} near full - {stEmpty:0.0} when empty, starts {p.m_staminaRegenDelay:0.#} s after use{MultNote(stMult)})</color>");
            if (maxEitr > 0f)
            {
                float eMult = 1f; seman.ModifyEitrRegen(ref eMult); eMult += p.GetEquipmentEitrRegenModifier();
                float eNow = (p.m_eiterRegen + (1f - p.GetEitr() / maxEitr) * p.m_eiterRegen) * eMult;
                Row("Eitr", $"{eNow:0.0}/s now  <color=#999999>({p.m_eiterRegen * eMult:0.0} - {p.m_eiterRegen * 2f * eMult:0.0}{MultNote(eMult)})</color>");
            }

            // Food
            Section("Food");
            if (foods.Count == 0) Lbl("<color=#999999>No food eaten</color>");
            foreach (var f in foods)
            {
                var s = f.m_item.m_shared;
                string left = $"{(int)f.m_time / 60}:{(int)f.m_time % 60:00}";
                string again = f.CanEatAgain() ? "  <color=#8cff59>can eat again</color>" : "";
                Row(Loc(s.m_name), $"+{f.m_health:0} HP, +{f.m_stamina:0} stam" + (f.m_eitr > 0f ? $", +{f.m_eitr:0} eitr" : "")
                    + (s.m_foodRegen > 0f ? $", {s.m_foodRegen:0.#} regen" : "") + $"  <color=#999999>{left} left</color>{again}");
            }

            // Weapon
            var w = p.GetCurrentWeapon();
            Section("Weapon");
            if (w == null) Lbl("<color=#999999>Nothing equipped</color>");
            else DrawWeapon(p, w, maxStam);

            // Measured combat
            Section("Combat (measured from your fighting)");
            float now = Time.time;
            while (Swings.Count > 0 && now - Swings.Peek() > 10f) Swings.Dequeue();
            while (Hits.Count > 0 && now - Hits.Peek().t > 10f) Hits.Dequeue();
            if (_fightStart < 0f) Lbl("<color=#999999>Hit something to measure swings / hits / damage per second.</color>");
            else
            {
                bool active = now - _lastAction <= FightGap;
                float dur = Mathf.Max(1f, _lastAction - _fightStart + (active ? now - _lastAction : 0f));
                Row(active ? "Current fight" : "Last fight", $"{dur:0.0} s, {_fightSwings} swings, {_fightHits} hits");
                Row("Swings per second", $"{_fightSwings / dur:0.00}");
                Row("Hits per second", $"{_fightHits / dur:0.00}  <color=#999999>(one swing can hit several targets)</color>");
                Row("Damage per second", $"{_fightDamage / dur:0.0}  <color=#999999>(sent, before the target's resistances)</color>");
                Row("Total / biggest hit", $"{_fightDamage:0} / {_biggest:0}");
                Row("Last 10 s", $"{Swings.Count / 10f:0.00} swings/s, {Hits.Sum(h => h.dmg) / 10f:0.0} DPS");
            }

            // Resistances
            Section("Resistances (gear + effects)");
            var mods = p.GetDamageModifiers();
            var types = new[] { HitData.DamageType.Blunt, HitData.DamageType.Slash, HitData.DamageType.Pierce, HitData.DamageType.Fire,
                                HitData.DamageType.Frost, HitData.DamageType.Lightning, HitData.DamageType.Poison, HitData.DamageType.Spirit };
            bool any = false;
            foreach (var t in types)
            {
                var m = mods.GetModifier(t);
                if (m == HitData.DamageModifier.Normal) continue;
                any = true;
                Row(t.ToString(), ModText(m));
            }
            if (!any) Lbl("<color=#999999>No resistances or weaknesses</color>");

            // Effects
            Section("Active effects");
            var effects = seman.GetStatusEffects();
            if (effects.Count == 0) Lbl("<color=#999999>None</color>");
            foreach (var e in effects)
            {
                string name = Loc(e.m_name);
                if (string.IsNullOrEmpty(name)) name = e.name;
                string time = e.m_ttl > 0f ? $"{(int)e.GetRemaningTime() / 60}:{(int)e.GetRemaningTime() % 60:00} left" : "";
                string tip = FirstLine(Loc(e.GetTooltipString()));
                Row(name, $"<color=#999999>{time}</color>  {tip}");
            }
        }

        private static void DrawWeapon(Player p, ItemDrop.ItemData w, float maxStam)
        {
            var s = w.m_shared;
            Row(Loc(s.m_name), $"lvl {w.m_quality}" + (s.m_useDurability ? $", durability {w.m_durability:0}/{w.GetMaxDurability():0}" : ""));

            var dmg = w.GetDamage();
            var ammo = s.m_itemType == ItemDrop.ItemData.ItemType.Bow ? p.GetAmmoItem() : null;
            if (ammo != null) dmg.Add(ammo.GetDamage());
            Row("Damage types", DamageList(dmg) + (ammo != null ? $"  <color=#999999>(incl. {Loc(ammo.m_shared.m_name)})</color>" : ""));

            // Skill range (the game rolls lerp(0.4, 1, skill) +/- 0.15) and your attack buffs.
            float sf = p.GetSkillFactor(s.m_skillType);
            float mid = Mathf.Lerp(0.4f, 1f, sf), lo = Mathf.Clamp01(mid - 0.15f), hi = Mathf.Clamp01(mid + 0.15f);
            var hit = new HitData { m_damage = dmg };
            p.GetSEMan().ModifyAttack(s.m_skillType, ref hit);
            float total = hit.m_damage.GetTotalDamage();
            float buff = dmg.GetTotalDamage() > 0f ? total / dmg.GetTotalDamage() : 1f;
            Row("Damage per hit", $"{total * lo:0} - {total * hi:0}  <color=#999999>(avg {total * mid:0}; {s.m_skillType} skill {sf * 100f:0}"
                + (Mathf.Abs(buff - 1f) > 0.01f ? $", buffs x{buff:0.00}" : "") + ")</color>");
            if (s.m_backstabBonus > 1f) Row("Sneak attack", $"x{s.m_backstabBonus:0.#}");
            if (s.m_attackForce > 0f) Row("Knockback", $"{s.m_attackForce:0}");

            // Stamina per swing, as Attack.GetAttackStamina does it.
            float Cost(Attack a)
            {
                if (a == null || a.m_attackStamina <= 0f) return 0f;
                float c = a.m_attackStamina * (1f + p.GetEquipmentAttackStaminaModifier());
                p.GetSEMan().ModifyAttackStaminaUsage(c, ref c);
                return c - c * 0.33f * sf;
            }
            float c1 = Cost(s.m_attack), c2 = Cost(s.m_secondaryAttack);
            if (c1 > 0f) Row("Stamina per attack", $"{c1:0.#}  <color=#999999>({maxStam / c1:0.#} attacks from full)</color>");
            if (c2 > 0f) Row("Stamina per special", $"{c2:0.#}");
            if (s.m_attack != null && s.m_attack.m_attackEitr > 0f) Row("Eitr per attack", $"{s.m_attack.m_attackEitr:0.#}");

            // Blocking (weapon or shield in the other hand).
            var blocker = p.GetInventory().GetEquippedItems().FirstOrDefault(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield) ?? w;
            float bsf = p.GetSkillFactor(Skills.SkillType.Blocking);
            float block = blocker.GetBlockPower(bsf);
            if (block > 0f)
                Row("Block", $"{block:0} ({Loc(blocker.m_shared.m_name)}), parry x{blocker.m_shared.m_timedBlockBonus:0.#}, deflection {blocker.GetDeflectionForce():0}");
        }

        // ----- helpers -----
        private static void Section(string title)
        {
            GUILayout.Space(6f);
            GUILayout.Label($"<b>{title}</b>", _rich);
        }

        private static void Row(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   " + label, _rich, GUILayout.Width(170f));
            GUILayout.Label(value, _rich);
            GUILayout.EndHorizontal();
        }

        private static void Lbl(string text) => GUILayout.Label("   " + text, _rich);

        private static string Loc(string s) => string.IsNullOrEmpty(s) ? "" : Localization.instance.Localize(s);
        private static string Pct(float f) => f == 0f ? "0%" : $"{(f > 0f ? "+" : "")}{f * 100f:0}%";
        private static string MultNote(float m) => Mathf.Abs(m - 1f) > 0.01f ? $", effects x{m:0.00}" : "";
        private static string FirstLine(string s) => string.IsNullOrEmpty(s) ? "" : s.Split('\n')[0].Trim();

        private static string DamageList(HitData.DamageTypes d)
        {
            var parts = new List<string>();
            void Add(string n, float v) { if (v > 0f) parts.Add($"{n} {v:0}"); }
            Add("true", d.m_damage); Add("blunt", d.m_blunt); Add("slash", d.m_slash); Add("pierce", d.m_pierce);
            Add("chop", d.m_chop); Add("pickaxe", d.m_pickaxe); Add("fire", d.m_fire); Add("frost", d.m_frost);
            Add("lightning", d.m_lightning); Add("poison", d.m_poison); Add("spirit", d.m_spirit);
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }

        private static string ModText(HitData.DamageModifier m)
        {
            switch (m)
            {
                case HitData.DamageModifier.Resistant: return "<color=#8cff59>resistant (-50%)</color>";
                case HitData.DamageModifier.VeryResistant: return "<color=#8cff59>very resistant (-75%)</color>";
                case HitData.DamageModifier.SlightlyResistant: return "<color=#8cff59>slightly resistant (-25%)</color>";
                case HitData.DamageModifier.Immune: return "<color=#4dccff>immune</color>";
                case HitData.DamageModifier.Ignore: return "<color=#4dccff>ignored</color>";
                case HitData.DamageModifier.Weak: return "<color=#ff6060>weak (+50%)</color>";
                case HitData.DamageModifier.VeryWeak: return "<color=#ff6060>very weak (+100%)</color>";
                case HitData.DamageModifier.SlightlyWeak: return "<color=#ff6060>slightly weak (+25%)</color>";
                default: return m.ToString();
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class PlayerStats_Swing
    {
        private static void Postfix(Humanoid __instance, bool __result)
        {
            if (__result && VanillaPlusPlugin.IsLocalPlayer(__instance)) PlayerStats.OnSwing();
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class PlayerStats_Hit
    {
        [HarmonyPriority(Priority.Last)] // after other boosts, so the number is what's actually sent
        private static void Prefix(Character __instance, HitData hit)
        {
            if (hit != null && !VanillaPlusPlugin.IsLocalPlayer(__instance) && VanillaPlusPlugin.IsLocalPlayer(hit.GetAttacker()))
                PlayerStats.OnHit(hit.GetTotalDamage());
        }
    }
}
