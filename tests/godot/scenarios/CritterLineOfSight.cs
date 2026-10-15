using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Critters' line of sight. line_of_sight (uw1/src/critter/PATHFIND.C:800-903,
/// uw2/src/critter/PATHFIND.C:943-1047, the same code) steps a line from (x1, y1, z1) to
/// (x2, y2, z2), positions in fine units, tile by tile along its major axis, testing each step with
/// hyp_see (uw1 PATHFIND.C:571-603, uw2 PATHFIND.C:711-743), which fails on a wall between the two
/// squares (tile_walls, uw1/src/event/TRIGGER.C:76, uw2/src/event/TRIGGER.C:227; the TW_ bits,
/// uw1/src/include/event.h:46-49) or a floor above the line. The slope of the two cases that
/// step backwards along the major axis divides by -dy (uw1 PATHFIND.C:837-845) and -dx
/// (uw1 PATHFIND.C:862-870), so it is positive; and both branches of hyp_see test the same walls.
///
/// The scenario is a vector table on the live level 1 map: for lines from a square with open ground round it
/// (the one on the level with the most) to squares around it in every direction, at several distances and fine offsets, it compares the
/// port's Pathfind.TestBetweenPoints with the original's line_of_sight, rewritten below with its
/// unsigned char arithmetic.
///
/// Both games. The port divides by dy and dx in those two cases (src/npc/pathfind.cs:1553, 1623)
/// and swaps the first square's east and west masks in hyp_see's second branch
/// (src/npc/pathfind.cs:1833).
/// </summary>
public sealed class CritterLineOfSight : Scenario
{
    public override string Name => "critter-line-of-sight";
    public override string Cites => "underworld-exhumed uw1/src/critter/PATHFIND.C:571-603, 800-903; uw2/src/critter/PATHFIND.C:711-743, 943-1047; uw1/src/event/TRIGGER.C:76";

    // tile_walls, uw1/src/event/TRIGGER.C:76 and uw2/src/event/TRIGGER.C:227.
    static readonly byte[] TileWalls = { 30, 0, 19, 21, 11, 13, 32, 32, 32, 32, 0, 0, 0, 0, 0, 30 };
    const int TW_WEST = 0x02, TW_EAST = 0x04, TW_SOUTH = 0x08, TW_NORTH = 0x10;

    string _problem;
    int _cases, _visible;
    readonly List<string> _mismatches = new();

    int _cx, _cy;

    public override Task Setup(ScenarioContext c)
    {
        // The centre: an open square, away from the map's edge, whose neighbours are open, with the
        // most open squares round it.
        var tiles = UWTileMap.current_tilemap.Tiles;
        int best = -1;
        for (int x = 12; x <= 51; x++)
        for (int y = 12; y <= 51; y++)
        {
            bool ring = true;
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                if (tiles[x + i, y + j].tileType != 1) ring = false;
            if (!ring) continue;
            int open = 0;
            for (int i = -7; i <= 7; i++)
            for (int j = -7; j <= 7; j++)
                if (tiles[x + i, y + j].tileType != 0) open++;
            if (open > best) { best = open; _cx = x; _cy = y; }
        }
        if (best < 0) _problem = "no open square with open neighbours on the level away from the edge";
        else c.Note($"centre ({_cx}, {_cy}) on level {playerdat.dungeon_level}, {best} of 225 squares round it open");
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        int cx = _cx, cy = _cy;
        int z = 0x70;   // above most floors, so walls decide most lines
        int[] fine = { 1, 4, 6 };
        for (int ox = -1; ox <= 1; ox++)
        for (int oy = -1; oy <= 1; oy++)
        for (int dxT = -6; dxT <= 6; dxT++)
        for (int dyT = -6; dyT <= 6; dyT++)
        {
            if ((dxT + dyT) % 2 != 0) continue;   // thin the table, every direction still covered
            foreach (int f in fine)
            {
                int x1 = ((cx + ox) << 3) + f, y1 = ((cy + oy) << 3) + (7 - f);
                int x2 = ((cx + ox + dxT) << 3) + (7 - f), y2 = ((cy + oy + dyT) << 3) + f;
                bool expected = LineOfSight(x1, y1, z, x2, y2, z) != 0;
                bool actual = Pathfind.TestBetweenPoints(x1, y1, z, x2, y2, z);
                _cases++;
                if (expected) _visible++;
                if (expected != actual && _mismatches.Count < 400)
                    _mismatches.Add($"({x1},{y1})->({x2},{y2}) DOS {(expected ? 1 : 0)} port {(actual ? 1 : 0)}");
            }
        }
        c.Note($"{_cases} lines from around ({cx}, {cy}), {_visible} clear in DOS; {_mismatches.Count} differ");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_visible == 0 || _visible == _cases) return $"precondition: the table is not a test, {_visible} of {_cases} lines clear";
        if (_mismatches.Count == 0) return null;
        return $"line of sight differs from DOS on {_mismatches.Count} of {_cases} lines, e.g. {string.Join("; ", _mismatches.GetRange(0, System.Math.Min(4, _mismatches.Count)))} (PATHFIND.C line_of_sight, hyp_see)";
    }

    static (int type, int height) Tile(byte x, byte y)
    {
        var t = UWTileMap.current_tilemap.Tiles[x, y];
        return (t.tileType, t.floorHeight);
    }

    /// <summary>hyp_see, uw1/src/critter/PATHFIND.C:571-603 (uw2 PATHFIND.C:711-743).</summary>
    static int HypSee(byte oldx, byte oldy, byte x1, byte y1, byte x2, byte y2, byte height)
    {
        var (type1, _) = Tile(x1, y1);
        var (type2, h2) = Tile(x2, y2);
        int w1 = TileWalls[type1 & 0xF], w2 = TileWalls[type2 & 0xF];
        if (oldx == 0 || (oldx == x1 && oldy == y1))
        {
            if (x2 > x1 && (w2 & TW_WEST) != 0) return 0;
            if (x2 < x1 && (w2 & TW_EAST) != 0) return 0;
            if (y2 > y1 && (w2 & TW_SOUTH) != 0) return 0;
            if (y2 < y1 && (w2 & TW_NORTH) != 0) return 0;
            if (x2 > x1 && (w1 & TW_EAST) != 0) return 0;
            if (x2 < x1 && (w1 & TW_WEST) != 0) return 0;
            if (y2 > y1 && (w1 & TW_NORTH) != 0) return 0;
            if (y2 < y1 && (w1 & TW_SOUTH) != 0) return 0;
            return (height >> 3) < h2 ? 0 : 1;
        }
        if (x2 == 0) return 1;
        if (x2 > x1 && (w2 & TW_WEST) != 0) return 0;
        if (x2 < x1 && (w2 & TW_EAST) != 0) return 0;
        if (y2 > y1 && (w2 & TW_SOUTH) != 0) return 0;
        if (y2 < y1 && (w2 & TW_NORTH) != 0) return 0;
        if (x2 > x1 && (w1 & TW_EAST) != 0) return 0;
        if (x2 < x1 && (w1 & TW_WEST) != 0) return 0;
        if (y2 > y1 && (w1 & TW_NORTH) != 0) return 0;
        if (y2 < y1 && (w1 & TW_SOUTH) != 0) return 0;
        if ((height >> 3) < h2) return 0;
        return 1;
    }

    /// <summary>line_of_sight, uw1/src/critter/PATHFIND.C:800-903 (uw2 PATHFIND.C:943-1047).</summary>
    static int LineOfSight(int x1, int y1, int z1, int x2, int y2, int z2)
    {
        int dx = x2 - x1, dy = y2 - y1, dz = z2 - z1;
        byte x, y, oldx, oldy, lastx, lasty, destx, desty, z;
        byte diff, count, step, dir, slope, frac;
        bool majorIsX;
        x = oldx = lastx = (byte)(x1 >> 3);
        y = oldy = lasty = (byte)(y1 >> 3);
        destx = (byte)(x2 >> 3);
        desty = (byte)(y2 >> 3);
        if (dx == 0 && dy == 0) return 1;
        if (dx >= dy)
        {
            if (-dy <= dx)
            {
                majorIsX = true; diff = (byte)(dx >> 3); step = 1;
                dir = (byte)(dy > 0 ? 1 : -1);
                if (dir == 1)
                {
                    slope = (byte)((dy << 7) / dx);
                    frac = (byte)(((y1 & 7) << 4) + slope * (7 - (x1 & 7)) / 8);
                }
                else
                {
                    slope = (byte)((-dy << 7) / dx);
                    frac = (byte)(((7 - (y1 & 7)) << 4) + slope * (7 - (x1 & 7)) / 8);
                }
            }
            else
            {
                majorIsX = false; diff = (byte)(-dy >> 3); step = 0xFF;
                dir = (byte)(dx > 0 ? 1 : -1);
                if (dir == 1)
                {
                    slope = (byte)((dx << 7) / -dy);
                    frac = (byte)(((7 - (x1 & 7)) << 4) + slope * (y1 & 7) / 8);
                }
                else
                {
                    slope = (byte)((-dx << 7) / -dy);
                    frac = (byte)(((x1 & 7) << 4) + slope * (y1 & 7) / 8);
                }
            }
        }
        else
        {
            if (-dy <= dx)
            {
                majorIsX = false; diff = (byte)(dy >> 3); step = 1;
                dir = (byte)(dx > 0 ? 1 : -1);
                if (dir == 1)
                {
                    slope = (byte)((dx << 7) / dy);
                    frac = (byte)(((x1 & 7) << 4) + slope * (7 - (y1 & 7)) / 8);
                }
                else
                {
                    slope = (byte)((-dx << 7) / dy);
                    frac = (byte)(((7 - (x1 & 7)) << 4) + slope * (7 - (y1 & 7)) / 8);
                }
            }
            else
            {
                majorIsX = true; diff = (byte)(-dx >> 3); step = 0xFF;
                dir = (byte)(dy > 0 ? 1 : -1);
                if (dir == 1)
                {
                    slope = (byte)((dy << 7) / -dx);
                    frac = (byte)(((7 - (y1 & 7)) << 4) + slope * (x1 & 7) / 8);
                }
                else
                {
                    slope = (byte)((-dy << 7) / -dx);
                    frac = (byte)(((y1 & 7) << 4) + slope * (x1 & 7) / 8);
                }
            }
        }
        z = (byte)z1;
        count = 0;
        for (;;)
        {
            if ((frac & 0x80) != 0)
            {
                frac = (byte)(frac & 0x7F);
                if (majorIsX) y = (byte)(y + dir); else x = (byte)(x + dir);
                if (HypSee(oldx, oldy, lastx, lasty, x, y, z) == 0) return 0;
                if (x == destx && y == desty) return HypSee(lastx, lasty, x, y, 0, 0, z);
                oldx = lastx; oldy = lasty; lastx = x; lasty = y;
            }
            if (majorIsX) x = (byte)(x + step); else y = (byte)(y + step);
            count++;
            if (count > 10) return 0;
            if (diff != 0) z = (byte)(z1 + count * dz / diff);
            if (HypSee(oldx, oldy, lastx, lasty, x, y, z) == 0) return 0;
            if (x == destx && y == desty) return HypSee(lastx, lasty, x, y, 0, 0, z);
            oldx = lastx; oldy = lasty; lastx = x; lasty = y;
            frac = (byte)(frac + slope);
        }
    }
}
