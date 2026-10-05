using System;
using BepInEx.Configuration;

namespace ValheimVanillaPlus
{
    // Asks for a line of text with the game's own "Enter text" box (used by menu buttons that edit
    // a text setting). The menu closes so the box can take the keyboard.
    internal static class TextPrompt
    {
        public static void EditSetting(string title, ConfigEntry<string> entry) =>
            Show(title, entry.Value, v => entry.Value = v);

        public static void Show(string title, string current, Action<string> onSet)
        {
            if (TextInput.instance == null) return;
            MenuWindow.IsOpen = false;
            TextInput.instance.RequestText(new Receiver(current, onSet), title, 500);
        }

        private class Receiver : TextReceiver
        {
            private readonly string _text; private readonly Action<string> _set;
            public Receiver(string text, Action<string> set) { _text = text; _set = set; }
            public string GetText() => _text;
            public void SetText(string text) => _set(text.Trim());
        }
    }
}
