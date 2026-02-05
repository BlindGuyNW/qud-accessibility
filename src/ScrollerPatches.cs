using System.Reflection;
using HarmonyLib;
using Qud.API;
using Qud.UI;
using XRL.Rules;
using XRL.UI;
using XRL.UI.Framework;
using XRL.World;
using XRL.World.Parts.Mutation;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for scrollers, menus, and screen announcements.
    /// Contains GetElementLabel() which extracts readable text from any
    /// FrameworkDataElement subclass, plus postfixes for FrameworkScroller,
    /// PaperdollScroller, and various screen Show() methods.
    /// </summary>
    [HarmonyPatch]
    public static class ScrollerPatches
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

        // -----------------------------------------------------------------
        // Keybinds screen: announce title on first show
        // -----------------------------------------------------------------
        private static bool _keybindsFirstShow;
        private static FieldInfo _selectFirstField;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(KeybindsScreen), nameof(KeybindsScreen.Show))]
        public static void KeybindsScreen_Show_Prefix(KeybindsScreen __instance)
        {
            if (_selectFirstField == null)
                _selectFirstField = typeof(KeybindsScreen).GetField("SelectFirst",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _keybindsFirstShow = (bool)(_selectFirstField?.GetValue(__instance) ?? false);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(KeybindsScreen), nameof(KeybindsScreen.Show))]
        public static void KeybindsScreen_Show_Postfix(KeybindsScreen __instance)
        {
            if (!_keybindsFirstShow)
                return;

            string first = null;
            var data = __instance.keybindsScroller?.scrollContext?.data;
            if (data != null)
            {
                int pos = __instance.keybindsScroller.selectedPosition;
                if (pos >= 0 && pos < data.Count)
                    first = GetElementLabel(data[pos]);
            }

            string announcement = first != null
                ? "Keybinds. " + first
                : "Keybinds";
            ScreenReader.SetScreenContent(announcement);
            Speech.Interrupt(announcement);
        }

        // -----------------------------------------------------------------
        // Save/Continue screen: announce title + first save on open
        // -----------------------------------------------------------------

        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveManagement), nameof(SaveManagement.Show))]
        public static void SaveManagement_Show_Postfix(SaveManagement __instance)
        {
            string first = null;
            var data = __instance.savesScroller?.scrollContext?.data;
            if (data != null && data.Count > 0)
            {
                int pos = __instance.savesScroller.selectedPosition;
                if (pos >= 0 && pos < data.Count)
                    first = GetElementLabel(data[pos]);
            }

            string announcement = first != null
                ? "Continue. " + first
                : "Continue";
            ScreenReader.SetScreenContent(announcement);
            Speech.Interrupt(announcement);
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

        // -----------------------------------------------------------------
        // Pick item screen: announce title when opened (e.g. "g" to get)
        // -----------------------------------------------------------------

        [HarmonyPrefix]
        [HarmonyPatch(typeof(PickGameObjectScreen), "showScreen")]
        public static void PickGameObjectScreen_showScreen_Prefix(string Title)
        {
            if (!string.IsNullOrEmpty(Title))
            {
                Speech.Interrupt(Speech.Clean(Title));
            }
        }

        // -----------------------------------------------------------------
        // Character sheet: announce tab name on switch
        // -----------------------------------------------------------------

        [HarmonyPostfix]
        [HarmonyPatch(typeof(StatusScreensScreen), nameof(StatusScreensScreen.UpdateActiveScreen))]
        public static void StatusScreensScreen_UpdateActiveScreen_Postfix(StatusScreensScreen __instance)
        {
            // activeScreen is private, replicate the lookup from UpdateActiveScreen line 216-217
            int index = __instance.CurrentScreen % __instance.Screens.Count;
            var screen = __instance.Screens[index]?.GetComponent<IStatusScreen>();
            string tabName = screen?.GetTabString();
            if (!string.IsNullOrEmpty(tabName))
            {
                tabName = Speech.Clean(tabName);
                Speech.Interrupt(tabName);
                ScreenReader.SetScreenContent(tabName);
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
                string colorSuffix = Speech.GetObjectColorSuffix(equipped);
                string itemName = equipped != null ? Speech.Clean(equipped.DisplayName) : "empty";
                return partName + ": " + itemName + colorSuffix;
            }

            if (element is InventoryLineData invData)
            {
                if (invData.category)
                {
                    string name = Speech.Clean(invData.categoryName ?? "");
                    return name + ", " + invData.categoryAmount + " items, " + invData.categoryWeight + " pounds";
                }
                string colorSuffix = Speech.GetObjectColorSuffix(invData.go);
                string display = Speech.Clean(invData.displayName ?? "");
                int weight = invData.go?.Weight ?? 0;
                return display + colorSuffix + ", " + weight + " pounds";
            }

            if (element is PickGameObjectLineData pickData)
            {
                if (pickData.type == PickGameObjectLineDataType.Category)
                {
                    string catName = Speech.Clean(pickData.category ?? "");
                    return catName + ", " + (pickData.collapsed ? "collapsed" : "expanded");
                }
                if (pickData.go != null)
                {
                    string colorSuffix = Speech.GetObjectColorSuffix(pickData.go);
                    string itemName = Speech.Clean(pickData.go.DisplayName ?? "");
                    int weight = pickData.go.Weight;
                    return itemName + colorSuffix + ", " + weight + " pounds";
                }
                return null;
            }

            if (element is TradeLineData tradeData)
            {
                if (tradeData.type == TradeLineDataType.Category)
                {
                    string catName = Speech.Clean(tradeData.category ?? "");
                    return catName + ", " + (tradeData.collapsed ? "collapsed" : "expanded");
                }
                if (tradeData.go != null)
                {
                    string itemName = Speech.Clean(tradeData.go.DisplayName ?? "");
                    string colorSuffix = Speech.GetObjectColorSuffix(tradeData.go);
                    double price = TradeUI.GetValue(tradeData.go, tradeData.traderInventory);
                    string result = itemName + colorSuffix + ", " + $"{price:0.##}" + " drams";
                    int count = tradeData.go.Count;
                    if (count > 1)
                        result += ", quantity " + count;
                    if (tradeData.numberSelected > 0)
                        result += ", " + tradeData.numberSelected + " selected";
                    return result;
                }
                return null;
            }

            if (element is SaveInfoData saveData)
            {
                var sg = saveData.SaveGame;
                if (sg == null) return null;
                string name = Speech.Clean(sg.Name ?? "");
                string desc = Speech.Clean(sg.Description ?? "");
                string location = Speech.Clean(sg.Info ?? "");
                string saved = sg.SaveTime ?? "";
                return name + ", " + desc + ". " + location + ". Last saved " + saved;
            }

            if (element is KeybindDataRow keybindRow)
            {
                string label = keybindRow.KeyDescription ?? keybindRow.KeyId ?? "";
                string binds = "";
                string sep = "";
                foreach (var b in new[] { keybindRow.Bind1, keybindRow.Bind2, keybindRow.Bind3, keybindRow.Bind4 })
                {
                    if (!string.IsNullOrEmpty(b))
                    {
                        binds += sep + b;
                        sep = ", ";
                    }
                }
                return binds.Length > 0 ? label + ": " + binds : label + ": unbound";
            }

            if (element is KeybindCategoryRow catRow)
            {
                string state = catRow.Collapsed ? "collapsed" : "expanded";
                return catRow.CategoryDescription + ", " + state;
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

            if (element is ButtonBar.ButtonBarButtonData buttonData)
            {
                return buttonData.label;
            }

            if (element is FilterBarCategoryButtonData filterCatData)
            {
                return filterCatData.category == "*All" ? "All" : filterCatData.category;
            }

            if (element is AbilityManagerLineData abilData)
            {
                if (abilData.category != null)
                {
                    string catName = Speech.Clean(abilData.category);
                    return catName + ", " + (abilData.collapsed ? "collapsed" : "expanded");
                }
                if (abilData.ability != null)
                {
                    string abilName = Speech.Clean(abilData.ability.DisplayName ?? "");
                    if (abilData.ability.Toggleable && abilData.ability.ToggleState)
                        return abilName + ", active";
                    return abilName;
                }
                return null;
            }

            // ----- Character sheet data types -----

            if (element is CharacterAttributeLineData attrData)
            {
                if (attrData.stat == "CP")
                    return "Compute Power: " + CharacterStatusScreen.CP;
                if (attrData.data == null)
                    return attrData.stat ?? "";
                string attrName = Statistic.GetStatCapitalizedDisplayName(attrData.data.Name);
                string shortName = attrData.data.GetShortDisplayName();
                int value;
                if (shortName == "MS")
                    value = 200 - attrData.data.Value;
                else if (shortName == "AV" && attrData.go != null)
                    value = Stats.GetCombatAV(attrData.go);
                else if (shortName == "DV" && attrData.go != null)
                    value = Stats.GetCombatDV(attrData.go);
                else if (shortName == "MA" && attrData.go != null)
                    value = Stats.GetCombatMA(attrData.go);
                else
                    value = attrData.data.Value;
                string modifier = "";
                if (attrData.category == CharacterAttributeLineData.Category.primary)
                    modifier = " [" + (attrData.data.Modifier >= 0 ? "+" : "") + attrData.data.Modifier + "]";
                return attrName + ": " + value + modifier;
            }

            if (element is CharacterMutationLineData mutData)
            {
                if (mutData.mutation == null) return null;
                string mutName = Speech.Clean(mutData.mutation.GetDisplayName());
                if (mutData.mutation.ShouldShowLevel())
                    return mutName + " (" + mutData.mutation.GetUIDisplayLevel() + ")";
                return mutName;
            }

            if (element is CharacterEffectLineData effectData)
            {
                return Speech.Clean(effectData.effect?.GetDescription() ?? "");
            }

            if (element is SkillsAndPowersLineData spData)
            {
                if (spData.entry == null) return null;
                string spName = spData.entry.Name;
                if (spData.go != null)
                {
                    var learned = spData.entry.IsLearned(spData.go);
                    string status = learned == SPNode.LearnedStatus.Learned ? "Learned"
                                  : learned == SPNode.LearnedStatus.Partial ? "Partially Learned"
                                  : "Unlearned";
                    return spName + ", " + status;
                }
                return spName;
            }

            if (element is QuestsLineData questData)
            {
                if (questData.quest == null) return "No active quests";
                string questName = Speech.Clean(questData.quest.DisplayName ?? "");
                string questState = questData.expanded ? "expanded" : "collapsed";
                return questName + ", " + questState;
            }

            if (element is FactionsLineData factionData)
            {
                string factionName = Speech.Clean(factionData.name ?? "");
                return factionName + ", " + factionData.rep + " reputation"
                    + ", " + (factionData.expanded ? "expanded" : "collapsed");
            }

            if (element is JournalLineData journalData)
            {
                if (journalData.category)
                {
                    string catName = Speech.Clean(journalData.categoryName ?? "");
                    return catName + ", " + (journalData.categoryExpanded ? "expanded" : "collapsed");
                }
                string entryText = journalData.entry?.GetDisplayText() ?? "";
                return Speech.Clean(entryText);
            }

            if (element is TinkeringLineData tinkerData)
            {
                if (tinkerData.category)
                {
                    string catName = Speech.Clean(tinkerData.categoryName ?? "");
                    return catName + ", " + tinkerData.categoryCount + " items"
                        + ", " + (tinkerData.categoryExpanded ? "expanded" : "collapsed");
                }
                string itemName = Speech.Clean(tinkerData.data?.DisplayName ?? "");
                string cost = tinkerData.costString ?? "";
                return string.IsNullOrEmpty(cost) ? itemName : itemName + ", cost: " + Speech.Clean(cost);
            }

            if (element is MessageLogLineData msgData)
            {
                return Speech.Clean(msgData.text ?? "");
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
