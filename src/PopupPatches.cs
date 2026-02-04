using System.Collections.Generic;
using Genkit;
using HarmonyLib;
using XRL.UI;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for Popup dialogs to vocalize their text content.
    /// </summary>
    [HarmonyPatch]
    public static class PopupPatches
    {
        /// <summary>
        /// Before WaitNewPopupMessage displays, speak the title (if any) and message.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Popup), nameof(Popup.WaitNewPopupMessage))]
        public static void WaitNewPopupMessage_Prefix(string message, string title)
        {
            SpeakPopup(title, message);
        }

        /// <summary>
        /// Before NewPopupMessageAsync displays, speak the title (if any) and message.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Popup), nameof(Popup.NewPopupMessageAsync))]
        public static void NewPopupMessageAsync_Prefix(string message, string title)
        {
            SpeakPopup(title, message);
        }

        /// <summary>
        /// Before Popup.Show displays, speak the title (if any) and message.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Popup), nameof(Popup.Show), new[] {
            typeof(string), typeof(string), typeof(string),
            typeof(bool), typeof(bool), typeof(bool), typeof(bool),
            typeof(Location2D)
        })]
        public static void Show_Prefix(string Message, string Title)
        {
            SpeakPopup(Title, Message);
        }

        /// <summary>
        /// Before Popup.ShowAsync displays, speak the message.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Popup), nameof(Popup.ShowAsync), new[] {
            typeof(string), typeof(bool), typeof(bool),
            typeof(bool), typeof(bool), typeof(bool)
        })]
        public static void ShowAsync_Prefix(string Message)
        {
            SpeakPopup(null, Message);
        }

        private static void SpeakPopup(string title, string message)
        {
            string toSpeak = "";

            if (!string.IsNullOrEmpty(title))
            {
                toSpeak = title + ". ";
            }

            if (!string.IsNullOrEmpty(message))
            {
                toSpeak += message;
            }

            if (!string.IsNullOrEmpty(toSpeak))
            {
                ScreenReader.SetScreenContent(toSpeak);
                Speech.Interrupt(toSpeak);
            }
        }
    }
}
