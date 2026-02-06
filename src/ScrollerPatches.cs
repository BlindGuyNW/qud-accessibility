using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Qud.API;
using Qud.UI;
using XRL.Rules;
using XRL.UI;
using XRL.CharacterBuilds.Qud.UI;
using XRL.UI.Framework;
using XRL.World;
using XRL.World.Parts;
using XRL.World.Parts.Mutation;
using XRL.World.Skills;

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
        // Help screen: announce title on first show, F2 content on highlight
        // -----------------------------------------------------------------
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
                    first = GetElementLabel(data[pos]);
            }

            string announcement = first != null
                ? "Help. " + first
                : "Help";
            ScreenReader.SetScreenContent(announcement);
            Speech.Interrupt(announcement);
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

        // -----------------------------------------------------------------
        // Ability manager screen: announce title on open
        // -----------------------------------------------------------------
        private static bool _abilityManagerFirstShow;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AbilityManagerScreen), "showScreen")]
        public static void AbilityManagerScreen_showScreen_Prefix()
        {
            _abilityManagerFirstShow = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AbilityManagerScreen), nameof(AbilityManagerScreen.BeforeShow))]
        public static void AbilityManagerScreen_BeforeShow_Postfix(AbilityManagerScreen __instance)
        {
            if (!_abilityManagerFirstShow)
                return;
            _abilityManagerFirstShow = false;

            string first = null;
            var data = __instance.leftSideScroller?.scrollContext?.data;
            if (data != null && data.Count > 0)
            {
                int pos = __instance.leftSideScroller.selectedPosition;
                if (pos >= 0 && pos < data.Count)
                    first = GetElementLabel(data[pos]);
            }

            string announcement = first != null
                ? "Abilities. " + first
                : "Abilities";
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
            ScreenReader.SetBlockProvider(BuildCharSheetBlocks);

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

        private static List<ScreenReader.ContentBlock> BuildCharSheetBlocks()
        {
            var instance = SingletonWindowBase<StatusScreensScreen>.instance;
            if (instance == null || !instance.navigationContext.IsActive())
                return null;

            var go = StatusScreensScreen.GO;
            if (go == null)
                return new List<ScreenReader.ContentBlock>();

            int count = instance.Screens.Count;
            if (count == 0)
                return new List<ScreenReader.ContentBlock>();

            int index = instance.CurrentScreen % count;
            var activeScreen = instance.Screens[index]?.GetComponent<IStatusScreen>();

            if (activeScreen is CharacterStatusScreen)
            {
                var blocks = new List<ScreenReader.ContentBlock>();

                // Character Summary
                int level = go.Level;
                int hp = go.Stat("Hitpoints");
                int maxHp = go.GetStat("Hitpoints").BaseValue;
                int xp = go.Stat("XP");
                int nextXp = Leveler.GetXPForLevel(go.Stat("Level") + 1);
                int weight = go.Weight;
                blocks.Add(new ScreenReader.ContentBlock
                {
                    Title = "Character Summary",
                    Body = $"Level {level}, HP {hp}/{maxHp}, XP {xp}/{nextXp}, Weight {weight}"
                });

                // Attribute Points
                int ap = go.Stat("AP");
                blocks.Add(new ScreenReader.ContentBlock
                {
                    Title = "Attribute Points",
                    Body = ap > 0 ? ap + " available" : "none available"
                });

                // Mutation Points
                string mutTerm;
                string mutColor;
                GetMutationTermEvent.GetFor(go, out mutTerm, out mutColor);
                string termCapital = XRL.Language.Grammar.MakeTitleCase(mutTerm);
                int mp = go.Stat("MP");
                blocks.Add(new ScreenReader.ContentBlock
                {
                    Title = termCapital + " Points",
                    Body = mp > 0 ? mp + " available" : "none available"
                });

                return blocks;
            }

            if (activeScreen is SkillsAndPowersStatusScreen)
            {
                var blocks = new List<ScreenReader.ContentBlock>();

                int sp = go.GetStat("SP").Value;
                blocks.Add(new ScreenReader.ContentBlock
                {
                    Title = "Skill Points",
                    Body = sp > 0 ? sp + " available" : "none available"
                });

                return blocks;
            }

            // Other tabs: return empty list to prevent falling through to map blocks
            return new List<ScreenReader.ContentBlock>();
        }

        // -----------------------------------------------------------------
        // Cybernetics/Generic terminal: announce body + first option on open
        // -----------------------------------------------------------------

        [HarmonyPostfix]
        [HarmonyPatch(typeof(CyberneticsTerminalScreen), nameof(CyberneticsTerminalScreen.Show))]
        public static void CyberneticsTerminalScreen_Show_Postfix(CyberneticsTerminalScreen __instance)
        {
            ScreenReader.SetBlockProvider(BuildTerminalBlocks);

            var data = __instance.displayScroller?.scrollContext?.data;
            if (data == null || data.Count == 0)
                return;

            var sb = new System.Text.StringBuilder();

            // Body text is the first element (OptionID == -1)
            if (data[0] is CyberneticsTerminalLineData bodyData && bodyData.OptionID < 0)
            {
                string bodyText = Speech.Clean(bodyData.Text ?? "");
                if (!string.IsNullOrEmpty(bodyText))
                    sb.Append(bodyText);
            }

            // Announce first option
            if (data.Count > 1 && data[1] is CyberneticsTerminalLineData firstOpt && firstOpt.OptionID >= 0)
            {
                string optText = Speech.Clean(firstOpt.Text ?? "");
                if (!string.IsNullOrEmpty(optText))
                {
                    if (sb.Length > 0) sb.Append(". ");
                    sb.Append(optText);
                }
            }

            string announcement = sb.ToString();
            if (!string.IsNullOrEmpty(announcement))
            {
                ScreenReader.SetScreenContent(announcement);
                Speech.Interrupt(announcement);
            }
        }

        private static List<ScreenReader.ContentBlock> BuildTerminalBlocks()
        {
            var instance = SingletonWindowBase<CyberneticsTerminalScreen>.instance;
            if (instance == null || !instance.globalContext.IsActive())
                return null;

            var data = instance.displayScroller?.scrollContext?.data;
            if (data == null || data.Count == 0)
                return null;

            var blocks = new List<ScreenReader.ContentBlock>();

            // Body text block
            if (data[0] is CyberneticsTerminalLineData bodyData && bodyData.OptionID < 0)
            {
                string bodyText = Speech.Clean(bodyData.Text ?? "");
                if (!string.IsNullOrEmpty(bodyText))
                    blocks.Add(new ScreenReader.ContentBlock { Title = "Terminal", Body = bodyText });
            }

            // One block per option
            for (int i = 0; i < data.Count; i++)
            {
                if (data[i] is CyberneticsTerminalLineData optData && optData.OptionID >= 0)
                {
                    string optText = Speech.Clean(optData.Text ?? "");
                    if (!string.IsNullOrEmpty(optText))
                        blocks.Add(new ScreenReader.ContentBlock
                        {
                            Title = "Option " + (optData.OptionID + 1),
                            Body = optText
                        });
                }
            }

            return blocks;
        }

        // -----------------------------------------------------------------
        // Game summary (death/ending): announce name and details
        // -----------------------------------------------------------------

        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSummaryScreen), nameof(GameSummaryScreen.Show))]
        public static void GameSummaryScreen_Show_Postfix(GameSummaryScreen __instance)
        {
            string name = Speech.Clean(__instance.Name ?? "");
            string details = Speech.Clean(__instance.Details ?? "");

            string announcement = !string.IsNullOrEmpty(name)
                ? "Game Summary. " + name
                : "Game Summary";
            Speech.Interrupt(announcement);

            // F2 content includes full details
            string screenContent = announcement;
            if (!string.IsNullOrEmpty(details))
                screenContent += ". " + details;
            ScreenReader.SetScreenContent(screenContent);

            ScreenReader.SetBlockProvider(BuildGameSummaryBlocks);
        }

        private static List<ScreenReader.ContentBlock> BuildGameSummaryBlocks()
        {
            var instance = SingletonWindowBase<GameSummaryScreen>.instance;
            if (instance == null || instance.vertNav.disabled)
                return null;

            var blocks = new List<ScreenReader.ContentBlock>();

            string name = Speech.Clean(instance.Name ?? "");
            if (!string.IsNullOrEmpty(name))
                blocks.Add(new ScreenReader.ContentBlock { Title = "Name", Body = name });

            string details = Speech.Clean(instance.Details ?? "");
            if (!string.IsNullOrEmpty(details))
                blocks.Add(new ScreenReader.ContentBlock { Title = "Details", Body = details });

            return blocks;
        }

        // -----------------------------------------------------------------
        // Options screen: announce title on first show, F2 content on highlight
        // -----------------------------------------------------------------
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
                    first = GetElementLabel(data[pos]);
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
                    // Game bug: InventoryLineData.set() never assigns categoryAmount,
                    // so it's always 0.  Count items from the scroller data instead.
                    int itemCount = 0;
                    var scrollData = invData.screen?.inventoryController?.scrollContext?.data;
                    if (scrollData != null)
                    {
                        bool found = false;
                        for (int i = 0; i < scrollData.Count; i++)
                        {
                            if (scrollData[i] == invData)
                            {
                                found = true;
                                continue;
                            }
                            if (!found) continue;
                            if (scrollData[i] is InventoryLineData next && next.category)
                                break;
                            itemCount++;
                        }
                    }
                    return name + ", " + itemCount + " items, " + invData.categoryWeight + " pounds";
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

            if (element is AttributeDataElement chargenAttr)
            {
                string label = chargenAttr.Attribute + " " + chargenAttr.Value;
                int mod = Stat.GetScoreModifier(chargenAttr.Value);
                label += ", modifier " + (mod >= 0 ? "+" + mod : mod.ToString());
                label += ", " + chargenAttr.APToRaise + " point" +
                    (chargenAttr.APToRaise != 1 ? "s" : "") + " to raise";
                return label;
            }

            if (element is PrefixMenuOption prefixOpt)
            {
                // Prefix is e.g. "[ ][{{G|3}}]" (unselected, cost 3)
                // or "[■][{{G|3}}]" (selected) or "[2][{{R|-2}}]" (2 selected)
                // Parse the raw prefix to extract selection state and cost.
                string raw = ConsoleLib.Console.ColorUtility.StripFormatting(
                    prefixOpt.Prefix ?? "");
                string selection = "not selected";
                string cost = "";
                // First bracket pair is selection: [ ], [■], or [N]
                int close1 = raw.IndexOf(']');
                if (close1 > 0)
                {
                    string inside = raw.Substring(1, close1 - 1).Trim();
                    if (inside == "\u25A0" || inside == "*")
                        selection = "selected";
                    else if (inside.Length > 0 && inside != " " && inside != "")
                        selection = inside + " selected";
                    // Second bracket pair is cost
                    int open2 = raw.IndexOf('[', close1);
                    if (open2 >= 0)
                    {
                        int close2 = raw.IndexOf(']', open2);
                        if (close2 > open2)
                            cost = raw.Substring(open2 + 1, close2 - open2 - 1).Trim();
                    }
                }
                string desc = Speech.Clean(prefixOpt.Description ?? "");
                string label = desc;
                if (!string.IsNullOrEmpty(cost))
                    label += ", cost " + cost;
                label += ", " + selection;
                if (!string.IsNullOrEmpty(prefixOpt.LongDescription))
                    label += ". " + Speech.Clean(prefixOpt.LongDescription);
                return label;
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
                    string label = abilName;
                    if (abilData.ability.Toggleable && abilData.ability.ToggleState)
                        label += ", active";
                    if (abilData.ability.Cooldown > 0)
                        label += ", " + abilData.ability.CooldownDescription + " cooldown";
                    if (!string.IsNullOrEmpty(abilData.ability.Class))
                        label += ". Type: " + abilData.ability.Class;
                    if (!string.IsNullOrEmpty(abilData.ability.Description))
                        label += ". " + Speech.Clean(abilData.ability.Description);
                    return label;
                }
                return null;
            }

            // ----- Character sheet data types -----

            if (element is CharacterAttributeLineData attrData)
            {
                if (attrData.stat == "CP")
                    // CP isn't a real Statistic (attrData.data is null), so there's no
                    // GetHelpText() to call. The game itself hardcodes this string in
                    // CharacterStatusScreen.HandleHighlightAttribute — no other source exists.
                    return "Compute Power: " + CharacterStatusScreen.CP
                        + ". Your Compute Power scales the bonuses of certain compute-enabled items and cybernetic implants. Your base compute power is 0.";
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
                string label = attrName + ": " + value + modifier;
                string helpText = attrData.data.GetHelpText();
                if (!string.IsNullOrEmpty(helpText))
                    label += ". " + Speech.Clean(helpText);
                return label;
            }

            if (element is CharacterMutationLineData mutData)
            {
                if (mutData.mutation == null) return null;
                string mutName = Speech.Clean(mutData.mutation.GetDisplayName());
                string label = mutName;
                if (mutData.mutation.ShouldShowLevel())
                    label += " (" + mutData.mutation.GetUIDisplayLevel() + ")";
                string desc = mutData.mutation.GetDescription();
                if (!string.IsNullOrEmpty(desc))
                    label += ". " + Speech.Clean(desc);
                string levelText = mutData.mutation.GetLevelText(mutData.mutation.Level);
                if (!string.IsNullOrEmpty(levelText))
                    label += ". " + Speech.Clean(levelText);
                return label;
            }

            if (element is CharacterEffectLineData effectData)
            {
                if (effectData.effect == null) return null;
                string label = Speech.Clean(effectData.effect.GetDescription() ?? "");
                string details = effectData.effect.GetDetails();
                if (!string.IsNullOrEmpty(details) && details != "[effect details]")
                {
                    var go = StatusScreensScreen.GO;
                    details = Campfire.ProcessEffectDescription(details, go);
                    label += ". " + Speech.Clean(details);
                }
                return label;
            }

            if (element is SkillsAndPowersLineData spData)
            {
                if (spData.entry == null) return null;
                string spName = spData.entry.Name;
                string label = spName;
                if (spData.go != null)
                {
                    var learned = spData.entry.IsLearned(spData.go);
                    string status = learned == SPNode.LearnedStatus.Learned ? "Learned"
                                  : learned == SPNode.LearnedStatus.Partial ? "Partially Learned"
                                  : "Unlearned";
                    label += ", " + status;
                    if (learned != SPNode.LearnedStatus.Learned)
                    {
                        int cost = spData.entry.Skill != null ? spData.entry.Skill.Cost : spData.entry.Power?.Cost ?? 0;
                        if (cost > 0)
                            label += ", " + cost + " SP";
                    }

                    // Prerequisites for unlearned powers
                    if (learned != SPNode.LearnedStatus.Learned && spData.entry.Power != null)
                    {
                        var power = spData.entry.Power;

                        // Stat requirements (OR-groups)
                        var reqs = power.requirements;
                        if (reqs != null && reqs.Count > 0)
                        {
                            for (int ri = 0; ri < reqs.Count; ri++)
                            {
                                var req = reqs[ri];
                                if (ri > 0)
                                    label += ", or";
                                for (int ai = 0; ai < req.Attributes.Count; ai++)
                                {
                                    string attrName = req.Attributes[ai];
                                    int minimum = req.Minimums[ai];
                                    bool met = spData.go.BaseStat(attrName) >= minimum;
                                    if (ai > 0)
                                        label += " and";
                                    label += ", Requires " + minimum + " " + attrName
                                           + ", " + (met ? "met" : "not met");
                                }
                            }
                        }

                        // Prerequisite skills/mutations
                        if (!string.IsNullOrEmpty(power.Requires))
                        {
                            foreach (string reqClass in power.Requires.CachedCommaExpansion())
                            {
                                string reqName = reqClass;
                                bool met = false;
                                if (SkillFactory.Factory.TryGetFirstEntry(reqClass, out var entry))
                                {
                                    // Skip initiatory implicit prereqs
                                    if (power.IsSkillInitiatory)
                                    {
                                        int idx = power.ParentSkill.PowerList.IndexOf(power);
                                        if (idx > 0 && power.ParentSkill.PowerList[idx - 1] == entry)
                                            continue;
                                    }
                                    reqName = entry.Name;
                                    met = spData.go.HasSkill(reqClass);
                                }
                                else if (XRL.MutationFactory.HasMutation(reqClass))
                                {
                                    reqName = XRL.MutationFactory.GetMutationEntryByName(reqClass).Name;
                                    met = spData.go.HasPart(reqClass);
                                }
                                label += ", Requires " + reqName + ", " + (met ? "met" : "not met");
                            }
                        }

                        // Exclusions
                        if (power.Exclusion != null)
                        {
                            foreach (string exClass in power.Exclusion.CachedCommaExpansion())
                            {
                                string exName = exClass;
                                bool satisfied = false;
                                if (SkillFactory.Factory.TryGetFirstEntry(exClass, out var exEntry))
                                {
                                    exName = exEntry.Name;
                                    satisfied = !spData.go.HasSkill(exClass);
                                }
                                else if (XRL.MutationFactory.HasMutation(exClass))
                                {
                                    exName = XRL.MutationFactory.GetMutationEntryByName(exClass).Name;
                                    satisfied = !spData.go.HasPart(exClass);
                                }
                                label += ", Excludes " + exName + ", " + (satisfied ? "met" : "not met");
                            }
                        }
                    }
                }
                string desc = spData.entry.Description;
                if (!string.IsNullOrEmpty(desc))
                    label += ". " + Speech.Clean(desc);
                return label;
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

            // ----- Help screen -----

            if (element is HelpDataRow helpRow)
            {
                string catName = helpRow.CategoryId ?? helpRow.Description ?? "";
                return catName + ", " + (helpRow.Collapsed ? "collapsed" : "expanded");
            }

            // ----- Book screen -----
            // Book text is handled by page-turn patches; suppress scroller speech.
            if (element is BookLineData)
            {
                return null;
            }

            // ----- Cybernetics/Generic terminal -----

            if (element is CyberneticsTerminalLineData termData)
            {
                // Body text (OptionID == -1) is too long for navigation speech
                if (termData.OptionID < 0)
                    return null;
                return Speech.Clean(termData.Text ?? "");
            }

            // ----- Options screen -----
            // OptionsCategoryRow must come before OptionsDataRow (inheritance)

            if (element is OptionsCategoryRow optCat)
            {
                string catTitle = optCat.Title ?? optCat.CategoryId ?? "";
                return catTitle + ", " + (optCat.categoryExpanded ? "expanded" : "collapsed");
            }

            if (element is OptionsCheckboxRow optCheck)
            {
                string checkTitle = optCheck.Title ?? "";
                return checkTitle + ", " + (optCheck.Value ? "enabled" : "disabled");
            }

            if (element is OptionsSliderRow optSlider)
            {
                return (optSlider.Title ?? "") + ", " + optSlider.Value;
            }

            if (element is OptionsComboBoxRow optCombo)
            {
                string comboTitle = optCombo.Title ?? "";
                string displayValue = optCombo.Value ?? "";
                // Map internal value to display label if display options exist
                var vals = optCombo.Options;
                var displayVals = optCombo.DisplayOptions;
                if (vals != null && displayVals != null)
                {
                    int idx = Array.IndexOf(vals, optCombo.Value);
                    if (idx >= 0 && idx < displayVals.Length)
                        displayValue = displayVals[idx];
                }
                return comboTitle + ", " + displayValue;
            }

            if (element is OptionsMenuButtonRow optBtn)
            {
                return optBtn.Title ?? "";
            }

            // Catch-all for other OptionsDataRow subtypes
            if (element is OptionsDataRow optRow)
            {
                return optRow.Title ?? "";
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
