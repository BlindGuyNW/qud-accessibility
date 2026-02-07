using System.Collections.Generic;
using HarmonyLib;
using Qud.UI;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the game summary (death/ending) screen:
    /// name/details announcement and F3/F4 block provider.
    /// </summary>
    [HarmonyPatch]
    public static class GameSummaryPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameSummaryScreen), nameof(GameSummaryScreen.Show))]
        public static void GameSummaryScreen_Show_Postfix(GameSummaryScreen __instance)
        {
            string name = Speech.Clean(__instance.Name ?? "");
            string details = Speech.Clean(__instance.Details ?? "");

            string announcement = !string.IsNullOrEmpty(name)
                ? "Game Summary. " + name
                : "Game Summary";
            Speech.Announce(announcement);

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
    }
}
