using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Hotkeys in the menu: one row per key setting of the mod (every KeyboardShortcut and KeyCode
    // entry in its config file, found by itself, so a new hotkey shows up without touching this).
    // Click a row's button, then press the new key (with Ctrl / Shift / Alt held if wanted);
    // Esc cancels, "None" unbinds, "Default" puts back the original key.
    internal static class HotkeyEditor
    {
        private static ConfigFile _listedFor;
        private static readonly List<ConfigEntryBase> Entries = new List<ConfigEntryBase>();
        private static ConfigEntryBase _waiting;   // the hotkey whose next key press is being read
        private static float _quietUntil;

        // True while a new key is being read and for a moment after, so the key you press to bind
        // doesn't also act as a hotkey or a game key.
        public static bool Busy => _waiting != null || Time.unscaledTime < _quietUntil;

        private static List<ConfigEntryBase> List(ConfigFile config)
        {
            if (_listedFor == config) return Entries;
            _listedFor = config;
            Entries.Clear();
            foreach (var kv in config)
            {
                var e = kv.Value;
                if (e.SettingType != typeof(KeyboardShortcut) && e.SettingType != typeof(KeyCode)) continue;
                if (e.Description?.Description != null && e.Description.Description.StartsWith("No longer used")) continue;
                Entries.Add(e);
            }
            return Entries;
        }

        // Call at the very start of the plugin's OnGUI: takes the key press for the hotkey being set.
        public static void Capture()
        {
            if (_waiting == null) return;
            var e = Event.current;
            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
            KeyCode key = e.keyCode;
            e.Use();

            if (key == KeyCode.Escape) { Stop(); return; }
            if (_waiting.SettingType == typeof(KeyCode))
            {
                _waiting.BoxedValue = key; // a single key; Ctrl / Shift / Alt themselves are allowed
                Stop();
                return;
            }
            if (IsModifier(key)) return;   // wait for the main key; held modifiers are read below
            var held = new List<KeyCode>();
            foreach (var m in Modifiers) if (Input.GetKey(m)) held.Add(m);
            _waiting.BoxedValue = new KeyboardShortcut(key, held.ToArray());
            Stop();
        }

        private static void Stop()
        {
            _waiting = null;
            _quietUntil = Time.unscaledTime + 0.3f;
        }

        private static readonly KeyCode[] Modifiers =
            { KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt };

        private static bool IsModifier(KeyCode k) => System.Array.IndexOf(Modifiers, k) >= 0
            || k == KeyCode.LeftCommand || k == KeyCode.RightCommand || k == KeyCode.LeftWindows || k == KeyCode.RightWindows || k == KeyCode.AltGr;

        public static void Draw(ConfigFile config)
        {
            GUILayout.Label("   Hotkeys (click one, then press the new key; Esc cancels):");
            foreach (var entry in List(config))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("      " + Name(entry), GUILayout.Width(210f));
                bool waiting = _waiting == entry;
                if (GUILayout.Button(waiting ? "Press a key…" : Shown(entry), GUILayout.MinWidth(110f)))
                    _waiting = waiting ? null : entry;
                if (GUILayout.Button("None", GUILayout.Width(52f)))
                {
                    entry.BoxedValue = entry.SettingType == typeof(KeyCode) ? (object)KeyCode.None : KeyboardShortcut.Empty;
                    if (waiting) _waiting = null;
                }
                if (GUILayout.Button("Default", GUILayout.Width(64f)))
                {
                    entry.BoxedValue = entry.DefaultValue;
                    if (waiting) _waiting = null;
                }
                GUILayout.EndHorizontal();
            }
        }

        private static string Shown(ConfigEntryBase entry)
        {
            object v = entry.BoxedValue;
            if (v is KeyboardShortcut s) return s.MainKey == KeyCode.None ? "(none)" : s.ToString();
            if (v is KeyCode k) return k == KeyCode.None ? "(none)" : k.ToString();
            return v?.ToString() ?? "";
        }

        // "ToggleNightVision" in [Hotkeys] -> "Toggle night vision"; "BatchMoveKey" in [Inventory] -> "Inventory: batch move key".
        private static string Name(ConfigEntryBase entry)
        {
            string key = Words(entry.Definition.Key);
            string section = entry.Definition.Section;
            if (section == "Hotkeys") return key;
            return section + ": " + key.ToLowerInvariant();
        }

        private static string Words(string camel)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < camel.Length; i++)
            {
                char c = camel[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(camel[i - 1])) { sb.Append(' '); sb.Append(char.ToLowerInvariant(c)); }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
