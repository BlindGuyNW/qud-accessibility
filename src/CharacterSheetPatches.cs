using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Qud.UI;
using XRL.Rules;
using XRL.World;
using XRL.World.Parts;
using XRL.World.Parts.Mutation;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the character sheet (StatusScreensScreen):
    /// tab name announcement and F3/F4 block provider for stats/points.
    /// </summary>
    [HarmonyPatch]
    public static class CharacterSheetPatches
    {
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

            if (activeScreen is FactionsStatusScreen facScreen)
            {
                var blocks = new List<ScreenReader.ContentBlock>();
                var scrollData = facScreen.controller?.scrollContext?.data;
                if (scrollData != null)
                {
                    int pos = facScreen.controller.selectedPosition;
                    if (pos >= 0 && pos < scrollData.Count
                        && scrollData[pos] is FactionsLineData fld && fld.id != null)
                    {
                        var faction = Factions.Get(fld.id);
                        if (faction != null)
                        {
                            string feeling = Speech.Clean(faction.GetFeelingText());
                            if (!string.IsNullOrEmpty(feeling))
                                blocks.Add(new ScreenReader.ContentBlock
                                    { Title = "Status", Body = feeling });

                            string rankParts = "";
                            string rank = Speech.Clean(faction.GetRankText());
                            if (!string.IsNullOrEmpty(rank))
                                rankParts = rank;
                            string pet = Speech.Clean(faction.GetPetText());
                            if (!string.IsNullOrEmpty(pet))
                                rankParts = rankParts.Length > 0
                                    ? rankParts + " " + pet : pet;
                            string holy = Speech.Clean(faction.GetHolyPlaceText());
                            if (!string.IsNullOrEmpty(holy))
                                rankParts = rankParts.Length > 0
                                    ? rankParts + " " + holy : holy;
                            if (!string.IsNullOrEmpty(rankParts))
                                blocks.Add(new ScreenReader.ContentBlock
                                    { Title = "Rank", Body = rankParts });

                            string secrets = Speech.Clean(
                                Faction.GetPreferredSecretDescription(fld.id));
                            if (!string.IsNullOrEmpty(secrets))
                                blocks.Add(new ScreenReader.ContentBlock
                                    { Title = "Interests", Body = secrets });
                        }
                    }
                }
                return blocks;
            }

            if (activeScreen is QuestsStatusScreen questScreen)
            {
                var blocks = new List<ScreenReader.ContentBlock>();
                var scrollData = questScreen.controller?.scrollContext?.data;
                if (scrollData != null)
                {
                    int pos = questScreen.controller.selectedPosition;
                    if (pos >= 0 && pos < scrollData.Count
                        && scrollData[pos] is QuestsLineData qld && qld.quest != null)
                    {
                        var quest = qld.quest;
                        blocks.Add(new ScreenReader.ContentBlock
                        {
                            Title = Speech.Clean(quest.DisplayName) ?? "Quest",
                            Body = "Given by "
                                + (quest.QuestGiverName ?? "unknown")
                                + " at "
                                + (quest.QuestGiverLocationName ?? "unknown")
                        });

                        if (quest.StepsByID != null)
                        {
                            foreach (var step in quest.StepsByID.Values
                                .OrderBy(s => s.Ordinal))
                            {
                                if (step.Hidden) continue;
                                string status = step.Failed ? "Failed"
                                    : step.Finished ? "Complete" : "Active";
                                string optional = step.Optional
                                    ? "Optional: " : "";
                                string name = Speech.Clean(step.Name) ?? "";
                                string body = optional + name;
                                string text = (!step.Finished || !step.Collapse)
                                    ? Speech.Clean(step.Text) : null;
                                if (!string.IsNullOrEmpty(text))
                                    body += ". " + text;
                                blocks.Add(new ScreenReader.ContentBlock
                                    { Title = status, Body = body });
                            }
                        }
                    }
                }
                return blocks;
            }

            if (activeScreen is InventoryAndEquipmentStatusScreen eqScreen)
            {
                var blocks = new List<ScreenReader.ContentBlock>();

                string mode = eqScreen.showCybernetics ? "cybernetics" : "equipment";
                string toggleKey = ControlManager.getCommandInputDescription(
                    "Toggle", mapGlyphs: false);
                if (!string.IsNullOrEmpty(toggleKey))
                {
                    blocks.Add(new ScreenReader.ContentBlock
                    {
                        Title = "View Mode",
                        Body = "Showing " + mode + ". " + toggleKey + ": toggle"
                    });
                }

                return blocks;
            }

            // Other tabs: return empty list to prevent falling through to map blocks
            return new List<ScreenReader.ContentBlock>();
        }
    }
}
