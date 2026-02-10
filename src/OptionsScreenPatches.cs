using System.Reflection;
using HarmonyLib;
using Qud.UI;
using XRL.UI.Framework;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the Options screen: title announcement on first
    /// show and highlight tracking for F2 content. Also vocalizes the search
    /// bar and Advanced checkbox which live outside the main scroller.
    /// </summary>
    [HarmonyPatch]
    public static class OptionsScreenPatches
    {
        private static bool _optionsFirstShow;
        private static FieldInfo _optionsSelectFirstField;
        private static NavigationContext _lastOptionsContext;
        private static bool? _lastAdvancedValue;

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
            Speech.Announce(announcement);
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

        /// <summary>
        /// Detect when focus enters the Advanced checkbox, which is a separate
        /// navigation context outside the main options scroller.
        /// Re-announces when its value is toggled.
        /// Search bar is handled by the universal FrameworkSearchInput patch.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(OptionsScreen), nameof(OptionsScreen.Update))]
        public static void OptionsScreen_Update_Postfix(OptionsScreen __instance)
        {
            if (!__instance.globalContext.IsActive())
            {
                _lastOptionsContext = null;
                _lastAdvancedValue = null;
                return;
            }

            var active = NavigationController.instance.activeContext;
            if (active == null)
                return;

            bool contextChanged = active != _lastOptionsContext;
            _lastOptionsContext = active;

            // Advanced checkbox: removed from scroller, rendered standalone
            if (__instance.advancedOptionsCheck != null
                && __instance.advancedOptionsScrollProxy.IsActive())
            {
                bool currentValue = __instance.advancedOptionsCheck.Value;
                if (contextChanged || _lastAdvancedValue != currentValue)
                {
                    _lastAdvancedValue = currentValue;
                    string label = ScrollerPatches.GetElementLabel(
                        __instance.advancedOptionsCheck);
                    ScreenReader.SetScreenContent(label);
                    Speech.SayIfNew(label);
                }
                return;
            }

            _lastAdvancedValue = null;
        }
    }
}
