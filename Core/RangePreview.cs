using UnityEngine;

namespace ValheimVanillaPlus
{
    // Range preview: while you drag a distance slider in the menu or the storage window ("Range (m)", "Chest range (m)", ...),
    // a circle of that radius is drawn on the ground around you, following the terrain (or the water
    // surface), with the value written on it. It fades out ~2 s after you stop changing the value.
    // Purely visual.
    internal static class RangePreview
    {
        private const int Segments = 72;
        private const float Hold = 1.5f, Fade = 0.7f;
        private static float _radius, _changedAt = -99f;
        private static GUIStyle _label;
        private static readonly Vector3[] Ring = new Vector3[Segments + 1];

        // Called by a range slider whenever its value changes.
        public static void Show(float radius)
        {
            _radius = radius;
            _changedAt = Time.unscaledTime;
        }

        public static void Draw()
        {
            float age = Time.unscaledTime - _changedAt;
            if (age > Hold + Fade || _radius <= 0.1f || !VanillaPlusPlugin.RangePreviewEnabled.Value) return;
            var p = Player.m_localPlayer;
            var cam = Camera.main;
            if (p == null || cam == null) return;

            float alpha = age <= Hold ? 1f : 1f - (age - Hold) / Fade;
            var color = new Color(0.3f, 0.9f, 1f, 0.9f * alpha);
            Vector3 c = p.transform.position;

            for (int i = 0; i <= Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                var pt = new Vector3(c.x + Mathf.Cos(a) * _radius, c.y, c.z + Mathf.Sin(a) * _radius);
                pt.y = SurfaceY(pt, c.y) + 0.15f;
                Ring[i] = pt;
            }

            Vector2? prev = null;
            float labelY = float.MinValue; Vector2 labelAt = default;
            for (int i = 0; i <= Segments; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(Ring[i]);
                if (sp.z <= 0.1f) { prev = null; continue; }
                var cur = new Vector2(sp.x, Screen.height - sp.y);
                if (prev.HasValue && (cur - prev.Value).sqrMagnitude < 4f * Screen.width * Screen.width)
                    Overlay.DrawLine(prev.Value, cur, color, 2.5f);
                prev = cur;
                if (cur.y > labelY && cur.x > 0 && cur.x < Screen.width && cur.y < Screen.height) { labelY = cur.y; labelAt = cur; }
            }

            // Value label on the nearest visible part of the ring (lowest on screen).
            if (labelY > float.MinValue)
            {
                if (_label == null) _label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
                string text = $"{_radius:0} m";
                GUI.color = new Color(0f, 0f, 0f, alpha);
                GUI.Label(new Rect(labelAt.x - 59f, labelAt.y - 29f, 120f, 22f), text, _label);
                GUI.color = color;
                GUI.Label(new Rect(labelAt.x - 60f, labelAt.y - 30f, 120f, 22f), text, _label);
                GUI.color = Color.white;
            }
        }

        // Ground height, or the water surface where that's higher; falls back to the player's height.
        private static float SurfaceY(Vector3 pt, float fallback)
        {
            float y = ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(pt) : fallback;
            float water = Floating.GetLiquidLevel(pt);
            if (water > y && water > -1000f) y = water;
            // Very far points may be in unloaded terrain (height 0): keep them near the player's level.
            return Mathf.Abs(y - fallback) > 200f ? fallback : y;
        }
    }
}
