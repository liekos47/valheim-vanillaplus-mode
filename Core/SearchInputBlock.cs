using HarmonyLib;
using UnityEngine;

namespace ValheimVanillaPlus
{
    // While the menu's search box has keyboard focus, the game's own key/button reads return false,
    // so typing "m" doesn't open the map, "tab" the inventory, etc. Mouse input is left alone.
    // (One small patch class per method: the safe, standard Harmony form.)
    internal static class SearchInputBlock
    {
        public static bool Block(ref bool result)
        {
            if (!MenuWindow.Typing) return true;
            result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown), typeof(string))]
    internal static class SearchBlock_ButtonDown { private static bool Prefix(ref bool __result) => SearchInputBlock.Block(ref __result); }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButton), typeof(string))]
    internal static class SearchBlock_Button { private static bool Prefix(ref bool __result) => SearchInputBlock.Block(ref __result); }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonUp), typeof(string))]
    internal static class SearchBlock_ButtonUp { private static bool Prefix(ref bool __result) => SearchInputBlock.Block(ref __result); }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyDown), typeof(KeyCode), typeof(bool))]
    internal static class SearchBlock_KeyDown { private static bool Prefix(ref bool __result) => SearchInputBlock.Block(ref __result); }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKey), typeof(KeyCode), typeof(bool))]
    internal static class SearchBlock_Key { private static bool Prefix(ref bool __result) => SearchInputBlock.Block(ref __result); }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetKeyUp), typeof(KeyCode), typeof(bool))]
    internal static class SearchBlock_KeyUp { private static bool Prefix(ref bool __result) => SearchInputBlock.Block(ref __result); }
}
