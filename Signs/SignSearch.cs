using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Sign search: type some text and every sign within range whose text contains it (any case, colour
    // and size tags ignored) is marked for a few seconds with a box, a line from the bottom of the
    // screen and its text, all cycling through the rainbow. Several words separated by commas match
    // any of them. Visual and local only; the signs themselves are not changed.
    internal static class SignSearch
    {
        private static readonly Regex Tags = new Regex("<[^>]+>");
        private static readonly List<(Sign sign, string text)> Hits = new List<(Sign, string)>();
        private static float _until;

        public static void Ask()
        {
            TextPrompt.Show("Search nearby signs for (text, commas = any of)", VanillaPlusPlugin.SignSearchText.Value, v =>
            {
                VanillaPlusPlugin.SignSearchText.Value = v;
                Run();
            });
        }

        public static void Run()
        {
            Hits.Clear();
            var p = Player.m_localPlayer;
            if (p == null) return;
            var terms = new List<string>();
            foreach (string part in VanillaPlusPlugin.SignSearchText.Value.Split(','))
                if (part.Trim().Length > 0) terms.Add(part.Trim());
            if (terms.Count == 0) { p.Message(MessageHud.MessageType.Center, "Sign search: nothing to look for"); return; }

            float r = VanillaPlusPlugin.SignSearchRadius.Value;
            Vector3 me = p.transform.position;
            int looked = 0;
            foreach (var sign in Object.FindObjectsByType<Sign>(FindObjectsSortMode.None))
            {
                if ((sign.transform.position - me).sqrMagnitude > r * r) continue;
                looked++;
                string text = Tags.Replace(sign.GetText() ?? "", "").Trim();
                foreach (string term in terms)
                    if (text.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) >= 0) { Hits.Add((sign, text)); break; }
            }
            _until = Time.unscaledTime + VanillaPlusPlugin.SignSearchSeconds.Value;
            string what = string.Join(", ", terms);
            p.Message(MessageHud.MessageType.Center, Hits.Count > 0
                ? $"Sign search: {Hits.Count} sign{(Hits.Count == 1 ? "" : "s")} with \"{what}\""
                : $"Sign search: no sign with \"{what}\" within {r:0} m ({looked} checked)");
            VanillaPlusPlugin.Log.LogInfo($"Sign search: \"{what}\" -> {Hits.Count} of {looked} signs within {r:0} m");
        }

        public static void Draw()
        {
            if (Hits.Count == 0) return;
            if (Time.unscaledTime > _until) { Hits.Clear(); return; }
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 camPos = cam.transform.position;
            int i = 0;
            foreach (var (sign, text) in Hits)
            {
                if (sign == null) continue;
                Vector3 pos = sign.transform.position;
                // Each hit sits a little further along the rainbow, so neighbours are told apart.
                Color color = Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * 0.8f + i++ * 0.13f, 1f), 1f, 1f);
                if (!Overlay.TryProjectBox(cam, pos - Vector3.up * 0.4f, 0.6f, 0.8f, out Rect rect)) continue;
                Overlay.DrawLine(new Vector2(Screen.width / 2f, Screen.height), new Vector2(rect.center.x, rect.yMax), color, 2f);
                Overlay.DrawBox(rect, color);
                Overlay.DrawLabel(rect, $"{(text.Length > 40 ? text.Substring(0, 40) + "…" : text)} [{Vector3.Distance(camPos, pos):0}m]", color);
            }
        }
    }
}
