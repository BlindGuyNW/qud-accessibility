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
    ///   Nearby object scanner:
    ///     Ctrl+PgDn/PgUp = cycle category (Creatures, Items, Corpses, Features)
    ///     PgDn/PgUp = cycle objects in current category
    ///     Home = re-announce current object with fresh direction
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
        // Nearby object scanner
        // -----------------------------------------------------------------
        private static readonly string[] ScanCategories =
            { "Creatures", "Items", "Corpses", "Features" };
        private static int _categoryIndex;
        private static readonly List<ScanEntry> _scanEntries = new List<ScanEntry>();
        private static int _scanIndex = -1;

        private struct ScanEntry
        {
            public XRL.World.GameObject Object;
            public string Name;
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
                            string doorState = GetDoorStateSuffix(currentTarget);
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

            // Nearby object scanner — map screen only, not in look mode
            if (!InLookMode && XRL.The.Player?.CurrentCell != null)
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl)
                         || Input.GetKey(KeyCode.RightControl);

                if (Input.GetKeyDown(KeyCode.PageDown) && ctrl)
                    CycleCategory(1);
                else if (Input.GetKeyDown(KeyCode.PageUp) && ctrl)
                    CycleCategory(-1);
                else if (Input.GetKeyDown(KeyCode.PageDown) && !ctrl)
                    CycleScanResult(1);
                else if (Input.GetKeyDown(KeyCode.PageUp) && !ctrl)
                    CycleScanResult(-1);
                else if (Input.GetKeyDown(KeyCode.Home))
                    AnnounceCurrentResult();
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

        // -----------------------------------------------------------------
        // Scanner: category cycling
        // -----------------------------------------------------------------
        private static void CycleCategory(int direction)
        {
            _categoryIndex = (_categoryIndex + direction + ScanCategories.Length)
                           % ScanCategories.Length;
            RefreshScan();

            string cat = ScanCategories[_categoryIndex];
            string msg = _scanEntries.Count > 0
                ? cat + ": " + _scanEntries.Count + " found"
                : "No " + cat.ToLower() + " found";
            Speech.Interrupt(msg);
            SetScreenContent(msg);
        }

        // -----------------------------------------------------------------
        // Scanner: object cycling within category
        // -----------------------------------------------------------------
        private static void CycleScanResult(int direction)
        {
            // Lazy scan on first use
            if (_scanEntries.Count == 0)
                RefreshScan();

            if (_scanEntries.Count == 0)
            {
                string msg = "No " + ScanCategories[_categoryIndex].ToLower() + " found";
                Speech.Interrupt(msg);
                return;
            }

            _scanIndex += direction;
            if (_scanIndex >= _scanEntries.Count) _scanIndex = 0;
            if (_scanIndex < 0) _scanIndex = _scanEntries.Count - 1;

            AnnounceEntry(_scanEntries[_scanIndex]);
        }

        // -----------------------------------------------------------------
        // Scanner: Home re-announces current result with fresh direction
        // -----------------------------------------------------------------
        private static void AnnounceCurrentResult()
        {
            if (_scanIndex < 0 || _scanIndex >= _scanEntries.Count)
            {
                RefreshScan();
                if (_scanEntries.Count == 0)
                {
                    string msg = "No " + ScanCategories[_categoryIndex].ToLower() + " found";
                    Speech.Interrupt(msg);
                    return;
                }
                _scanIndex = 0;
            }

            AnnounceEntry(_scanEntries[_scanIndex]);
        }

        // -----------------------------------------------------------------
        // Scanner: speak an entry with distance + compass direction
        // -----------------------------------------------------------------
        private static void AnnounceEntry(ScanEntry entry)
        {
            var player = XRL.The.Player;
            if (player?.CurrentCell == null || entry.Object?.CurrentCell == null)
                return;

            int dx = entry.Object.CurrentCell.X - player.CurrentCell.X;
            int dy = entry.Object.CurrentCell.Y - player.CurrentCell.Y;
            int dist = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy));
            string dir = GetCompassDirection(dx, dy);

            string msg = dist == 0
                ? entry.Name + ", here"
                : entry.Name + ", " + dist + " " + dir;

            Speech.Interrupt(msg);
            SetScreenContent(msg);
        }

        // -----------------------------------------------------------------
        // Scanner: scan the current zone for objects matching the category
        // -----------------------------------------------------------------
        private static void RefreshScan()
        {
            _scanEntries.Clear();
            _scanIndex = -1;

            var player = XRL.The.Player;
            if (player?.CurrentCell == null)
                return;

            var zone = player.CurrentZone;
            if (zone == null)
                return;

            string category = ScanCategories[_categoryIndex];

            for (int x = 0; x < zone.Width; x++)
            {
                for (int y = 0; y < zone.Height; y++)
                {
                    var cell = zone.GetCell(x, y);
                    if (cell == null)
                        continue;

                    // Creatures: must be currently visible (they move)
                    // Others: explored is enough (static objects)
                    if (category == "Creatures")
                    {
                        if (!cell.IsVisible())
                            continue;
                    }
                    else
                    {
                        if (!cell.IsExplored())
                            continue;
                    }

                    foreach (var obj in cell.GetObjectsInCell())
                    {
                        if (!MatchesCategory(obj, category))
                            continue;

                        string colorSuffix = Speech.GetObjectColorSuffix(obj);
                        string name = Speech.Clean(
                            obj.GetDisplayName(Stripped: true));
                        if (string.IsNullOrEmpty(name))
                            continue;

                        _scanEntries.Add(new ScanEntry
                        {
                            Object = obj,
                            Name = name + GetDoorStateSuffix(obj) + colorSuffix
                        });
                    }
                }
            }

            // Sort by distance to player (closest first)
            int px = player.CurrentCell.X;
            int py = player.CurrentCell.Y;
            _scanEntries.Sort((a, b) =>
            {
                int da = ChebyshevDist(a.Object, px, py);
                int db = ChebyshevDist(b.Object, px, py);
                return da.CompareTo(db);
            });
        }

        private static bool MatchesCategory(XRL.World.GameObject obj, string category)
        {
            switch (category)
            {
                case "Creatures":
                    return obj.HasPart("Brain")
                        && obj != XRL.The.Player
                        && obj.IsVisible();
                case "Items":
                    return obj.IsTakeable()
                        && !obj.HasPropertyOrTag("Corpse");
                case "Corpses":
                    return obj.HasPropertyOrTag("Corpse");
                case "Features":
                    return obj.HasPart("StairsUp")
                        || obj.HasPart("StairsDown")
                        || obj.HasPart("Door");
                default:
                    return false;
            }
        }

        private static int ChebyshevDist(XRL.World.GameObject obj, int px, int py)
        {
            if (obj?.CurrentCell == null)
                return int.MaxValue;
            return System.Math.Max(
                System.Math.Abs(obj.CurrentCell.X - px),
                System.Math.Abs(obj.CurrentCell.Y - py));
        }

        /// <summary>
        /// Returns a door state suffix like " (open)", " (closed)", or " (locked)"
        /// for objects with a Door part. Returns "" for non-doors.
        /// </summary>
        private static string GetDoorStateSuffix(XRL.World.GameObject obj)
        {
            var door = obj.GetPart<Door>();
            if (door == null)
                return "";
            if (door.Open)
                return " (open)";
            if (door.Locked)
                return " (locked)";
            return " (closed)";
        }

        private static string GetCompassDirection(int dx, int dy)
        {
            string ns = dy < 0 ? "north" : dy > 0 ? "south" : "";
            string ew = dx > 0 ? "east" : dx < 0 ? "west" : "";
            if (ns.Length > 0 && ew.Length > 0) return ns + ew;
            if (ns.Length > 0) return ns;
            if (ew.Length > 0) return ew;
            return "here";
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
