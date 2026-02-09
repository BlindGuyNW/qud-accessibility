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
        // Indicate direction tracking — gamepad left stick compass direction.
        // Announces the contents of the adjacent tile being indicated.
        // -----------------------------------------------------------------
        private static string _lastNavDirection;

        private static readonly Dictionary<string, (int dx, int dy, string name)> _dirMap
            = new Dictionary<string, (int, int, string)>
        {
            { "N",  ( 0, -1, "North") },
            { "S",  ( 0,  1, "South") },
            { "E",  ( 1,  0, "East") },
            { "W",  (-1,  0, "West") },
            { "NE", ( 1, -1, "Northeast") },
            { "NW", (-1, -1, "Northwest") },
            { "SE", ( 1,  1, "Southeast") },
            { "SW", (-1,  1, "Southwest") },
        };

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
        private static bool _useMissileBuffer;
        private static int _lastPickX = int.MinValue;
        private static int _lastPickY = int.MinValue;
        private static PickTarget.PickStyle _pickStyle;
        private static int _pickRadius;
        private static int _pickRange;
        private static bool _isBowOrRifle;
        private static FieldInfo _missilePath;
        private static bool _missilePathFieldResolved;

        internal static void EnterPickTargetMode()
        {
            InPickTargetMode = true;
            _useMissileBuffer = false;
            _lastPickX = int.MinValue;
            _lastPickY = int.MinValue;
            _pickStyle = PickTarget.PickStyle.EmptyCell;
            _pickRadius = 0;
            _pickRange = 9999;
        }

        internal static void EnterPickTargetMode(
            PickTarget.PickStyle style, int radius, int range)
        {
            InPickTargetMode = true;
            _useMissileBuffer = false;
            _lastPickX = int.MinValue;
            _lastPickY = int.MinValue;
            _pickStyle = style;
            _pickRadius = radius;
            _pickRange = range;
        }

        internal static void EnterMissileTargetMode(int range, bool bowOrRifle)
        {
            InPickTargetMode = true;
            _useMissileBuffer = true;
            _lastPickX = int.MinValue;
            _lastPickY = int.MinValue;
            _pickStyle = PickTarget.PickStyle.Line;
            _pickRadius = 0;
            _pickRange = range;
            _isBowOrRifle = bowOrRifle;
            SetBlockProvider(BuildMissileTargetBlocks);
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

            if (InLookMode)
            {
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
                                    var info = Look.GenerateTooltipInformation(currentTarget);

                                    // Extract creature status from tooltip
                                    string feeling = Speech.Clean(info.FeelingText);
                                    string difficulty = Speech.Clean(info.DifficultyText);
                                    string wound = Speech.Clean(info.WoundLevel);
                                    // Scanning WoundLevel uses CP437 icons:
                                    // \u0004 (diamond) = AV, \t (tab) = DV
                                    if (wound != null)
                                        wound = wound.Replace("\u0004", "AV ").Replace("\t", " DV ");

                                    // Speak: name, feeling, difficulty, health, coords
                                    var spoken = new StringBuilder(clean);
                                    spoken.Append(doorState);
                                    spoken.Append(colorSuffix);
                                    if (!string.IsNullOrEmpty(feeling))
                                        spoken.Append(", ").Append(feeling);
                                    if (!string.IsNullOrEmpty(difficulty))
                                        spoken.Append(", ").Append(difficulty);
                                    if (!string.IsNullOrEmpty(wound))
                                        spoken.Append(", ").Append(wound);
                                    spoken.Append(' ').Append(coords);
                                    Speech.SayIfNew(spoken.ToString());

                                    // F2 content: full details
                                    var full = new StringBuilder();
                                    full.Append(Speech.Clean(info.DisplayName ?? ""));
                                    if (!string.IsNullOrEmpty(feeling) || !string.IsNullOrEmpty(difficulty))
                                    {
                                        full.Append(". ");
                                        if (!string.IsNullOrEmpty(feeling))
                                            full.Append(feeling);
                                        if (!string.IsNullOrEmpty(feeling) && !string.IsNullOrEmpty(difficulty))
                                            full.Append(", ");
                                        if (!string.IsNullOrEmpty(difficulty))
                                            full.Append(difficulty);
                                    }
                                    if (!string.IsNullOrEmpty(wound))
                                        full.Append(". ").Append(wound);
                                    if (!string.IsNullOrEmpty(info.LongDescription))
                                        full.Append(". ").Append(Speech.Clean(info.LongDescription));
                                    SetScreenContent(full.ToString());
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
            }

            // Pick target mode: track cursor and announce cell contents + AoE
            if (InPickTargetMode)
            {
                var pickBuffer = _useMissileBuffer
                    ? ConsoleLib.Console.TextConsole.ScrapBuffer
                    : PickTarget.Buffer;
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
                                int ox = player.CurrentCell.X;
                                int oy = player.CurrentCell.Y;
                                int dx = cx - ox;
                                int dy = cy - oy;
                                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                                string position = dist == 0
                                    ? "here"
                                    : dist + " " + NearbyScanner.GetCompassDirection(dx, dy);

                                if (_useMissileBuffer)
                                {
                                    var cell = zone.GetCell(cx, cy);
                                    string desc = GetCellDescription(cell);
                                    string msg = desc + ", " + position;
                                    if (dist > _pickRange)
                                    {
                                        msg += ", out of range";
                                    }
                                    else
                                    {
                                        string cover = GetMissileCover();
                                        if (cover != null)
                                            msg += ", " + cover;
                                    }
                                    string friendlies = GetFriendliesInPath();
                                    if (friendlies != null)
                                        msg += ", " + friendlies;
                                    Speech.SayIfNew(msg);
                                    SetScreenContent(msg);
                                }
                                else if (_pickStyle == PickTarget.PickStyle.EmptyCell)
                                {
                                    var cell = zone.GetCell(cx, cy);
                                    string desc = GetCellDescription(cell);
                                    string msg = desc + ", " + position;
                                    Speech.SayIfNew(msg);
                                    SetScreenContent(msg);
                                }
                                else
                                {
                                    var aoeCells = ComputeAoECells(
                                        zone, ox, oy, cx, cy);
                                    string shape = GetShapeDescription(
                                        aoeCells.Count);
                                    string creatures = BuildCreatureSummary(
                                        aoeCells);
                                    string msg = position + ". " + shape
                                        + ". " + creatures;
                                    Speech.SayIfNew(msg);
                                    SetScreenContent(msg);
                                }
                            }
                        }
                    }
                }
            }

            // Indicate direction: announce adjacent tile contents when stick direction changes
            if (!InLookMode && !InPickTargetMode && XRL.The.Player?.CurrentCell != null)
            {
                string navDir = ControlManager.ResolveAxisDirection("IndicateDirection");
                if (navDir != _lastNavDirection)
                {
                    _lastNavDirection = navDir;
                    if (navDir != null && navDir != "."
                        && _dirMap.TryGetValue(navDir, out var dir))
                    {
                        var cell = XRL.The.Player.CurrentCell;
                        var zone = cell.ParentZone;
                        int tx = cell.X + dir.dx;
                        int ty = cell.Y + dir.dy;
                        if (zone != null && tx >= 0 && tx < zone.Width
                            && ty >= 0 && ty < zone.Height)
                        {
                            string desc = GetCellDescription(zone.GetCell(tx, ty));
                            Speech.SayIfNew(dir.name + ", " + desc);
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

        /// <summary>
        /// Read cover percentage at the target from MissileWeapon's private
        /// PlayerMissilePath. Returns a human-readable string like "60% cover"
        /// or null if unavailable.
        /// </summary>
        private static string GetMissileCover()
        {
            if (!_missilePathFieldResolved)
            {
                _missilePath = typeof(XRL.World.Parts.MissileWeapon).GetField(
                    "PlayerMissilePath",
                    BindingFlags.NonPublic | BindingFlags.Static);
                _missilePathFieldResolved = true;
            }

            if (_missilePath == null)
                return null;

            var path = _missilePath.GetValue(null) as XRL.World.Parts.MissilePath;
            if (path?.Cover == null || path.Cover.Count == 0)
                return null;

            // Last entry in Cover corresponds to the cursor position
            float cover = path.Cover[path.Cover.Count - 1];
            int pct = (int)(cover * 100);
            if (pct <= 0)
                return "clear shot";
            if (pct >= 100)
                return "full cover";
            return pct + "% cover";
        }

        /// <summary>
        /// Check MissilePath for friendly creatures between player and target.
        /// Returns a warning string like "friendly in path: snapjaw" or null.
        /// </summary>
        private static string GetFriendliesInPath()
        {
            if (!_missilePathFieldResolved)
            {
                _missilePath = typeof(MissileWeapon).GetField(
                    "PlayerMissilePath",
                    BindingFlags.NonPublic | BindingFlags.Static);
                _missilePathFieldResolved = true;
            }

            if (_missilePath == null)
                return null;

            var path = _missilePath.GetValue(null) as MissilePath;
            if (path?.Path == null || path.Path.Count < 2)
                return null;

            var player = XRL.The.Player;
            if (player == null)
                return null;

            var friendlyNames = new List<string>();
            // Check cells along the path except the last (target cell)
            for (int i = 0; i < path.Path.Count - 1; i++)
            {
                var cell = path.Path[i];
                foreach (var obj in cell.GetObjectsInCell())
                {
                    if (obj == null || !obj.IsVisible() || obj.IsPlayer())
                        continue;
                    if (!obj.IsCombatObject())
                        continue;
                    if (!obj.IsHostileTowards(player)
                        && !player.IsHostileTowards(obj))
                    {
                        string name = Speech.Clean(
                            obj.GetDisplayName(Stripped: true));
                        if (!string.IsNullOrEmpty(name))
                            friendlyNames.Add(name);
                    }
                }
            }

            if (friendlyNames.Count == 0)
                return null;
            return "friendly in path: " + string.Join(", ", friendlyNames);
        }

        // -----------------------------------------------------------------
        // Pick target AoE computation and block provider
        // -----------------------------------------------------------------

        /// <summary>
        /// Compute the cells affected by the current targeting shape.
        /// Replicates the game's AoE computation from PickTarget.ShowPicker.
        /// </summary>
        private static List<Cell> ComputeAoECells(
            Zone zone, int originX, int originY, int cursorX, int cursorY)
        {
            var cells = new List<Cell>();

            switch (_pickStyle)
            {
                case PickTarget.PickStyle.EmptyCell:
                    cells.Add(zone.GetCell(cursorX, cursorY));
                    break;

                case PickTarget.PickStyle.Cone:
                {
                    var coneCells = XRL.Rules.Geometry.GetCone(
                        Genkit.Location2D.Get(originX, originY),
                        Genkit.Location2D.Get(cursorX, cursorY),
                        _pickRange, _pickRadius);
                    foreach (var loc in coneCells)
                    {
                        if (loc.X >= 0 && loc.X < zone.Width
                            && loc.Y >= 0 && loc.Y < zone.Height)
                            cells.Add(zone.GetCell(loc.X, loc.Y));
                    }
                    break;
                }

                case PickTarget.PickStyle.Line:
                {
                    var linePoints = new List<Point>();
                    Zone.Line(originX, originY, cursorX, cursorY, linePoints);
                    // Skip index 0 (the origin/player cell)
                    for (int i = 1; i < linePoints.Count; i++)
                    {
                        int px = linePoints[i].X;
                        int py = linePoints[i].Y;
                        if (px >= 0 && px < zone.Width
                            && py >= 0 && py < zone.Height)
                            cells.Add(zone.GetCell(px, py));
                    }
                    break;
                }

                case PickTarget.PickStyle.Circle:
                {
                    int x1 = Math.Max(0, cursorX - _pickRadius);
                    int x2 = Math.Min(zone.Width - 1, cursorX + _pickRadius);
                    int y1 = Math.Max(0, cursorY - _pickRadius);
                    int y2 = Math.Min(zone.Height - 1, cursorY + _pickRadius);
                    for (int y = y1; y <= y2; y++)
                    {
                        for (int x = x1; x <= x2; x++)
                        {
                            if (Math.Sqrt((x - cursorX) * (x - cursorX)
                                        + (y - cursorY) * (y - cursorY))
                                <= _pickRadius)
                                cells.Add(zone.GetCell(x, y));
                        }
                    }
                    break;
                }

                case PickTarget.PickStyle.Burst:
                {
                    int x1 = Math.Max(0, cursorX - _pickRadius);
                    int x2 = Math.Min(zone.Width - 1, cursorX + _pickRadius);
                    int y1 = Math.Max(0, cursorY - _pickRadius);
                    int y2 = Math.Min(zone.Height - 1, cursorY + _pickRadius);
                    for (int y = y1; y <= y2; y++)
                    {
                        for (int x = x1; x <= x2; x++)
                            cells.Add(zone.GetCell(x, y));
                    }
                    break;
                }
            }

            return cells;
        }

        private static string GetShapeDescription(int cellCount)
        {
            switch (_pickStyle)
            {
                case PickTarget.PickStyle.Cone:
                    return "Cone " + cellCount + " cells";
                case PickTarget.PickStyle.Line:
                    return "Line " + cellCount + " cells";
                case PickTarget.PickStyle.Burst:
                    return "Burst " + cellCount + " cells";
                case PickTarget.PickStyle.Circle:
                    return "Circle " + cellCount + " cells";
                default:
                    return cellCount + " cells";
            }
        }

        /// <summary>
        /// Build a creature summary for AoE cells, with names and positions.
        /// </summary>
        private static string BuildCreatureSummary(List<Cell> cells)
        {
            var hostile = new List<string>();
            var friendly = new List<string>();
            bool includesPlayer = false;
            var player = XRL.The.Player;
            int ox = player.CurrentCell.X;
            int oy = player.CurrentCell.Y;

            foreach (var cell in cells)
            {
                foreach (var obj in cell.GetObjectsInCell())
                {
                    if (obj == null || !obj.IsVisible())
                        continue;
                    if (obj == player)
                    {
                        includesPlayer = true;
                        continue;
                    }
                    if (!obj.HasPart("Brain"))
                        continue;

                    string name = Speech.Clean(
                        obj.GetDisplayName(Stripped: true));
                    if (string.IsNullOrEmpty(name))
                        continue;

                    int dx = obj.CurrentCell.X - ox;
                    int dy = obj.CurrentCell.Y - oy;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    string dir = dist == 0
                        ? "here"
                        : dist + " " + NearbyScanner.GetCompassDirection(dx, dy);

                    if (obj.IsHostileTowards(player))
                        hostile.Add(name + " " + dir);
                    else
                        friendly.Add(name + " " + dir);
                }
            }

            var sb = new StringBuilder();
            if (hostile.Count > 0)
            {
                sb.Append("Hostile: ");
                sb.Append(string.Join(", ", hostile));
            }
            if (friendly.Count > 0)
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append("Friendly: ");
                sb.Append(string.Join(", ", friendly));
            }
            if (includesPlayer)
            {
                if (sb.Length > 0) sb.Append(". ");
                sb.Append("Includes you");
            }
            if (sb.Length == 0)
                sb.Append("no creatures");

            return sb.ToString();
        }


        // -----------------------------------------------------------------
        // Missile targeting block provider (F3/F4): weapon, ammo, fire modes
        // -----------------------------------------------------------------
        private static List<ContentBlock> BuildMissileTargetBlocks()
        {
            if (!InPickTargetMode || !_useMissileBuffer)
                return null;

            var player = XRL.The.Player;
            if (player == null)
                return null;

            var blocks = new List<ContentBlock>();

            // Block 1: Weapon & Ammo
            var body = player.GetPart<Body>();
            if (body != null)
            {
                var weapons = body.GetMissileWeapons();
                if (weapons != null && weapons.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var weapon in weapons)
                    {
                        if (sb.Length > 0) sb.Append("; ");
                        string name = Speech.Clean(
                            weapon.GetDisplayName(Stripped: true));
                        if (!string.IsNullOrEmpty(name))
                            sb.Append(name);
                        else
                            sb.Append("weapon");
                        var loader = weapon.GetPart<MagazineAmmoLoader>();
                        if (loader != null)
                        {
                            int remaining = loader.Ammo != null
                                ? loader.Ammo.Count : 0;
                            sb.Append(", " + remaining + " of "
                                + loader.MaxAmmo + " loaded");
                        }
                    }
                    sb.Append(". Range " + _pickRange);
                    blocks.Add(new ContentBlock
                    {
                        Title = "Weapon",
                        Body = sb.ToString()
                    });
                }
            }

            // Block 2: Fire Modes (rifle/bow with Draw a Bead)
            if (_isBowOrRifle && player.HasSkill("Rifle_DrawABead"))
            {
                var sb = new StringBuilder();
                sb.Append("M to mark target");

                var modes = new List<string>();
                if (player.HasSkill("Rifle_SuppressiveFire"))
                {
                    modes.Add("1 " + (player.HasSkill("Rifle_FlatteningFire")
                        ? "Flattening Fire" : "Suppressive Fire"));
                }
                if (player.HasSkill("Rifle_WoundingFire"))
                {
                    modes.Add("2 " + (player.HasSkill("Rifle_DisorientingFire")
                        ? "Disorienting Fire" : "Wounding Fire"));
                }
                if (player.HasSkill("Rifle_SureFire"))
                {
                    modes.Add("3 " + (player.HasSkill("Rifle_BeaconFire")
                        ? "Beacon Fire" : "Sure Fire"));
                }
                if (player.HasSkill("Rifle_OneShot"))
                {
                    modes.Add("4 Ultra Fire");
                }

                if (modes.Count > 0)
                {
                    sb.Append(". After marking: ");
                    sb.Append(string.Join(", ", modes));
                }
                blocks.Add(new ContentBlock
                {
                    Title = "Fire Modes",
                    Body = sb.ToString()
                });
            }

            return blocks;
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

            // Block 4: Exploration
            var exploSb = new StringBuilder();
            if (zone != null)
            {
                int unexplored = 0;
                for (int y = 0; y < zone.Height; y++)
                {
                    for (int x = 0; x < zone.Width; x++)
                    {
                        if (!zone.GetReallyExplored(x, y))
                        {
                            var c = zone.GetCell(x, y);
                            if (!c.HasWall() || c.HasAdjacentLocalNonwallCell())
                                unexplored++;
                        }
                    }
                }

                if (unexplored == 0)
                    exploSb.Append("Fully explored");
                else
                    exploSb.Append(unexplored + " unexplored cells");

                bool hasStairsUp = zone.FindObject(o => o.HasPart("StairsUp")) != null;
                bool hasStairsDown = zone.FindObject(o => o.HasPart("StairsDown")) != null;
                if (hasStairsUp && hasStairsDown)
                    exploSb.Append(". Stairs up and down");
                else if (hasStairsUp)
                    exploSb.Append(". Stairs up");
                else if (hasStairsDown)
                    exploSb.Append(". Stairs down");
                else
                    exploSb.Append(". No stairs");
            }
            blocks.Add(new ContentBlock { Title = "Exploration", Body = exploSb.ToString() });

            // Block 5: Messages (last 5)
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

            // Block 6: Abilities
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
