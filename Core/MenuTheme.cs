using UnityEngine;

namespace ValheimVanillaPlus
{
    internal enum MenuThemeMode { Classic, Dark, Light }

    // Dark / light look for the IMGUI menu: a copy of Unity's default skin with flat colored
    // backgrounds and matching text colors. Classic = Unity's own gray skin.
    internal static class MenuTheme
    {
        private static GUISkin _dark, _light;

        private struct Palette
        {
            public Color Window, Box, Button, ButtonHover, ButtonActive, Selected, Field, Text, TextDim;
        }

        private static readonly Palette DarkColors = new Palette
        {
            Window = new Color(0.11f, 0.12f, 0.14f, 0.97f), Box = new Color(0.16f, 0.17f, 0.20f),
            Button = new Color(0.22f, 0.24f, 0.28f), ButtonHover = new Color(0.29f, 0.32f, 0.37f),
            ButtonActive = new Color(0.18f, 0.40f, 0.62f), Selected = new Color(0.20f, 0.45f, 0.70f),
            Field = new Color(0.07f, 0.08f, 0.09f), Text = new Color(0.92f, 0.93f, 0.95f), TextDim = new Color(0.65f, 0.67f, 0.70f),
        };

        private static readonly Palette LightColors = new Palette
        {
            Window = new Color(0.95f, 0.95f, 0.96f, 0.98f), Box = new Color(0.88f, 0.89f, 0.91f),
            Button = new Color(0.82f, 0.84f, 0.87f), ButtonHover = new Color(0.75f, 0.78f, 0.83f),
            ButtonActive = new Color(0.55f, 0.70f, 0.90f), Selected = new Color(0.45f, 0.63f, 0.88f),
            Field = Color.white, Text = new Color(0.10f, 0.11f, 0.13f), TextDim = new Color(0.35f, 0.37f, 0.40f),
        };

        // The skin to use now, or null for Unity's default.
        public static GUISkin Current(GUISkin baseSkin)
        {
            switch (VanillaPlusPlugin.MenuThemeSetting.Value)
            {
                case MenuThemeMode.Dark: return _dark ?? (_dark = Build(baseSkin, DarkColors));
                case MenuThemeMode.Light: return _light ?? (_light = Build(baseSkin, LightColors));
                default: return null;
            }
        }

        private static GUISkin Build(GUISkin baseSkin, Palette c)
        {
            var skin = Object.Instantiate(baseSkin);
            skin.name = "VanillaPlusTheme";

            Flat(skin.window, c.Window, c.Window, c.Window, c.Text);
            skin.window.onNormal.background = skin.window.normal.background;
            skin.window.onNormal.textColor = c.Text;
            skin.window.fontStyle = FontStyle.Bold;

            Flat(skin.box, c.Box, c.Box, c.Box, c.Text);
            Flat(skin.button, c.Button, c.ButtonHover, c.ButtonActive, c.Text);
            skin.button.onNormal.background = Tex(c.Selected); skin.button.onNormal.textColor = Color.white;
            skin.button.onHover.background = Tex(c.Selected); skin.button.onHover.textColor = Color.white;
            skin.button.onActive.background = Tex(c.ButtonActive); skin.button.onActive.textColor = Color.white;
            Flat(skin.textField, c.Field, c.Field, c.Field, c.Text);
            skin.textField.focused.background = Tex(c.Field); skin.textField.focused.textColor = c.Text;
            skin.settings.cursorColor = c.Text;

            foreach (var st in new[] { skin.label, skin.toggle })
            {
                st.normal.textColor = st.hover.textColor = st.active.textColor = c.Text;
                st.onNormal.textColor = st.onHover.textColor = st.onActive.textColor = c.Text;
            }
            Flat(skin.horizontalSlider, c.Field, c.Field, c.Field, c.Text);
            Flat(skin.horizontalSliderThumb, c.Selected, c.ButtonActive, c.ButtonActive, c.Text);
            skin.horizontalSliderThumb.fixedWidth = 12f;
            Flat(skin.verticalScrollbar, c.Box, c.Box, c.Box, c.Text);
            Flat(skin.verticalScrollbarThumb, c.Button, c.ButtonHover, c.ButtonActive, c.Text);
            skin.toggle.onNormal.textColor = c.Text;
            skin.label.normal.textColor = c.Text;
            skin.box.normal.textColor = c.TextDim;
            return skin;
        }

        private static void Flat(GUIStyle s, Color normal, Color hover, Color active, Color text)
        {
            s.normal.background = Tex(normal); s.normal.textColor = text;
            s.hover.background = Tex(hover); s.hover.textColor = text;
            s.active.background = Tex(active); s.active.textColor = text;
            s.focused.background = Tex(normal); s.focused.textColor = text;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
