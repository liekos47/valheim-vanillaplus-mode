using UnityEngine;

namespace ValheimVanillaPlus
{
    // Where to draw a panel that goes with the game's "Enter text" box (sign editor, color panel,
    // item icon grid) so that it never covers the box, whatever the screen size and interface scale.
    internal static class PanelPlace
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        // To the right of the box when at least minWidth fits there (narrower than `width` if need
        // be), else to its left, else under or above it, whichever has more room, cut short at the
        // screen edge rather than pushed over the box.
        public static Rect Beside(GameObject box, float width, float minWidth, float height)
        {
            const float gap = 12f, edge = 4f;
            var rt = box != null ? box.GetComponent<RectTransform>() : null;
            if (rt == null)
                return new Rect(Mathf.Min(Screen.width / 2f + 260f, Screen.width - width - edge), Mathf.Max(edge, Screen.height / 2f - height / 2f), width, height);

            rt.GetWorldCorners(Corners); // 0 = bottom-left, 2 = top-right, in screen pixels
            float left = Corners[0].x, right = Corners[2].x;
            float top = Screen.height - Corners[2].y, bottom = Screen.height - Corners[0].y;

            // Beside it: top edges level, kept on screen.
            float y = Mathf.Clamp(top, edge, Mathf.Max(edge, Screen.height - height - edge));
            float roomRight = Screen.width - edge - (right + gap);
            if (roomRight >= minWidth) return new Rect(right + gap, y, Mathf.Min(width, roomRight), Mathf.Min(height, Screen.height - 2f * edge));
            float roomLeft = left - gap - edge;
            if (roomLeft >= minWidth)
            {
                float w = Mathf.Min(width, roomLeft);
                return new Rect(left - gap - w, y, w, Mathf.Min(height, Screen.height - 2f * edge));
            }

            // Under or above it.
            float fullWidth = Mathf.Min(width, Screen.width - 2f * edge);
            float x = Mathf.Clamp(left, edge, Mathf.Max(edge, Screen.width - fullWidth - edge));
            float roomBelow = Screen.height - edge - (bottom + gap), roomAbove = top - gap - edge;
            if (roomBelow >= roomAbove) return new Rect(x, bottom + gap, fullWidth, Mathf.Clamp(roomBelow, 60f, height));
            float h = Mathf.Clamp(roomAbove, 60f, height);
            return new Rect(x, top - gap - h, fullWidth, h);
        }
    }
}
