using System.Collections.Generic;
using HarmonyLib;
using Qud.UI;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the cybernetics/generic terminal screen:
    /// body + first option announcement and F3/F4 block provider.
    /// </summary>
    [HarmonyPatch]
    public static class TerminalPatches
    {
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
                Speech.Announce(announcement);
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
    }
}
