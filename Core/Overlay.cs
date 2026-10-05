using UnityEngine;

namespace ValheimVanillaPlus
{
    // Shared screen-space drawing for the highlight features (call from OnGUI, Repaint only).
    internal static class Overlay
    {
        private const float LineWidth = 2f;
        private static readonly Vector3[] Corners = new Vector3[8];
        private static GUIStyle _label;

        // Projects an upright box (radius x height, standing on basePos) and returns its screen rect.
        public static bool TryProjectBox(Camera cam, Vector3 basePos, float radius, float height, out Rect rect)
        {
            rect = default;
            int i = 0;
            for (int y = 0; y < 2; y++)
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        Corners[i++] = basePos + new Vector3(x * radius, y * height, z * radius);

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var corner in Corners)
            {
                Vector3 sp = cam.WorldToScreenPoint(corner);
                if (sp.z <= 0f) return false; // behind the camera
                float guiY = Screen.height - sp.y;
                minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
                minY = Mathf.Min(minY, guiY); maxY = Mathf.Max(maxY, guiY);
            }

            if (maxX < 0 || minX > Screen.width || maxY < 0 || minY > Screen.height) return false;
            rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        public static void DrawBox(Rect r, Color color)
        {
            GUI.color = color;
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, r.width, LineWidth), tex);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - LineWidth, r.width, LineWidth), tex);
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, LineWidth, r.height), tex);
            GUI.DrawTexture(new Rect(r.xMax - LineWidth, r.yMin, LineWidth, r.height), tex);
            GUI.color = Color.white;
        }

        // Line between two GUI points (tracers).
        public static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;
            var saved = GUI.matrix;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2f, len, width), Texture2D.whiteTexture);
            GUI.matrix = saved;
            GUI.color = Color.white;
        }

        public static void DrawLabel(Rect box, string text, Color color)
        {
            if (_label == null)
                _label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerCenter, fontSize = 13, fontStyle = FontStyle.Bold };
            GUI.color = color;
            GUI.Label(new Rect(box.center.x - 150f, box.yMin - 22f, 300f, 20f), text, _label);
            GUI.color = Color.white;
        }
    }
}
