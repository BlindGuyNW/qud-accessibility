using System;
using System.Collections.Generic;
using UnityEngine;
using XRL.World;
using XRL.World.Capabilities;
using XRL.World.Parts;

namespace QudAccessibility
{
    /// <summary>
    /// Nearby object scanner for the map screen.
    ///   Ctrl+] / Ctrl+[ = cycle category (Hostile, Friendly, Items, Corpses, Features, Unexplored)
    ///   ] / [ = cycle objects in current category
    ///   \ = re-announce current object with fresh direction
    ///   Ctrl+\ = walk to selected object via automovement
    /// </summary>
    internal static class NearbyScanner
    {
        private static readonly string[] ScanCategories =
            { "Hostile", "Friendly", "Items", "Corpses", "Features", "Unexplored" };
        private static int _categoryIndex;
        private static readonly List<ScanEntry> _scanEntries = new List<ScanEntry>();
        private static int _scanIndex = -1;

        private struct ScanEntry
        {
            public XRL.World.GameObject Object;
            public string Name;
            // For coordinate-only entries (e.g. unexplored patches)
            public int X;
            public int Y;
        }

        /// <summary>
        /// Check for scanner key input and dispatch. Called from ScreenReader.Update()
        /// when on the map screen (not in look or pick target mode).
        /// </summary>
        internal static void HandleInput()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl)
                     || Input.GetKey(KeyCode.RightControl);

            if (Input.GetKeyDown(KeyCode.RightBracket) && ctrl)
                CycleCategory(1);
            else if (Input.GetKeyDown(KeyCode.LeftBracket) && ctrl)
                CycleCategory(-1);
            else if (Input.GetKeyDown(KeyCode.RightBracket) && !ctrl)
                CycleScanResult(1);
            else if (Input.GetKeyDown(KeyCode.LeftBracket) && !ctrl)
                CycleScanResult(-1);
            else if (Input.GetKeyDown(KeyCode.Backslash) && ctrl)
                WalkToCurrentResult();
            else if (Input.GetKeyDown(KeyCode.Backslash) && !ctrl)
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
        // Walk to the currently selected scan result via automovement
        // -----------------------------------------------------------------
        private static void WalkToCurrentResult()
        {
            if (_scanIndex < 0 || _scanIndex >= _scanEntries.Count)
            {
                Speech.Interrupt("No object selected");
                return;
            }

            var entry = _scanEntries[_scanIndex];
            int x, y;
            if (entry.Object?.CurrentCell != null)
            {
                x = entry.Object.CurrentCell.X;
                y = entry.Object.CurrentCell.Y;
            }
            else if (ScanCategories[_categoryIndex] == "Unexplored")
            {
                x = entry.X;
                y = entry.Y;
            }
            else
            {
                Speech.Interrupt("Object no longer available");
                return;
            }

            Speech.Interrupt("Walking to " + entry.Name);
            AutoAct.Setting = "M" + x + "," + y;
            XRL.The.ActionManager.SkipPlayerTurn = true;
        }

        // -----------------------------------------------------------------
        // Speak an entry with distance + compass direction
        // -----------------------------------------------------------------
        private static void AnnounceEntry(ScanEntry entry)
        {
            var player = XRL.The.Player;
            if (player?.CurrentCell == null)
                return;

            int cx, cy;
            if (entry.Object?.CurrentCell != null)
            {
                cx = entry.Object.CurrentCell.X;
                cy = entry.Object.CurrentCell.Y;
            }
            else
            {
                cx = entry.X;
                cy = entry.Y;
            }

            int dx = cx - player.CurrentCell.X;
            int dy = cy - player.CurrentCell.Y;
            int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
            string dir = GetCompassDirection(dx, dy);

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
            int px = player.CurrentCell.X;
            int py = player.CurrentCell.Y;

            if (category == "Unexplored")
            {
                ScanUnexploredPatches(zone, px, py);
                return;
            }

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
            _scanEntries.Sort((a, b) =>
            {
                int da = ChebyshevDist(a, px, py);
                int db = ChebyshevDist(b, px, py);
                return da.CompareTo(db);
            });
        }

        // -----------------------------------------------------------------
        // Unexplored patch scanner — flood fill to find connected regions
        // -----------------------------------------------------------------
        private static void ScanUnexploredPatches(Zone zone, int px, int py)
        {
            int w = zone.Width;
            int h = zone.Height;
            var visited = new bool[w * h];

            // Mark explored and solid-rock cells as visited (skip them)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (zone.GetReallyExplored(x, y))
                    {
                        visited[x + y * w] = true;
                        continue;
                    }
                    var c = zone.GetCell(x, y);
                    if (c.HasWall() && !c.HasAdjacentLocalNonwallCell())
                        visited[x + y * w] = true;
                }
            }

            // BFS flood fill to find connected patches
            var queue = new Queue<int>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (visited[x + y * w])
                        continue;

                    // New patch — BFS from this cell
                    int count = 0;
                    int nearX = x, nearY = y;
                    int nearDist = Math.Max(Math.Abs(x - px), Math.Abs(y - py));

                    queue.Enqueue(x + y * w);
                    visited[x + y * w] = true;

                    while (queue.Count > 0)
                    {
                        int idx = queue.Dequeue();
                        int cx = idx % w;
                        int cy = idx / w;
                        count++;

                        int d = Math.Max(Math.Abs(cx - px), Math.Abs(cy - py));
                        if (d < nearDist)
                        {
                            nearDist = d;
                            nearX = cx;
                            nearY = cy;
                        }

                        // 8-connectivity neighbors
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = cx + dx;
                                int ny = cy + dy;
                                if (nx < 0 || nx >= w || ny < 0 || ny >= h)
                                    continue;
                                int ni = nx + ny * w;
                                if (!visited[ni])
                                {
                                    visited[ni] = true;
                                    queue.Enqueue(ni);
                                }
                            }
                        }
                    }

                    string name = count == 1
                        ? "1 unexplored cell"
                        : count + " unexplored cells";
                    _scanEntries.Add(new ScanEntry
                    {
                        Name = name,
                        X = nearX,
                        Y = nearY
                    });
                }
            }

            // Sort by distance (nearest patch first)
            _scanEntries.Sort((a, b) =>
                ChebyshevDist(a, px, py).CompareTo(ChebyshevDist(b, px, py)));
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

        private static int ChebyshevDist(ScanEntry entry, int px, int py)
        {
            if (entry.Object?.CurrentCell != null)
                return Math.Max(
                    Math.Abs(entry.Object.CurrentCell.X - px),
                    Math.Abs(entry.Object.CurrentCell.Y - py));
            return Math.Max(Math.Abs(entry.X - px), Math.Abs(entry.Y - py));
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
