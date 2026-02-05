using HarmonyLib;
using Qud.UI;
using XRL.UI.Framework;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the Main Menu to vocalize navigable elements.
    /// </summary>
    [HarmonyPatch]
    public static class MainMenuPatches
    {
        /// <summary>
        /// After the main menu is shown, announce "Main Menu" and speak the
        /// first highlighted option.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MainMenu), nameof(MainMenu.Show))]
        public static void MainMenu_Show_Postfix(MainMenu __instance)
        {
            string first = null;
            var choices = __instance.leftScroller?.scrollContext?.data;
            if (choices != null && choices.Count > 0)
            {
                int pos = __instance.leftScroller.selectedPosition;
                if (pos >= 0 && pos < choices.Count)
                {
                    first = GetElementLabel(choices[pos]);
                }
            }

            string announcement = first != null
                ? "Main Menu. " + first
                : "Main Menu";

            ScreenReader.SetScreenContent(announcement);
            Speech.Interrupt(announcement);
        }

        /// <summary>
        /// After FrameworkScroller.UpdateSelection(), speak the newly
        /// highlighted element. This covers both MainMenu scrollers and
        /// character creation scrollers that use the base FrameworkScroller.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(FrameworkScroller), nameof(FrameworkScroller.UpdateSelection))]
        public static void FrameworkScroller_UpdateSelection_Postfix(FrameworkScroller __instance)
        {
            // Skip if this is a HorizontalScroller — the chargen patch handles
            // those with richer title+description output.
            if (__instance is HorizontalScroller)
                return;

            var data = __instance.scrollContext?.data;
            if (data == null || data.Count == 0)
                return;

            int pos = __instance.selectedPosition;
            if (pos < 0 || pos >= data.Count)
                return;

            string label = GetElementLabel(data[pos]);
            if (!string.IsNullOrEmpty(label))
            {
                Speech.SayIfNew(label);
            }
        }

        /// <summary>
        /// After PaperdollScroller.UpdateSelection(), speak the newly
        /// highlighted element. The paperdoll is the default equipment view
        /// and is NOT a FrameworkScroller, so our existing postfix doesn't fire.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PaperdollScroller), nameof(PaperdollScroller.UpdateSelection))]
        public static void PaperdollScroller_UpdateSelection_Postfix(PaperdollScroller __instance)
        {
            var data = __instance.choices;
            if (data == null || data.Count == 0)
                return;

            int pos = __instance.selectedPosition;
            if (pos < 0 || pos >= data.Count)
                return;

            string label = GetElementLabel(data[pos]);
            if (!string.IsNullOrEmpty(label))
            {
                Speech.SayIfNew(label);
            }
        }

        /// <summary>
        /// Extract a human-readable label from any FrameworkDataElement subclass.
        /// </summary>
        internal static string GetElementLabel(FrameworkDataElement element)
        {
            if (element is MainMenuOptionData menuOpt)
            {
                return menuOpt.Text;
            }

            if (element is ChoiceWithColorIcon choice)
            {
                return choice.Title;
            }

            if (element is SummaryBlockData summary)
            {
                return summary.Title;
            }

            if (element is EquipmentLineData equipData)
            {
                var bp = equipData.bodyPart;
                if (bp == null)
                    return null;
                string partName = Speech.Clean(bp.GetCardinalDescription());
                if (bp.Primary)
                    partName = "Primary " + partName;
                var equipped = equipData.showCybernetics ? bp.Cybernetics : bp.Equipped;
                string itemName = equipped != null ? Speech.Clean(equipped.DisplayName) : "empty";
                return partName + ": " + itemName;
            }

            if (element is InventoryLineData invData)
            {
                if (invData.category)
                {
                    string name = Speech.Clean(invData.categoryName ?? "");
                    return name + ", " + invData.categoryAmount + " items, " + invData.categoryWeight + " pounds";
                }
                string display = Speech.Clean(invData.displayName ?? "");
                int weight = invData.go?.Weight ?? 0;
                return display + ", " + weight + " pounds";
            }

            if (element is PrefixMenuOption prefixOpt)
            {
                string prefix = prefixOpt.Prefix ?? "";
                string desc = prefixOpt.Description ?? "";
                return prefix + desc;
            }

            if (element is MenuOption menuOption)
            {
                return menuOption.Description;
            }

            // Fallback: use Description, then Id.
            // Prefer Description over Id since Id can be internal gibberish.
            if (!string.IsNullOrEmpty(element.Description))
            {
                return element.Description;
            }

            return element.Id;
        }
    }
}
