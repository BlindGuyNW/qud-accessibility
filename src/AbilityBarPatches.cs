using HarmonyLib;
using Qud.UI;

namespace QudAccessibility
{
    [HarmonyPatch]
    public static class AbilityBarPatches
    {
        private static string GetAbilityLabel(AbilityBar bar)
        {
            var ability = bar.currentlySelectedAbility;
            if (ability == null)
                return "no ability selected";

            string label = ability.DisplayName ?? "";
            if (ability.Cooldown > 0)
                label += ", " + ability.CooldownDescription + " cooldown";
            else if (!ability.Enabled)
                label += ", disabled";
            else if (ability.Toggleable && ability.ToggleState)
                label += ", active";

            return label;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AbilityBar), nameof(AbilityBar.MoveSelection))]
        public static void MoveSelection_Postfix(AbilityBar __instance)
        {
            string label = GetAbilityLabel(__instance);
            Speech.Interrupt(label);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(AbilityBar), nameof(AbilityBar.MovePage))]
        public static void MovePage_Postfix(AbilityBar __instance)
        {
            string label = GetAbilityLabel(__instance);
            Speech.Interrupt(label);
        }
    }
}
