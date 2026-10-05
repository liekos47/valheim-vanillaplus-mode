using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimVanillaPlus
{
    internal enum MenuThemeMode { Classic, Dark, Light, Valheim }

    // Look of everything the mod draws with IMGUI (menu, windows, buttons, search boxes): a copy of
    // Unity's default skin, restyled.
    //   Classic = Unity's own gray skin.
    //   Dark / Light = flat colored backgrounds and matching text colors.
    //   Valheim = the game's own look: its font, and the panel, button and text-field pictures of the
    //   game's "Enter text" box. Those only exist once you are in a world, so until then (and if
    //   anything can't be read) the theme uses plain wood-brown colors instead.
    internal static class MenuTheme
    {
        private static GUISkin _dark, _light, _game;
        private static bool _gameLook;      // _game already carries the game's pictures and font
        private static float _gameRetryAt;

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

        // Stand-in for the Valheim theme until the game's own pictures can be read.
        private static readonly Palette WoodColors = new Palette
        {
            Window = new Color(0.13f, 0.09f, 0.06f, 0.97f), Box = new Color(0.08f, 0.06f, 0.04f, 0.85f),
            Button = new Color(0.30f, 0.20f, 0.11f), ButtonHover = new Color(0.40f, 0.27f, 0.14f),
            ButtonActive = new Color(0.52f, 0.34f, 0.15f), Selected = new Color(0.58f, 0.38f, 0.16f),
            Field = new Color(0.06f, 0.04f, 0.03f), Text = new Color(1f, 0.97f, 0.90f), TextDim = new Color(0.75f, 0.70f, 0.62f),
        };

        // The skin to use now, or null for Unity's default. baseSkin must be Unity's default skin
        // (GUI.skin at the start of OnGUI), since the themed skins are copies of it.
        public static GUISkin Current(GUISkin baseSkin)
        {
            switch (VanillaPlusPlugin.MenuThemeSetting.Value)
            {
                case MenuThemeMode.Dark: return _dark ?? (_dark = Build(baseSkin, DarkColors));
                case MenuThemeMode.Light: return _light ?? (_light = Build(baseSkin, LightColors));
                case MenuThemeMode.Valheim: return _game ?? (_game = Build(baseSkin, WoodColors));
                default: return null;
            }
        }

        // Called every frame: once the game's "Enter text" box exists, its pictures and font are put
        // into the Valheim theme. (Done here rather than while drawing, as it renders into a texture.)
        public static void Update()
        {
            if (_game == null || _gameLook || Time.unscaledTime < _gameRetryAt) return;
            _gameRetryAt = Time.unscaledTime + 2f;
            try { _gameLook = ApplyGameLook(_game); }
            catch (System.Exception e)
            {
                _gameLook = true; // keep the plain colors rather than trying again every 2 s
                VanillaPlusPlugin.Log.LogWarning($"Valheim theme: couldn't read the game's interface, using plain colors ({e.GetType().Name}: {e.Message})");
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

        // ----- Valheim theme: the game's own font and pictures -----

        // False while the game's "Enter text" box doesn't exist yet (main menu); true once applied.
        private static bool ApplyGameLook(GUISkin skin)
        {
            var ti = TextInput.instance;
            if (ti == null || ti.m_panel == null) return false;

            // The box's own buttons (not the Clear button a mod added) and its text field.
            var button = ti.m_panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b => !b.name.EndsWith("_ClearButton"));
            var buttonImage = button != null ? button.targetGraphic as Image : null;
            var buttonLabel = button != null ? button.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault() : null;
            var field = ti.m_inputField;
            var fieldImage = field != null ? (field.GetComponent<Image>() ?? field.targetGraphic as Image) : null;
            var panelImage = PanelImage(ti.m_panel, buttonImage, fieldImage);

            var atlases = new Dictionary<Texture, Texture2D>();
            try
            {
                // Font and sizes.
                var font = GameFont(buttonLabel != null ? buttonLabel.font : null);
                if (font != null)
                {
                    skin.font = font;
                    foreach (var st in new[] { skin.label, skin.button, skin.toggle, skin.textField, skin.box, skin.window })
                    { st.font = null; st.fontSize = 13; }
                }

                // Buttons: the game's button picture, with its hover / pressed tints.
                var normal = buttonImage != null ? Copy(buttonImage.sprite, buttonImage.color * Tint(button, 0), atlases) : null;
                if (normal != null)
                {
                    Texture2D hover, pressed;
                    if (button.transition == Selectable.Transition.SpriteSwap)
                    {
                        hover = Copy(button.spriteState.highlightedSprite, buttonImage.color, atlases) ?? normal;
                        pressed = Copy(button.spriteState.pressedSprite, buttonImage.color, atlases) ?? hover;
                    }
                    else
                    {
                        hover = Copy(buttonImage.sprite, buttonImage.color * Tint(button, 1), atlases) ?? normal;
                        pressed = Copy(buttonImage.sprite, buttonImage.color * Tint(button, 2), atlases) ?? hover;
                    }
                    Color text = buttonLabel != null ? Opaque(buttonLabel.color) : skin.button.normal.textColor;
                    var b = skin.button;
                    b.normal.background = normal; b.focused.background = normal;
                    b.hover.background = hover; b.active.background = pressed;
                    b.onNormal.background = pressed; b.onHover.background = pressed; b.onActive.background = pressed; b.onFocused.background = pressed;
                    b.normal.textColor = b.focused.textColor = text;
                    b.hover.textColor = b.active.textColor = Color.Lerp(text, Color.white, 0.5f);
                    b.onNormal.textColor = b.onHover.textColor = b.onActive.textColor = b.onFocused.textColor = Color.white;
                    b.border = Border(buttonImage.sprite, normal);
                    b.padding = new RectOffset(Mathf.Max(6, b.border.left / 2), Mathf.Max(6, b.border.right / 2), 3, 3);
                }

                // Text fields: the box's input field picture and text color.
                var fieldTex = fieldImage != null ? Copy(fieldImage.sprite, fieldImage.color, atlases) : null;
                if (fieldTex != null)
                {
                    var f = skin.textField;
                    f.normal.background = f.hover.background = f.active.background = f.focused.background = fieldTex;
                    f.border = Border(fieldImage.sprite, fieldTex);
                    f.padding = new RectOffset(Mathf.Max(4, f.border.left / 2), Mathf.Max(4, f.border.right / 2), 3, 3);
                    if (field.textComponent != null)
                    {
                        Color text = Opaque(field.textComponent.color);
                        f.normal.textColor = f.hover.textColor = f.active.textColor = f.focused.textColor = text;
                        skin.settings.cursorColor = text;
                    }
                }

                // Windows: the box's own background.
                var panelTex = panelImage != null ? Copy(panelImage.sprite, panelImage.color, atlases) : null;
                if (panelTex != null)
                {
                    var w = skin.window;
                    w.normal.background = w.onNormal.background = w.hover.background = w.active.background = w.focused.background = panelTex;
                    w.border = Border(panelImage.sprite, panelTex);
                    var pad = w.padding;
                    w.padding = new RectOffset(Mathf.Max(pad.left, w.border.left / 2 + 6), Mathf.Max(pad.right, w.border.right / 2 + 6),
                        Mathf.Max(pad.top, w.border.top / 2 + 16), Mathf.Max(pad.bottom, w.border.bottom / 2 + 6));
                }

                VanillaPlusPlugin.Log.LogInfo($"Valheim theme: font {(font != null ? font.name : "not found")}, button {Describe(normal)}, text field {Describe(fieldTex)}, window {Describe(panelTex)}");
                return true;
            }
            finally
            {
                foreach (var copy in atlases.Values) if (copy != null) Object.Destroy(copy);
            }
        }

        private static string Describe(Texture2D t) => t != null ? $"{t.width}x{t.height}" : "plain color";

        // The background of the box: its own picture, or else the biggest picture inside it.
        private static Image PanelImage(GameObject panel, Image button, Image field)
        {
            var own = panel.GetComponent<Image>();
            if (own != null && own.sprite != null) return own;
            Image best = null;
            float bestArea = 0f;
            foreach (var img in panel.GetComponentsInChildren<Image>(true))
            {
                if (img == button || img == field || img.sprite == null || img.GetComponentInParent<Button>() != null) continue;
                var r = img.rectTransform.rect;
                float area = Mathf.Abs(r.width * r.height);
                if (area > bestArea) { bestArea = area; best = img; }
            }
            return best;
        }

        // The game's interface font as a plain Unity font (IMGUI can't use TextMeshPro fonts).
        private static Font GameFont(TMP_FontAsset tmp)
        {
            var fonts = Resources.FindObjectsOfTypeAll<Font>();
            return fonts.FirstOrDefault(f => f.name == "AveriaSerifLibre-Bold")
                ?? (tmp != null ? tmp.sourceFontFile : null)
                ?? fonts.FirstOrDefault(f => f.name.StartsWith("AveriaSerifLibre"))
                ?? fonts.FirstOrDefault(f => f.name.StartsWith("Averia"));
        }

        // A button's tint in a state (0 normal, 1 hover, 2 pressed), when it shows states by tinting.
        private static Color Tint(Button b, int state)
        {
            if (b == null || b.transition != Selectable.Transition.ColorTint) return Color.white;
            var c = b.colors;
            Color col = state == 1 ? c.highlightedColor : state == 2 ? c.pressedColor : c.normalColor;
            col *= c.colorMultiplier;
            col.a = Mathf.Clamp01(col.a);
            return col;
        }

        private static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);

        // The stretchable edges of a picture, never more than fits in it.
        private static RectOffset Border(Sprite s, Texture2D copy)
        {
            Vector4 b = s.border; // left, bottom, right, top
            int maxX = Mathf.Max(0, copy.width / 2 - 1), maxY = Mathf.Max(0, copy.height / 2 - 1);
            return new RectOffset(Mathf.Min((int)b.x, maxX), Mathf.Min((int)b.z, maxX), Mathf.Min((int)b.w, maxY), Mathf.Min((int)b.y, maxY));
        }

        // A picture of the game's interface as a texture of its own, tinted. The game keeps many
        // pictures on one big sheet that can't be read directly, so the sheet is drawn into a
        // temporary texture once and the wanted part is cut out of that.
        private static Texture2D Copy(Sprite s, Color tint, Dictionary<Texture, Texture2D> sheets)
        {
            if (s == null || s.texture == null) return null;
            if (s.packed && (s.packingMode == SpritePackingMode.Tight || s.packingRotation != SpritePackingRotation.None)) return null;
            var src = s.texture;
            if (!sheets.TryGetValue(src, out var sheet))
            {
                var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                try
                {
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    sheet = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                    sheet.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
                    sheet.Apply();
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                }
                sheets[src] = sheet;
            }

            Rect r = s.textureRect;
            int x = Mathf.Clamp(Mathf.RoundToInt(r.x), 0, sheet.width - 1), y = Mathf.Clamp(Mathf.RoundToInt(r.y), 0, sheet.height - 1);
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width), 1, sheet.width - x), h = Mathf.Clamp(Mathf.RoundToInt(r.height), 1, sheet.height - y);
            var px = sheet.GetPixels(x, y, w, h);
            if (tint != Color.white)
                for (int i = 0; i < px.Length; i++) px[i] *= tint;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            copy.SetPixels(px);
            copy.Apply();
            return copy;
        }
    }
}
