using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace ValheimVanillaPlus
{
    // Extra sign icons. TextMeshPro can only draw <sprite name="..."> for sprites it knows, and the
    // game ships just a small emoji sheet (+ the "?" box, ".notdef"). This registers the game's own
    // item icons (by prefab name: Wood, Stone, Iron, Coins, ...) and map-pin icons (pin_Fire, ...) as
    // extra sprite sets, as fallbacks of the game's default set, so signs can show them.
    // IMPORTANT: that's local. Players without this mod don't have these sets and see the raw tag text
    // on such signs. Symbols (plain characters), the "?" box and the faces work for everyone.
    internal static class SignIcons
    {
        internal class Icon { public string Name, Label; public Sprite Sprite; }

        public static readonly List<Icon> Items = new List<Icon>();
        public static readonly List<Icon> Pins = new List<Icon>();
        private static readonly Dictionary<Texture, TMP_SpriteAsset> Assets = new Dictionary<Texture, TMP_SpriteAsset>();
        private static readonly System.Reflection.FieldInfo VersionField = AccessTools.Field(typeof(TMP_Asset), "m_Version");
        private static readonly System.Reflection.FieldInfo FaceField = AccessTools.Field(typeof(TMP_Asset), "m_FaceInfo");
        private static ObjectDB _builtFor;
        private static bool _pinsBuilt;

        // Plain characters offered as "symbols"; only the ones the sign font can draw are listed.
        public const string SymbolCandidates =
            "←↑→↓↔↕⇐⇒★☆✓✔✗✘●○■□▲▼◆◇♥♦♣♠☠⚠⚔⚒⚓☀☁❄⚡☂♪♫✦✧☘⚑⚐∞≈≠±×÷°•…§¶©®™";

        public static void Update()
        {
            if (!VanillaPlusPlugin.SignEditorOn || !VanillaPlusPlugin.SignCustomIcons.Value) return;
            var db = ObjectDB.instance;
            if (db == null || db.m_items == null || db.m_items.Count == 0 || TMP_Settings.defaultSpriteAsset == null) return;
            bool changed = false;
            if (_builtFor != db) { _builtFor = db; BuildItems(db); changed = true; }
            if (!_pinsBuilt && Minimap.instance != null) { _pinsBuilt = true; BuildPins(); changed = true; }
            if (!changed) return;

            // Signs that were drawn before the sets existed show the raw tag: redraw them.
            foreach (var sign in Object.FindObjectsByType<Sign>(FindObjectsSortMode.None))
                if (sign != null && sign.m_textWidget != null && sign.m_textWidget.text != null && sign.m_textWidget.text.Contains("<sprite"))
                    sign.m_textWidget.ForceMeshUpdate(true, true);
            VanillaPlusPlugin.Log.LogInfo($"Sign icons: {Items.Count} item icons, {Pins.Count} map pin icons registered in {Assets.Count} sprite set(s)");
        }

        // Hot reload: take our sprite sets out of the game's fallback list; the next build registers its own.
        public static void Cleanup()
        {
            var fallbacks = TMP_Settings.instance != null && TMP_Settings.defaultSpriteAsset != null ? TMP_Settings.defaultSpriteAsset.fallbackSpriteAssets : null;
            if (fallbacks != null) foreach (var a in Assets.Values) fallbacks.Remove(a);
            Assets.Clear();
        }

        private static void BuildItems(ObjectDB db)
        {
            Items.Clear();
            foreach (var go in db.m_items)
            {
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                var icons = drop?.m_itemData?.m_shared?.m_icons;
                if (icons == null || icons.Length == 0 || icons[0] == null) continue;
                if (!Register(go.name, icons[0])) continue;
                string label = Localization.instance.Localize(drop.m_itemData.m_shared.m_name);
                Items.Add(new Icon { Name = go.name, Label = string.IsNullOrEmpty(label) || label.StartsWith("$") ? go.name : label, Sprite = icons[0] });
            }
            Items.Sort((a, b) => string.Compare(a.Label, b.Label, System.StringComparison.OrdinalIgnoreCase));
            foreach (var a in Assets.Values) a.UpdateLookupTables();
        }

        private static void BuildPins()
        {
            Pins.Clear();
            foreach (var data in Minimap.instance.m_icons)
            {
                if (data.m_icon == null) continue;
                string name = "pin_" + data.m_name;
                if (Register(name, data.m_icon)) Pins.Add(new Icon { Name = name, Label = data.m_name.ToString(), Sprite = data.m_icon });
            }
            foreach (var a in Assets.Values) a.UpdateLookupTables();
        }

        // One sprite set per source texture (TextMeshPro sprite sets have a single texture each).
        private static bool Register(string name, Sprite sprite)
        {
            var tex = sprite.texture;
            if (tex == null) return false;
            if (!Assets.TryGetValue(tex, out var asset))
            {
                var def = TMP_Settings.defaultSpriteAsset;
                asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                asset.name = "VanillaPlusIcons_" + tex.name;
                VersionField?.SetValue(asset, "1.1.0"); // otherwise TextMeshPro "upgrades" it and wipes the tables
                asset.spriteSheet = tex;
                var mat = new Material(def.material) { name = asset.name };
                mat.SetTexture(ShaderUtilities.ID_MainTex, tex);
                asset.material = mat;
                var face = new FaceInfo { pointSize = 64, scale = 1f, lineHeight = 64f, ascentLine = 52f, descentLine = -12f, baseline = 0f };
                FaceField?.SetValue(asset, face);
                if (asset.spriteGlyphTable == null || asset.spriteCharacterTable == null) return false;
                Assets[tex] = asset;
                if (def.fallbackSpriteAssets == null) def.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
                def.fallbackSpriteAssets.Add(asset);
            }
            if (asset.spriteCharacterTable.Any(c => c.name == name)) return true;

            Rect r = sprite.textureRect;
            uint index = (uint)asset.spriteGlyphTable.Count;
            float scale = 64f / Mathf.Max(1f, r.height); // every icon ends up about one text line tall
            var glyph = new TMP_SpriteGlyph(index,
                new GlyphMetrics(r.width, r.height, 0f, r.height * 0.82f, r.width),
                new GlyphRect((int)r.x, (int)r.y, (int)r.width, (int)r.height), scale, 0, sprite);
            asset.spriteGlyphTable.Add(glyph);
            var ch = new TMP_SpriteCharacter(0xFFFE, asset, glyph) { name = name, scale = 1f };
            asset.spriteCharacterTable.Add(ch);
            return true;
        }

        // Arrow characters, offered on their own tab when the sign font can draw them.
        public const string ArrowCandidates = "←↑→↓↖↗↘↙↔↕⇐⇑⇒⇓⇔➔➜➤▶◀▲▼►◄«»‹›";

        // Symbols the given font (with its fallbacks) can draw.
        public static List<string> Symbols(TMP_FontAsset font) => Available(font, SymbolCandidates);

        public static List<string> Available(TMP_FontAsset font, string candidates)
        {
            var list = new List<string>();
            foreach (char c in candidates)
                if (font == null || font.HasCharacter(c, true)) list.Add(c.ToString());
            return list;
        }
    }
}
