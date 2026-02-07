using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Qud.UI;
using XRL.UI.Framework;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the Help screen: title announcement, content
    /// scrolling feedback, highlight tracking, and F3/F4 block provider.
    /// </summary>
    [HarmonyPatch]
    public static class HelpScreenPatches
    {
        private static bool _helpFirstShow;
        private static FieldInfo _helpSelectFirstField;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HelpScreen), nameof(HelpScreen.Show))]
        public static void HelpScreen_Show_Prefix(HelpScreen __instance)
        {
            if (_helpSelectFirstField == null)
                _helpSelectFirstField = typeof(HelpScreen).GetField("SelectFirst",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _helpFirstShow = (bool)(_helpSelectFirstField?.GetValue(__instance) ?? false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HelpScreen), nameof(HelpScreen.Show))]
        public static void HelpScreen_Show_Postfix(HelpScreen __instance)
        {
            ScreenReader.SetBlockProvider(BuildHelpBlocks);

            if (!_helpFirstShow)
                return;

            string first = null;
            var data = __instance.helpScroller?.scrollContext?.data;
            if (data != null)
            {
                int pos = __instance.helpScroller.selectedPosition;
                if (pos >= 0 && pos < data.Count)
                    first = ScrollerPatches.GetElementLabel(data[pos]);
            }

            string announcement = first != null
                ? "Help. " + first
                : "Help";
            ScreenReader.SetScreenContent(announcement);
            Speech.Announce(announcement);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HelpRow), nameof(HelpRow.HandleUpDown))]
        public static void HelpRow_HandleUpDown_Postfix()
        {
            var evt = NavigationController.currentEvent;
            if (evt != null && evt.handled)
                Speech.SayIfNew("Scrolling. Press F2 or F3 for content.");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HelpScreen), nameof(HelpScreen.HandleHighlight))]
        public static void HelpScreen_HandleHighlight_Postfix(FrameworkDataElement element)
        {
            if (element is HelpDataRow helpRow)
            {
                string catName = helpRow.CategoryId ?? "";
                string helpText = helpRow.HelpText ?? "";
                string content = catName;
                if (!string.IsNullOrEmpty(helpText))
                    content += ". " + Speech.Clean(helpText);
                ScreenReader.SetScreenContent(content);
            }
        }

        private static List<ScreenReader.ContentBlock> BuildHelpBlocks()
        {
            var instance = SingletonWindowBase<HelpScreen>.instance;
            if (instance == null || !instance.globalContext.IsActive())
                return null;

            var blocks = new List<ScreenReader.ContentBlock>();

            // Current selection's help text
            var selected = instance.lastSelectedElement as HelpDataRow;
            if (selected != null && !string.IsNullOrEmpty(selected.HelpText))
            {
                blocks.Add(new ScreenReader.ContentBlock
                {
                    Title = selected.CategoryId ?? "Help",
                    Body = Speech.Clean(selected.HelpText)
                });
            }

            // Return empty list (not null) when screen is active but no content —
            // null would auto-clear the provider and fall through to map blocks
            return blocks;
        }
    }
}
