using System.Reflection;
using HarmonyLib;
using Qud.UI;
using XRL.UI.Framework;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the Options screen: title announcement on first
    /// show and highlight tracking for F2 content.
    /// </summary>
    [HarmonyPatch]
    public static class OptionsScreenPatches
    {
        private static bool _optionsFirstShow;
        private static FieldInfo _optionsSelectFirstField;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(OptionsScreen), nameof(OptionsScreen.Show))]
        public static void OptionsScreen_Show_Prefix(OptionsScreen __instance)
        {
            if (_optionsSelectFirstField == null)
                _optionsSelectFirstField = typeof(OptionsScreen).GetField("SelectFirst",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _optionsFirstShow = (bool)(_optionsSelectFirstField?.GetValue(__instance) ?? false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(OptionsScreen), nameof(OptionsScreen.Show))]
        public static void OptionsScreen_Show_Postfix(OptionsScreen __instance)
        {
            if (!_optionsFirstShow)
                return;

            string first = null;
            var data = __instance.optionsScroller?.scrollContext?.data;
            if (data != null)
            {
                int pos = __instance.optionsScroller.selectedPosition;
                if (pos >= 0 && pos < data.Count)
                    first = ScrollerPatches.GetElementLabel(data[pos]);
            }

            string announcement = first != null
                ? "Options. " + first
                : "Options";
            ScreenReader.SetScreenContent(announcement);
            Speech.Interrupt(announcement);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(OptionsScreen), nameof(OptionsScreen.HandleHighlight))]
        public static void OptionsScreen_HandleHighlight_Postfix(FrameworkDataElement element)
        {
            if (element is OptionsDataRow optRow)
            {
                string label = optRow.Title ?? "";
                string content = label;
                if (!string.IsNullOrEmpty(optRow.HelpText))
                    content += ". " + Speech.Clean(optRow.HelpText);
                ScreenReader.SetScreenContent(content);
            }
        }
    }
}
