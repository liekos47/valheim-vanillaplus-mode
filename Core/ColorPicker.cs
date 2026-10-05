using BepInEx.Configuration;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Color setting in the menu: a row of ready-made colors to click, R / G / B sliders and a hex box,
    // with a swatch showing the current color. The setting holds a hex code without the # (FFE64D),
    // or nothing for "no color"; a color name typed into the config file (white, red, ...) is shown too.
    internal static class ColorPicker
    {
        public const string Control = "vanillaplus_colorhex"; // the hex box: game keys are blocked while it has focus

        private static readonly string[] Palette =
            { "FF4040", "FF8C1A", "FFE64D", "8CFF59", "33CC55", "4DCCFF", "4D6BFF", "CC66FF", "FF66CC", "FFFFFF", "AAAAAA", "000000" };

        private static GUIStyle _swatch;
        private static string _hex = "", _hexFor;

        public static void Draw(string label, ConfigEntry<string> entry, string emptyText)
        {
            if (!MenuWindow.Show(label)) return;
            if (_swatch == null)
            {
                // A plain white square, so each color shows as it is whatever the theme's buttons look like.
                _swatch = new GUIStyle { margin = new RectOffset(2, 2, 3, 3) };
                _swatch.normal.background = _swatch.hover.background = _swatch.active.background = Texture2D.whiteTexture;
            }

            string cur = (entry.Value ?? "").Trim().TrimStart('#');
            bool has = TryColor(cur, out Color color);
            if (entry.Value != _hexFor) { _hexFor = entry.Value; _hex = has ? ColorUtility.ToHtmlStringRGB(color) : ""; }

            // Current value + swatch + "no color".
            GUILayout.BeginHorizontal();
            GUILayout.Label($"   {label}: " + (cur.Length == 0 ? emptyText : has ? cur : cur + " (not a color)"), GUILayout.Width(250f));
            var box = GUILayoutUtility.GetRect(46f, 20f, GUILayout.Width(46f), GUILayout.Height(20f));
            if (has && Event.current.type == EventType.Repaint)
            {
                var old = GUI.color; GUI.color = new Color(color.r, color.g, color.b, 1f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = old;
            }
            if (GUILayout.Button(char.ToUpper(emptyText[0]) + emptyText.Substring(1), GUILayout.Width(120f))) entry.Value = "";
            GUILayout.EndHorizontal();

            // Ready-made colors.
            GUILayout.BeginHorizontal();
            GUILayout.Space(14f);
            foreach (string p in Palette)
            {
                ColorUtility.TryParseHtmlString("#" + p, out var pc);
                GUI.backgroundColor = pc;
                if (GUILayout.Button("", _swatch, GUILayout.Width(26f), GUILayout.Height(22f))) entry.Value = p;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            // Any color: sliders (starting from white when there is no color yet) and a hex code.
            Color start = has ? color : Color.white;
            float r = Channel("R", start.r), g = Channel("G", start.g), b = Channel("B", start.b);
            if (r != start.r || g != start.g || b != start.b) entry.Value = ColorUtility.ToHtmlStringRGB(new Color(r, g, b));

            GUILayout.BeginHorizontal();
            GUILayout.Label("      Hex  #", GUILayout.Width(74f));
            GUI.SetNextControlName(Control);
            string hex = GUILayout.TextField(_hex ?? "", 6, GUILayout.Width(80f));
            if (hex != _hex)
            {
                _hex = hex;
                if (hex.Length == 6 && ColorUtility.TryParseHtmlString("#" + hex, out _))
                {
                    entry.Value = hex.ToUpperInvariant();
                    _hexFor = entry.Value; // keep what was typed in the box
                }
            }
            GUILayout.EndHorizontal();
        }

        private static float Channel(string name, float v)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"      {name} {Mathf.RoundToInt(v * 255f)}", GUILayout.Width(74f));
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
