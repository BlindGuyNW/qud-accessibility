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
    /// - ShowCellPopup / ShowCIDPopupAsync: in-game tutorial popups (modal, await accept)
    /// - Highlight: chargen tutorial overlays (called every frame from LateUpdate)
    /// - HighlightCell / HighlightObject: in-game tutorial highlights on map cells
    /// - HighlightByCID: highlights on UI controls (calls Highlight internally)
    /// </summary>
    [HarmonyPatch]
    public static class TutorialPatches
    {
        // Dedup trackers: raw for fast same-path comparison, clean for cross-path
        // (HighlightByCID wraps text in {{y|...}} before calling Highlight, so
        // raw strings differ even for the same content)
        private static string _lastHighlightRaw;
        private static string _lastHighlightClean;

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
        /// Catch tutorial highlight text shown during chargen and other steps
        /// via the Highlight(RectTransform, ...) method. Called every frame from
        /// LateUpdate — compare raw text first to avoid per-frame processing.
        /// Also reached through HighlightByCID which calls Highlight internally.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TutorialManager), "Highlight")]
        public static void Highlight_Prefix(string text)
        {
            SpeakHighlight(text);
        }

        /// <summary>
        /// Catch tutorial highlight text shown on map cells during gameplay.
        /// HighlightCell is a separate method from Highlight — used extensively
        /// by in-game tutorial steps for movement/combat/interaction hints.
        /// Also covers HighlightObject which delegates to HighlightCell.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TutorialManager), nameof(TutorialManager.HighlightCell))]
        public static void HighlightCell_Prefix(string text)
        {
            SpeakHighlight(text);
        }

        /// <summary>
        /// Shared logic for non-popup highlight announcements.
        /// Filters out control tags and deduplicates by both raw and cleaned text.
        /// </summary>
        private static void SpeakHighlight(string text)
        {
            if (string.IsNullOrEmpty(text) ||
                text.Contains("<noframe>") ||
                text.Contains("<nohighlight>") ||
                text.Contains("<no message>"))
                return;

            // Fast path: exact raw text match (handles per-frame same-method calls)
            if (text == _lastHighlightRaw)
                return;

            _lastHighlightRaw = text;

            // Cross-path dedup: HighlightByCID wraps text in {{y|...}} before
            // calling Highlight, so raw strings differ for the same content.
            // Compare cleaned text to catch popup → highlight re-speak.
            string clean = Speech.Clean(text);
            if (string.IsNullOrEmpty(clean) || clean == _lastHighlightClean)
                return;

            _lastHighlightClean = clean;
            // Tutorial text is spoken but does NOT overwrite screen content.
            // This preserves the primary content (e.g., summary blocks) for F2.
            Speech.Announce(text);
        }

        /// <summary>
        /// Speak tutorial popup text with dismiss instruction.
        /// All tutorial popups await Accept to continue.
        /// </summary>
        private static void SpeakTutorial(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                // Track body text for dedup against subsequent highlight calls
                _lastHighlightRaw = text;
                string clean = Speech.Clean(text);
                if (!string.IsNullOrEmpty(clean))
                    _lastHighlightClean = clean;

                // Append dismiss instruction — resolved by Speech.Clean via
                // ResolveCommands (e.g. ~Accept → "Space")
                string spoken = text + ". ~Accept to continue";

                // In-game tutorial popups DO set screen content (they're modal).
                ScreenReader.SetScreenContent(spoken);
                Speech.Announce(spoken);
            }
        }
    }
}
