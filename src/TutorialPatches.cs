using System;
using System.Threading.Tasks;
using Genkit;
using HarmonyLib;
using UnityEngine;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for Tutorial popups and highlights to vocalize their text.
    /// Tutorial text is displayed through several different paths:
    /// - ShowCellPopup / ShowCIDPopupAsync: in-game tutorial popups
    /// - Highlight: chargen tutorial overlays (called every frame from LateUpdate)
    /// </summary>
    [HarmonyPatch]
    public static class TutorialPatches
    {
        // Track raw text to avoid per-frame string processing in Highlight
        private static string _lastHighlightRaw;

        /// <summary>
        /// Before a cell-anchored tutorial popup displays, speak its text.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TutorialManager), nameof(TutorialManager.ShowCellPopup))]
        public static void ShowCellPopup_Prefix(string text)
        {
            SpeakTutorial(text);
        }

        /// <summary>
        /// Before a control-ID-anchored tutorial popup displays, speak its text.
        /// This also covers ShowIntermissionPopupAsync which delegates here.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TutorialManager), nameof(TutorialManager.ShowCIDPopupAsync))]
        public static void ShowCIDPopupAsync_Prefix(string text)
        {
            SpeakTutorial(text);
        }

        /// <summary>
        /// Catch tutorial highlight text shown during chargen and other steps.
        /// Called every frame from LateUpdate — compare raw text first to
        /// avoid per-frame string processing.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TutorialManager), "Highlight")]
        public static void Highlight_Prefix(string text)
        {
            if (string.IsNullOrEmpty(text) ||
                text.Contains("<noframe>") ||
                text.Contains("<nohighlight>") ||
                text.Contains("<no message>"))
                return;

            // Only process and speak when the raw text has changed
            if (text == _lastHighlightRaw)
                return;

            _lastHighlightRaw = text;
            // Tutorial text is spoken but does NOT overwrite screen content.
            // This preserves the primary content (e.g., summary blocks) for F2.
            Speech.Interrupt(text);
        }

        private static void SpeakTutorial(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                // In-game tutorial popups DO set screen content (they're modal).
                ScreenReader.SetScreenContent(text);
                Speech.Interrupt(text);
            }
        }
    }
}
