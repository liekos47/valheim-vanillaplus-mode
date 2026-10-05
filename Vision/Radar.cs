using UnityEngine;

namespace ValheimVanillaPlus
{
    internal enum RadarCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    // Radar: a round overlay in a screen corner showing creatures and players within [Radar] Range
    // around you, turning with the camera (up = where you look). Dots: red = hostile, yellow =
    // passive, green = tamed, purple = boss, blue = players (with names). A small ^ / v next to a
    // dot means it's well above / below you. Purely visual and local.
    internal static class Radar
    {
        private static readonly Color Hostile = new Color(1f, 0.25f, 0.25f), Passive = new Color(1f, 0.9f, 0.3f),
            Tamed = new Color(0.3f, 1f, 0.4f), Boss = new Color(0.8f, 0.4f, 1f), OtherPlayer = new Color(0.3f, 0.8f, 1f);

        private static Texture2D _disc, _dot;
        private static GUIStyle _name;

        public static void Draw()
        {
            if (!VanillaPlusPlugin.RadarOn) return;
            var p = Player.m_localPlayer;
            var cam = Camera.main;
            if (p == null || cam == null) return;
            if (_disc == null) { _disc = Circle(128, true); _dot = Circle(16, false); }
            if (_name == null) _name = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };

            float size = VanillaPlusPlugin.RadarSize.Value;
            float range = Mathf.Max(5f, VanillaPlusPlugin.RadarRange.Value);
            Rect box = Placement(size);
            Vector2 center = box.center;
            float radius = size / 2f;

            // Background + range rings + center arrow.
            GUI.color = new Color(0f, 0f, 0f, VanillaPlusPlugin.RadarOpacity.Value);
            GUI.DrawTexture(box, _disc);
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(center.x - 0.5f, center.y - radius, 1f, size), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(center.x - radius, center.y - 0.5f, size, 1f), Texture2D.whiteTexture);
            RingOutline(center, radius / 2f, new Color(1f, 1f, 1f, 0.15f));
            RingOutline(center, radius - 1f, new Color(1f, 1f, 1f, 0.35f));
            GUI.color = Color.white;
            Overlay.DrawLine(center + new Vector2(0f, -7f), center + new Vector2(-5f, 5f), Color.white, 2f);
            Overlay.DrawLine(center + new Vector2(0f, -7f), center + new Vector2(5f, 5f), Color.white, 2f);
            GUI.Label(new Rect(box.x + 4f, box.yMax - 18f, 80f, 16f), $"{range:0} m", _name);

            // Heading: camera yaw.
            Vector3 f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.001f) f = p.transform.forward;
            f.Normalize();
            Vector3 r = new Vector3(f.z, 0f, -f.x);
            Vector3 me = p.transform.position;

            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c == p || c.IsDead()) continue;
                bool isPlayer = c.IsPlayer();
                if (isPlayer ? !VanillaPlusPlugin.RadarPlayers.Value : !VanillaPlusPlugin.RadarMobs.Value) continue;
                bool tamed = !isPlayer && c.IsTamed();
                bool hostile = !isPlayer && !tamed && BaseAI.IsEnemy(p, c);
                if (!isPlayer && !hostile && !tamed && !VanillaPlusPlugin.RadarPassive.Value) continue;

                Vector3 rel = c.transform.position - me;
                float flat = new Vector2(rel.x, rel.z).magnitude;
                if (flat > range) continue;
                var pos = center + new Vector2(Vector3.Dot(rel, r), -Vector3.Dot(rel, f)) * (radius - 4f) / range;

                Color col = isPlayer ? OtherPlayer : c.IsBoss() ? Boss : tamed ? Tamed : hostile ? Hostile : Passive;
                float d = isPlayer || c.IsBoss() ? 8f : 6f;
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(pos.x - d / 2f - 1f, pos.y - d / 2f - 1f, d + 2f, d + 2f), _dot);
                GUI.color = col;
                GUI.DrawTexture(new Rect(pos.x - d / 2f, pos.y - d / 2f, d, d), _dot);

                if (Mathf.Abs(rel.y) > 5f)
                    GUI.Label(new Rect(pos.x + 4f, pos.y - 12f, 14f, 14f), rel.y > 0f ? "^" : "v", _name);
                if (isPlayer && VanillaPlusPlugin.RadarNames.Value)
                    GUI.Label(new Rect(pos.x + 6f, pos.y - 7f, 140f, 14f), c.GetHoverName(), _name);
            }
            GUI.color = Color.white;
        }

        private static Rect Placement(float size)
        {
            float ox = VanillaPlusPlugin.RadarOffsetX.Value, oy = VanillaPlusPlugin.RadarOffsetY.Value;
            switch (VanillaPlusPlugin.RadarPosition.Value)
            {
                case RadarCorner.TopLeft: return new Rect(ox, oy, size, size);
                case RadarCorner.BottomLeft: return new Rect(ox, Screen.height - oy - size, size, size);
                case RadarCorner.BottomRight: return new Rect(Screen.width - ox - size, Screen.height - oy - size, size, size);
                default: return new Rect(Screen.width - ox - size, oy, size, size);
            }
        }

        private static void RingOutline(Vector2 c, float rad, Color col)
        {
            const int n = 48;
            Vector2 prev = c + new Vector2(rad, 0f);
            for (int i = 1; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var cur = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                Overlay.DrawLine(prev, cur, col, 1f);
                prev = cur;
            }
        }

        // Soft-edged white disc, tinted with GUI.color when drawn.
        private static Texture2D Circle(int n, bool soft)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            float r = n / 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01(r - d);                  // anti-aliased edge
                    if (soft) a *= Mathf.Lerp(1f, 0.85f, d / r);     // slightly lighter toward the edge
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }
    }
}
