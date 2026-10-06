using UnityEngine;

namespace ValheimVanillaPlus
{
    // Resizing for the mod's IMGUI windows: a grip in the bottom-right corner that you drag.
    // Inside a window function call Handle() FIRST (so the grip gets the mouse before a button that
    // happens to sit in that corner) and Draw() LAST (so the grip is painted on top).
    internal static class WindowResize
    {
        private const float Grip = 18f;
        private static Vector2 _startMouse, _startSize;

        // size: the window's wanted size, changed while dragging. Returns true on the frame the drag
        // ends, which is when to save the size.
        public static bool Handle(int windowId, Rect window, ref Vector2 size, Vector2 min, Vector2 max)
        {
            int id = GUIUtility.GetControlID(windowId ^ 0x5A17, FocusType.Passive);
            var e = Event.current;
            var grip = new Rect(window.width - Grip, window.height - Grip, Grip, Grip);
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && grip.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        _startMouse = GUIUtility.GUIToScreenPoint(e.mousePosition);
                        _startSize = new Vector2(window.width, window.height);
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        Vector2 moved = GUIUtility.GUIToScreenPoint(e.mousePosition) - _startMouse;
                        size = new Vector2(Mathf.Clamp(_startSize.x + moved.x, min.x, Mathf.Max(min.x, max.x)),
                                           Mathf.Clamp(_startSize.y + moved.y, min.y, Mathf.Max(min.y, max.y)));
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                        return true;
                    }
                    break;
            }
            return false;
        }

        // Three short diagonal strokes in the corner.
        public static void Draw(Rect window)
        {
            if (Event.current.type != EventType.Repaint) return;
            var color = GUI.skin.label.normal.textColor;
            color.a = 0.6f;
            float x = window.width - 4f, y = window.height - 4f;
            for (int i = 1; i <= 3; i++)
                Overlay.DrawLine(new Vector2(x - i * 4f, y), new Vector2(x, y - i * 4f), color, 1.5f);
        }
    }
}
