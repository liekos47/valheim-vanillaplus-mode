using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Fonts and two-tone ("gradient") presets a sign can name with <font="..."> and <gradient="...">.
    // TextMeshPro resolves such a name in two steps: a table of assets registered by name, then the
    // game's Resources folder. So there are two kinds:
    //   - ones the game itself ships in that folder: every player sees them;
    //   - ones registered here (every other font the game has loaded, and four two-tone presets made
    //     by this mod): only players running this mod see them, others see the tag as plain text.
    // A gradient is applied per letter (its four corners), so presets are top-to-bottom two-tones.
    internal static class SignTags
    {
        public static readonly List<(string name, bool everyone)> Fonts = new List<(string, bool)>();
        public static readonly List<(string name, bool everyone)> Gradients = new List<(string, bool)>();
        private static bool _done;
        private static float _retryAt;

        public static void Ensure()
        {
            if (_done || Time.unscaledTime < _retryAt) return;
            _retryAt = Time.unscaledTime + 5f;
            if (TMP_Settings.instance == null) return;
            try
            {
                Fonts.Clear(); Gradients.Clear();
                var seen = new HashSet<string>();
                foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (font == null || string.IsNullOrEmpty(font.name) || !seen.Add(font.name)) continue;
                    bool shipped = Resources.Load<TMP_FontAsset>(TMP_Settings.defaultFontAssetPath + font.name) != null;
                    if (!shipped) MaterialReferenceManager.AddFontAsset(font);
                    Fonts.Add((font.name, shipped));
                }
                Fonts.Sort((a, b) => a.everyone != b.everyone ? (a.everyone ? -1 : 1) : string.CompareOrdinal(a.name, b.name));

                foreach (var preset in Resources.LoadAll<TMP_ColorGradient>(TMP_Settings.defaultColorGradientPresetsPath))
                    if (preset != null && seen.Add("g:" + preset.name)) Gradients.Add((preset.name, true));
                Add("Fire", "#FFE64D", "#FF3B1A");
                Add("Ice", "#FFFFFF", "#4DA6FF");
                Add("Gold", "#FFF2A6", "#E08A1A");
                Add("Toxic", "#D9FF4D", "#1F9933");

                _done = true;
                VanillaPlusPlugin.Log.LogInfo($"Sign tags: fonts {Describe(Fonts)}; two-tones {Describe(Gradients)}");
            }
            catch (System.Exception e) { VanillaPlusPlugin.Log.LogInfo($"Sign tags: not available ({e.Message})"); _done = true; }
        }

        private static void Add(string name, string top, string bottom)
        {
            ColorUtility.TryParseHtmlString(top, out var t);
            ColorUtility.TryParseHtmlString(bottom, out var b);
            var preset = ScriptableObject.CreateInstance<TMP_ColorGradient>();
            preset.name = name;
            preset.colorMode = ColorMode.VerticalGradient;
            preset.topLeft = preset.topRight = t;
            preset.bottomLeft = preset.bottomRight = b;
            Object.DontDestroyOnLoad(preset);
            MaterialReferenceManager.AddColorGradientPreset(Hash(name), preset);
            Gradients.Add((name, false));
        }

        // The hash TextMeshPro's tag parser builds for an attribute value (case-insensitive).
        private static int Hash(string s)
        {
            int h = 0;
            foreach (char c in s) h = ((h << 5) + h) ^ char.ToUpperInvariant(c);
            return h;
        }

        private static string Describe(List<(string name, bool everyone)> list)
        {
            var parts = new List<string>();
            foreach (var (name, everyone) in list) parts.Add(name + (everyone ? "" : " (mod only)"));
            return parts.Count > 0 ? string.Join(", ", parts) : "none";
        }
    }
}
