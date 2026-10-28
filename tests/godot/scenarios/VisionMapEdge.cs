using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The view and automap update with the camera on or next to the edge of the map. The original
/// builds its vision grid by walking two edge rays outwards from the eye a row at a time
/// (init_grid, move_to_next, newdels, enc_n_chk, incvec, decvec: uw1/src/3d/VIEW3D.C:536-852 and
/// uw2/src/3d/VIEW3D.C:531-845). Each ray keeps a map pointer that it steps by chgtable's
/// +-1 and +-64 (uw2 VIEW3D.C:67-68, 562-575, 696-764), and enc_n_chk reads the neighbours of an open cell
/// (uw2 VIEW3D.C:600-635), all with no bounds check. Off column 0 or 63 the pointer wraps into the
/// next or previous row; past row 0 or 63 it leaves the 64 by 64 map, and what it reads there is not
/// defined (the level block, uw2/src/include/level.h:16-30). The edges of a real map are solid, and a
/// solid cell stops the ray (enc_dat's row 0 is all 0, uw2 VIEW3D.C:84-85; tile_walls of a solid
/// tile has every side wall). The automap is only written for cells whose map index is inside the
/// map (i &amp; 0xF000, uw1/src/3d/GRIDDB.C:312-362).
///
/// The scenario puts the camera on each edge and in each corner of the map, and one square in from
/// them, opening the camera's own square for the test if it is solid (init_grid draws nothing from a
/// solid square, uw2 VIEW3D.C:531-534), and runs the port's view update (playerdat.PositionCamera
/// with line of sight: VisionParams.SetRangeOfVisionParams, GetViewDistance, FakeRender) facing
/// each of the four ways at every light level. Every update must complete. The port once threw a
/// NullReferenceException in src/utility/visionlos.cs, because UWTileMap.GetTileByPTR returns null
/// for a square off the map.
/// </summary>
public sealed class VisionMapEdge : Scenario
{
    public override string Name => "vision-map-edge";
    public override string Cites => "underworld-exhumed uw2/src/3d/VIEW3D.C:67-68, 84-85, 531-845; uw1/src/3d/VIEW3D.C:68-69, 85-86, 536-852; uw1/src/3d/GRIDDB.C:312-362; uw2/src/include/level.h:16-30";

    static readonly short[] Yaw = { 0, 16384, -32768, -16384 };   // motion.PlayerCardinalHeadingLookupTable
    readonly List<string> _failures = new();
    int _runs;
    string _problem;

    public override Task Setup(ScenarioContext c)
    {
        if (playerdat.playerObject == null) _problem = "no player object";
        else if (typeof(playerdat).GetMethod("PositionCamera", BindingFlags.NonPublic | BindingFlags.Static) == null) _problem = "playerdat.PositionCamera not found";
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var position = typeof(playerdat).GetMethod("PositionCamera", BindingFlags.NonPublic | BindingFlags.Static);
        var p = playerdat.playerObject;
        var tiles = UWTileMap.current_tilemap.Tiles;
        int oldX = p.tileX, oldY = p.tileY, oldLight = playerdat.lightlevel;
        var squares = new List<(string where, int x, int y)>();
        foreach (int d in new[] { 0, 1 })
        {
            string near = d == 0 ? "on" : "next to";
            squares.Add(($"{near} the south edge", 32, d)); squares.Add(($"{near} the north edge", 32, 63 - d));
            squares.Add(($"{near} the west edge", d, 32)); squares.Add(($"{near} the east edge", 63 - d, 32));
            squares.Add(($"{near} the south west corner", d, d)); squares.Add(($"{near} the south east corner", 63 - d, d));
            squares.Add(($"{near} the north west corner", d, 63 - d)); squares.Add(($"{near} the north east corner", 63 - d, 63 - d));
        }
        try
        {
            foreach (var s in squares)
            {
                var tile = tiles[s.x, s.y];
                short oldType = tile.tileType;
                if (oldType == UWTileMap.TILE_SOLID) tile.tileType = 1;   // open
                p.tileX = s.x; p.tileY = s.y;
                try
                {
                    for (int light = 0; light < 8; light++)
                        for (int dir = 0; dir < 4; dir++)
                        {
                            playerdat.lightlevel = light;
                            _runs++;
                            try
                            {
                                position.Invoke(null, new object[] { (short)((s.x << 8) + 0x80), (short)((s.y << 8) + 0x80), (short)0, Yaw[dir], (short)0, (short)0, false, true, false, false });
                            }
                            catch (TargetInvocationException e)
                            {
                                var inner = e.InnerException ?? e;
                                string f = $"{s.where} ({s.x}, {s.y}) facing {"NESW"[dir]}: {inner.GetType().Name}";
                                if (!_failures.Exists(x => x.StartsWith($"{s.where} ({s.x}, {s.y}) facing {"NESW"[dir]}"))) _failures.Add(f);
                            }
                        }
                }
                finally
                {
                    tile.tileType = oldType;
                }
            }
        }
        finally
        {
            p.tileX = oldX; p.tileY = oldY;
            playerdat.lightlevel = oldLight;
        }
        c.Note($"{_runs} view updates at {squares.Count} squares on or next to the edge, {_failures.Count} square and heading pairs failed");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_runs != 16 * 8 * 4) return $"precondition: {_runs} of {16 * 8 * 4} view updates run";
        if (_failures.Count == 0) return null;
        return "view update on the map edge did not complete (expected the edge to count as solid, VIEW3D.C): " + string.Join("; ", _failures);
    }
}
