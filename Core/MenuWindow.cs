using BepInEx.Configuration;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // In-game IMGUI options menu: theme, search and tabs. While open, the cursor is freed and the
    // player ignores movement/attack/look input.
    internal static class MenuWindow
    {
        public static bool IsOpen;

        private const int WindowId = 0x7A91;
        private static Rect _rect = new Rect(40f, 80f, 520f, 0f);
        private static Vector2 _size = new Vector2(520f, 620f); // set with the grip in the bottom-right corner
        private static readonly Vector2 MinSize = new Vector2(440f, 300f);
        private static Vector2 _scroll;
        private static GUIStyle _header;

        private static readonly string[] Tabs = { "Interface", "Crafting", "Items", "Vision", "World", "Stats", "More" };
        private static int _tab;

        private static bool _placed, _moved;
        private static GUISkin _headerSkin;

        public static void Draw()
        {
            if (!IsOpen) { Typing = false; SavePosition(); return; }

            // Start where it was last left (kept on screen if the resolution changed).
            if (!_placed)
            {
                _placed = true;
                _size = new Vector2(Mathf.Max(MinSize.x, VanillaPlusPlugin.MenuWidth.Value), Mathf.Max(MinSize.y, VanillaPlusPlugin.MenuHeight.Value));
                _rect.width = _size.x;
                _rect.x = Mathf.Clamp(VanillaPlusPlugin.MenuX.Value, 0f, Mathf.Max(0f, Screen.width - _rect.width));
                _rect.y = Mathf.Clamp(VanillaPlusPlugin.MenuY.Value, 0f, Mathf.Max(0f, Screen.height - 200f));
            }

            var savedSkin = GUI.skin;
            var theme = MenuTheme.Current(savedSkin);
            if (theme != null) GUI.skin = theme;
            if (_header == null || _headerSkin != GUI.skin)
            {
                _headerSkin = GUI.skin;
                _header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 14 };
            }

            // The size is the one you dragged it to with the corner grip (never taller than the screen
            // allows; the window itself won't go smaller than its contents need).
            var before = _rect.position;
            _rect.width = _size.x;
            _rect.height = Mathf.Min(_size.y, Screen.height - _rect.y - 20f);
            _rect = GUILayout.Window(WindowId, _rect, DrawContents, $"{VanillaPlusPlugin.Name} {VanillaPlusPlugin.Version}");
            if (_rect.position != before) _moved = true;
            if (_moved && Event.current.rawType == EventType.MouseUp) SavePosition();
            GUI.skin = savedSkin;
        }

        // Written once the drag ends (or the menu closes), not every frame of the drag.
        private static void SavePosition()
        {
            if (!_moved) return;
            _moved = false;
            VanillaPlusPlugin.MenuX.Value = Mathf.Round(_rect.x);
            VanillaPlusPlugin.MenuY.Value = Mathf.Round(_rect.y);
        }

        private static void DrawContents(int id)
        {
            if (WindowResize.Handle(WindowId, _rect, ref _size, MinSize, new Vector2(Screen.width - _rect.x - 4f, Screen.height - _rect.y - 4f)))
            {
                VanillaPlusPlugin.MenuWidth.Value = Mathf.Round(_size.x);
                VanillaPlusPlugin.MenuHeight.Value = Mathf.Round(_size.y);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Theme:", GUILayout.Width(55f));
            int themeSel = GUILayout.Toolbar((int)VanillaPlusPlugin.MenuThemeSetting.Value, new[] { "Classic", "Dark", "Light", "Valheim" });
            if (themeSel != (int)VanillaPlusPlugin.MenuThemeSetting.Value) VanillaPlusPlugin.MenuThemeSetting.Value = (MenuThemeMode)themeSel;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(55f));
            GUI.SetNextControlName(SearchControl);
            _search = GUILayout.TextField(_search ?? "", 40);
            if (GUILayout.Button("✕", GUILayout.Width(28f))) { _search = ""; GUI.FocusControl(null); }
            GUILayout.EndHorizontal();
            if (Event.current.type == EventType.Repaint) Typing = GUI.GetNameOfFocusedControl() == SearchControl || HotkeyEditor.Busy;

            if (!Searching)
            {
                int tab = GUILayout.Toolbar(_tab, Tabs);
                if (tab != _tab) { _tab = tab; _scroll = Vector2.zero; }
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (Searching)
            {
                // Every tab, but only the controls whose label or section matches the search.
                for (int i = 0; i < Tabs.Length; i++) DrawTab(i);
            }
            else DrawTab(_tab);
            GUILayout.EndScrollView();

            if (GUILayout.Button($"Close ({VanillaPlusPlugin.MenuKey.Value})")) IsOpen = false;
            WindowResize.Draw(_rect);
            GUI.DragWindow();
        }

        private static void DrawTab(int i)
        {
            _section = Tabs[i];
            switch (i)
            {
                case 0: InterfaceTab(); break;
                case 1: CraftingTab(); break;
                case 2: ItemsTab(); break;
                case 3: VisionTab(); break;
                case 4: WorldTab(); break;
                case 5: if (Show(null)) { PlayerStats.Draw(Player.m_localPlayer); DeathLog.DrawMenu(); } break;
                case 6: MoreTab(); break;
            }
        }

        private static void WorldTab()
        {
            var player = Player.m_localPlayer;
            Header("Map");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its map reveal is used instead of this one.");
            Toggle(VanillaPlusPlugin.RevealMap, "Reveal the whole world map (removes the map fog; not saved)");

            Header("Waypoints");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its waypoints are used instead of these.");
            Toggle(VanillaPlusPlugin.WaypointsEnabled, "Waypoints");
            Toggle(VanillaPlusPlugin.WaypointOnScreen, "   ...show on screen (name + distance)");
            Slider(VanillaPlusPlugin.WaypointRange, "   On-screen range (m, 0 = any)", 0f, 5000f, "0");
            Toggle(VanillaPlusPlugin.WaypointMapPins, "   ...show as map pins (not saved)");
            Toggle(VanillaPlusPlugin.WaypointDeath, "   ...add a 'Last death' waypoint when I die");
            Toggle(VanillaPlusPlugin.WaypointPinMarkers, "   ...also show markers for the pins I place on the map");
            if (Show("pins I place on the map"))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("      for these pin icons:", GUILayout.Width(170f));
                foreach (var icon in Waypoints.Icons)
                    if (IconButton(Waypoints.PinSprite(icon), Waypoints.MarkerIcon(icon), "")) Waypoints.ToggleMarkerIcon(icon);
                GUILayout.Label(VanillaPlusPlugin.WaypointPinMarkers.Value ? $"  {Waypoints.MarkerPinCount()} pins shown" : "");
                GUILayout.EndHorizontal();
                Lbl("      Cross a pin out on the map to hide its marker.");
            }
            if (player != null && Btn("Add waypoint here")) Waypoints.AddHere(player);
            if (player != null && Show(null))
            {
                foreach (var w in new System.Collections.Generic.List<Waypoint>(Waypoints.ForThisWorld()))
                {
                    float d = Vector3.Distance(player.transform.position, w.Pos);
                    GUILayout.BeginHorizontal();
                    // The waypoint's map pin icon: click to switch to the next of the game's five.
                    bool death = Waypoints.IsDeath(w);
                    if (IconButton(Waypoints.PinSprite(death ? Minimap.PinType.Death : w.Icon), false, death ? "" : "Click for the next pin icon") && !death)
                        Waypoints.NextIcon(w);
                    GUILayout.Label($"{w.Name}  ({d:0} m {WorldInfo.Compass(player.transform.position, w.Pos)})", GUILayout.Width(220f));
                    if (GUILayout.Button("Map")) Waypoints.ShowOnMap(w);
                    if (GUILayout.Button("Rename")) Waypoints.Rename(w);
                    if (GUILayout.Button("Delete")) Waypoints.Delete(w);
                    GUILayout.EndHorizontal();
                }
            }
        }

        // A small button showing one of the game's pictures; framed when `on`.
        private static bool IconButton(Sprite sprite, bool on, string tip)
        {
            var r = GUILayoutUtility.GetRect(28f, 26f, GUILayout.Width(28f), GUILayout.Height(26f));
            bool clicked = GUI.Button(r, new GUIContent("", tip));
            if (Event.current.type == EventType.Repaint)
            {
                if (sprite != null && sprite.texture != null)
                {
                    var tr = sprite.textureRect; var tex = sprite.texture;
                    GUI.DrawTextureWithTexCoords(new Rect(r.x + 4f, r.y + 3f, 20f, 20f), tex,
                        new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
                }
                if (on)
                {
                    var old = GUI.color; GUI.color = new Color(0.35f, 1f, 0.4f);
                    var t = Texture2D.whiteTexture;
                    GUI.DrawTexture(new Rect(r.xMin, r.yMin, r.width, 2f), t);
                    GUI.DrawTexture(new Rect(r.xMin, r.yMax - 2f, r.width, 2f), t);
                    GUI.DrawTexture(new Rect(r.xMin, r.yMin, 2f, r.height), t);
                    GUI.DrawTexture(new Rect(r.xMax - 2f, r.yMin, 2f, r.height), t);
                    GUI.color = old;
                }
            }
            return clicked;
        }

        private static void ItemsTab()
        {
            Header("Inventory");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its batch click is used instead of this one.");
            Toggle(VanillaPlusPlugin.BatchTransferEnabled, $"{VanillaPlusPlugin.BatchMoveKey.Value}+click: move all of that item to/from the chest; +Shift: throw all of it out");

            Header("Storage window & Store all");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its storage window is used instead of this one.");
            Toggle(VanillaPlusPlugin.StorageEnabled, $"Storage window ({VanillaPlusPlugin.StorageKey.Value}): all nearby chests in one searchable list");
            Slider(VanillaPlusPlugin.StorageRange, "   Range (m)", 5f, 100f, "0");
            if (Player.m_localPlayer != null && VanillaPlusPlugin.StorageOn && Btn("Open storage window")) StorageWindow.Toggle();
            Toggle(VanillaPlusPlugin.StoreAllButton, "'Store all' button under an open chest");
            Lbl("   'Store everything' and 'Store all' keep equipped items, and:");
            GUILayout.BeginHorizontal();
            Toggle(VanillaPlusPlugin.StorageKeepArmor, "Armor");
            Toggle(VanillaPlusPlugin.StorageKeepWeapons, "Weapons");
            Toggle(VanillaPlusPlugin.StorageKeepTools, "Tools");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Toggle(VanillaPlusPlugin.StorageKeepFood, "Food / potions");
            Toggle(VanillaPlusPlugin.StorageKeepAmmo, "Ammo / bait");
            Toggle(VanillaPlusPlugin.StorageKeepTrophies, "Trophies");
            GUILayout.EndHorizontal();
            Toggle(VanillaPlusPlugin.StorageKeepHotbar, "   Hotbar (top row)");
            Lbl($"   Never store: {(string.IsNullOrEmpty(VanillaPlusPlugin.StorageNeverStore.Value) ? "(none)" : VanillaPlusPlugin.StorageNeverStore.Value)}");
            if (Btn("   Edit never-store list")) ItemPicker.Open("Never store: item names, commas between, * wildcard (e.g. Coins, *Mead*, Wishbone)", VanillaPlusPlugin.StorageNeverStore, new Color(1f, 0.8f, 0.25f), true);

            Header("Repair alert");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its repair alert is used instead of this one.");
            Toggle(VanillaPlusPlugin.RepairAlertEnabled, "Repair alert (equipped gear low / broken)");
            Slider(VanillaPlusPlugin.RepairAlertPercent, "   Warn below %", 5f, 75f, "0");
            Slider(VanillaPlusPlugin.RepairAlertRepeatMinutes, "   Remind every (min, 0 = once)", 0f, 30f, "0");

            Header("Food alert");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its food alert is used instead of this one.");
            Toggle(VanillaPlusPlugin.FoodAlertEnabled, "Food alert (a food is about to run out / has run out)");
            Slider(VanillaPlusPlugin.FoodAlertMinutes, "   Warn below (min)", 0.5f, 10f);
            Toggle(VanillaPlusPlugin.FoodAlertEatAgain, "   ...note when a food can be eaten again");
            Toggle(VanillaPlusPlugin.FoodAlertEmptySlot, "   ...note when a food slot is empty");
            Slider(VanillaPlusPlugin.FoodAlertRepeatMinutes, "   Remind every (min, 0 = once)", 0f, 30f, "0");
            Toggle(VanillaPlusPlugin.FoodAlertQuietWhenSafe, "   ...stay quiet while resting or indoors at a base");

            Header("Auto-pickup filter");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its pickup filter is used instead of this one.");
            Lbl("Which items auto-pickup takes (pressing E still picks up anything):");
            var modes = new[] { "Off", "Whitelist", "Blacklist" };
            int mode = Bar((int)VanillaPlusPlugin.PickupFilterMode.Value, modes);
            if (mode != (int)VanillaPlusPlugin.PickupFilterMode.Value) VanillaPlusPlugin.PickupFilterMode.Value = (PickupFilterMode)mode;
            Lbl(PickupFilter.Summary());
            GUILayout.BeginHorizontal();
            // The text box opens with a grid of item icons beside it: click icons instead of typing names.
            if (Btn("Edit whitelist")) ItemPicker.Open("Pickup whitelist: item names, commas between, * wildcard (Trophy*, *Ore)", VanillaPlusPlugin.PickupWhitelist, new Color(0.35f, 1f, 0.4f), true);
            if (Btn("Edit blacklist")) ItemPicker.Open("Pickup blacklist: item names, commas between, * wildcard (Trophy*, *Ore)", VanillaPlusPlugin.PickupBlacklist, new Color(1f, 0.35f, 0.3f), true);
            GUILayout.EndHorizontal();
        }

        private static void VisionTab()
        {
            Header("Night vision");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its night vision is used instead of this one.");
            Toggle(VanillaPlusPlugin.NightVisionEnabled, $"Night vision ({VanillaPlusPlugin.NightVisionKey.Value})");
            Slider(VanillaPlusPlugin.NightVisionBrightness, "   Brightness", 0.1f, 1f);

            Header("Radar");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its radar is used instead of this one.");
            Toggle(VanillaPlusPlugin.RadarEnabled, "Radar overlay (mobs & players around me)");
            Slider(VanillaPlusPlugin.RadarRange, "   Range (m)", 10f, 200f, "0");
            Slider(VanillaPlusPlugin.RadarSize, "   Size (px)", 100f, 400f, "0");
            Slider(VanillaPlusPlugin.RadarOpacity, "   Background", 0f, 1f, "0.00");
            if (Show(null))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("   Corner:", GUILayout.Width(70f));
                int corner = GUILayout.Toolbar((int)VanillaPlusPlugin.RadarPosition.Value, new[] { "Top left", "Top right", "Bottom left", "Bottom right" });
                if (corner != (int)VanillaPlusPlugin.RadarPosition.Value) VanillaPlusPlugin.RadarPosition.Value = (RadarCorner)corner;
                GUILayout.EndHorizontal();
            }
            Slider(VanillaPlusPlugin.RadarOffsetX, "   Offset from side (px)", 0f, 800f, "0");
            Slider(VanillaPlusPlugin.RadarOffsetY, "   Offset from top / bottom (px)", 0f, 800f, "0");
            Toggle(VanillaPlusPlugin.RadarMobs, "   ...creatures");
            Toggle(VanillaPlusPlugin.RadarPassive, "      ...including passive animals");
            Toggle(VanillaPlusPlugin.RadarPlayers, "   ...players");
            Toggle(VanillaPlusPlugin.RadarNames, "      ...with names");
        }

        private static void CraftingTab()
        {
            Header("Crafting list");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its craft search is used instead of this one.");
            Toggle(VanillaPlusPlugin.CraftSearchEnabled, "Search box on the crafting list (item or ingredient)");

            Header("Chests");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its craft from chests is used instead of this one.");
            Toggle(VanillaPlusPlugin.CraftFromChestsEnabled, "Craft & build from nearby chests");
            if (CraftFromChests.OtherModLoaded && Show(null))
                GUILayout.Label("   <color=#ffb030>CraftFromContainers is also installed - use only one of the two, or the craft list shows wrong states.</color>", new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
            Slider(VanillaPlusPlugin.CraftChestRange, "   Chest range (m)", 5f, 100f, "0");
        }

        private static void InterfaceTab()
        {
            Header("Text box");
            Toggle(VanillaPlusPlugin.TextClearButton, "'Clear' button in the Enter text box");

            Header("FPS");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its FPS counter is used instead of this one.");
            Toggle(VanillaPlusPlugin.ShowFps, "Show FPS on screen (top left)");

            Header("Signs");
            if (Btn("Search nearby signs for text…")) SignSearch.Ask();
            if (VanillaPlusPlugin.SignSearchText.Value.Trim().Length > 0 && Btn($"   Find \"{VanillaPlusPlugin.SignSearchText.Value}\" again")) SignSearch.Run();
            Slider(VanillaPlusPlugin.SignSearchRadius, "   Range (m)", 10f, 150f, "0");
            Slider(VanillaPlusPlugin.SignSearchSeconds, "   Highlight for (s)", 3f, 60f, "0");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its sign editor is used instead of this one.");
            Toggle(VanillaPlusPlugin.SignEditorEnabled, "Sign editor: colors, bold / italic / size, icons while editing a sign");
            IntSlider(VanillaPlusPlugin.SignCharLimit, "   Character limit (game: 50)", 50, 500);
            Toggle(VanillaPlusPlugin.SignCustomIcons, "Item & map pin icons on signs (only seen by players with this mod)");
            if (Btn("   Default sign color: " + (string.IsNullOrEmpty(VanillaPlusPlugin.SignDefaultColor.Value) ? "game default" : VanillaPlusPlugin.SignDefaultColor.Value) + "  (edit)"))
                ColorPicker.Open("Default sign color: hex (FFFFFF) or name (white, red, ...). Empty = game default", VanillaPlusPlugin.SignDefaultColor, "game default");
        }

        private static void MoreTab()
        {
            Header("General");
            Toggle(VanillaPlusPlugin.RangePreviewEnabled, "Show a circle on the ground when dragging a range slider");
            if (Show("Hotkeys")) HotkeyEditor.Draw(VanillaPlusPlugin.Settings);

            // The server sends its world's name, seed and generator version to every client on join
            // (ZNet.RPC_PeerInfo), because the client builds the terrain itself. Nothing is guessed.
            Header("World seed");
            var world = ZNet.World;
            if (Show("World seed") && world != null)
            {
                GUILayout.Label($"World: {world.m_name}    Seed: {world.m_seedName}");
                GUILayout.Label($"Seed number: {world.m_seed}    Generator version: {world.m_worldGenVersion}");
                if (Btn("Copy seed to clipboard"))
                {
                    GUIUtility.systemCopyBuffer = world.m_seedName;
                    Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"Seed copied: {world.m_seedName}");
                    VanillaPlusPlugin.Log.LogInfo($"World seed: '{world.m_name}' seed {world.m_seedName} ({world.m_seed}), generator version {world.m_worldGenVersion}");
                }
            }

            Header("Auto reconnect");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its auto reconnect is used instead of this one.");
            Toggle(VanillaPlusPlugin.AutoReconnectEnabled, "Auto reconnect after disconnect");
            Slider(VanillaPlusPlugin.AutoReconnectDelay, "   Delay (s)", 3f, 120f, "0");
            IntSlider(VanillaPlusPlugin.AutoReconnectMaxAttempts, "   Max attempts", 1, 50);

            Header("Settings file");
            GUILayout.BeginHorizontal();
            if (Btn("Open config file")) VanillaPlusPlugin.OpenConfigFile();
            if (Btn("Reload config file")) VanillaPlusPlugin.ReloadConfig();
            GUILayout.EndHorizontal();

            Header("Config backups");
            Lbl("A backup holds your settings, your waypoints and your death history. Import replaces all three with the backup's, after saving what you have now as a \"_before-import\" backup.");
            var player = Player.m_localPlayer;
            GUILayout.BeginHorizontal();
            if (Btn("Save current config as a backup"))
            {
                string name = ConfigBackup.Export();
                player?.Message(MessageHud.MessageType.Center, $"Config exported: {name}");
            }
            if (Btn("Open backup folder")) ConfigBackup.OpenFolder();
            GUILayout.EndHorizontal();

            // The list is refreshed on the layout pass only, so a click that adds or removes a backup
            // doesn't change the rows halfway through a frame.
            if (Event.current.type == EventType.Layout) _backups = ConfigBackup.Exports().Take(10).ToList();
            if (Show(null))
            {
                if (_backups.Count == 0) GUILayout.Label("   No backups yet.");
                foreach (string name in _backups)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("   " + name + (ConfigBackup.HasWaypoints(name) ? "" : "  (settings only)"), GUILayout.Width(250f));
                    if (GUILayout.Button("Import"))
                    {
                        string saved = ConfigBackup.Import(name);
                        if (saved != null) player?.Message(MessageHud.MessageType.Center, $"Config imported: {name}\nWhat you had is saved as {saved}");
                    }
                    // Delete asks once more: the second click within 4 s removes the backup for good.
                    bool armed = _deleteArmed == name && Time.unscaledTime - _deleteArmedAt < 4f;
                    if (GUILayout.Button(armed ? "Really delete?" : "Delete", GUILayout.Width(110f)))
                    {
                        if (armed) { ConfigBackup.Delete(name); _deleteArmed = null; }
                        else { _deleteArmed = name; _deleteArmedAt = Time.unscaledTime; }
                    }
                    GUILayout.EndHorizontal();
                }
            }
        }

        private static System.Collections.Generic.List<string> _backups = new System.Collections.Generic.List<string>();
        private static string _deleteArmed;
        private static float _deleteArmedAt;

        private const string SearchControl = "vanillaplus_search";
        private static string _search = "";
        private static string _section = "";
        public static bool Typing;   // search box focused: game keys are blocked (see ZInput patches)

        public static bool Searching => !string.IsNullOrEmpty(_search?.Trim());
        public static string SearchText => (_search ?? "").Trim();

        public static bool Matches(string text) =>
            text != null && text.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0;

        // A control is shown when not searching, or when its label or its section header matches.
        public static bool Show(string label) => !Searching || Matches(_section) || Matches(label);

        private static void Header(string text)
        {
            _section = text;
            if (Searching && !Matches(text)) return;
            GUILayout.Space(8f);
            GUILayout.Label(text, _header);
        }

        private static void Toggle(ConfigEntry<bool> entry, string label)
        {
            if (!Show(label)) return;
            bool v = GUILayout.Toggle(entry.Value, label);
            if (v != entry.Value) entry.Value = v;
        }

        private static void Slider(ConfigEntry<float> entry, string label, float min, float max, string format = "0.0")
        {
            if (!Show(label)) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {entry.Value.ToString(format)}", GUILayout.Width(170f));
            float v = GUILayout.HorizontalSlider(entry.Value, min, max);
            GUILayout.EndHorizontal();
            v = Mathf.Round(v * 10f) / 10f;
            if (!Mathf.Approximately(v, entry.Value))
            {
                entry.Value = v;
                if (IsDistance(label)) RangePreview.Show(v); // circle on the ground while dragging
            }
        }

        // Distance sliders are the ones labelled in meters: "Range (m)", "Chest range (m)", ...
        private static bool IsDistance(string label) => label.Contains("(m)") || label.Contains("(m,");

        private static void IntSlider(ConfigEntry<int> entry, string label, int min, int max)
        {
            if (!Show(label)) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {entry.Value}", GUILayout.Width(170f));
            int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value, min, max));
            GUILayout.EndHorizontal();
            if (v != entry.Value) entry.Value = v;
        }

        private static bool Btn(string label, params GUILayoutOption[] opts) => Show(label) && GUILayout.Button(label, opts);

        private static void Lbl(string text, params GUILayoutOption[] opts) { if (Show(null)) GUILayout.Label(text, opts); }

        private static int Bar(int selected, string[] labels) => Show(null) ? GUILayout.Toolbar(selected, labels) : selected;
    }

    // ---------- Input blocking while the menu is open ----------

    [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
    internal static class Menu_MouseCapture
    {
        private static bool Prefix()
        {
            if (!MenuWindow.IsOpen && !StorageWindow.IsOpen) return true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    internal static class Menu_ControllerInput
    {
        private static void Postfix(ref bool __result)
        {
            if (MenuWindow.IsOpen || StorageWindow.IsOpen) __result = false;
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class Menu_PlayerInput
    {
        private static void Postfix(ref bool __result)
        {
            if (MenuWindow.IsOpen || StorageWindow.IsOpen) __result = false;
        }
    }
}
