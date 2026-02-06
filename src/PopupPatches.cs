using System;
using System.Collections.Generic;
using Genkit;
using HarmonyLib;
using XRL.UI;
using XRL.World.Parts;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for Popup dialogs to vocalize their text content.
    /// </summary>
    [HarmonyPatch]
    public static class PopupPatches
    {
        /// <summary>
        /// Augment the "not owned by you" confirmation with witness count
        /// by running the same flood-fill the game uses for theft detection.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Popup), nameof(Popup.ShowYesNoCancel))]
        public static void ShowYesNoCancel_Prefix(ref string Message)
        {
            if (Message == null || !Message.StartsWith("That is not owned by you"))
                return;

            string witnessInfo = GetWitnessInfo();
            if (witnessInfo != null)
                Message += " " + witnessInfo;
        }

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

        /// <summary>
        /// Simulate the game's theft-detection flood fill to count witnesses.
        /// Scans the player's cell and adjacent cells for the owned object,
        /// then flood-fills from it to find NPCs affiliated with the owner faction.
        /// </summary>
        private static string GetWitnessInfo()
        {
            var player = XRL.The.Player;
            if (player?.CurrentCell == null)
                return null;

            var zone = player.CurrentZone;
            if (zone == null)
                return null;

            var playerCell = player.CurrentCell;

            // Find the owned object the player is interacting with
            XRL.World.GameObject ownedObj = null;
            for (int dx = -1; dx <= 1 && ownedObj == null; dx++)
            {
                for (int dy = -1; dy <= 1 && ownedObj == null; dy++)
                {
                    int nx = playerCell.X + dx;
                    int ny = playerCell.Y + dy;
                    if (nx < 0 || nx >= zone.Width || ny < 0 || ny >= zone.Height)
                        continue;
                    var cell = zone.GetCell(nx, ny);
                    if (cell == null)
                        continue;
                    foreach (var obj in cell.GetObjectsInCell())
                    {
                        if (obj != player
                            && !string.IsNullOrEmpty(obj.Owner)
                            && obj.InInventory != player
                            && obj.Equipped != player)
                        {
                            ownedObj = obj;
                            break;
                        }
                    }
                }
            }

            if (ownedObj == null)
                return null;

            string ownerFaction = ownedObj.Owner;
            var objCell = ownedObj.CurrentCell;
            if (objCell == null)
                return null;

            // Same flood fill the game uses: radius 20, searching for Brain parts
            var found = zone.FastFloodVisibility(
                objCell.X, objCell.Y, 20, typeof(Brain), null);

            var names = new List<string>();
            foreach (var npc in found)
            {
                if (npc == player || npc.Brain == null)
                    continue;
                if (npc.Brain.GetAllegianceLevel(ownerFaction)
                    >= Brain.AllegianceLevel.Affiliated)
                {
                    names.Add(Speech.Clean(
                        npc.GetDisplayName(Stripped: true)));
                }
            }

            if (names.Count == 0)
                return "No witnesses.";

            return names.Count + " witness" + (names.Count != 1 ? "es" : "")
                + ": " + string.Join(", ", names) + ".";
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
