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
        public const string Guid = "local.valheimvanillaplus";
        public const string Name = "Valheim Vanilla Plus";
        public const string Version = "0.1.0";

        internal static ConfigEntry<bool> TextClearButton;
        internal static ConfigEntry<MenuThemeMode> MenuThemeSetting;
        internal static ConfigEntry<float> MenuX, MenuY;
        internal static ConfigEntry<KeyboardShortcut> MenuKey;

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

        // Opens local.valheimvanillaplus.cfg in Notepad (edit, save, then "Reload config file").
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
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Config reloaded");
        }

        private void Awake()
        {
            Log = Logger;
            _instance = this;

            TextClearButton = Config.Bind("Interface", "ClearButton", true,
                "Add a 'Clear' button to the game's 'Enter text' box (left of Cancel) that empties the text field.");

            MenuKey = Config.Bind("Menu", "Key", new KeyboardShortcut(KeyCode.Insert), "Opens / closes the options menu.");
            MenuThemeSetting = Config.Bind("Menu", "Theme", MenuThemeMode.Dark, "Menu look: Classic (Unity gray), Dark or Light.");
            MenuX = Config.Bind("Menu", "X", 40f, "Menu position (left edge, pixels). Saved when you drag the menu.");
            MenuY = Config.Bind("Menu", "Y", 80f, "Menu position (top edge, pixels). Saved when you drag the menu.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(VanillaPlusPlugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy() => _harmony?.UnpatchSelf();

        private void Update()
        {
            if (Player.m_localPlayer == null)
            {
                MenuWindow.IsOpen = false;
                return;
            }

            // Per-frame features go here, one Safe(...) line each.

            // Don't toggle while typing in chat / console / a text box.
            if (Console.IsVisible() || Chat.instance?.HasFocus() == true || TextInput.IsVisible())
                return;

            if (MenuKey.Value.IsDown()) MenuWindow.IsOpen = !MenuWindow.IsOpen;
        }

        private void OnGUI()
        {
            Safe("Menu", MenuWindow.Draw);
        }
    }
}
