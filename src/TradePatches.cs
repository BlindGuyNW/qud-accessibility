using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Qud.UI;
using XRL.UI;
using XRL.UI.Framework;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the Trade/Container screen to vocalize:
    /// - Screen title on open ("Trading with X" / "Contents of X")
    /// - Column switching between trader and player inventories
    /// - Quantity changes when adding/removing items from trade
    /// - Trade summary for F2 re-read
    /// Item navigation is handled by the existing FrameworkScroller.UpdateSelection
    /// postfix combined with the TradeLineData case in GetElementLabel.
    /// </summary>
    [HarmonyPatch]
    public static class TradePatches
    {
        private static bool _newTradeSession;
        private static int _lastSide = -1;
        private static XRL.World.GameObject _lastTrackedGo;
        private static int _lastTrackedCount = -1;

        /// <summary>
        /// Reset tracking state when a new trade session begins.
        /// showScreen is the async entry point for the modern trade UI.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TradeScreen), "showScreen")]
        public static void TradeScreen_showScreen_Prefix()
        {
            _newTradeSession = true;
            _lastSide = -1;
            _lastTrackedGo = null;
            _lastTrackedCount = -1;
            ScreenReader.SetBlockProvider(BuildTradeBlocks);
        }

        /// <summary>
        /// After UpdateTotals, update the F2 screen content with trade summary.
        /// Only applies in Trade mode where dram values are meaningful.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TradeScreen), nameof(TradeScreen.UpdateTotals))]
        public static void TradeScreen_UpdateTotals_Postfix(TradeScreen __instance)
        {
            if (__instance.mode != TradeUI.TradeScreenMode.Trade)
                return;

            string traderName = TradeScreen.Trader != null
                ? Speech.Clean(TradeScreen.Trader.BaseDisplayName)
                : "trader";

            string total0 = TradeUI.FormatPrice(__instance.Totals[0], TradeScreen.CostMultiple);
            string total1 = TradeUI.FormatPrice(__instance.Totals[1], TradeScreen.CostMultiple);
            int playerDrams = XRL.The.Player?.GetFreeDrams() ?? 0;

            string summary = traderName + " offers " + total0 + " drams, "
                + "you offer " + total1 + " drams. "
                + "You have " + playerDrams + " free drams.";

            ScreenReader.SetScreenContent(summary);
        }

        /// <summary>
        /// Per-frame tracking in TradeScreen.Update for:
        /// - Opening announcement (title + starting column)
        /// - Column (side) switching between trader/player inventories
        /// - Quantity change detection on the currently selected item
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(TradeScreen), nameof(TradeScreen.Update))]
        public static void TradeScreen_Update_Postfix(TradeScreen __instance)
        {
            if (__instance.navigationContext == null || !__instance.navigationContext.IsActive())
                return;

            if (__instance.scrollerControllers == null)
                return;

            int side = __instance.selectedSide;

            // New trade session — announce title and starting column
            if (_newTradeSession)
            {
                _newTradeSession = false;
                _lastSide = side;

                string traderName = TradeScreen.Trader != null
                    ? Speech.Clean(TradeScreen.Trader.BaseDisplayName)
                    : "trader";

                string announcement;
                if (__instance.mode == TradeUI.TradeScreenMode.Container)
                    announcement = "Contents of " + traderName;
                else
                    announcement = "Trading with " + traderName;

                if (side == 0)
                    announcement += ". " + traderName + "'s items";
                else if (side == 1)
                    announcement += ". Your items";

                Speech.Interrupt(announcement);
                return;
            }

            // Column (side) switch
            if (side != _lastSide && side >= 0)
            {
                _lastSide = side;
                _lastTrackedGo = null;
                _lastTrackedCount = -1;

                if (side == 0)
                {
                    string traderName = TradeScreen.Trader != null
                        ? Speech.Clean(TradeScreen.Trader.BaseDisplayName)
                        : "Trader";
                    Speech.Interrupt(traderName + "'s items");
                }
                else
                {
                    Speech.Interrupt("Your items");
                }
                return;
            }

            // Quantity change tracking on the currently selected item
            if (side < 0 || side >= __instance.scrollerControllers.Length)
                return;

            var scroller = __instance.scrollerControllers[side];
            var data = scroller.scrollContext?.data;
            if (data == null)
                return;

            int pos = scroller.selectedPosition;
            if (pos < 0 || pos >= data.Count)
                return;

            if (!(data[pos] is TradeLineData tld) || tld.go == null)
                return;

            if (!__instance.ObjectSide.ContainsKey(tld.go))
            {
                _lastTrackedGo = null;
                _lastTrackedCount = -1;
                return;
            }

            int currentSelected = __instance.howManySelected(tld.go);
            if (tld.go == _lastTrackedGo)
            {
                if (currentSelected != _lastTrackedCount)
                {
                    _lastTrackedCount = currentSelected;
                    int total = tld.go.Count;
                    string msg;
                    if (currentSelected == 0)
                        msg = "None selected";
                    else if (total == 1)
                        msg = "Selected";
                    else if (currentSelected >= total)
                        msg = "All " + currentSelected + " selected";
                    else
                        msg = currentSelected + " of " + total + " selected";
                    Speech.Interrupt(msg);
                }
            }
            else
            {
                _lastTrackedGo = tld.go;
                _lastTrackedCount = currentSelected;
            }
        }

        // -----------------------------------------------------------------
        // Trade screen block provider for F3/F4 navigation
        // -----------------------------------------------------------------
        private static List<ScreenReader.ContentBlock> BuildTradeBlocks()
        {
            var instance = SingletonWindowBase<TradeScreen>.instance;
            if (instance == null || instance.navigationContext == null
                || !instance.navigationContext.IsActive())
                return null;

            var blocks = new List<ScreenReader.ContentBlock>();

            // Block 1: Trade summary
            var sumSb = new StringBuilder();
            if (instance.mode == TradeUI.TradeScreenMode.Trade)
            {
                string total0 = TradeUI.FormatPrice(instance.Totals[0], TradeScreen.CostMultiple);
                string total1 = TradeUI.FormatPrice(instance.Totals[1], TradeScreen.CostMultiple);
                int traderDrams = TradeScreen.Trader?.GetFreeDrams() ?? 0;
                int playerDrams = XRL.The.Player?.GetFreeDrams() ?? 0;
                sumSb.Append("Trader offers " + total0 + " drams, you offer " + total1 + " drams. ");
                sumSb.Append("Trader has " + traderDrams + " free drams, you have " + playerDrams + " free drams.");
            }
            else
            {
                sumSb.Append("Container mode");
            }
            blocks.Add(new ScreenReader.ContentBlock { Title = "Trade Summary", Body = sumSb.ToString() });

            // Block 2: Commands
            var cmdSb = new StringBuilder();
            AppendCommand(cmdSb, "CmdTradeOffer", "offer trade");
            AppendCommand(cmdSb, "CmdTradeAdd", "add one");
            AppendCommand(cmdSb, "CmdTradeRemove", "remove one");
            AppendCommand(cmdSb, "CmdTradeAllItems", "toggle item");
            AppendCommand(cmdSb, "CmdVendorActions", "vendor actions");
            AppendCommand(cmdSb, "Cancel", "close");
            blocks.Add(new ScreenReader.ContentBlock { Title = "Commands", Body = cmdSb.ToString() });

            return blocks;
        }

        private static void AppendCommand(StringBuilder sb, string command, string label)
        {
            string key = ControlManager.getCommandInputDescription(command, mapGlyphs: false);
            if (string.IsNullOrEmpty(key))
                return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(key + " " + label);
        }
    }
}
