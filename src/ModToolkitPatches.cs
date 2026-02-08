using HarmonyLib;
using Qud.UI;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the Modding Toolkit menu: title announcement
    /// and menu item selection vocalization.
    /// </summary>
    [HarmonyPatch]
    public static class ModToolkitPatches
    {
        private static int _lastSelectedOption = -1;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModToolkit), nameof(ModToolkit.Show))]
        public static void ModToolkit_Show_Postfix(ModToolkit __instance)
        {
            var mc = __instance.menuController;
            string first = null;
            if (mc?.menuData != null && mc.menuData.Count > 0)
            {
                int sel = mc.selectedOption;
                if (sel >= 0 && sel < mc.menuData.Count)
                    first = Speech.Clean(mc.menuData[sel].text);
            }

            string announcement = first != null
                ? "Modding Toolkit. " + first
                : "Modding Toolkit";
            ScreenReader.SetScreenContent(announcement);
            Speech.Announce(announcement);
            _lastSelectedOption = mc?.selectedOption ?? 0;
        }

        /// <summary>
        /// Track selection changes in QudTextMenuController, scoped to
        /// when the Modding Toolkit is the active window.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(QudTextMenuController), nameof(QudTextMenuController.Update))]
        public static void QudTextMenuController_Update_Postfix(QudTextMenuController __instance)
        {
            var toolkit = SingletonWindowBase<ModToolkit>.instance;
            if (toolkit == null || toolkit.menuController != __instance)
                return;
            if (!__instance.isCurrentWindow())
                return;

            int current = __instance.selectedOption;
            if (current == _lastSelectedOption)
                return;
            _lastSelectedOption = current;

            int inputCount = __instance.inputFields?.Count ?? 0;
            int itemCount = __instance.menuData?.Count ?? 0;

            if (current >= inputCount && current < inputCount + itemCount)
            {
                string text = Speech.Clean(__instance.menuData[current - inputCount].text);
                if (text != null)
                {
                    ScreenReader.SetScreenContent(text);
                    Speech.SayIfNew(text);
                }
            }
            else if (current >= inputCount + itemCount)
            {
                int btnIdx = current - inputCount - itemCount;
                if (btnIdx >= 0 && btnIdx < __instance.bottomContextOptions.Count)
                {
                    string text = Speech.Clean(__instance.bottomContextOptions[btnIdx].text);
                    if (text != null)
                    {
                        ScreenReader.SetScreenContent(text);
                        Speech.SayIfNew(text);
                    }
                }
            }
        }
    }
}
