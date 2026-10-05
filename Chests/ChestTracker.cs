using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Keeps a list of the chests the game has loaded, so features that need "the chests near me"
    // (craft from chests) don't have to search the whole scene. Nothing is drawn or changed here.
    internal static class ChestTracker
    {
        internal enum Kind { Loot, PlayerBuilt, Tombstone }

        internal struct Entry
        {
            public Kind Kind;
            public ZNetView View; // invalid for build-mode ghosts, which are skipped
        }

        // Filled by the Container.Awake patch; destroyed chests become Unity-null and are pruned.
        private static readonly Dictionary<Container, Entry> Known = new Dictionary<Container, Entry>();
        private static readonly List<Container> Dead = new List<Container>();
        private static bool _scanned;
        private static int _sincePrune;

        internal static void Register(Container c)
        {
            Kind kind = c.GetComponent<TombStone>() != null ? Kind.Tombstone
                : c.GetComponentInParent<Piece>() != null ? Kind.PlayerBuilt
                : Kind.Loot;
            Known[c] = new Entry { Kind = kind, View = c.GetComponentInParent<ZNetView>() };
            if (++_sincePrune >= 200) Prune();
        }

        internal static Dictionary<Container, Entry> All()
        {
            // Chests that were already loaded when this build started (hot reload) never ran the Awake patch.
            if (!_scanned)
            {
                _scanned = true;
                foreach (var c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None))
                    if (!Known.ContainsKey(c)) Register(c);
            }
            return Known;
        }

        private static void Prune()
        {
            _sincePrune = 0;
            foreach (var c in Known.Keys) if (c == null) Dead.Add(c);
            foreach (var c in Dead) Known.Remove(c);
            Dead.Clear();
        }
    }

    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class ChestTracker_Register
    {
        private static void Postfix(Container __instance) => ChestTracker.Register(__instance);
    }
}
