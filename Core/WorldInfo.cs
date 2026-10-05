using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Small shared helpers about the world you're in.
    internal static class WorldInfo
    {
        private static readonly FieldInfo ServerHostField = AccessTools.Field(typeof(ZNet), "m_serverHost");

        // Identifies the current world (server address + world name + world id), for per-world files.
        public static string WorldKey()
        {
            var z = ZNet.instance;
            return z == null ? "" : $"{ServerHostField?.GetValue(null) as string ?? ""}|{z.GetWorldName()}|{z.GetWorldUID()}";
        }

        // Compass direction from one spot to another: N, NE, E, ...
        public static string Compass(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float angle = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
            string[] dirs = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return dirs[Mathf.RoundToInt(angle / 45f) % 8];
        }
    }
}
