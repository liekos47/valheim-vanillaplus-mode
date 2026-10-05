using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace ValheimVanillaPlus
{
    internal class Waypoint
    {
        public string World, Name;
        public Vector3 Pos;
        public Minimap.PinData Pin; // runtime only
    }

    // Waypoints: named spots per world, shown on screen (name + distance, through walls) and
    // optionally as map pins, with Show on map / Rename / Delete in the menu. A "Last death"
    // waypoint can be added automatically. Stored only in a small file on your PC
    // (BepInEx/config/local.valheimvanillaplus.waypoints.txt); map pins are added with save = false,
    // so nothing goes into your character or the world. There is no teleport.
    internal static class Waypoints
    {
        public static readonly List<Waypoint> All = new List<Waypoint>();
        private static string FilePath => Path.Combine(Paths.ConfigPath, "local.valheimvanillaplus.waypoints.txt");
        private const string DeathName = "Last death";
        private static GUIStyle _label;

        public static IEnumerable<Waypoint> ForThisWorld()
        {
            string w = WorldInfo.WorldKey();
            return All.Where(x => x.World == w);
        }

        // ----- file -----

        public static void Load()
        {
            All.Clear();
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var f = line.Split('\t');
                if (f.Length < 5) continue;
                try { All.Add(new Waypoint { World = f[0], Name = f[1], Pos = new Vector3(P(f[2]), P(f[3]), P(f[4])) }); }
                catch (FormatException) { }
            }
        }

        public static void Save() =>
            File.WriteAllLines(FilePath, All.Select(x => string.Join("\t", x.World, (x.Name ?? "").Replace("\t", " "),
                S(x.Pos.x), S(x.Pos.y), S(x.Pos.z))));

        private static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static string S(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        // ----- actions -----

        public static void AddHere(Player p)
        {
            var w = new Waypoint { World = WorldInfo.WorldKey(), Name = $"Waypoint {ForThisWorld().Count() + 1}", Pos = p.transform.position };
            All.Add(w);
            Save();
            Rename(w);
        }

        public static void Rename(Waypoint w) =>
            TextPrompt.Show("Waypoint name", w.Name, v => { if (v.Length > 0) { w.Name = v; RemovePin(w); Save(); } });

        public static void Delete(Waypoint w) { RemovePin(w); All.Remove(w); Save(); }

        public static void ShowOnMap(Waypoint w)
        {
            if (Minimap.instance == null) return;
            MenuWindow.IsOpen = false;
            Minimap.instance.ShowPointOnMap(w.Pos);
        }

        internal static void OnDeath(Player p)
        {
            if (!VanillaPlusPlugin.WaypointsOn || !VanillaPlusPlugin.WaypointDeath.Value || p != Player.m_localPlayer) return;
            string world = WorldInfo.WorldKey();
            var old = All.FirstOrDefault(x => x.World == world && x.Name == DeathName);
            if (old != null) Delete(old);
            All.Add(new Waypoint { World = world, Name = DeathName, Pos = p.transform.position });
            Save();
        }

        // ----- map pins (not saved) -----

        public static void UpdatePins()
        {
            var map = Minimap.instance;
            if (map == null) return;
            bool on = VanillaPlusPlugin.WaypointsOn && VanillaPlusPlugin.WaypointMapPins.Value;
            string world = WorldInfo.WorldKey();
            foreach (var w in All)
            {
                bool want = on && w.World == world;
                if (want && w.Pin == null)
                    w.Pin = map.AddPin(w.Pos, w.Name == DeathName ? Minimap.PinType.Death : Minimap.PinType.Icon3, w.Name, false, false);
                else if (!want && w.Pin != null) RemovePin(w);
            }
        }

        private static void RemovePin(Waypoint w)
        {
            if (w.Pin != null && Minimap.instance != null) Minimap.instance.RemovePin(w.Pin);
            w.Pin = null;
        }

        // Hot reload: the next build adds its own pins.
        public static void Cleanup()
        {
            foreach (var w in All) RemovePin(w);
        }

        // ----- on-screen markers -----

        public static void Draw()
        {
            if (!VanillaPlusPlugin.WaypointsOn || !VanillaPlusPlugin.WaypointOnScreen.Value) return;
            var cam = Camera.main;
            var p = Player.m_localPlayer;
            if (cam == null || p == null) return;
            if (_label == null) _label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold };

            float maxDist = VanillaPlusPlugin.WaypointRange.Value;
            var color = new Color(1f, 0.85f, 0.2f);
            foreach (var w in ForThisWorld())
            {
                float d = Vector3.Distance(p.transform.position, w.Pos);
                if (maxDist > 0f && d > maxDist) continue;
                Vector3 sp = cam.WorldToScreenPoint(w.Pos + Vector3.up * 1.5f);
                if (sp.z <= 0f) continue;
                float x = sp.x, y = Screen.height - sp.y;
                GUI.color = color;
                GUI.DrawTexture(new Rect(x - 4f, y - 4f, 8f, 8f), Texture2D.whiteTexture);
                GUI.color = Color.black;
                GUI.Label(new Rect(x - 149f, y - 25f, 300f, 20f), $"{w.Name} [{d:0}m]", _label);
                GUI.color = color;
                GUI.Label(new Rect(x - 150f, y - 26f, 300f, 20f), $"{w.Name} [{d:0}m]", _label);
                GUI.color = Color.white;
            }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class Waypoints_Death
    {
        private static void Prefix(Player __instance) => Waypoints.OnDeath(__instance);
    }
}
