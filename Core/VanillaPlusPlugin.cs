using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Vanilla Plus: convenience only. Nothing here changes what your character can do, what the
    // world gives you or what is saved; every feature is something you could do by hand in the
    // unmodded game, just with fewer clicks. So there is no admin / host check.
    [BepInPlugin(Guid, Name, Version)]
    public class VanillaPlusPlugin : BaseUnityPlugin
    {
        public const string Guid = "liekos47.valheimvanillaplus";
        public const string Name = "Valheim Vanilla Plus";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> TextClearButton, ShowFps;
        internal static ConfigEntry<bool> CraftSearchEnabled;
        internal static ConfigEntry<PickupFilterMode> PickupFilterMode;
        internal static ConfigEntry<string> PickupWhitelist, PickupBlacklist;
        internal static ConfigEntry<bool> RepairAlertEnabled;
        internal static ConfigEntry<float> RepairAlertPercent, RepairAlertRepeatMinutes;
        internal static ConfigEntry<bool> AutoReconnectEnabled;
        internal static ConfigEntry<float> AutoReconnectDelay;
        internal static ConfigEntry<int> AutoReconnectMaxAttempts;
        internal static ConfigEntry<bool> CraftFromChestsEnabled;
        internal static ConfigEntry<float> CraftChestRange;
        internal static ConfigEntry<bool> BatchTransferEnabled;
        internal static ConfigEntry<KeyCode> BatchMoveKey;
        internal static ConfigEntry<bool> WaypointsEnabled, WaypointOnScreen, WaypointMapPins, WaypointDeath;
        internal static ConfigEntry<float> WaypointRange;
        internal static ConfigEntry<bool> RadarEnabled, RadarMobs, RadarPassive, RadarPlayers, RadarNames;
        internal static ConfigEntry<float> RadarRange, RadarSize, RadarOpacity, RadarOffsetX, RadarOffsetY;
        internal static ConfigEntry<RadarCorner> RadarPosition;
        internal static ConfigEntry<bool> StorageEnabled, StorageKeepHotbar;
        internal static ConfigEntry<bool> StorageKeepArmor, StorageKeepWeapons, StorageKeepTools, StorageKeepFood, StorageKeepAmmo, StorageKeepTrophies;
        internal static ConfigEntry<float> StorageRange, StorageX, StorageY;
        internal static ConfigEntry<KeyboardShortcut> StorageKey;
        internal static ConfigEntry<string> StorageNeverStore;
        internal static ConfigEntry<bool> NightVisionEnabled;
        internal static ConfigEntry<float> NightVisionBrightness;
        internal static ConfigEntry<KeyboardShortcut> NightVisionKey;
        internal static ConfigEntry<bool> SignEditorEnabled, SignCustomIcons;
        internal static ConfigEntry<int> SignCharLimit;
        internal static ConfigEntry<string> SignDefaultColor;
        internal static ConfigEntry<string> SignSearchText;
        internal static ConfigEntry<float> SignSearchRadius, SignSearchSeconds;
        internal static ConfigEntry<MenuThemeMode> MenuThemeSetting;
        internal static ConfigEntry<float> MenuX, MenuY;
        internal static ConfigEntry<KeyboardShortcut> MenuKey;
        internal static ConfigEntry<bool> HotReloadEnabled;

        internal static BepInEx.Logging.ManualLogSource Log;
        private static VanillaPlusPlugin _instance;
        private Harmony _harmony;

        private static readonly System.Collections.Generic.HashSet<string> _reportedErrors = new System.Collections.Generic.HashSet<string>();

        // Each feature runs on its own: if one throws, it's logged once and the rest still run.
        private static void Safe(string name, System.Action action)
        {
            try { action(); }
            catch (System.Exception e)
            {
                if (_reportedErrors.Add(name)) Log.LogError($"{name} failed (further errors from it are silenced): {e}");
            }
        }

        // Valheim God Mode has every feature below too; with both mods installed, its copies are the ones that run.
        private const string GodModeGuid = "local.valheimgodmode";
        internal static bool GodModeLoaded => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(GodModeGuid);
        internal static bool SignEditorOn => SignEditorEnabled.Value && !GodModeLoaded;
        internal static bool CraftSearchOn => CraftSearchEnabled.Value && !GodModeLoaded;
        internal static bool ShowFpsOn => ShowFps.Value && !GodModeLoaded;
        internal static bool RepairAlertOn => RepairAlertEnabled.Value && !GodModeLoaded;
        internal static bool AutoReconnectOn => AutoReconnectEnabled.Value && !GodModeLoaded;
        internal static bool CraftFromChestsOn => CraftFromChestsEnabled.Value && !GodModeLoaded;
        internal static bool BatchTransferOn => BatchTransferEnabled.Value && !GodModeLoaded;
        internal static bool WaypointsOn => WaypointsEnabled.Value && !GodModeLoaded;
        internal static bool RadarOn => RadarEnabled.Value && !GodModeLoaded;
        internal static bool StorageOn => StorageEnabled.Value && !GodModeLoaded;
        internal static bool NightVisionOn => NightVisionEnabled.Value && !GodModeLoaded;

        internal static bool IsLocalPlayer(Character c) => c != null && c == Player.m_localPlayer;

        // Waypoint markers and the radar step aside while the inventory, a trader or the map is open.
        internal static bool OverlaysHidden => InventoryGui.IsVisible() || StoreGui.IsVisible() || Minimap.IsOpen() || StorageWindow.IsOpen;

        // Opens liekos47.valheimvanillaplus.cfg in Notepad (edit, save, then "Reload config file").
        internal static void OpenConfigFile()
        {
            try
            {
                _instance.Config.Save();
                System.Diagnostics.Process.Start("notepad.exe", $"\"{_instance.Config.ConfigFilePath}\"");
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"Couldn't open config file: {e.Message}");
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Couldn't open the config file (see log)");
            }
        }

        internal static void ReloadConfig()
        {
            _instance.Config.Reload();
            PickupFilter.RebuildLists();
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Config reloaded");
        }

        private void Awake()
        {
            Log = Logger;
            _instance = this;

            TextClearButton = Config.Bind("Interface", "ClearButton", true,
                "Add a 'Clear' button to the game's 'Enter text' box (left of Cancel) that empties the text field.");

            ShowFps = Config.Bind("Interface", "ShowFps", false, "Show the game's frames per second in the top left corner of the screen.");

            CraftSearchEnabled = Config.Bind("Crafting", "CraftSearch", true,
                "Show a search box above the crafting list that filters recipes by the item made or any ingredient.");

            PickupFilterMode = Config.Bind("AutoPickup", "FilterMode", ValheimVanillaPlus.PickupFilterMode.Off,
                "Auto-pickup filter: Off = pick up everything, Whitelist = only listed items, Blacklist = everything except listed items. " +
                "Only affects auto-pickup; pressing E still picks up anything.");
            PickupWhitelist = Config.Bind("AutoPickup", "Whitelist", "Coins, Amber, AmberPearl, Ruby, SilverNecklace",
                "Comma-separated items for Whitelist mode. Use prefab names (Wood, TrophyDeer, Coins) or the name shown in game; * matches anything (Trophy*, *Ore).");
            PickupBlacklist = Config.Bind("AutoPickup", "Blacklist", "Stone, Wood, Resin, Feathers",
                "Comma-separated items for Blacklist mode. Use prefab names (Wood, TrophyDeer, Coins) or the name shown in game; * matches anything (Trophy*, *Ore).");
            PickupFilter.Init();

            RepairAlertEnabled = Config.Bind("RepairAlert", "Enabled", true, "Warn when equipped weapons / tools / armor are low on durability or broken.");
            RepairAlertPercent = Config.Bind("RepairAlert", "BelowPercent", 20f, "Warn below this durability %.");
            RepairAlertRepeatMinutes = Config.Bind("RepairAlert", "RepeatMinutes", 5f, "Remind (top-left) every N minutes while something is still low. 0 = only once.");

            AutoReconnectEnabled = Config.Bind("AutoReconnect", "Enabled", false,
                "When a server session ends with 'disconnected', join the same server again from the main menu after a countdown. Never after a kick or ban.");
            AutoReconnectDelay = Config.Bind("AutoReconnect", "DelaySeconds", 10f, "Wait before each attempt (min 3 s).");
            AutoReconnectMaxAttempts = Config.Bind("AutoReconnect", "MaxAttempts", 10, "Give up after this many attempts in a row.");

            CraftFromChestsEnabled = Config.Bind("Crafting", "CraftFromChests", false,
                "Crafting and building also use items from your chests (player-built chests, carts, ship storage) within ChestRange. Missing items are taken from the closest chests first.");
            CraftChestRange = Config.Bind("Crafting", "ChestRange", 30f,
                "Max distance (meters) of chests used by CraftFromChests. Only chests in the area the game has loaded around you count (roughly 60-100 m).");

            BatchTransferEnabled = Config.Bind("Inventory", "BatchClick", true,
                "BatchMoveKey (Left Alt) + left click an item with a chest open: move every stack of that item to the other side. BatchMoveKey + Shift + left click: drop every stack of it.");
            BatchMoveKey = Config.Bind("Inventory", "BatchMoveKey", KeyCode.LeftAlt, "Hold while left-clicking an item with a chest open to move all stacks of it between inventory and chest.");

            WaypointsEnabled = Config.Bind("Waypoints", "Enabled", true, "Waypoints: named spots per world (stored in a local file only).");
            WaypointOnScreen = Config.Bind("Waypoints", "OnScreen", true, "Show waypoints on screen with name and distance (through walls).");
            WaypointMapPins = Config.Bind("Waypoints", "MapPins", true, "Show waypoints as map pins (not saved to your character).");
            WaypointDeath = Config.Bind("Waypoints", "LastDeath", true, "Add / move a 'Last death' waypoint where you die.");
            WaypointRange = Config.Bind("Waypoints", "OnScreenRange", 0f, "Only show on-screen markers within this distance (0 = any distance).");
            Waypoints.Load();

            RadarEnabled = Config.Bind("Radar", "Enabled", false, "Round radar overlay of nearby creatures and players (turns with the camera).");
            RadarRange = Config.Bind("Radar", "Range", 60f, "Radar range (meters).");
            RadarSize = Config.Bind("Radar", "Size", 180f, "Radar size on screen (pixels).");
            RadarOpacity = Config.Bind("Radar", "Opacity", 0.55f, "Background darkness (0 - 1).");
            RadarPosition = Config.Bind("Radar", "Corner", RadarCorner.TopRight, "Screen corner.");
            RadarOffsetX = Config.Bind("Radar", "OffsetX", 20f, "Distance from the left / right screen edge (pixels).");
            RadarOffsetY = Config.Bind("Radar", "OffsetY", 280f, "Distance from the top / bottom screen edge (pixels). Default sits under the minimap.");
            RadarMobs = Config.Bind("Radar", "Mobs", true, "Show hostile / tamed creatures and bosses.");
            RadarPassive = Config.Bind("Radar", "Passive", true, "Also show passive animals (deer, boar, ...).");
            RadarPlayers = Config.Bind("Radar", "Players", true, "Show other players.");
            RadarNames = Config.Bind("Radar", "PlayerNames", true, "Write player names next to their dots.");

            StorageEnabled = Config.Bind("Storage", "Enabled", true, "Storage window: all nearby chests as one searchable list with take / store.");
            StorageRange = Config.Bind("Storage", "Range", 30f, "Chests within this distance (meters) are included.");
            StorageKey = Config.Bind("Hotkeys", "ToggleStorage", new KeyboardShortcut(KeyCode.F2), "Open / close the Storage window.");
            StorageX = Config.Bind("Storage", "PositionX", 600f, "Window position (remembered).");
            StorageY = Config.Bind("Storage", "PositionY", 80f, "Window position (remembered).");
            StorageKeepHotbar = Config.Bind("Storage", "KeepHotbar", true, "'Store everything' never moves items from your hotbar (top row).");
            StorageKeepArmor = Config.Bind("Storage", "KeepArmor", true, "'Store everything' keeps armor, capes, belts and trinkets.");
            StorageKeepWeapons = Config.Bind("Storage", "KeepWeapons", true, "'Store everything' keeps weapons, bows and shields.");
            StorageKeepTools = Config.Bind("Storage", "KeepTools", true, "'Store everything' keeps tools (hammer, pickaxe, hoe, ...) and torches.");
            StorageKeepFood = Config.Bind("Storage", "KeepFood", true, "'Store everything' keeps food, potions and meads.");
            StorageKeepAmmo = Config.Bind("Storage", "KeepAmmo", true, "'Store everything' keeps arrows, bolts and bait.");
            StorageKeepTrophies = Config.Bind("Storage", "KeepTrophies", false, "'Store everything' keeps trophies.");
            StorageNeverStore = Config.Bind("Storage", "NeverStore", "",
                "Comma-separated items 'Store everything' never moves (prefab or shown names, * wildcard, e.g. Coins, *Mead*).");

            NightVisionEnabled = Config.Bind("NightVision", "Enabled", false, "See at night and in caves/crypts without a torch.");
            NightVisionBrightness = Config.Bind("NightVision", "Brightness", 0.6f, "Minimum ambient light (0.1 = dim, 1 = full daylight).");
            NightVisionKey = Config.Bind("Hotkeys", "ToggleNightVision", new KeyboardShortcut(KeyCode.Home));

            SignEditorEnabled = Config.Bind("Signs", "Editor", true, "Show the color / style / icon panel next to the text box while editing a sign.");
            SignCustomIcons = Config.Bind("Signs", "CustomIcons", true, "Register the game's item icons and map pin icons as sign icons (<sprite name=\"Wood\">). Only players with this mod see them; others see the tag text.");
            SignDefaultColor = Config.Bind("Signs", "DefaultColor", "", "Color added to sign text that has no color of its own when you confirm it: a hex code (FFFFFF) or a name (white, red, yellow, ...). Empty = game default.");
            SignCharLimit = Config.Bind("Signs", "CharacterLimit", 150, "Sign text limit while the editor is on (vanilla 50; color tags use characters).");
            SignSearchText = Config.Bind("Signs", "SearchText", "", "Last text searched for on nearby signs (commas = any of).");
            SignSearchRadius = Config.Bind("Signs", "SearchRadius", 60f, "Radius (meters) of the sign search.");
            SignSearchSeconds = Config.Bind("Signs", "SearchHighlightSeconds", 10f, "How long found signs stay highlighted.");

            MenuKey = Config.Bind("Menu", "Key", new KeyboardShortcut(KeyCode.Insert), "Opens / closes the options menu.");
            MenuThemeSetting = Config.Bind("Menu", "Theme", MenuThemeMode.Valheim, "Look of the menu, windows and buttons: Valheim (the game's own font, buttons and panels), Dark, Light or Classic (Unity gray).");
            MenuX = Config.Bind("Menu", "X", 40f, "Menu position (left edge, pixels). Saved when you drag the menu.");
            MenuY = Config.Bind("Menu", "Y", 80f, "Menu position (top edge, pixels). Saved when you drag the menu.");

            HotReloadEnabled = Config.Bind("General", "HotReload", true,
                "Load a new build of this plugin as soon as it is copied into BepInEx/plugins, without restarting the game.");
            HotReload.Init();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(VanillaPlusPlugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        // Shut this copy of the plugin down so another build can take over (see HotReload): what it
        // added to the game's screens is taken out again, then the patches are removed.
        private bool _unloading;
        internal void Teardown()
        {
            _unloading = true;
            MenuWindow.IsOpen = false;
            Safe("ClearButton cleanup", TextInputClear.Cleanup);
            Safe("SignIcons cleanup", SignIcons.Cleanup);
            Safe("Waypoints cleanup", Waypoints.Cleanup);
            // Unpatch now and forget the instance: this component is destroyed at the end of the frame,
            // after the next build has patched under the same Harmony id, and OnDestroy must not strip those.
            _harmony?.UnpatchSelf();
            _harmony = null;
            BepInEx.Logging.Logger.Sources.Remove(Logger);
        }

        private void Update()
        {
            if (_unloading) return;
            if (HotReload.Due()) { HotReload.Reload(this); return; }
            FpsCounter.CountFrame();
            MenuTheme.Update();
            Safe("SignEditor", SignEditor.Update);
            Safe("SignIcons", SignIcons.Update);
            Safe("AutoReconnect", AutoReconnect.Update);
            if (Player.m_localPlayer == null)
            {
                MenuWindow.IsOpen = false;
                StorageWindow.IsOpen = false;
                return;
            }

            // Per-frame features go here, one Safe(...) line each.
            var p = Player.m_localPlayer;
            Safe("RepairAlert", () => DurabilityAlert.Update(p));
            Safe("CraftListRefresh", () => CraftFromChests.Update(p));
            Safe("WaypointPins", Waypoints.UpdatePins);
            AutoReconnect.OnInWorld();

            // Don't toggle while typing in chat / console / a text box.
            if (Console.IsVisible() || Chat.instance?.HasFocus() == true || TextInput.IsVisible())
                return;

            if (MenuKey.Value.IsDown()) { MenuWindow.IsOpen = !MenuWindow.IsOpen; if (MenuWindow.IsOpen) StorageWindow.IsOpen = false; }
            if (MenuWindow.Typing || CraftSearch.Typing || StorageWindow.Typing) return;

            if (StorageKey.Value.IsDown() && StorageOn) StorageWindow.Toggle();

            if (NightVisionKey.Value.IsDown() && !GodModeLoaded) Toggle(NightVisionEnabled, "Night vision");
        }

        private static void Toggle(ConfigEntry<bool> entry, string label)
        {
            entry.Value = !entry.Value;
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"{label}: {(entry.Value ? "ON" : "OFF")}");
        }

        private void OnGUI()
        {
            if (_unloading) return;
            // GUI.skin is Unity's default at this point (Unity resets it before every OnGUI).
            var unity = GUI.skin;
            var theme = MenuTheme.Current(unity);

            // Markers drawn over the world keep Unity's plain look: their colors carry meaning.
            if (Event.current.type == EventType.Repaint) Safe("SignSearch", SignSearch.Draw);
            if (Event.current.type == EventType.Repaint) Safe("Fps", FpsCounter.Draw);
            if (Event.current.type == EventType.Repaint && !OverlaysHidden)
            {
                Safe("Waypoints", Waypoints.Draw);
                Safe("Radar", Radar.Draw);
            }

            // Everything you click or type in (buttons, search boxes, panels, windows) uses the menu theme.
            if (theme != null) GUI.skin = theme;
            Safe("AutoReconnect", AutoReconnect.Draw);
            Safe("CraftSearch", CraftSearch.Draw);
            Safe("SignEditor", SignEditor.Draw);
            Safe("Menu", MenuWindow.Draw);
            Safe("Storage", StorageWindow.Draw);
            GUI.skin = unity;
        }
    }
}
