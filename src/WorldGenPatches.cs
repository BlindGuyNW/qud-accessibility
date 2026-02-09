using HarmonyLib;
using Qud.UI;
using XRL.UI;

namespace QudAccessibility
{
    /// <summary>
    /// Vocalize major world generation phase transitions so blind users get
    /// feedback during the 0-30 seconds of otherwise silent world creation.
    /// Only announces NextStep calls (major phases), not every sub-step.
    /// </summary>
    [HarmonyPatch(typeof(WorldCreationProgress), nameof(WorldCreationProgress.NextStep))]
    public class WorldCreationProgress_NextStep_Postfix
    {
        static void Postfix(string Text)
        {
            Speech.Interrupt(Text);
        }
    }

    [HarmonyPatch(typeof(WorldGenerationScreen), nameof(WorldGenerationScreen.HideWorldGenerationScreen))]
    public class WorldGenScreen_Hide_Postfix
    {
        static void Postfix()
        {
            Speech.Announce("World generation complete.");
        }
    }
}
