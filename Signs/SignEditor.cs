using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Sign editor: while you edit a sign, a panel next to the text box adds what Valheim's signs
    // support but give no UI for. Signs are drawn with TextMeshPro rich text, so they take:
    //  - colors, in the short hash form <#RRGGBB>...</color> (palette, RGB sliders or a hex code),
    //    for the selected text or the whole sign;
    //  - bold / italic / size, underline / strikethrough, and a highlight behind the text
    //    (<mark=#RRGGBBAA>...</mark>, in the current color);
    //  - icons, IF this version of the game ships a TextMeshPro sprite set: the panel lists whatever
    //    icons the game reports and inserts <sprite name="..."> at the cursor; if it has none, it says so.
    //  - a default color for your signs ([Signs] DefaultColor, or "Use as my default" in the panel):
    //    when you confirm a sign whose text has no color tag, it's wrapped in that color automatically;
    //    "No default" leaves signs in the game's own text color.
    // Tags count toward the sign's character limit, so the limit is raised (default 50 -> 150, config).
    // The text is saved as normal sign text; other players see the colors without needing the mod.
    internal static class SignEditor
    {
        private static readonly AccessTools.FieldRef<TextInput, TextReceiver> Queued =
            AccessTools.FieldRefAccess<TextInput, TextReceiver>("m_queuedSign");
        private static readonly string[] Palette =
            { "#FF4040", "#FF8C1A", "#FFE64D", "#8CFF59", "#33CC55", "#4DCCFF", "#4D6BFF", "#CC66FF", "#FF66CC", "#FFFFFF", "#AAAAAA", "#000000" };
        private static readonly Regex ColorTags = new Regex(@"</?color[^>]*>|<#[0-9a-fA-F]{3,8}>", RegexOptions.IgnoreCase);

        private static readonly Regex MarkTags = new Regex(@"</?mark[^>]*>", RegexOptions.IgnoreCase);
        private const string MarkAlpha = "66"; // 40% opaque, so the text stays readable through it

        private static Color _color = new Color(1f, 0.25f, 0.25f);
        private static string _hex = "FF4040";
        private static int _selStart, _selEnd, _caret;   // last known selection / cursor in the text box
        private static Vector2 _iconScroll;
        private static List<string> _icons, _symbols, _arrows;
        private static int _iconTab, _formatTab;
        private static Vector2 _fontScroll;
        private static string _iconSearch = "";
        private static GUIStyle _rich, _preview, _swatch;
        private static GUISkin _styleSkin;
        private static readonly Vector3[] Corners = new Vector3[4];

        private static bool EditingSign(out TMP_InputField field)
        {
            field = null;
            var ti = TextInput.instance;
            if (ti == null || !TextInput.IsVisible() || !(Queued(ti) is Sign)) return false;
            field = ti.m_inputField;
            return field != null;
        }

        // Remember where the cursor / selection is while the box has focus (clicking our panel unfocuses it).
        public static void Update()
        {
            if (VanillaPlusPlugin.SignEditorOn) SignTags.Ensure(); // so signs using the mod's fonts / two-tones draw
            if (!VanillaPlusPlugin.SignEditorOn || !EditingSign(out var f) || !f.isFocused) return;
            int a = f.selectionStringAnchorPosition, b = f.selectionStringFocusPosition;
            _selStart = Mathf.Min(a, b); _selEnd = Mathf.Max(a, b);
            _caret = f.stringPosition;
        }

        public static void Draw()
        {
            if (!VanillaPlusPlugin.SignEditorOn || !EditingSign(out var f)) return;
            if (_rich == null || _styleSkin != GUI.skin)
            {
                _styleSkin = GUI.skin;
                _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
                _preview = new GUIStyle(GUI.skin.box) { richText = true, wordWrap = true, fontSize = 18, alignment = TextAnchor.MiddleCenter };
                // Color swatches: a plain white square, so the color shows as it is whatever the theme's buttons look like.
                _swatch = new GUIStyle { margin = new RectOffset(2, 2, 3, 3) };
                _swatch.normal.background = _swatch.hover.background = _swatch.active.background = Texture2D.whiteTexture;
            }

            // Beside the game's text box, never over it.
            GUILayout.BeginArea(PanelPlace.Beside(TextInput.instance.m_panel, 380f, 380f, 610f), GUI.skin.box);
            GUILayout.Label($"<b>Sign colors & icons</b>   <color=#999999>{f.text.Length}/{f.characterLimit} characters</color>", _rich);
            GUILayout.Label(Preview(f.text), _preview, GUILayout.MinHeight(44f));

            // Palette
            GUILayout.BeginHorizontal();
            foreach (var p in Palette)
            {
                ColorUtility.TryParseHtmlString(p, out var pc);
                GUI.backgroundColor = pc;
                if (GUILayout.Button("", _swatch, GUILayout.Width(26f), GUILayout.Height(22f))) SetColor(pc);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            // Picker: sliders + hex
            float r = Channel("R", _color.r), g = Channel("G", _color.g), b = Channel("B", _color.b);
            if (r != _color.r || g != _color.g || b != _color.b) SetColor(new Color(r, g, b));
            GUILayout.BeginHorizontal();
            var swatch = GUILayoutUtility.GetRect(50f, 20f, GUILayout.Width(50f), GUILayout.Height(20f));
            var old = GUI.color; GUI.color = _color;
            GUI.DrawTexture(swatch, Texture2D.whiteTexture);
            GUI.color = old;
            GUILayout.Label("#", GUILayout.Width(12f));
            string hex = GUILayout.TextField(_hex, 6, GUILayout.Width(70f));
            if (hex != _hex)
            {
                _hex = hex;
                if (hex.Length == 6 && ColorUtility.TryParseHtmlString("#" + hex, out var parsed)) _color = parsed;
            }
            GUILayout.Label($"<color=#999999>tag: &lt;#{Hex()}&gt;</color>", _rich);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Color selection")) ColorSelection(f);
            if (GUILayout.Button("Color whole sign")) Apply(f, $"<#{Hex()}>" + ColorTags.Replace(f.text, "") + "</color>", -1);
            if (GUILayout.Button("Remove colors")) Apply(f, ColorTags.Replace(f.text, ""), -1);
            GUILayout.EndHorizontal();
            // Everything else the sign's text engine parses, grouped. Each button wraps the selection
            // (or puts the pair at the cursor); "insert" ones put a single tag at the cursor.
            _formatTab = GUILayout.Toolbar(_formatTab, new[] { "Style", "Size", "Layout", "Spacing", "Fonts" });
            switch (_formatTab)
            {
                case 0:
                    Row(() =>
                    {
                        Tag(f, "Bold", "<b>", "</b>"); Tag(f, "Italic", "<i>", "</i>");
                        Tag(f, "Underline", "<u>", "</u>"); Tag(f, "Strike", "<s>", "</s>");
                    });
                    Row(() =>
                    {
                        // Highlight = <mark>: a see-through block of the current color behind the text.
                        Tag(f, "Highlight", $"<mark=#{Hex()}{MarkAlpha}>", "</mark>");
                        Tag(f, "Sub", "<sub>", "</sub>"); Tag(f, "Super", "<sup>", "</sup>");
                        Tag(f, "Faded", "<alpha=#80>", "<alpha=#FF>");
                    });
                    Row(() =>
                    {
                        if (GUILayout.Button("Remove highlight")) Apply(f, MarkTags.Replace(f.text, ""), -1);
                        Tag(f, "Show tags as text", "<noparse>", "</noparse>");
                        if (GUILayout.Button("Plain")) Apply(f, Regex.Replace(f.text, "<[^>]+>", ""), -1);
                    });
                    break;
                case 1:
                    Row(() =>
                    {
                        Tag(f, "Bigger", "<size=130%>", "</size>"); Tag(f, "Smaller", "<size=75%>", "</size>");
                        Tag(f, "Huge", "<size=200%>", "</size>"); Tag(f, "Tiny", "<size=50%>", "</size>");
                    });
                    Row(() =>
                    {
                        Tag(f, "UPPER", "<uppercase>", "</uppercase>"); Tag(f, "lower", "<lowercase>", "</lowercase>");
                        Tag(f, "Small caps", "<smallcaps>", "</smallcaps>");
                    });
                    Row(() =>
                    {
                        Tag(f, "Stretch wide", "<scale=1.5>", "</scale>"); Tag(f, "Squeeze", "<scale=0.7>", "</scale>");
                        Tag(f, "Heavy", "<font-weight=900>", "</font-weight>"); Tag(f, "Light", "<font-weight=300>", "</font-weight>");
                    });
                    break;
                case 2:
                    Row(() =>
                    {
                        Tag(f, "Left", "<align=left>", "</align>"); Tag(f, "Center", "<align=center>", "</align>");
                        Tag(f, "Right", "<align=right>", "</align>"); Tag(f, "Justify", "<align=justified>", "</align>");
                    });
                    Row(() =>
                    {
                        if (GUILayout.Button("New line (insert)")) Insert(f, "<br>");
                        Tag(f, "Keep on one line", "<nobr>", "</nobr>");
                        Tag(f, "Indent", "<indent=15%>", "</indent>");
                    });
                    Row(() =>
                    {
                        Tag(f, "Tilt left", "<rotate=15>", "</rotate>"); Tag(f, "Tilt right", "<rotate=-15>", "</rotate>");
                        Tag(f, "Upside down", "<rotate=180>", "</rotate>");
                        Tag(f, "Margins", "<margin=1em>", "</margin>"); Tag(f, "Narrow", "<width=60%>", "</width>");
                    });
                    break;
                case 3:
                    Row(() =>
                    {
                        Tag(f, "Letters apart", "<cspace=0.3em>", "</cspace>"); Tag(f, "Letters tight", "<cspace=-0.05em>", "</cspace>");
                        Tag(f, "Fixed width", "<mspace=0.6em>", "</mspace>");
                    });
                    Row(() =>
                    {
                        Tag(f, "Lines apart", "<line-height=140%>", "</line-height>"); Tag(f, "Lines tight", "<line-height=75%>", "</line-height>");
                        if (GUILayout.Button("Gap (insert)")) Insert(f, "<space=1em>");
                    });
                    Row(() =>
                    {
                        Tag(f, "Raise", "<voffset=0.3em>", "</voffset>"); Tag(f, "Lower", "<voffset=-0.3em>", "</voffset>");
                        if (GUILayout.Button("Jump to middle (insert)")) Insert(f, "<pos=50%>");
                    });
                    break;
                default:
                    SignTags.Ensure();
                    _fontScroll = GUILayout.BeginScrollView(_fontScroll, GUILayout.Height(84f));
                    GUILayout.Label("<color=#999999>Fonts</color>", _rich);
                    Grid(SignTags.Fonts.Count, 2, i =>
                    {
                        var (name, everyone) = SignTags.Fonts[i];
                        if (GUILayout.Button(new GUIContent(name, everyone ? "Everyone sees this font." : "<color=#ffb030>Only players with this mod see this font; others see the tag text.</color>"), GUILayout.Width(170f)))
                            Wrap(f, $"<font=\"{name}\">", "</font>");
                    });
                    GUILayout.Label("<color=#999999>Two-tone letters (top to bottom)</color>", _rich);
                    Grid(SignTags.Gradients.Count, 4, i =>
                    {
                        var (name, everyone) = SignTags.Gradients[i];
                        if (GUILayout.Button(new GUIContent(name, everyone ? "Everyone sees this." : "<color=#ffb030>Only players with this mod see this; others see the tag text.</color>"), GUILayout.Width(84f)))
                            Wrap(f, $"<gradient=\"{name}\">", "</gradient>");
                    });
                    GUILayout.EndScrollView();
                    break;
            }
            GUILayout.Label("<color=#999999>Select text in the box first, then press a button. With nothing selected, the tag pair is put at the cursor.</color>", _rich);

            // Default color for signs without their own color.
            string def = VanillaPlusPlugin.SignDefaultColor.Value?.Trim().TrimStart('#') ?? "";
            GUILayout.BeginHorizontal();
            GUILayout.Label(def.Length > 0 ? $"Default for my signs: <color=#{def}>#{def}</color>" : "Default for my signs: <color=#999999>game default</color>", _rich);
            if (GUILayout.Button("Use current color", GUILayout.Width(125f)))
            {
                VanillaPlusPlugin.SignDefaultColor.Value = Hex();
                Recolor(f); // and switch the text in the box to it: existing colors are replaced
            }
            if (GUILayout.Button("No default", GUILayout.Width(85f))) VanillaPlusPlugin.SignDefaultColor.Value = "";
            GUILayout.EndHorizontal();

            // Icons: four sets, picked with tabs.
            if (_icons == null) _icons = FindIcons(f);
            if (_symbols == null) _symbols = SignIcons.Symbols((Queued(TextInput.instance) as Sign)?.m_textWidget?.font);
            if (_arrows == null) _arrows = SignIcons.Available((Queued(TextInput.instance) as Sign)?.m_textWidget?.font, SignIcons.ArrowCandidates);
            _iconTab = GUILayout.Toolbar(_iconTab, new[] { "Symbols", "Items", "Map pins", "Faces & ?", "Arrows" });
            _iconScroll = GUILayout.BeginScrollView(_iconScroll, GUILayout.Height(150f));
            switch (_iconTab)
            {
                case 0: // plain characters: everyone sees these
                    if (_symbols.Count == 0) GUILayout.Label("<color=#ffb030>The sign font has none of the symbols.</color>", _rich);
                    Grid(_symbols.Count, 9, i => { if (GUILayout.Button(_symbols[i], GUILayout.Width(34f), GUILayout.Height(28f))) Insert(f, _symbols[i]); });
                    break;
                case 1:
                case 2:
                    var set = _iconTab == 1 ? SignIcons.Items : SignIcons.Pins;
                    if (!VanillaPlusPlugin.SignCustomIcons.Value) { GUILayout.Label("<color=#ffb030>Item / map pin icons are turned off ([Signs] CustomIcons).</color>", _rich); break; }
                    if (_iconTab == 1)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Find:", GUILayout.Width(40f));
                        _iconSearch = GUILayout.TextField(_iconSearch ?? "", 30);
                        GUILayout.EndHorizontal();
                    }
                    string q = _iconTab == 1 ? (_iconSearch ?? "").Trim() : "";
                    var shown = set.Where(ic => q.Length == 0 || ic.Label.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0
                                             || ic.Name.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) >= 0).Take(120).ToList();
                    if (set.Count == 0) GUILayout.Label("<color=#999999>Not loaded yet.</color>", _rich);
                    Grid(shown.Count, 8, i =>
                    {
                        var ic = shown[i];
                        var r = GUILayoutUtility.GetRect(38f, 38f, GUILayout.Width(38f), GUILayout.Height(38f));
                        if (GUI.Button(r, new GUIContent("", ic.Label))) Insert(f, $"<sprite name=\"{ic.Name}\">");
                        DrawSprite(new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f), ic.Sprite);
                    });
                    if (shown.Count == 120) GUILayout.Label("<color=#999999>Showing the first 120 - type to narrow down.</color>", _rich);
                    break;
                case 4:
                    // Arrow characters the sign font really has.
                    if (_arrows.Count > 0)
                    {
                        GUILayout.Label("<color=#999999>Arrow characters in the sign font</color>", _rich);
                        Grid(_arrows.Count, 9, i => { if (GUILayout.Button(_arrows[i], GUILayout.Width(34f), GUILayout.Height(28f))) Insert(f, _arrows[i]); });
                    }
                    else GUILayout.Label("<color=#ffb030>The sign font has no arrow characters; use the turned ones below.</color>", _rich);
                    // Eight directions made by turning one character, so they work whatever the font has:
                    // the font's own → if it has one, and the > sign, which every font has.
                    if (_arrows.Contains("→"))
                    {
                        GUILayout.Label("<color=#999999>Arrow, any direction</color>", _rich);
                        Row(() => { for (int i = 0; i < Directions.Length; i++) Turned(f, "→", i); });
                    }
                    GUILayout.Label("<color=#999999>Pointer, any direction (works in every font)</color>", _rich);
                    Row(() => { for (int i = 0; i < Directions.Length; i++) Turned(f, ">", i); });
                    break;
                default: // the game's built-in sheet
                    Grid(_icons.Count, 3, i =>
                    {
                        if (GUILayout.Button(Friendly(_icons[i]), GUILayout.Width(112f))) Insert(f, $"<sprite name=\"{_icons[i]}\">");
                    });
                    break;
            }
            GUILayout.EndScrollView();
            string tip = GUI.tooltip;
            GUILayout.Label(!string.IsNullOrEmpty(tip) ? tip
                : _iconTab == 1 || _iconTab == 2 ? "<color=#ffb030>Only players with this mod see item / map pin icons; others see the tag text.</color>"
                : "<color=#999999>Everyone sees these, with or without the mod.</color>", _rich);
            GUILayout.EndArea();
        }

        // The game's icon set is a small emoji sheet; most entries are named by their Unicode code.
        private static readonly Dictionary<string, string> IconNames = new Dictionary<string, string>
        {
            { ".notdef", "? box" },
            { "1f600", "Grinning" }, { "1f601", "Beaming" }, { "1f602", "Tears of joy" }, { "1f603", "Big smile" },
            { "1f604", "Smile" }, { "1f605", "Sweat smile" }, { "1f606", "Laughing" }, { "1f609", "Wink" },
            { "1f60a", "Smiling eyes" }, { "1f60b", "Yum" }, { "1f60d", "Heart eyes" }, { "1f60e", "Sunglasses" },
            { "1f923", "Rolling laugh" }, { "263a", "Smiling" }, { "2639", "Frowning" },
            { "Smiling face with smiling eyes", "Smiling eyes" }, { "Grinning face", "Grinning" }, { "Face with tears of joy", "Tears of joy" },
        };

        private static string Friendly(string name) => IconNames.TryGetValue(name, out var n) ? n : name;

        // count items in rows of `perRow`.
        private static void Grid(int count, int perRow, System.Action<int> cell)
        {
            int col = 0;
            for (int i = 0; i < count; i++)
            {
                if (col == 0) GUILayout.BeginHorizontal();
                cell(i);
                if (++col == perRow) { GUILayout.EndHorizontal(); col = 0; }
            }
            if (col != 0) GUILayout.EndHorizontal();
        }

        // Button label and how far the character is turned (degrees, anticlockwise from pointing right).
        private static readonly (string label, int angle)[] Directions =
            { ("↑", 90), ("↗", 45), ("→", 0), ("↘", -45), ("↓", -90), ("↙", -135), ("←", 180), ("↖", 135) };

        private static void Turned(TMP_InputField f, string character, int direction)
        {
            var (label, angle) = Directions[direction];
            if (!GUILayout.Button(label, GUILayout.Width(40f), GUILayout.Height(28f))) return;
            Insert(f, angle == 0 ? character : $"<rotate={angle}>{character}</rotate>");
        }

        private static void Row(System.Action buttons)
        {
            GUILayout.BeginHorizontal();
            buttons();
            GUILayout.EndHorizontal();
        }

        private static void Tag(TMP_InputField f, string label, string open, string close)
        {
            if (GUILayout.Button(label)) Wrap(f, open, close);
        }

        private static void DrawSprite(Rect r, Sprite sprite)
        {
            if (sprite == null || sprite.texture == null || Event.current.type != EventType.Repaint) return;
            var tr = sprite.textureRect; var tex = sprite.texture;
            GUI.DrawTextureWithTexCoords(r, tex, new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
        }

        private static float Channel(string label, float v)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} {Mathf.RoundToInt(v * 255f)}", GUILayout.Width(50f));
            v = GUILayout.HorizontalSlider(v, 0f, 1f);
            GUILayout.EndHorizontal();
            return Mathf.Round(v * 255f) / 255f;
        }

        private static void SetColor(Color c) { _color = c; _hex = ColorUtility.ToHtmlStringRGB(c); }
        private static string Hex() => ColorUtility.ToHtmlStringRGB(_color);

        // Put open/close around the selection (or an empty pair at the cursor).
        private static readonly Regex OpenColor = new Regex(@"<color[^>]*>|<#[0-9a-fA-F]{3,8}>", RegexOptions.IgnoreCase);

        // Replace every color already in the text with the current one; text without any color gets
        // wrapped in it. Icons, bold / italic / size tags are left as they are.
        private static void Recolor(TMP_InputField f)
        {
            string t = f.text ?? "";
            if (t.Length == 0) return;
            string tag = $"<#{Hex()}>";
            Apply(f, OpenColor.IsMatch(t) ? OpenColor.Replace(t, tag) : tag + t + "</color>", -1);
        }

        // Color the selection with the current color, replacing a color it already has instead of
        // nesting a second tag inside the first.
        private static void ColorSelection(TMP_InputField f)
        {
            string t = f.text ?? "";
            int a = Mathf.Clamp(_selStart, 0, t.Length), b = Mathf.Clamp(_selEnd, 0, t.Length);
            string tag = $"<#{Hex()}>";
            if (a == b) { Wrap(f, tag, "</color>"); return; }

            string before = t.Substring(0, a), sel = t.Substring(a, b - a), after = t.Substring(b);
            // Selection is exactly what an existing color tag wraps: just swap that tag's color.
            var open = Regex.Match(before, @"(<color[^>]*>|<#[0-9a-fA-F]{3,8}>)$", RegexOptions.IgnoreCase);
            if (open.Success && after.StartsWith("</color>", System.StringComparison.OrdinalIgnoreCase) && !ColorTags.IsMatch(sel))
            {
                Apply(f, before.Substring(0, open.Index) + tag + sel + after, open.Index + tag.Length + sel.Length + "</color>".Length);
                return;
            }
            // Selection includes its own color tags (whole or partial): drop them, then wrap once.
            sel = ColorTags.Replace(sel, "");
            Apply(f, before + tag + sel + "</color>" + after, before.Length + tag.Length + sel.Length + "</color>".Length);
        }

        private static void Wrap(TMP_InputField f, string open, string close)
        {
            string t = f.text ?? "";
            int a = Mathf.Clamp(_selStart, 0, t.Length), b = Mathf.Clamp(_selEnd, 0, t.Length);
            if (a == b) { a = b = Mathf.Clamp(_caret, 0, t.Length); }
            string result = t.Substring(0, a) + open + t.Substring(a, b - a) + close + t.Substring(b);
            Apply(f, result, a == b ? a + open.Length : b + open.Length + close.Length);
        }

        private static void Insert(TMP_InputField f, string what)
        {
            string t = f.text ?? "";
            int at = Mathf.Clamp(_caret, 0, t.Length);
            Apply(f, t.Substring(0, at) + what + t.Substring(at), at + what.Length);
        }

        private static void Apply(TMP_InputField f, string text, int caret)
        {
            if (f.characterLimit > 0 && text.Length > f.characterLimit)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, $"Too long for the sign ({text.Length}/{f.characterLimit})");
                return;
            }
            f.text = text;
            f.ActivateInputField();
            int pos = caret < 0 ? text.Length : Mathf.Clamp(caret, 0, text.Length);
            f.stringPosition = pos;
            f.selectionStringAnchorPosition = pos;
            f.selectionStringFocusPosition = pos;
            _selStart = _selEnd = _caret = pos;
        }

        // IMGUI understands <color=#hex>, not TMP's <#hex>; icons are shown as [name].
        private static string Preview(string t)
        {
            if (string.IsNullOrEmpty(t)) return " ";
            t = Regex.Replace(t, @"<#([0-9a-fA-F]{6,8})>", "<color=#$1>");
            t = Regex.Replace(t, "<sprite[^>]*name=\"([^\"]+)\"[^>]*>", m => "[" + Friendly(m.Groups[1].Value) + "]");
            // Everything IMGUI can't draw is dropped from the preview (it keeps b, i and color).
            t = Regex.Replace(t, @"</?(?!b>|i>|color)[a-z-]+[^>]*>", "", RegexOptions.IgnoreCase);
            return t;
        }

        // Icons TextMeshPro can resolve by name: the sign text's own sprite asset, the game's default
        // one, and their fallbacks.
        private static List<string> FindIcons(TMP_InputField f)
        {
            var names = new List<string>();
            var seen = new HashSet<TMP_SpriteAsset>();
            void Add(TMP_SpriteAsset a)
            {
                if (a == null || !seen.Add(a) || a.name.StartsWith("VanillaPlusIcons_")) return; // our item / pin sets have their own tabs
                if (a.spriteCharacterTable != null)
                    foreach (var ch in a.spriteCharacterTable)
                        if (!string.IsNullOrEmpty(ch?.name) && !names.Contains(ch.name)) names.Add(ch.name);
                if (a.fallbackSpriteAssets != null) foreach (var fb in a.fallbackSpriteAssets) Add(fb);
            }
            try
            {
                Add(TMP_Settings.defaultSpriteAsset);
                if (Queued(TextInput.instance) is Sign sign && sign.m_textWidget != null) Add(sign.m_textWidget.spriteAsset);
            }
            catch (System.Exception e) { VanillaPlusPlugin.Log.LogInfo($"Sign editor: no icon set ({e.Message})"); }
            VanillaPlusPlugin.Log.LogInfo($"Sign editor: {names.Count} sign icons available" + (names.Count > 0 ? ": " + string.Join(", ", names.Take(40)) : ""));
            return names.OrderBy(n => n == ".notdef" ? "" : Friendly(n)).ToList(); // "? box" first
        }
    }

    // Default color: a confirmed sign text without any color tag gets wrapped in [Signs] DefaultColor.
    [HarmonyPatch(typeof(Sign), nameof(Sign.SetText))]
    internal static class SignEditor_DefaultColor
    {
        private static readonly Regex HasColor = new Regex(@"<color[^>]*>|<#[0-9a-fA-F]{3,8}>", RegexOptions.IgnoreCase);

        private static void Prefix(ref string text)
        {
            if (!VanillaPlusPlugin.SignEditorOn || string.IsNullOrWhiteSpace(text)) return;
            string def = VanillaPlusPlugin.SignDefaultColor.Value?.Trim().TrimStart('#') ?? "";
            if (def.Length == 0 || HasColor.IsMatch(text)) return;
            if (!ColorUtility.TryParseHtmlString("#" + def, out _) && !ColorUtility.TryParseHtmlString(def, out _)) return; // not a color
            // Hex codes use the short <#RRGGBB> tag; names (white, red, ...) the <color=name> form.
            bool isHex = Regex.IsMatch(def, "^[0-9a-fA-F]{3,8}$");
            text = (isHex ? $"<#{def}>" : $"<color={def}>") + text + "</color>";
        }
    }

    // Tags use up characters, so signs get a longer limit while the editor is on.
    [HarmonyPatch(typeof(TextInput), nameof(TextInput.RequestText))]
    internal static class SignEditor_Limit
    {
        private static void Prefix(TextReceiver sign, ref int charLimit)
        {
            if (VanillaPlusPlugin.SignEditorOn && sign is Sign)
                charLimit = Mathf.Max(charLimit, VanillaPlusPlugin.SignCharLimit.Value);
        }
    }
}
