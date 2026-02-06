using System.Collections.Generic;
using UnityEngine;
using XRL.World.Parts;

namespace QudAccessibility
{
    /// <summary>
    /// Nearby object scanner for the map screen.
    ///   Ctrl+PgDn/PgUp = cycle category (Hostile, Friendly, Items, Corpses, Features)
    ///   PgDn/PgUp = cycle objects in current category
    ///   Home = re-announce current object with fresh direction
    /// </summary>
    internal static class NearbyScanner
    {
        private static readonly string[] ScanCategories =
            { "Hostile", "Friendly", "Items", "Corpses", "Features" };
        private static int _categoryIndex;
        private static readonly List<ScanEntry> _scanEntries = new List<ScanEntry>();
        private static int _scanIndex = -1;

        private struct ScanEntry
        {
            public XRL.World.GameObject Object;
            public string Name;
        }

        /// <summary>
        /// Check for scanner key input and dispatch. Called from ScreenReader.Update()
        /// when on the map screen (not in look or pick target mode).
        /// </summary>
        internal static void HandleInput()
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

        // -----------------------------------------------------------------
        // Category cycling
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
            ScreenReader.SetScreenContent(msg);
        }

        // -----------------------------------------------------------------
        // Object cycling within category
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
        // Home re-announces current result with fresh direction
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
        // Speak an entry with distance + compass direction
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

            int cx = entry.Object.CurrentCell.X;
            int cy = entry.Object.CurrentCell.Y;
            string msg = dist == 0
                ? entry.Name + ", here (" + cx + "," + cy + ")"
                : entry.Name + ", " + dist + " " + dir + " (" + cx + "," + cy + ")";

            Speech.Interrupt(msg);
            ScreenReader.SetScreenContent(msg);
        }

        // -----------------------------------------------------------------
        // Scan the current zone for objects matching the category
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
                    if (category == "Hostile" || category == "Friendly")
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
                case "Hostile":
                    return obj.HasPart("Brain")
                        && obj != XRL.The.Player
                        && obj.IsVisible()
                        && obj.IsHostileTowards(XRL.The.Player);
                case "Friendly":
                    return obj.HasPart("Brain")
                        && obj != XRL.The.Player
                        && obj.IsVisible()
                        && !obj.IsHostileTowards(XRL.The.Player);
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
        /// Also used by ScreenReader for look mode and cell descriptions.
        /// </summary>
        internal static string GetDoorStateSuffix(XRL.World.GameObject obj)
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

        /// <summary>
        /// Convert a dx,dy offset to a compass direction string.
        /// Also used by ScreenReader for pick target mode.
        /// </summary>
        internal static string GetCompassDirection(int dx, int dy)
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
