using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XRL.UI;

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

        public static void EnsureInitialized()
        {
            if (_instance != null)
                return;

            var go = new GameObject("QudAccessibility_ScreenReader");
            _instance = go.AddComponent<ScreenReader>();
            Object.DontDestroyOnLoad(go);
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
                        if (currentTarget != null)
                        {
                            string name = currentTarget.GetDisplayName(Stripped: true);
                            string clean = Speech.Clean(name);
                            if (!string.IsNullOrEmpty(clean))
                            {
                                Speech.SayIfNew(clean);
                                var info = Look.GenerateTooltipInformation(currentTarget);
                                string full = info.DisplayName ?? "";
                                if (!string.IsNullOrEmpty(info.LongDescription))
                                    full += ". " + info.LongDescription;
                                SetScreenContent(Speech.Clean(full));
                            }
                        }
                        else
                        {
                            Speech.SayIfNew("empty");
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
            if (_blocks.Count == 0)
                return;

            _blockIndex += direction;
            if (_blockIndex >= _blocks.Count)
                _blockIndex = 0;
            if (_blockIndex < 0)
                _blockIndex = _blocks.Count - 1;

            string text = _blocks[_blockIndex].ToSpeech();
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

                        string name = Speech.Clean(
                            obj.GetDisplayName(Stripped: true));
                        if (string.IsNullOrEmpty(name))
                            continue;

                        _scanEntries.Add(new ScanEntry
                        {
                            Object = obj,
                            Name = name
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

        private static string GetCompassDirection(int dx, int dy)
        {
            string ns = dy < 0 ? "north" : dy > 0 ? "south" : "";
            string ew = dx > 0 ? "east" : dx < 0 ? "west" : "";
            if (ns.Length > 0 && ew.Length > 0) return ns + ew;
            if (ns.Length > 0) return ns;
            if (ew.Length > 0) return ew;
            return "here";
        }
    }
}
