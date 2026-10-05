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

        private static readonly string[] Tabs = { "Interface", "More" };
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
            int themeSel = GUILayout.Toolbar((int)VanillaPlusPlugin.MenuThemeSetting.Value, new[] { "Classic", "Dark", "Light" });
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
                case 1: MoreTab(); break;
            }
        }

        private static void InterfaceTab()
        {
            Header("Text box");
            Toggle(VanillaPlusPlugin.TextClearButton, "'Clear' button in the Enter text box");
        }

        private static void MoreTab()
        {
            Header("Settings file");
            GUILayout.BeginHorizontal();
            if (Btn("Open config file")) VanillaPlusPlugin.OpenConfigFile();
            if (Btn("Reload config file")) VanillaPlusPlugin.ReloadConfig();
            GUILayout.EndHorizontal();
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
            if (!MenuWindow.IsOpen) return true;
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
            if (MenuWindow.IsOpen) __result = false;
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class Menu_PlayerInput
    {
        private static void Postfix(ref bool __result)
        {
            if (MenuWindow.IsOpen) __result = false;
        }
    }
}
