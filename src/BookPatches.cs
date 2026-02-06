using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Qud.UI;
using XRL.UI;
using XRL.UI.Framework;
using XRL.World.Parts;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the BookScreen to vocalize:
    /// - Book title and page info on open
    /// - Page number and content on page turns
    /// - F2 re-read with full page text
    /// - F3/F4 block navigation with page content
    /// </summary>
    [HarmonyPatch]
    public static class BookPatches
    {
        private static bool _newBookSession;
        private static FieldInfo _currentPageField;

        /// <summary>
        /// Mark new session on showScreen(MarkovBook, ...) entry.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BookScreen), "showScreen", new[] { typeof(MarkovBook), typeof(string), typeof(System.Action<int>), typeof(System.Action<int>) })]
        public static void BookScreen_showScreen_MarkovBook_Prefix()
        {
            _newBookSession = true;
        }

        /// <summary>
        /// Mark new session on showScreen(string BookID, ...) entry.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BookScreen), "showScreen", new[] { typeof(string), typeof(string), typeof(System.Action<int>), typeof(System.Action<int>) })]
        public static void BookScreen_showScreen_BookID_Prefix()
        {
            _newBookSession = true;
        }

        /// <summary>
        /// After UpdateViewFromData (called on open + every page turn),
        /// announce page info and set F2 content.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BookScreen), "UpdateViewFromData")]
        public static void BookScreen_UpdateViewFromData_Postfix(BookScreen __instance)
        {
            if (_currentPageField == null)
                _currentPageField = typeof(BookScreen).GetField("CurrentPage",
                    BindingFlags.NonPublic | BindingFlags.Instance);

            int currentPage = (int)(_currentPageField?.GetValue(__instance) ?? 0);

            // Get total page count
            int pageCount = 0;
            if (__instance.Book != null)
                pageCount = __instance.Book.Pages.Count;
            else if (__instance.BookID != null && BookUI.Books.ContainsKey(__instance.BookID))
                pageCount = BookUI.Books[__instance.BookID].Pages.Count;

            // Get title
            string title = null;
            if (__instance.Book != null)
                title = __instance.Book.Title;
            else if (__instance.BookID != null && BookUI.Books.ContainsKey(__instance.BookID))
                title = BookUI.Books[__instance.BookID].Title;
            title = Speech.Clean(title ?? "");

            // Get page text from the scroller data
            string pageText = "";
            var data = __instance.pageControllers?[0]?.scrollContext?.data;
            if (data != null && data.Count > 0 && data[0] is BookLineData lineData)
                pageText = Speech.Clean(lineData.text ?? "") ?? "";

            string pageInfo = "Page " + (currentPage + 1) + " of " + pageCount;

            // Build speech announcement
            var sb = new StringBuilder();
            if (_newBookSession)
            {
                _newBookSession = false;
                if (!string.IsNullOrEmpty(title))
                    sb.Append(title).Append(". ");
                sb.Append(pageInfo);
                ScreenReader.SetBlockProvider(BuildBookBlocks);
            }
            else
            {
                sb.Append(pageInfo);
            }

            Speech.Interrupt(sb.ToString());

            // F2 content: full page text
            string screenContent = "";
            if (!string.IsNullOrEmpty(title))
                screenContent = title + ". ";
            screenContent += pageInfo;
            if (!string.IsNullOrEmpty(pageText))
                screenContent += ". " + pageText;
            ScreenReader.SetScreenContent(screenContent);
        }

        private static List<ScreenReader.ContentBlock> BuildBookBlocks()
        {
            var instance = SingletonWindowBase<BookScreen>.instance;
            if (instance == null || !instance.navigationContext.IsActive())
                return null;

            var blocks = new List<ScreenReader.ContentBlock>();

            // Page content block
            string pageText = "";
            var data = instance.pageControllers?[0]?.scrollContext?.data;
            if (data != null && data.Count > 0 && data[0] is BookLineData lineData)
                pageText = Speech.Clean(lineData.text ?? "") ?? "";

            if (!string.IsNullOrEmpty(pageText))
                blocks.Add(new ScreenReader.ContentBlock { Title = "Page Content", Body = pageText });

            return blocks;
        }
    }
}
