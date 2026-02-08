using System.Collections.Generic;
using HarmonyLib;
using Qud.UI;
using XRL;

namespace QudAccessibility
{
    /// <summary>
    /// Harmony patches for the Mod Manager screen: title announcement,
    /// mod selection/state vocalization, context button tracking,
    /// and F3/F4 block provider for detailed mod info.
    /// </summary>
    [HarmonyPatch]
    public static class ModsPatches
    {
        private static int _lastSelectedOption = -1;
        private static bool _suppressNextSelect;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ModManagerUI), nameof(ModManagerUI.Show))]
        public static void ModManagerUI_Show_Prefix()
        {
            _suppressNextSelect = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModManagerUI), nameof(ModManagerUI.Show))]
        public static void ModManagerUI_Show_Postfix(ModManagerUI __instance)
        {
            string first = null;
            if (__instance.ms1?.mods != null && __instance.ms1.mods.Count > 0)
                first = BuildModAnnouncement(__instance.ms1.mods[0]);

            string announcement = first != null
                ? "Mod Manager. " + first
                : "Mod Manager";
            ScreenReader.SetScreenContent(announcement);
            Speech.Announce(announcement);
            ScreenReader.SetBlockProvider(BuildModBlocks);
            _lastSelectedOption = 0;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModManagerUI), nameof(ModManagerUI.OnSelect))]
        public static void ModManagerUI_OnSelect_Postfix(ModInfo modInfo)
        {
            if (_suppressNextSelect)
            {
                _suppressNextSelect = false;
                return;
            }
            string announcement = BuildModAnnouncement(modInfo);
            ScreenReader.SetScreenContent(BuildModScreenContent(modInfo));
            Speech.SayIfNew(announcement);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModScrollerOne), nameof(ModScrollerOne.OnActivate))]
        public static void ModScrollerOne_OnActivate_Postfix(ModInfo modInfo)
        {
            Speech.Interrupt(GetStateName(modInfo.State));
        }

        /// <summary>
        /// Track selection changes to announce context buttons (Toggle All,
        /// Space action, Back) which don't fire selectHandlers.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ModScrollerOne), nameof(ModScrollerOne.Update))]
        public static void ModScrollerOne_Update_Postfix(ModScrollerOne __instance)
        {
            int current = __instance.selectedOption;
            if (current == _lastSelectedOption)
                return;
            _lastSelectedOption = current;

            int modCount = __instance.mods?.Count ?? 0;
            if (current >= modCount)
            {
                int btnIdx = current - modCount;
                if (btnIdx >= 0 && btnIdx < __instance.bottomContextOptions.Count)
                {
                    string text = Speech.Clean(__instance.bottomContextOptions[btnIdx].text);
                    if (text != null)
                    {
                        ScreenReader.SetScreenContent(text);
                        Speech.SayIfNew(text);
                    }
                }
            }
        }

        private static string BuildModAnnouncement(ModInfo modInfo)
        {
            string title = Speech.Clean(modInfo.DisplayTitle) ?? modInfo.ID;
            string state = GetStateName(modInfo.State);
            string result = title + ". " + state;

            if (!string.IsNullOrEmpty(modInfo.Manifest.Author))
                result += ". by " + modInfo.Manifest.Author;

            if (modInfo.HasUpdate)
                result += ". Update available";

            return result;
        }

        private static string BuildModScreenContent(ModInfo modInfo)
        {
            string content = BuildModAnnouncement(modInfo);
            if (!string.IsNullOrEmpty(modInfo.Manifest.Description))
                content += ". " + Speech.Clean(modInfo.Manifest.Description);
            return content;
        }

        private static string GetStateName(ModState state)
        {
            switch (state)
            {
                case ModState.Enabled: return "Enabled";
                case ModState.Disabled: return "Disabled";
                case ModState.MissingDependency: return "Missing dependency";
                case ModState.Failed: return "Failed";
                default: return state.ToString();
            }
        }

        private static List<ScreenReader.ContentBlock> BuildModBlocks()
        {
            var instance = SingletonWindowBase<ModManagerUI>.instance;
            if (instance == null || !instance.IsCurrentWindow())
                return null;

            var ms1 = instance.ms1;
            if (ms1?.mods == null || ms1.mods.Count == 0)
                return null;

            int idx = ms1.selectedOption;
            if (idx < 0 || idx >= ms1.mods.Count)
                return null;

            ModInfo mod = ms1.mods[idx];
            var blocks = new List<ScreenReader.ContentBlock>();

            // Status block: title + state + flags
            string statusBody = Speech.Clean(mod.DisplayTitle) + ". " + GetStateName(mod.State);
            if (mod.HasUpdate)
                statusBody += ". Update available";
            if (mod.IsScripting)
                statusBody += ". Scripting";
            if (mod.Harmony != null)
                statusBody += ". Harmony patches";
            blocks.Add(new ScreenReader.ContentBlock { Title = "Status", Body = statusBody });

            if (!string.IsNullOrEmpty(mod.Manifest.Author))
                blocks.Add(new ScreenReader.ContentBlock { Title = "Author", Body = mod.Manifest.Author });

            if (!string.IsNullOrEmpty(mod.Manifest.Description))
                blocks.Add(new ScreenReader.ContentBlock { Title = "Description", Body = Speech.Clean(mod.Manifest.Description) });

            if (!mod.Manifest.Version.IsZero())
                blocks.Add(new ScreenReader.ContentBlock { Title = "Version", Body = mod.Manifest.Version.ToString() });

            if (mod.Manifest.Tags != null && mod.Manifest.Tags.Length > 0)
                blocks.Add(new ScreenReader.ContentBlock { Title = "Tags", Body = string.Join(", ", mod.Manifest.Tags) });

            return blocks;
        }
    }
}
