using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Color setting: a menu button opens the game's "Enter text" box holding the color (a hex code
    // without the #, a color name, or nothing for "no color"), and while that box is open a small
    // panel beside it, like the sign editor's, offers ready-made colors and R / G / B sliders that
    // write the hex code into the box. OK saves it, Cancel leaves the setting alone.
    internal static class ColorPicker
    {
        private static readonly AccessTools.FieldRef<TextInput, TextReceiver> Queued =
            AccessTools.FieldRefAccess<TextInput, TextReceiver>("m_queuedSign");
        private static readonly string[] Palette =
            { "FF4040", "FF8C1A", "FFE64D", "8CFF59", "33CC55", "4DCCFF", "4D6BFF", "CC66FF", "FF66CC", "FFFFFF", "AAAAAA", "000000" };

        private static Receiver _open, _placedFor;
        private static GUIStyle _swatch, _rich;
        private static GUISkin _styleSkin;

        // emptyText: what an empty value means, e.g. "game default".
        public static void Open(string title, ConfigEntry<string> entry, string emptyText)
        {
            if (TextInput.instance == null) return;
            MenuWindow.IsOpen = false;
            _open = new Receiver(entry, emptyText);
            TextInput.instance.RequestText(_open, title, 20);
        }

        private class Receiver : TextReceiver
        {
            private readonly ConfigEntry<string> _entry;
            public readonly string EmptyText;
            public Receiver(ConfigEntry<string> entry, string emptyText) { _entry = entry; EmptyText = emptyText; }
            public string GetText() => (_entry.Value ?? "").Trim().TrimStart('#');
            public void SetText(string text) => _entry.Value = (text ?? "").Trim().TrimStart('#');
        }

        // Drawn every OnGUI: shows only while the box opened by Open() is up.
        public static void DrawPanel()
        {
            var ti = TextInput.instance;
            if (_open == null || ti == null || !TextInput.IsVisible() || Queued(ti) != _open) return;
            var f = ti.m_inputField;
            if (f == null) return;

            if (_swatch == null || _styleSkin != GUI.skin)
            {
                _styleSkin = GUI.skin;
                _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
                // A plain white square, so each color shows as it is whatever the theme's buttons look like.
                _swatch = new GUIStyle { margin = new RectOffset(2, 2, 3, 3) };
                _swatch.normal.background = _swatch.hover.background = _swatch.active.background = Texture2D.whiteTexture;
            }

            // Beside the game's text box, never over it; in a narrow window the panel gets narrower
            // and the ready-made colors wrap onto more rows.
            var area = PanelPlace.Beside(ti.m_panel, 372f, 236f, 270f);
            if (_placedFor != _open)
            {
                _placedFor = _open; // once per opening, so a missing or misplaced panel can be traced in the log
                VanillaPlusPlugin.Log.LogInfo($"Color panel at {area} on a {Screen.width}x{Screen.height} screen");
            }

            string cur = (f.text ?? "").Trim().TrimStart('#');
            bool has = TryColor(cur, out Color color);

            GUILayout.BeginArea(area, GUI.skin.box);

            // Current color.
            GUILayout.BeginHorizontal();
            var box = GUILayoutUtility.GetRect(46f, 20f, GUILayout.Width(46f), GUILayout.Height(20f));
            if (has && Event.current.type == EventType.Repaint)
            {
                var old = GUI.color; GUI.color = new Color(color.r, color.g, color.b, 1f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = old;
            }
            GUILayout.Label(cur.Length == 0 ? $"<b>No color</b> ({_open.EmptyText})" : has ? $"<b>{cur}</b>" : $"<b>{cur}</b>  <color=#ffb030>not a color</color>", _rich);
            GUILayout.EndHorizontal();

            // Ready-made colors, as many per row as fit.
            int perRow = Mathf.Clamp(Mathf.FloorToInt((area.width - 16f) / 30f), 1, Palette.Length);
            for (int i = 0; i < Palette.Length; i++)
            {
                if (i % perRow == 0) GUILayout.BeginHorizontal();
                ColorUtility.TryParseHtmlString("#" + Palette[i], out var pc);
                GUI.backgroundColor = pc;
                if (GUILayout.Button("", _swatch, GUILayout.Width(26f), GUILayout.Height(22f))) Set(f, Palette[i], true);
                if (i % perRow == perRow - 1 || i == Palette.Length - 1) GUILayout.EndHorizontal();
            }
            GUI.backgroundColor = Color.white;

            // Any color: sliders, starting from white when the box holds no color yet.
            Color start = has ? color : Color.white;
            float r = Channel("R", start.r), g = Channel("G", start.g), b = Channel("B", start.b);
            if (r != start.r || g != start.g || b != start.b) Set(f, ColorUtility.ToHtmlStringRGB(new Color(r, g, b)), false);

            // Hex code: the same text as in the game's box, editable here too.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Hex  #", GUILayout.Width(50f));
            string typed = GUILayout.TextField(cur, 20, GUILayout.Width(110f));
            if (typed != cur) Set(f, typed.Trim().TrimStart('#'), false);
            GUILayout.EndHorizontal();

            if (GUILayout.Button($"No color ({_open.EmptyText})")) Set(f, "", true);
            GUILayout.Label("<color=#999999>A color name (white, red, ...) works too. OK saves it.</color>", _rich);
            GUILayout.EndArea();
        }

        // focus: put the cursor back in the box (not while a slider is being dragged).
        private static void Set(TMPro.TMP_InputField f, string text, bool focus)
        {
            f.text = text;
            if (!focus) return;
            f.ActivateInputField();
            f.stringPosition = text.Length;
            f.selectionStringAnchorPosition = text.Length;
            f.selectionStringFocusPosition = text.Length;
        }

        private static float Channel(string name, float v)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name} {Mathf.RoundToInt(v * 255f)}", GUILayout.Width(50f));
            v = GUILayout.HorizontalSlider(v, 0f, 1f);
            GUILayout.EndHorizontal();
            return Mathf.Round(v * 255f) / 255f;
        }

        // Hex code (with or without #) or a color name.
        private static bool TryColor(string text, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(text)) return false;
            return ColorUtility.TryParseHtmlString("#" + text, out color) || ColorUtility.TryParseHtmlString(text, out color);
        }
    }
}
