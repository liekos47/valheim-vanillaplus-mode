using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // Night vision: after Valheim sets the scene lighting for the frame (EnvMan.SetEnv), raise the
    // ambient light to a minimum brightness. Fog is left as the game sets it. Daytime is barely
    // affected, since daylight is already brighter than the minimum. Purely visual and local.
    [HarmonyPatch(typeof(EnvMan), "SetEnv")]
    internal static class NightVision
    {
        private static void Postfix()
        {
            if (!VanillaPlusPlugin.NightVisionOn) return;

            float b = VanillaPlusPlugin.NightVisionBrightness.Value;
            Color a = RenderSettings.ambientLight;
            RenderSettings.ambientLight = new Color(Mathf.Max(a.r, b), Mathf.Max(a.g, b), Mathf.Max(a.b, b), a.a);
        }
    }
}
