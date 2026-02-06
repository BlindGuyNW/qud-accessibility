using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using XRL.Messages;
using XRL.Rules;
using XRL.UI;
using XRL.World;
using XRL.World.Parts;

namespace QudAccessibility
{
    /// <summary>
    /// MonoBehaviour that provides:
    ///   F2 = re-read full screen content
    ///   F3 = next content block / F4 = previous content block
    ///   Look mode cursor tracking (speaks objects + "empty")
    ///   Pick target mode cursor tracking
    /// Dispatches to NearbyScanner for PgUp/PgDn/Home on the map screen.
    /// Created lazily by Speech.EnsureInitialized().
    /// </summary>
    public class ScreenReader : MonoBehaviour
    {
        private static ScreenReader _instance;

        // Full screen content for F2 re-read
        private static string _screenContent;

        // Navigable content blocks for F3/F4
        private static readonly List<ContentBlock> _blocks = new List<ContentBlock>();
        private static int _blockIndex = -1;

        // Dynamic block providers — screen-specific and fallback (map)
        private static Func<List<ContentBlock>> _blockProvider;
        private static Func<List<ContentBlock>> _defaultProvider;

        public struct ContentBlock
        {
            public string Title;
            public string Body;

            public string ToSpeech()
            {
                if (!string.IsNullOrEmpty(Title) && !string.IsNullOrEmpty(Body))
                    return Title + ": " + Body;
                return Title ?? Body ?? "";
            }
        }

        /// <summary>
        /// Store the current screen's readable content for F2 re-read.
        /// Also clears any previous block navigation.
        /// </summary>
        public static void SetScreenContent(string content)
        {
            _screenContent = content;
            _blocks.Clear();
            _blockIndex = -1;
        }

        /// <summary>
        /// Set navigable content blocks for F3/F4 cycling.
        /// Does not affect the F2 screen content.
        /// </summary>
        public static void SetBlocks(List<ContentBlock> blocks)
        {
            _blocks.Clear();
            if (blocks != null)
                _blocks.AddRange(blocks);
            _blockIndex = -1;
        }

        /// <summary>
        /// Set a screen-specific block provider. Called when a screen opens.
        /// The provider returns null when its screen is no longer active,
        /// which auto-clears it and falls back to the default provider.
        /// </summary>
        public static void SetBlockProvider(Func<List<ContentBlock>> provider)
        {
            _blockProvider = provider;
            _blockIndex = -1;
        }

        /// <summary>
        /// Manually clear the screen-specific block provider.
        /// </summary>
        public static void ClearBlockProvider()
        {
            _blockProvider = null;
            _blockIndex = -1;
        }

        /// <summary>
        /// Set the fallback block provider (map screen). Called once at init.
        /// </summary>
        public static void SetDefaultProvider(Func<List<ContentBlock>> provider)
        {
            _defaultProvider = provider;
        }

        public static void EnsureInitialized()
        {
            if (_instance != null)
                return;

            var go = new UnityEngine.GameObject("QudAccessibility_ScreenReader");
            _instance = go.AddComponent<ScreenReader>();
            UnityEngine.Object.DontDestroyOnLoad(go);

            SetDefaultProvider(BuildMapBlocks);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        // -----------------------------------------------------------------
        // Look mode tracking — we track both the cursor position (via
        // reflection into Look's private Buffer) and the lookingAt object.
        // Position tracking catches empty tiles; object tracking catches
        // cycling objects on the same tile (V Positive/V Negative).
        // -----------------------------------------------------------------
        internal static bool InLookMode;
        private static XRL.World.GameObject _lastLookTarget;
        private static FieldInfo _lookBufferField;
        private static int _lastLookX = int.MinValue;
        private static int _lastLookY = int.MinValue;

        // -----------------------------------------------------------------
        // Pick target mode tracking
        // -----------------------------------------------------------------
        internal static bool InPickTargetMode;
        private static int _lastPickX = int.MinValue;
        private static int _lastPickY = int.MinValue;

        internal static void EnterPickTargetMode()
        {
            InPickTargetMode = true;
            _lastPickX = int.MinValue;
            _lastPickY = int.MinValue;
        }

        internal static void ExitPickTargetMode()
        {
            InPickTargetMode = false;
        }

        // -----------------------------------------------------------------
        // Update — runs every frame
        // -----------------------------------------------------------------
        private void Update()
        {
            // F2: re-read screen content
            if (Input.GetKeyDown(KeyCode.F2))
            {
                if (!string.IsNullOrEmpty(_screenContent))
                    Speech.Interrupt(_screenContent);
            }

            // F3/F4: navigate content blocks
            if (Input.GetKeyDown(KeyCode.F3))
                NavigateBlock(1);
            if (Input.GetKeyDown(KeyCode.F4))
                NavigateBlock(-1);

            // Look mode: track cursor position and lookingAt changes
            if (_lookBufferField == null)
                _lookBufferField = typeof(Look).GetField("Buffer",
                    BindingFlags.NonPublic | BindingFlags.Static);

            var lookBuffer = _lookBufferField?.GetValue(null)
                as ConsoleLib.Console.ScreenBuffer;
            if (lookBuffer != null)
            {
                int cx = lookBuffer.focusPosition.x;
                int cy = lookBuffer.focusPosition.y;
                var currentTarget = Look.lookingAt;

                bool positionChanged = cx != _lastLookX || cy != _lastLookY;
                bool targetChanged = currentTarget != _lastLookTarget;

                if (positionChanged || targetChanged)
                {
                    _lastLookX = cx;
                    _lastLookY = cy;
                    _lastLookTarget = currentTarget;

                    // Only speak if position is valid (we're in look mode)
                    if (cx != int.MinValue)
                    {
                        string coords = $"({cx}, {cy})";
                        if (currentTarget != null)
                        {
                            string colorSuffix = Speech.GetObjectColorSuffix(currentTarget);
                            string doorState = NearbyScanner.GetDoorStateSuffix(currentTarget);
                            string name = currentTarget.GetDisplayName(Stripped: true);
                            string clean = Speech.Clean(name);
                            if (!string.IsNullOrEmpty(clean))
                            {
                                Speech.SayIfNew($"{clean}{doorState}{colorSuffix} {coords}");
                                var info = Look.GenerateTooltipInformation(currentTarget);
                                string full = info.DisplayName ?? "";
                                if (!string.IsNullOrEmpty(info.LongDescription))
                                    full += ". " + info.LongDescription;
                                SetScreenContent(Speech.Clean(full));
                            }
                        }
                        else
                        {
                            Speech.SayIfNew($"empty {coords}");
                            SetScreenContent("empty");
                        }
                    }
                }
            }

            // Pick target mode: track cursor and announce cell contents
            if (InPickTargetMode)
            {
                var pickBuffer = PickTarget.Buffer;
                if (pickBuffer != null)
                {
                    int cx = pickBuffer.focusPosition.x;
                    int cy = pickBuffer.focusPosition.y;

                    if (cx != _lastPickX || cy != _lastPickY)
                    {
                        _lastPickX = cx;
                        _lastPickY = cy;

                        var player = XRL.The.Player;
                        if (player?.CurrentCell != null)
                        {
                            var zone = player.CurrentZone;
                            if (zone != null && cx >= 0 && cx < zone.Width
                                && cy >= 0 && cy < zone.Height)
                            {
                                var cell = zone.GetCell(cx, cy);
                                string desc = GetCellDescription(cell);

                                int dx = cx - player.CurrentCell.X;
                                int dy = cy - player.CurrentCell.Y;
                                int dist = System.Math.Max(
                                    System.Math.Abs(dx), System.Math.Abs(dy));
                                string position = dist == 0
                                    ? "here"
                                    : dist + " " + NearbyScanner.GetCompassDirection(dx, dy);

                                string msg = desc + ", " + position;
                                Speech.SayIfNew(msg);
                                SetScreenContent(msg);
                            }
                        }
                    }
                }
            }

            // Nearby object scanner — map screen only, not in look mode or pick target
            if (!InLookMode && !InPickTargetMode && XRL.The.Player?.CurrentCell != null)
            {
                NearbyScanner.HandleInput();
            }
        }

        // -----------------------------------------------------------------
        // Content block navigation (F3/F4)
        // -----------------------------------------------------------------
        private static void NavigateBlock(int direction)
        {
            List<ContentBlock> activeBlocks = null;

            // 1. Try screen-specific provider
            if (_blockProvider != null)
            {
                activeBlocks = _blockProvider();
                if (activeBlocks == null)
                {
                    // Screen no longer active — auto-clear
                    _blockProvider = null;
                    _blockIndex = -1;
                }
            }

            // 2. Fall back to static blocks (from SetBlocks, e.g. chargen summary)
            if (activeBlocks == null && _blocks.Count > 0)
                activeBlocks = _blocks;

            // 3. Fall back to default provider (map screen)
            if (activeBlocks == null && _defaultProvider != null)
                activeBlocks = _defaultProvider();

            if (activeBlocks == null || activeBlocks.Count == 0)
                return;

            _blockIndex += direction;
            if (_blockIndex >= activeBlocks.Count)
                _blockIndex = 0;
            if (_blockIndex < 0)
                _blockIndex = activeBlocks.Count - 1;

            string text = activeBlocks[_blockIndex].ToSpeech();
            if (!string.IsNullOrEmpty(text))
                Speech.Interrupt(text);
        }

        /// <summary>
        /// Describe the most relevant object in a cell for targeting feedback.
        /// Prioritizes combat objects, then other visible objects, then "empty".
        /// </summary>
        private static string GetCellDescription(XRL.World.Cell cell)
        {
            if (cell == null)
                return "empty";

            XRL.World.GameObject best = null;
            bool bestIsCombat = false;

            foreach (var obj in cell.GetObjectsInCell())
            {
                if (obj == null)
                    continue;
                // Skip the player
                if (obj == XRL.The.Player)
                    continue;
                // Skip invisible objects
                if (!obj.IsVisible())
                    continue;

                bool combat = obj.HasPart("Brain") || obj.HasPart("Combat");
                if (best == null || (combat && !bestIsCombat))
                {
                    best = obj;
                    bestIsCombat = combat;
                }
                // If we already have a combat object, stop looking
                if (bestIsCombat)
                    break;
            }

            if (best == null)
                return "empty";

            string name = Speech.Clean(best.GetDisplayName(Stripped: true));
            if (string.IsNullOrEmpty(name))
                return "empty";

            string colorSuffix = Speech.GetObjectColorSuffix(best);
            string doorState = NearbyScanner.GetDoorStateSuffix(best);
            return name + doorState + colorSuffix;
        }

        // -----------------------------------------------------------------
        // Default block provider: map screen HUD info
        // -----------------------------------------------------------------
        private static List<ContentBlock> BuildMapBlocks()
        {
            var player = XRL.The.Player;
            if (player?.CurrentCell == null)
                return null;

            var blocks = new List<ContentBlock>();

            // Block 1: Stats
            var statsSb = new StringBuilder();
            statsSb.Append("HP " + player.hitpoints + "/" + player.baseHitpoints);
            var avStat = player.Statistics.ContainsKey("AV") ? player.Statistics["AV"] : null;
            if (avStat != null)
                statsSb.Append(", AV " + Stats.GetCombatAV(player));
            statsSb.Append(", DV " + Stats.GetCombatDV(player));
            statsSb.Append(", MA " + Stats.GetCombatMA(player));
            statsSb.Append(", Quickness " + player.Speed);
            var msStatObj = player.Statistics.ContainsKey("MoveSpeed") ? player.Statistics["MoveSpeed"] : null;
            if (msStatObj != null)
                statsSb.Append(", Move Speed " + (200 - msStatObj.Value));
            statsSb.Append(", Weight " + player.GetCarriedWeight() + "/" + player.GetMaxCarriedWeight());
            blocks.Add(new ContentBlock { Title = "Stats", Body = statsSb.ToString() });

            // Block 2: Condition
            var condSb = new StringBuilder();
            var stomach = player.GetPart<Stomach>();
            if (stomach != null)
            {
                string food = Speech.Clean(stomach.FoodStatus());
                string water = Speech.Clean(stomach.WaterStatus());
                if (!string.IsNullOrEmpty(food))
                    condSb.Append(food);
                if (!string.IsNullOrEmpty(water))
                {
                    if (condSb.Length > 0) condSb.Append(", ");
                    condSb.Append(water);
                }
            }
            var physics = player.GetPart<XRL.World.Parts.Physics>();
            if (physics != null)
            {
                if (condSb.Length > 0) condSb.Append(", ");
                condSb.Append("Temperature " + physics.Temperature);
            }
            foreach (var effect in player.Effects)
            {
                if (effect.Duration > 0 && !effect.SuppressInStageDisplay())
                {
                    string desc = Speech.Clean(effect.GetDescription());
                    if (!string.IsNullOrEmpty(desc))
                    {
                        if (condSb.Length > 0) condSb.Append(", ");
                        condSb.Append(desc);
                    }
                }
            }
            blocks.Add(new ContentBlock { Title = "Condition", Body = condSb.ToString() });

            // Block 3: Location
            var locSb = new StringBuilder();
            var zone = player.CurrentCell.ParentZone;
            if (zone != null)
                locSb.Append(Speech.Clean(zone.DisplayName));
            string time = Calendar.GetTime();
            string day = Calendar.GetDay();
            string month = Calendar.GetMonth();
            if (locSb.Length > 0) locSb.Append(". ");
            locSb.Append(time + " " + day + " of " + month);
            blocks.Add(new ContentBlock { Title = "Location", Body = locSb.ToString() });

            // Block 4: Messages (last 5)
            var msgSb = new StringBuilder();
            var msgQueue = XRL.The.Game?.Player?.Messages;
            if (msgQueue != null)
            {
                var messages = msgQueue.Messages;
                int start = Math.Max(0, messages.Count - 5);
                for (int i = start; i < messages.Count; i++)
                {
                    string msg = Speech.Clean(messages[i]);
                    if (!string.IsNullOrEmpty(msg))
                    {
                        if (msgSb.Length > 0) msgSb.Append(". ");
                        msgSb.Append(msg);
                    }
                }
            }
            blocks.Add(new ContentBlock { Title = "Messages", Body = msgSb.ToString() });

            // Block 5: Abilities
            var abilSb = new StringBuilder();
            var abilities = player.GetPart<ActivatedAbilities>();
            if (abilities != null)
            {
                foreach (var entry in abilities.GetAbilityListOrderedByPreference())
                {
                    if (entry == null || string.IsNullOrEmpty(entry.DisplayName))
                        continue;
                    if (abilSb.Length > 0) abilSb.Append(", ");
                    abilSb.Append(entry.DisplayName);
                    if (entry.Toggleable && entry.ToggleState)
                        abilSb.Append(": active");
                    else if (entry.Cooldown > 0)
                        abilSb.Append(": " + entry.CooldownDescription + " cooldown");
                    else
                        abilSb.Append(": ready");
                }
            }
            blocks.Add(new ContentBlock { Title = "Abilities", Body = abilSb.ToString() });

            return blocks;
        }
    }
}
