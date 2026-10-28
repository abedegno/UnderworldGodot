using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The collision step for a mover on the edge of the map. The original's TerrainCheck reads the
/// tiles under the four corners of the footprint as centptr + tile_off[...] (uw1/src/motion/COLLIDE.C:281-292,
/// uw2 COLLIDE.C:260-271), and ObjectCheck walks the object lists of the tiles around the mover as
/// tile[(j &lt;&lt; 6) + i] with i and j from -1 to +1 (uw1 COLLIDE.C:503-510, uw2 COLLIDE.C:489-496). Neither
/// checks the bounds, so on row 0 or 63 they reach past the 64 by 64 map: below row 0 into whatever
/// comes before the level block, above row 63 into its mobile objects (uw1/src/include/level.h:17-29,
/// uw2 level.h:16-30). Off column 0 or 63 the offset only wraps into the next or previous row, which
/// is still on the map. What DOS finds off the map is not defined; the map's edge squares are solid,
/// so a mover there is blocked by its own square (SolveCenter: 0x200 for a solid tile,
/// uw1 COLLIDE.C:209-226, uw2 COLLIDE.C:189-208).
///
/// The scenario runs the port's TerrainCheck (ProcessMotionTileHeights) and ObjectCheck
/// (ScanForCollisions), the two calls of can_place (uw1 COLLIDE.C:597-610, uw2 COLLIDE.C:583-596), for a
/// mover with a radius at the far side of a solid edge square, so that both reach off the map: on
/// each of the four edges and in the four corners. Each must complete, report the square as a
/// wall (0x200), and, where the squares around it on the map hold no objects, find no collisions.
/// The port once threw a NullReferenceException here (src/physics/motion_collisions.cs:796-797,
/// src/physics/motion_calc.cs:419-447), because UWTileMap.GetTileByPTR returns null off the map.
/// </summary>
public sealed class CollisionMapEdge : Scenario
{
    public override string Name => "collision-map-edge";
    public override string Cites => "underworld-exhumed uw1/src/motion/COLLIDE.C:209-226, 281-292, 503-510, 597-610; uw2/src/motion/COLLIDE.C:189-208, 260-271, 489-496, 583-596; uw1/src/include/level.h:17-29";

    readonly List<(string where, int x, int y, string error, int hits0, int found, bool emptyAround)> _results = new();
    string _problem;
    int _item = -1;

    public override Task Setup(ScenarioContext c)
    {
        // A creature type with a radius, so that the footprint's corners reach the next square.
        for (int id = 64; id < 128 && _item < 0; id++)
            if (commonObjDat.radius(id) > 0 && commonObjDat.height(id) > 0) _item = id;
        if (_item < 0) _problem = "no creature type with a radius";
        return Task.CompletedTask;
    }

    static bool Solid(int x, int y) => UWTileMap.current_tilemap.Tiles[x, y].tileType == UWTileMap.TILE_SOLID;

    /// <summary>Whether the squares on the map around (x, y), and (x, y) itself, hold no objects.</summary>
    static bool EmptyAround(int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (UWTileMap.ValidTile(x + dx, y + dy) && UWTileMap.current_tilemap.Tiles[x + dx, y + dy].indexObjectList != 0)
                    return false;
        return true;
    }

    /// <summary>The solid square along an edge nearest its middle, preferring one with no objects around it.</summary>
    static (int x, int y) EdgeSquare(bool alongX, int fixedCoord)
    {
        (int, int) best = (-1, -1);
        for (int d = 0; d < 31; d++)
            foreach (int k in new[] { 32 - d, 32 + d })
            {
                int x = alongX ? k : fixedCoord, y = alongX ? fixedCoord : k;
                if (!Solid(x, y)) continue;
                if (EmptyAround(x, y)) return (x, y);
                if (best.Item1 < 0) best = (x, y);
            }
        return best;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var (sx, sy) = EdgeSquare(true, 0);
        var (nx, ny) = EdgeSquare(true, 63);
        var (wx, wy) = EdgeSquare(false, 0);
        var (ex, ey) = EdgeSquare(false, 63);
        // (square x, square y, fine x, fine y): the fine position is at the side facing off the map.
        var cases = new (string where, int x, int y, int fx, int fy)[]
        {
            ("south edge", sx, sy, 3, 0), ("north edge", nx, ny, 3, 7),
            ("west edge", wx, wy, 0, 3), ("east edge", ex, ey, 7, 3),
            ("south west corner", 0, 0, 0, 0), ("south east corner", 63, 0, 7, 0),
            ("north west corner", 0, 63, 0, 7), ("north east corner", 63, 63, 7, 7),
        };
        var saved = MotionCalcArray.PtrToMotionCalc;
        try
        {
            foreach (var k in cases)
            {
                if (k.x < 0) { _results.Add((k.where, k.x, k.y, "no solid square on this edge", 0, 0, false)); continue; }
                string error = null;
                int hits0 = 0, found = 0;
                try
                {
                    MotionCalcArray.PtrToMotionCalc = new byte[0x20];
                    MotionCalcArray.MotionArrayObjectIndexA = 0;
                    MotionCalcArray.Radius8 = (byte)commonObjDat.radius(_item);
                    MotionCalcArray.Height9 = (byte)commonObjDat.height(_item);
                    MotionCalcArray.x0 = (ushort)((k.x << 3) + k.fx);
                    MotionCalcArray.y2 = (ushort)((k.y << 3) + k.fy);
                    MotionCalcArray.z4 = 0;
                    motion.ProcessMotionTileHeights_seg028_2941_385(0);
                    hits0 = MotionCalcArray.UnkC_terrain;
                    motion.ScanForCollisions(0, 1);
                    found = MotionCalcArray.Unk14_collisoncount;
                }
                catch (Exception e)
                {
                    error = $"{e.GetType().Name}";
                }
                _results.Add((k.where, k.x, k.y, error, hits0, found, EmptyAround(k.x, k.y)));
                c.Note($"{k.where} ({k.x}, {k.y}) fine ({k.fx}, {k.fy}), radius {commonObjDat.radius(_item)}: {(error ?? $"hits 0x{hits0:X}, {found} collisions")}{(EmptyAround(k.x, k.y) ? "" : ", objects on the squares around")}");
            }
        }
        finally
        {
            MotionCalcArray.PtrToMotionCalc = saved;
        }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_results.Count != 8) return $"precondition: {_results.Count} of 8 edge positions tried";
        var k = new Checks();
        foreach (var r in _results)
        {
            if (r.x < 0) return $"precondition: {r.where}: {r.error}";
            if (!Solid(r.x, r.y)) return $"precondition: {r.where} square ({r.x}, {r.y}) is not solid";
            if (r.error != null) { k.True(false, $"collision step at the {r.where} ({r.x}, {r.y}) did not complete ({r.error} in the port), expected a wall (COLLIDE.C TerrainCheck, ObjectCheck)"); continue; }
            k.True((r.hits0 & 0x200) != 0, $"{r.where} ({r.x}, {r.y}): hits 0x{r.hits0:X}, expected the solid square's 0x200 (uw1 COLLIDE.C:209-226, uw2 COLLIDE.C:189-208)");
            if (r.emptyAround) k.Equal($"{r.where} ({r.x}, {r.y}): collisions with no objects on the squares around", 0, r.found, "uw1 COLLIDE.C:503-510, uw2 COLLIDE.C:489-496");
        }
        return k.Result;
    }
}
