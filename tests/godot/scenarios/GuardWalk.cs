using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Guard: running forward moves the player at the original's speed, and a wall stops him.
///
/// The original: the run key (W, scan code 0x11) sets ForwInpRate 0x70 and the forward command
/// (uw1/src/motion/PLAYMOVE.C:235-238, uw2 PLAYMOVE.C:228-231); do_player_input asks for the speed
/// pFPS[0] * (ForwInpRate >> 2) / 32 (uw1/src/motion/PHYSICS.C:478, uw2 PHYSICS.C:673), with pFPS[0]
/// = Run_FPS 0x3AC on foot (newFPS: ratio 10 of 10 in UW1, 20 of 20 in UW2; uw1 PHYSICS.C:70, 586-596,
/// uw2 PHYSICS.C:74, 752-768), which is 0x336; set_player_phys_params moves PN.speed toward it by at
/// most MaxPlayerAccel, 0x60 when the player carries no more than half he can (uw1 PHYSICS.C:296-309,
/// 603-606). A move turns speed into velocity, sin and cos times speed >> 15, and covers
/// velocity * time / 0x2000 cells of 1/8 square, so speed * time / 256 of the position's 1/256
/// squares (uw1/src/motion/MOTION.C:130-142, 218-246), time being the game clock's units
/// (check_physics adds the same delta to the clock, uw1 PLAYMOVE.C:326-356). So at full speed the
/// player covers 0x336 * (clock rise) / 256 position units. A solid square stops him: the
/// collision checks keep him out of it (MOTION.C's TerrainCheck).
///
/// The scenario faces the player down a straight run of at least 4 open, empty squares of one
/// floor height ending in a solid square, from the nearest square that has one (moving him there
/// first with the game's teleport if it is not his own), holds W
/// (an input event, as the keyboard sends it), checks the speed rises by 0x60 a step to 0x336, then
/// measures the distance covered at full speed against the clock's rise, and keeps W held until
/// the wall: he must stop short of the solid square.
/// </summary>
public sealed class GuardWalk : Scenario
{
    const int Target = 0x3AC * (0x70 >> 2) / 32;   // 0x336
    const int Accel = 0x60;
    static readonly short[] Yaw = { 0, 16384, -32768, -16384 };   // motion.PlayerCardinalHeadingLookupTable
    static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };

    public override string Name => "guard-walk";
    public override string Cites => "underworld-exhumed uw1/src/motion/PLAYMOVE.C:235-238, 326-356; uw1/src/motion/PHYSICS.C:70, 296-309, 478, 584-606; uw1/src/motion/MOTION.C:130-142, 218-246; uw2 PLAYMOVE.C:228-231, PHYSICS.C:74, 673, 750-778";

    string _problem, _camera;
    int _dir = -1, _run, _startTileX, _startTileY, _maxSpeed, _wx0, _wy0, _wx1, _wy1, _clk0, _clk1, _endX, _endY, _endTileX, _endTileY, _stillFrames;
    bool _measured;
    readonly List<int> _speeds = new();

    public override async Task Setup(ScenarioContext c)
    {
        _camera = await Guards.CameraOnPlayer(c);
        var p = playerdat.playerObject;
        // The nearest square (the player's own if it will do) with a run down a cardinal direction.
        int best = int.MaxValue, sx = -1, sy = -1;
        for (int y = 1; y < 63; y++)
            for (int x = 1; x < 63; x++)
            {
                int dd = (x - p.tileX) * (x - p.tileX) + (y - p.tileY) * (y - p.tileY);
                if (dd >= best) continue;
                var (d, run) = BestRun(x, y);
                if (d >= 0) { best = dd; sx = x; sy = y; _dir = d; _run = run; }
            }
        if (_dir < 0) { _problem = "no straight run of 4 or more open, empty squares ending in a wall on this level"; return; }
        if (sx != p.tileX || sy != p.tileY)
        {
            string from = $"({p.tileX}, {p.tileY})";
            if (!await Guards.TeleportHere(c, sx, sy)) { _problem = $"the move to ({sx}, {sy}) did not happen"; return; }
            _camera += $"; moved from {from}";
        }
        if (playerdat.WeightCarried * 2 > playerdat.WeightMax) { _problem = "the player carries more than half he can, which slows the acceleration"; return; }
        _startTileX = p.tileX; _startTileY = p.tileY;
        playerdat.PlayerCameraYaw_dseg_8294 = Yaw[_dir];
        await c.Frames(10);
        c.Note($"{_camera}; running {"NESW"[_dir]} from ({p.tileX}, {p.tileY}) down {_run} open squares to a wall");
    }

    /// <summary>
    /// The longest run (4 to 11) from (x, y) down a cardinal direction of open squares with no
    /// objects, of (x, y)'s floor height, ending in a solid square; (x, y) itself open and empty
    /// but for the player. (-1, 0) if none.
    /// </summary>
    static (int dir, int run) BestRun(int x, int y)
    {
        var map = UWTileMap.current_tilemap;
        var t0 = map.Tiles[x, y];
        if (t0.tileType != UWTileMap.TILE_OPEN) return (-1, 0);
        for (int i = t0.indexObjectList, g = 0; i != 0 && g < 64; g++)
        {
            if (i != 1) return (-1, 0);
            i = map.LevelObjects[i].next;
        }
        int bestDir = -1, bestRun = 0;
        for (int d = 0; d < 4; d++)
        {
            int k = 1;
            while (k < 12 && UWTileMap.ValidTile(x + k * Dx[d], y + k * Dy[d]))
            {
                var t = map.Tiles[x + k * Dx[d], y + k * Dy[d]];
                if (t.tileType != UWTileMap.TILE_OPEN || t.floorHeight != t0.floorHeight || t.indexObjectList != 0) break;
                k++;
            }
            int wx = x + k * Dx[d], wy = y + k * Dy[d];
            bool wall = UWTileMap.ValidTile(wx, wy) && map.Tiles[wx, wy].tileType == UWTileMap.TILE_SOLID;
            if (wall && k - 1 >= 4 && k - 1 > bestRun) { bestRun = k - 1; bestDir = d; }
        }
        return (bestDir, bestRun);
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        Guards.Key(Key.W, true);
        int lastSpeed = -1;
        int lx = 0, ly = 0;
        for (int f = 0; f < 600; f++)
        {
            await c.Frames(1);
            int s = motion.playerMotionParams.momentum_14;
            if (s != lastSpeed) { _speeds.Add(s); lastSpeed = s; }
            if (s > _maxSpeed) _maxSpeed = s;
            int x = motion.playerMotionParams.x_0, y = motion.playerMotionParams.y_2;
            // The measuring window: from the first frame at full speed, for half a second of clock.
            if (s == Target && _clk0 == 0) { _clk0 = playerdat.ClockValue; _wx0 = x; _wy0 = y; }
            else if (_clk0 != 0 && !_measured && playerdat.ClockValue - _clk0 >= 128)
            {
                if (s == Target) { _clk1 = playerdat.ClockValue; _wx1 = x; _wy1 = y; _measured = true; }
                else { _clk0 = 0; }
            }
            _stillFrames = (x == lx && y == ly) ? _stillFrames + 1 : 0;
            lx = x; ly = y;
            if (_measured && _stillFrames >= 60) break;
        }
        Guards.Key(Key.W, false);
        await c.Frames(10);
        var p = playerdat.playerObject;
        _endX = motion.playerMotionParams.x_0; _endY = motion.playerMotionParams.y_2;
        _endTileX = p.tileX; _endTileY = p.tileY;
        c.Note($"speeds seen {string.Join(",", _speeds.GetRange(0, System.Math.Min(_speeds.Count, 14)))}; window: clock +{_clk1 - _clk0}, moved ({_wx1 - _wx0}, {_wy1 - _wy0}); end at ({_endX}, {_endY}) square ({_endTileX}, {_endTileY}), still {_stillFrames} frames");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_speeds.Count < 2) return "precondition: the run key changed nothing (speed never rose)";
        var k = new Checks();
        k.Equal("speed after the first step", Accel, _speeds[1], "PN.speed rises by MaxPlayerAccel 0x60: uw1 PHYSICS.C:301-304, 606");
        k.Equal("top running speed", Target, _maxSpeed, "pFPS[0] * (0x70 >> 2) / 32 = 0x3AC * 28 / 32: uw1 PHYSICS.C:70, 478, 596, PLAYMOVE.C:236");
        if (!_measured) return k.Result + (k.Result == null ? "" : "; ") + "precondition: no half second at full speed before the wall";
        int along = Dx[_dir] * (_wx1 - _wx0) + Dy[_dir] * (_wy1 - _wy0);
        int across = System.Math.Abs(Dy[_dir] * (_wx1 - _wx0)) + System.Math.Abs(Dx[_dir] * (_wy1 - _wy0));
        long expected = (long)Target * (_clk1 - _clk0) / 256;
        k.Within($"distance along the run over clock +{_clk1 - _clk0} at full speed", expected * 95 / 100, expected * 105 / 100, along,
            "speed * time / 256 position units: uw1 MOTION.C:218-246 (within 5%)");
        k.Within("drift across the run", 0, expected / 20, across, "the heading is cardinal: sin or cos 0, uw1 MOTION.C:218-224");
        int wallX = _startTileX + (_run + 1) * Dx[_dir], wallY = _startTileY + (_run + 1) * Dy[_dir];
        k.True(!(_endTileX == wallX && _endTileY == wallY), $"the player ended in the solid square ({wallX}, {wallY})");
        int traveledSquares = Dx[_dir] * (_endTileX - _startTileX) + Dy[_dir] * (_endTileY - _startTileY);
        k.Equal("squares run before the wall stopped him", _run, traveledSquares, "the solid square stops him in the last open one");
        k.True(_stillFrames >= 60, $"the player was still moving at the end ({_stillFrames} still frames)");
        return k.Result;
    }
}
