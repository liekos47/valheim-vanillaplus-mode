using BepInEx.Configuration;
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

            var before = _rect.position;
            _rect.height = Mathf.Min(620f, Screen.height - _rect.y - 20f);
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
            if (Event.current.type == EventType.Repaint) Typing = GUI.GetNameOfFocusedControl() == SearchControl;

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
                case 5: if (Show(null)) PlayerStats.Draw(Player.m_localPlayer); break;
                case 6: MoreTab(); break;
            }
        }

        private static void WorldTab()
        {
            var player = Player.m_localPlayer;
            Header("Waypoints");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its waypoints are used instead of these.");
            Toggle(VanillaPlusPlugin.WaypointsEnabled, "Waypoints");
            Toggle(VanillaPlusPlugin.WaypointOnScreen, "   ...show on screen (name + distance)");
            Slider(VanillaPlusPlugin.WaypointRange, "   On-screen range (m, 0 = any)", 0f, 5000f, "0");
            Toggle(VanillaPlusPlugin.WaypointMapPins, "   ...show as map pins (not saved)");
            Toggle(VanillaPlusPlugin.WaypointDeath, "   ...add a 'Last death' waypoint when I die");
            if (player != null && Btn("Add waypoint here")) Waypoints.AddHere(player);
            if (player != null && Show(null))
            {
                foreach (var w in new System.Collections.Generic.List<Waypoint>(Waypoints.ForThisWorld()))
                {
                    float d = Vector3.Distance(player.transform.position, w.Pos);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"   {w.Name}  ({d:0} m {WorldInfo.Compass(player.transform.position, w.Pos)})", GUILayout.Width(250f));
                    if (GUILayout.Button("Map")) Waypoints.ShowOnMap(w);
                    if (GUILayout.Button("Rename")) Waypoints.Rename(w);
                    if (GUILayout.Button("Delete")) Waypoints.Delete(w);
                    GUILayout.EndHorizontal();
                }
            }
        }

        private static void ItemsTab()
        {
            Header("Inventory");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its batch click is used instead of this one.");
            Toggle(VanillaPlusPlugin.BatchTransferEnabled, $"{VanillaPlusPlugin.BatchMoveKey.Value}+click: move all of that item to/from the chest; +Shift: throw all of it out");

            Header("Storage window");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its storage window is used instead of this one.");
            Toggle(VanillaPlusPlugin.StorageEnabled, $"Storage window ({VanillaPlusPlugin.StorageKey.Value}): all nearby chests in one searchable list");
            Slider(VanillaPlusPlugin.StorageRange, "   Range (m)", 5f, 100f, "0");
            if (Player.m_localPlayer != null && VanillaPlusPlugin.StorageOn && Btn("Open storage window")) StorageWindow.Toggle();
            Lbl("   'Store everything' keeps equipped items, and:");
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
            if (Btn("   Edit never-store list")) TextPrompt.EditSetting("Never store: item names, commas between, * wildcard (e.g. Coins, *Mead*, Wishbone)", VanillaPlusPlugin.StorageNeverStore);

            Header("Repair alert");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its repair alert is used instead of this one.");
            Toggle(VanillaPlusPlugin.RepairAlertEnabled, "Repair alert (equipped gear low / broken)");
            Slider(VanillaPlusPlugin.RepairAlertPercent, "   Warn below %", 5f, 75f, "0");
            Slider(VanillaPlusPlugin.RepairAlertRepeatMinutes, "   Remind every (min, 0 = once)", 0f, 30f, "0");

            Header("Auto-pickup filter");
            if (VanillaPlusPlugin.GodModeLoaded) Lbl("Valheim God Mode is installed: its pickup filter is used instead of this one.");
            Lbl("Which items auto-pickup takes (pressing E still picks up anything):");
            var modes = new[] { "Off", "Whitelist", "Blacklist" };
            int mode = Bar((int)VanillaPlusPlugin.PickupFilterMode.Value, modes);
            if (mode != (int)VanillaPlusPlugin.PickupFilterMode.Value) VanillaPlusPlugin.PickupFilterMode.Value = (PickupFilterMode)mode;
            Lbl(PickupFilter.Summary());
            GUILayout.BeginHorizontal();
            if (Btn("Edit whitelist")) TextPrompt.EditSetting("Pickup whitelist: item names, commas between, * wildcard (Trophy*, *Ore)", VanillaPlusPlugin.PickupWhitelist);
            if (Btn("Edit blacklist")) TextPrompt.EditSetting("Pickup blacklist: item names, commas between, * wildcard (Trophy*, *Ore)", VanillaPlusPlugin.PickupBlacklist);
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
                TextPrompt.EditSetting("Default sign color: hex (FFFFFF) or name (white, red, ...). Empty = game default", VanillaPlusPlugin.SignDefaultColor);
        }

        private static void MoreTab()
        {
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

            Header("Development");
            Toggle(VanillaPlusPlugin.HotReloadEnabled, "Hot reload: load a new build without restarting the game");
        }

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
            if (!Mathf.Approximately(v, entry.Value)) entry.Value = v;
        }

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
