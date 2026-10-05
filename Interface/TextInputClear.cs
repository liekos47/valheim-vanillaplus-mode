using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimVanillaPlus
{
    // "Clear" button in the game's "Enter text" box (signs, names, portal tags): a copy of the
    // Cancel button placed to its left, same look, that empties the text field and keeps it
    // focused so you can type straight away.
    [HarmonyPatch(typeof(TextInput), "Show")]
    internal static class TextInputClear
    {
        private const string ButtonName = "VanillaPlus_ClearButton";
        // Valheim God Mode adds the same button under its own name; with both mods installed only one is shown.
        private const string ButtonSuffix = "_ClearButton";

        private static void Postfix(TextInput __instance)
        {
            if (__instance.m_panel == null) return;
            try { Ensure(__instance); }
            catch (System.Exception e) { VanillaPlusPlugin.Log.LogWarning($"Clear button: {e.Message}"); }
        }

        private static void Ensure(TextInput ti)
        {
            var all = ti.m_panel.GetComponentsInChildren<Button>(true);
            var existing = all.FirstOrDefault(b => b.name == ButtonName);
            bool wanted = VanillaPlusPlugin.TextClearButton.Value && !all.Any(b => b.name != ButtonName && b.name.EndsWith(ButtonSuffix));
            if (!wanted)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }

            // The box has two buttons side by side: Cancel (left) and OK (right).
            var buttons = all.Where(b => !b.name.EndsWith(ButtonSuffix)).OrderBy(b => b.transform.position.x).ToList();
            if (buttons.Count < 2) return;
            var cancel = buttons[0];
            var ok = buttons[buttons.Count - 1];
            var cancelRt = cancel.GetComponent<RectTransform>();
            var okRt = ok.GetComponent<RectTransform>();
            if (cancelRt == null || okRt == null) return;

            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                Place(existing.GetComponent<RectTransform>(), cancelRt, okRt);
                SetLabel(existing);
                return;
            }

            var go = Object.Instantiate(cancel.gameObject, cancel.transform.parent);
            go.name = ButtonName;
            Place(go.GetComponent<RectTransform>(), cancelRt, okRt);

            // If the buttons sit in an automatic row layout, being first in the row puts it on the left.
            if (cancel.transform.parent.GetComponent<LayoutGroup>() != null) go.transform.SetSiblingIndex(0);

            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent(); // drop the copied "Cancel" action
            button.onClick.AddListener(() =>
            {
                ti.m_inputField.text = "";
                ti.m_inputField.ActivateInputField();
            });
            SetLabel(button);
        }

        // As wide as the OK button (Cancel is wider), to the left of Cancel with the same gap that
        // separates Cancel from OK.
        private static void Place(RectTransform rt, RectTransform cancel, RectTransform ok)
        {
            float width = ok.rect.width;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            float cancelLeft = cancel.anchoredPosition.x - cancel.rect.width * cancel.pivot.x;
            float cancelRight = cancelLeft + cancel.rect.width;
            float okLeft = ok.anchoredPosition.x - ok.rect.width * ok.pivot.x;
            float gap = Mathf.Max(0f, okLeft - cancelRight);
            rt.anchoredPosition = new Vector2(cancelLeft - gap - width * (1f - rt.pivot.x), cancel.anchoredPosition.y);
        }

        // Re-applied on every show: copied buttons may carry a component that re-localizes their text.
        private static void SetLabel(Button button)
        {
            foreach (var t in button.GetComponentsInChildren<TMP_Text>(true))
                if (t.gameObject.name.IndexOf("hint", System.StringComparison.OrdinalIgnoreCase) < 0 && t.transform.parent != null
                    && t.GetComponentInParent<Button>() == button && !IsKeyHint(t))
                    t.text = "Clear";
        }

        // Gamepad / keyboard hint labels inside the button (e.g. "Esc") are left alone.
        private static bool IsKeyHint(TMP_Text t)
        {
            for (var p = t.transform; p != null && p.GetComponent<Button>() == null; p = p.parent)
                if (p.name.IndexOf("hint", System.StringComparison.OrdinalIgnoreCase) >= 0 || p.name.IndexOf("gamepad", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || p.name.IndexOf("key", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }
}
