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
    internal class Waypoint
    {
        public string World, Name;
        public Vector3 Pos;
        public Minimap.PinType Icon = Minimap.PinType.Icon3; // one of the game's five map pin icons
        public Minimap.PinData Pin; // runtime only
    }

    // Waypoints: named spots per world, shown on screen (the pin's icon + name + distance, through
    // walls) and optionally as map pins, with icon / Show on map / Rename / Delete in the menu. Each
    // waypoint uses one of the game's own five map pin icons. A "Last death" waypoint can be added
    // automatically. Stored only in a small file on your PC
    // (BepInEx/config/liekos47.valheimvanillaplus.waypoints.txt); map pins are added with save = false,
    // so nothing goes into your character or the world. There is no teleport.
    //
    // The pins you place on the game's own map can get the same on-screen markers ([Waypoints]
    // MapPinMarkers): those are only read, never changed; cross a pin out on the map to hide its marker.
    internal static class Waypoints
    {
        public static readonly List<Waypoint> All = new List<Waypoint>();
        internal static string FilePath => Path.Combine(Paths.ConfigPath, "liekos47.valheimvanillaplus.waypoints.txt");
        private const string DeathName = "Last death";
        private static GUIStyle _label;

        // The five icons the game lets you place on the map, in its own order.
        internal static readonly Minimap.PinType[] Icons =
            { Minimap.PinType.Icon0, Minimap.PinType.Icon1, Minimap.PinType.Icon2, Minimap.PinType.Icon3, Minimap.PinType.Icon4 };

        public static IEnumerable<Waypoint> ForThisWorld()
        {
            string w = WorldInfo.WorldKey();
            return All.Where(x => x.World == w);
        }

        public static bool IsDeath(Waypoint w) => w.Name == DeathName;

        // ----- file -----

        public static void Load()
        {
            All.Clear();
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var f = line.Split('\t');
                if (f.Length < 5) continue;
                try
                {
                    var w = new Waypoint { World = f[0], Name = f[1], Pos = new Vector3(P(f[2]), P(f[3]), P(f[4])) };
                    // The icon column came later: older lines have none and keep the default.
                    if (f.Length >= 6 && Enum.TryParse(f[5], out Minimap.PinType icon) && Array.IndexOf(Icons, icon) >= 0) w.Icon = icon;
                    All.Add(w);
                }
                catch (FormatException) { }
            }
        }

        // The file was replaced (config backup imported): drop the old pins and read it again.
        public static void Reload()
        {
            Cleanup();
            Load();
        }

        public static void Save() =>
            File.WriteAllLines(FilePath, All.Select(x => string.Join("\t", x.World, (x.Name ?? "").Replace("\t", " "),
                S(x.Pos.x), S(x.Pos.y), S(x.Pos.z), x.Icon.ToString())));

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

        // Next of the game's five pin icons.
        public static void NextIcon(Waypoint w)
        {
            int i = Array.IndexOf(Icons, w.Icon);
            w.Icon = Icons[(i + 1) % Icons.Length];
            RemovePin(w); // re-added with the new icon by UpdatePins
            Save();
        }

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

        private static Minimap.PinType PinTypeOf(Waypoint w) => IsDeath(w) ? Minimap.PinType.Death : w.Icon;

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
                    w.Pin = map.AddPin(w.Pos, PinTypeOf(w), w.Name, false, false);
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

        // The game's picture for a pin icon (null before the map exists).
        public static Sprite PinSprite(Minimap.PinType type)
        {
            var map = Minimap.instance;
            if (map == null || map.m_icons == null) return null;
            foreach (var data in map.m_icons)
                if (data.m_name == type) return data.m_icon;
            return null;
        }

        // ----- the pins you place on the game's own map -----

        private static readonly AccessTools.FieldRef<Minimap, List<Minimap.PinData>> MapPins =
            AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
        private static readonly Dictionary<Minimap.PinData, float> PinHeights = new Dictionary<Minimap.PinData, float>();
        private static readonly HashSet<Minimap.PinType> MarkerIcons = new HashSet<Minimap.PinType>();
        private static string _markerIconsFor;
        private static Minimap _heightsFor;

        // Which pin icons get a marker ([Waypoints] MapPinMarkerIcons).
        public static bool MarkerIcon(Minimap.PinType type)
        {
            string list = VanillaPlusPlugin.WaypointPinMarkerIcons.Value ?? "";
            if (list != _markerIconsFor)
            {
                _markerIconsFor = list;
                MarkerIcons.Clear();
                foreach (string part in list.Split(','))
                    if (Enum.TryParse(part.Trim(), out Minimap.PinType t)) MarkerIcons.Add(t);
            }
            return MarkerIcons.Contains(type);
        }

        public static void ToggleMarkerIcon(Minimap.PinType type)
        {
            var on = Icons.Where(t => MarkerIcon(t) != (t == type)).Select(t => t.ToString());
            VanillaPlusPlugin.WaypointPinMarkerIcons.Value = string.Join(", ", on);
        }

        // Your own pins that get a marker: placed by you (saved with your character), one of the five
        // icons, that icon ticked, and not crossed out. Pins shared from a cartography table, the
        // game's own markers (bed, death, bosses) and this mod's waypoint pins are left out.
        private static bool IsMarkerPin(Minimap.PinData pin) =>
            pin != null && pin.m_save && !pin.m_checked && pin.m_ownerID == 0L && Array.IndexOf(Icons, pin.m_type) >= 0 && MarkerIcon(pin.m_type);

        public static int MarkerPinCount()
        {
            var map = Minimap.instance;
            var pins = map != null ? MapPins(map) : null;
            if (pins == null) return 0;
            int n = 0;
            foreach (var pin in pins) if (IsMarkerPin(pin)) n++;
            return n;
        }

        // Map pins have no height of their own: use the terrain there, or the sea surface.
        private static float HeightAt(Minimap.PinData pin)
        {
            if (!PinHeights.TryGetValue(pin, out float y))
            {
                if (PinHeights.Count > 2000) PinHeights.Clear();
                y = pin.m_pos.y;
                if (Mathf.Abs(y) < 0.01f && WorldGenerator.instance != null)
                    y = Mathf.Max(WorldGenerator.instance.GetHeight(pin.m_pos.x, pin.m_pos.z), 30f);
                PinHeights[pin] = y;
            }
            return y;
        }

        // ----- on-screen markers -----

        public static void Draw()
        {
            if (!VanillaPlusPlugin.WaypointsOn) return;
            var cam = Camera.main;
            var p = Player.m_localPlayer;
            if (cam == null || p == null) return;
            if (_label == null) _label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold };

            float maxDist = VanillaPlusPlugin.WaypointRange.Value;
            Vector3 me = p.transform.position;

            if (VanillaPlusPlugin.WaypointOnScreen.Value)
                foreach (var w in ForThisWorld())
                    Marker(cam, me, w.Pos + Vector3.up * 1.5f, w.Name, PinSprite(PinTypeOf(w)), maxDist);

            if (VanillaPlusPlugin.WaypointPinMarkers.Value)
            {
                var map = Minimap.instance;
                var pins = map != null ? MapPins(map) : null;
                if (pins == null) return;
                if (_heightsFor != map) { _heightsFor = map; PinHeights.Clear(); }
                foreach (var pin in pins)
                {
                    if (!IsMarkerPin(pin)) continue;
                    var pos = new Vector3(pin.m_pos.x, HeightAt(pin) + 1.5f, pin.m_pos.z);
                    Marker(cam, me, pos, pin.m_name, pin.m_icon != null ? pin.m_icon : PinSprite(pin.m_type), maxDist);
                }
            }
        }

        private static void Marker(Camera cam, Vector3 me, Vector3 pos, string name, Sprite icon, float maxDist)
        {
            float d = Vector3.Distance(me, pos);
            if (maxDist > 0f && d > maxDist) return;
            Vector3 sp = cam.WorldToScreenPoint(pos);
            if (sp.z <= 0f) return;
            float x = sp.x, y = Screen.height - sp.y;
            if (x < -150f || x > Screen.width + 150f || y < -40f || y > Screen.height + 40f) return;

            var color = new Color(1f, 0.85f, 0.2f);
            if (icon != null && icon.texture != null)
            {
                var tr = icon.textureRect; var tex = icon.texture;
                GUI.color = Color.white;
                GUI.DrawTextureWithTexCoords(new Rect(x - 11f, y - 11f, 22f, 22f), tex,
                    new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
            }
            else
            {
                GUI.color = color;
                GUI.DrawTexture(new Rect(x - 4f, y - 4f, 8f, 8f), Texture2D.whiteTexture);
            }
            string text = string.IsNullOrEmpty(name) ? $"[{d:0}m]" : $"{name} [{d:0}m]";
            GUI.color = Color.black;
            GUI.Label(new Rect(x - 149f, y - 31f, 300f, 20f), text, _label);
            GUI.color = color;
            GUI.Label(new Rect(x - 150f, y - 32f, 300f, 20f), text, _label);
            GUI.color = Color.white;
        }
    }

    [HarmonyPatch(typeof(Player), "OnDeath")]
    internal static class Waypoints_Death
    {
        private static void Prefix(Player __instance) => Waypoints.OnDeath(__instance);
    }
}
