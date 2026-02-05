using HarmonyLib;
using Qud.UI;
using XRL.Core;
using XRL.UI;
using XRL.World;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for in-game accessibility:
    ///   - Message log TTS via XRLCore callback
    ///   - Look mode TTS via polling (handled in ScreenReader.Update)
    ///   - Look mode enter/exit tracking
    ///   - Popup menu (PickOption) TTS via SelectableTextMenuItem
    /// </summary>
    [HarmonyPatch]
    public static class GameplayPatches
    {
        /// <summary>
        /// Track when Look mode starts so the scanner keys don't fire
        /// while PgUp/PgDn are used for description scrolling.
        /// ShowLooker is a blocking loop, so prefix fires at entry
        /// and postfix fires when the user exits look mode.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Look), nameof(Look.ShowLooker))]
        public static void ShowLooker_Prefix()
        {
            ScreenReader.InLookMode = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(Look), nameof(Look.ShowLooker))]
        public static void ShowLooker_Postfix()
        {
            ScreenReader.InLookMode = false;
        }

        private static bool _messageCallbackRegistered;

        /// <summary>
        /// One-shot postfix on PlayerTurn to register the message log callback.
        /// We wait until PlayerTurn to ensure the game is fully initialized.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(XRLCore), nameof(XRLCore.PlayerTurn))]
        public static void PlayerTurn_Postfix()
        {
            if (!_messageCallbackRegistered)
            {
                XRLCore.RegisterNewMessageLogEntryCallback(OnMessageLogEntry);
                _messageCallbackRegistered = true;
            }
        }

        private static void OnMessageLogEntry(string message)
        {
            string clean = Speech.Clean(message);
            if (!string.IsNullOrEmpty(clean))
                Speech.Queue(clean);
        }

        /// <summary>
        /// After a SelectableTextMenuItem is selected, speak its text.
        /// Covers all popup menus: POI list, abilities, interact, etc.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SelectableTextMenuItem), nameof(SelectableTextMenuItem.SelectChanged))]
        public static void SelectableTextMenuItem_SelectChanged_Postfix(
            SelectableTextMenuItem __instance, bool newState)
        {
            if (!newState)
                return;

            string text = __instance.itemText;
            if (!string.IsNullOrEmpty(text))
            {
                Speech.SayIfNew(text);
            }
        }
    }
}
