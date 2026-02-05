using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using XRL.CharacterBuilds;
using XRL.CharacterBuilds.Qud.UI;
using XRL.CharacterBuilds.UI;
using XRL.UI.Framework;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the Embark Builder (character creation) flow
    /// to vocalize screen titles and highlighted choices.
    /// </summary>
    [HarmonyPatch]
    public static class ChargenPatches
    {
        /// <summary>
        /// After a character creation window is shown, announce its title.
        /// For the Summary screen, also reads all summary blocks.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(EmbarkBuilderModuleWindowDescriptor), nameof(EmbarkBuilderModuleWindowDescriptor.show))]
        public static void Descriptor_Show_Postfix(EmbarkBuilderModuleWindowDescriptor __instance)
        {
            string screenTitle = null;

            // Try breadcrumb title from the window
            if (__instance.window != null)
            {
                var breadcrumb = __instance.window.GetBreadcrumb();
                if (breadcrumb != null && !string.IsNullOrEmpty(breadcrumb.Title))
                {
                    screenTitle = breadcrumb.Title;
                }
            }

            // Fall back to descriptor title, then name
            if (string.IsNullOrEmpty(screenTitle))
            {
                screenTitle = __instance.title;
            }
            if (string.IsNullOrEmpty(screenTitle))
            {
                screenTitle = __instance.name;
            }

            // For the Summary screen, build the full content for F2 re-read.
            // If tutorial is active, don't speak now — the tutorial Highlight
            // will fire next frame and interrupt us. Store for F2 instead.
            if (__instance.window is QudBuildSummaryModuleWindow summaryWindow)
            {
                var sb = new StringBuilder();
                sb.Append(screenTitle ?? "Summary");

                var blocks = new List<ScreenReader.ContentBlock>();

                foreach (var block in summaryWindow.GetSelections())
                {
                    blocks.Add(new ScreenReader.ContentBlock
                    {
                        Title = block.Title,
                        Body = block.Description
                    });

                    if (!string.IsNullOrEmpty(block.Title))
                    {
                        sb.Append(". ").Append(block.Title);
                    }
                    if (!string.IsNullOrEmpty(block.Description))
                    {
                        sb.Append(": ").Append(block.Description);
                    }
                }

                string summaryText = sb.ToString();
                ScreenReader.SetScreenContent(summaryText);
                var capturedWindow = summaryWindow;
                var capturedBlocks = blocks;
                ScreenReader.SetBlockProvider(() =>
                {
                    if (capturedWindow == null || !capturedWindow.isActiveAndEnabled)
                        return null;
                    return capturedBlocks;
                });

                if (!TutorialManager.IsActive)
                {
                    Speech.Interrupt(summaryText);
                }
                return;
            }

            if (!string.IsNullOrEmpty(screenTitle))
            {
                ScreenReader.SetScreenContent(screenTitle);
                Speech.Interrupt(screenTitle);
            }
        }

        /// <summary>
        /// After HorizontalScroller.UpdateSelection(), speak the highlighted
        /// choice's title and description. This fires whenever the user
        /// navigates between choices in character creation screens.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(HorizontalScroller), nameof(HorizontalScroller.UpdateSelection))]
        public static void HorizontalScroller_UpdateSelection_Postfix(HorizontalScroller __instance)
        {
            var data = __instance.scrollContext?.data;
            if (data == null || data.Count == 0)
                return;

            int pos = __instance.scrollContext.selectedPosition;
            if (pos < 0 || pos >= data.Count)
                return;

            var element = data[pos];
            string label = ScrollerPatches.GetElementLabel(element);
            string description = element.Description;

            string toSpeak = label ?? "";
            if (!string.IsNullOrEmpty(description) && description != label)
            {
                toSpeak += ". " + description;
            }

            if (!string.IsNullOrEmpty(toSpeak))
            {
                Speech.SayIfNew(toSpeak);
            }
        }

        /// <summary>
        /// After Back/Next button updates, announce when the button gains focus.
        /// Update() runs every frame; SayIfNew() deduplicates so it only fires
        /// on the first frame the button becomes active.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(EmbarkBuilderModuleBackButton), "Update")]
        public static void BackButton_Update_Postfix(EmbarkBuilderModuleBackButton __instance)
        {
            if (__instance.navigationContext != null &&
                __instance.navigationContext.IsActive(checkParents: false) &&
                __instance.menuOption != null)
            {
                string label = __instance.menuOption.Description;
                if (!string.IsNullOrEmpty(label))
                {
                    Speech.SayIfNew(label + " button");
                }
            }
        }

        /// <summary>
        /// After random selection on Customize screen, announce the new value.
        /// UpdateUI() refreshes scroller data but doesn't trigger UpdateSelection()
        /// since the selected position stays the same.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(QudCustomizeCharacterModuleWindow), nameof(QudCustomizeCharacterModuleWindow.RandomSelection))]
        public static void RandomSelection_Postfix(QudCustomizeCharacterModuleWindow __instance)
        {
            var scroller = __instance.GetComponentInChildren<FrameworkScroller>();
            if (scroller == null)
                return;

            var data = scroller.scrollContext?.data;
            if (data == null || data.Count == 0)
                return;

            int pos = scroller.selectedPosition;
            if (pos < 0 || pos >= data.Count)
                return;

            string label = ScrollerPatches.GetElementLabel(data[pos]);
            if (!string.IsNullOrEmpty(label))
            {
                Speech.Interrupt(label);
            }
        }
    }
}
