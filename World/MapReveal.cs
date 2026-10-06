using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Reveal the whole map: clears the map's fog *texture* only. Your game already holds the entire
    // world map (it builds it from the seed the server sends); the fog is a second picture laid over
    // it on your side. The explored-area data (Minimap.m_explored, which is what gets saved to your
    // character) is never touched, so nothing is saved and turning it off brings back exactly the
    // areas you really explored. No dev command is used and nothing is asked of the server.
    internal static class MapReveal
    {
        private static readonly AccessTools.FieldRef<Minimap, Texture2D> FogTexture =
            AccessTools.FieldRefAccess<Minimap, Texture2D>("m_fogTexture");
        private static readonly AccessTools.FieldRef<Minimap, BitArray> Explored =
            AccessTools.FieldRefAccess<Minimap, BitArray>("m_explored");
        private static readonly AccessTools.FieldRef<Minimap, BitArray> ExploredOthers =
            AccessTools.FieldRefAccess<Minimap, BitArray>("m_exploredOthers");

        private static Minimap _revealedOn;
        private static Color32[] _snapshot;

        public static void Update()
        {
            var map = Minimap.instance;
            bool want = map != null && VanillaPlusPlugin.RevealMapOn;

            if (_revealedOn != null && (_revealedOn != map || !want)) Restore();
            if (want && _revealedOn == null) Reveal(map);
        }

        // Hot reload: put the fog back, so the next build starts from the real picture.
        public static void Cleanup()
        {
            if (_revealedOn != null) Restore();
        }

        private static void Reveal(Minimap map)
        {
            var tex = FogTexture(map);
            if (tex == null) return;
            _snapshot = tex.GetPixels32();
            var clear = new Color32[_snapshot.Length];
            for (int i = 0; i < clear.Length; i++)
                clear[i] = new Color32(0, 0, _snapshot[i].b, _snapshot[i].a); // r = me, g = others: 0 = explored
            tex.SetPixels32(clear);
            tex.Apply();
            _revealedOn = map;
        }

        private static void Restore()
        {
            var map = _revealedOn;
            _revealedOn = null;
            if (map == null || _snapshot == null) return; // map destroyed (left the world): nothing to restore
            var tex = FogTexture(map);
            var mine = Explored(map);
            var others = ExploredOthers(map);
            if (tex == null) return;

            // Snapshot plus anything explored while revealed.
            var px = _snapshot;
            for (int i = 0; i < px.Length; i++)
            {
                if (mine != null && i < mine.Length && mine[i]) px[i].r = 0;
                if (others != null && i < others.Length && others[i]) px[i].g = 0;
            }
            tex.SetPixels32(px);
            tex.Apply();
            _snapshot = null;
        }
    }
}
