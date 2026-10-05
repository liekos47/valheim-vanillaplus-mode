using UnityEngine;

namespace ValheimVanillaPlus
{
    // On-screen frames-per-second counter (top left). Display only.
    internal static class FpsCounter
    {
        public static float Fps;
        private static int _frames;
        private static float _since;
        private static GUIStyle _style;

        // Once per frame: plain frames per second over the last half second.
        public static void CountFrame()
        {
            _frames++;
            float now = Time.unscaledTime;
            if (now - _since < 0.5f) return;
            Fps = _frames / (now - _since);
            _frames = 0;
            _since = now;
        }

        // Green from 50, yellow from 25, red below.
        public static void Draw()
        {
            if (!VanillaPlusPlugin.ShowFpsOn) return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            string text = $"{Fps:0} FPS";
            var rect = new Rect(8f, 4f, 120f, 24f);
            _style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _style);
            _style.normal.textColor = Fps >= 50f ? Color.green : Fps >= 25f ? Color.yellow : Color.red;
            GUI.Label(rect, text, _style);
        }
    }
}
