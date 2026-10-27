using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// The step-and-turn moves: the three arrows below the 3D view and the step-back key.
///
/// The original binds player_simple_move to the three arrows below the view, turn left (-1), step
/// forward (0) and turn right (1), live in the 3D view for either mouse button (game/PLAYER.C: uw1
/// 183-185, uw2 177-179; ui/INPUT.C input_dispatch), and to the capital X for a step back (-2;
/// uw1 PLAYER.C:178, uw2 PLAYER.C:172). The screen is 320 by 200 with y counted up from the bottom
/// (ui/INPUT.C). In UW2 the information and spell areas, added later, win where they overlap the
/// arrows (uw2 ui/GAMESCR.C:140, 142; input_dispatch searches the newest first), so most of the
/// left arrow there is the information area. simple_fizix (uw1 motion/PHYSICS.C:188-283, uw2
/// PHYSICS.C:195-288): a turn from a heading on an eighth of a circle adds turn &lt;&lt; 13; from
/// one off it, it snaps to the eighth below (left) or the one above (right). A step moves PN.x,
/// PN.y by move_along(heading, dist) (uw1 sys/UTIL.C:34): along a cardinal heading the table's sine
/// is 0x7FFF, (0x7FFF / 0x80) * dist / 0x100 is 0x7F for dist 0x80 and 0x3F for 0x40, and the
/// rounding away from zero adds one, so exactly 0x80 forward and 0x40 back, nothing across.
///
/// The scenario puts the player on an open, empty square with an open, empty square of the same
/// floor height beside it, faces him at it, and clicks (an input event, as the mouse sends it)
/// the left arrow, the right arrow, the right arrow from a heading off the eighths, the left arrow
/// from there, the forward arrow, and holds Shift+X briefly. In UW2 it also clicks the part of the
/// left arrow the information area covers, which must not turn him.
/// </summary>
public sealed class StepMoves : Scenario
{
    static readonly short[] Yaw = { 0, 16384, -32768, -16384 };   // motion.PlayerCardinalHeadingLookupTable
    static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };

    public override string Name => "step-moves";
    public override string Cites => "underworld-exhumed uw1/src/game/PLAYER.C:178, 183-185; uw1/src/motion/PHYSICS.C:188-283; uw1/src/motion/PLAYMOVE.C:288-320; uw1/src/sys/UTIL.C:34; uw2 PLAYER.C:172, 177-179, PHYSICS.C:195-288, PLAYMOVE.C:281-313, ui/GAMESCR.C:140, 142";

    string _problem;
    int _dir = -1;
    short _y0, _yLeft, _yRight, _yOffRight, _yOffLeft, _yCovered = -1;
    int _x0, _ypos0, _xFwd, _yFwd, _xBack, _yBack, _tileXFwd, _tileYFwd;

    public override async Task Setup(ScenarioContext c)
    {
        await Guards.CameraOnPlayer(c);
        var p = playerdat.playerObject;
        int best = int.MaxValue, sx = -1, sy = -1;
        for (int y = 1; y < 63; y++)
            for (int x = 1; x < 63; x++)
            {
                int dd = (x - p.tileX) * (x - p.tileX) + (y - p.tileY) * (y - p.tileY);
                if (dd >= best) continue;
                for (int d = 0; d < 4; d++)
                    if (Clear(x, y, true) && Clear(x + Dx[d], y + Dy[d], false)
                        && UWTileMap.current_tilemap.Tiles[x, y].floorHeight == UWTileMap.current_tilemap.Tiles[x + Dx[d], y + Dy[d]].floorHeight)
                    {
                        best = dd; sx = x; sy = y; _dir = d;
                        break;
                    }
            }
        if (_dir < 0) { _problem = "no open, empty square beside another of the same height on this level"; return; }
        if ((sx != p.tileX || sy != p.tileY) && !await Guards.TeleportHere(c, sx, sy)) { _problem = $"the move to ({sx}, {sy}) did not happen"; return; }
        playerdat.PlayerCameraYaw_dseg_8294 = Yaw[_dir];
        await c.Frames(20);
        if (motion.playerMotionParams.momentum_14 != 0 || motion.playerMotionParams.gravity_10_Z != 0 || playerdat.TileState != 0)
            _problem = "the player is moving, falling or not on plain ground";
        c.Note($"at ({p.tileX}, {p.tileY}) facing {"NESW"[_dir]}");
    }

    /// <summary>An open square, inside the map, holding no objects (or only the player when player is set).</summary>
    static bool Clear(int x, int y, bool player)
    {
        if (!UWTileMap.ValidTile(x, y)) return false;
        var map = UWTileMap.current_tilemap;
        var t = map.Tiles[x, y];
        if (t.tileType != UWTileMap.TILE_OPEN) return false;
        for (int i = t.indexObjectList, g = 0; i != 0 && g < 64; g++)
        {
            if (!(player && i == 1)) return false;
            i = map.LevelObjects[i].next;
        }
        return true;
    }

    /// <summary>The middle of an arrow, original 320 by 200 coordinates, y up from the bottom, as a position on this screen.</summary>
    static Vector2 At(int x, int y) => new Vector2(x * 4 + 2, (199 - y) * 4 + 2);

    static async Task Click(ScenarioContext c, Vector2 at)
    {
        // The game screen is 1280 by 800 stretched to the window: the event carries window coordinates.
        at = c.GameRoot.GetViewport().GetFinalTransform() * at;
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at });
        await c.Frames(2);
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at });
        await c.Frames(12);   // past the 0x18-tick wait after a step
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        bool uw2 = c.Game == "uw2";
        // The arrows' middles; in UW2 rows the information and spell areas leave uncovered.
        var left = uw2 ? At(0x73, 0x21) : At(0x73, 0x28);
        var forward = uw2 ? At(0x8A, 0x20) : At(0x8A, 0x25);
        var right = uw2 ? At(0xA0, 0x28) : At(0xA2, 0x28);

        _y0 = playerdat.PlayerCameraYaw_dseg_8294;
        if (uw2)
        {
            await Click(c, At(0x73, 0x28));
            _yCovered = playerdat.PlayerCameraYaw_dseg_8294;
        }
        await Click(c, left);
        _yLeft = playerdat.PlayerCameraYaw_dseg_8294;
        await Click(c, right);
        _yRight = playerdat.PlayerCameraYaw_dseg_8294;

        playerdat.PlayerCameraYaw_dseg_8294 = (short)(_y0 + 0x123);
        await Click(c, right);
        _yOffRight = playerdat.PlayerCameraYaw_dseg_8294;
        playerdat.PlayerCameraYaw_dseg_8294 = (short)(_y0 + 0x123);
        await Click(c, left);
        _yOffLeft = playerdat.PlayerCameraYaw_dseg_8294;

        playerdat.PlayerCameraYaw_dseg_8294 = _y0;
        await c.Frames(5);
        _x0 = motion.playerMotionParams.x_0; _ypos0 = motion.playerMotionParams.y_2;
        await Click(c, forward);
        _xFwd = motion.playerMotionParams.x_0; _yFwd = motion.playerMotionParams.y_2;
        _tileXFwd = playerdat.playerObject.tileX; _tileYFwd = playerdat.playerObject.tileY;

        Guards.Key(Key.Shift, true);
        Guards.Key(Key.X, true);
        await c.Frames(3);   // under the 30 ticks a held key waits to repeat
        Guards.Key(Key.X, false);
        Guards.Key(Key.Shift, false);
        await c.Frames(12);
        _xBack = motion.playerMotionParams.x_0; _yBack = motion.playerMotionParams.y_2;
        c.Note($"yaw {_y0} -> left {_yLeft} -> right {_yRight}; from +0x123 right {_yOffRight}, left {_yOffLeft}; covered {_yCovered}; pos ({_x0}, {_ypos0}) -> forward ({_xFwd}, {_yFwd}) square ({_tileXFwd}, {_tileYFwd}) -> back ({_xBack}, {_yBack})");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        const string turn = "simple_fizix's turn: uw1 PHYSICS.C:218-225, uw2 PHYSICS.C:225-232";
        if (c.Game == "uw2")
            k.Equal("yaw after a click on the left arrow under the information area", _y0, _yCovered, "the information area wins: uw2 ui/GAMESCR.C:142, ui/INPUT.C input_dispatch");
        k.Equal("yaw after the left arrow", (short)(_y0 - 0x2000), _yLeft, turn);
        k.Equal("yaw after the right arrow", _y0, _yRight, turn);
        k.Equal("yaw after the right arrow from 0x123 past an eighth", (short)(_y0 + 0x2000), _yOffRight, turn);
        k.Equal("yaw after the left arrow from 0x123 past an eighth", _y0, _yOffLeft, turn);
        k.Equal("forward arrow: x moved", 0x80 * Dx[_dir], _xFwd - _x0, "half a tile: uw1 PHYSICS.C:210-213, UTIL.C:34");
        k.Equal("forward arrow: y moved", 0x80 * Dy[_dir], _yFwd - _ypos0, "half a tile: uw1 PHYSICS.C:210-213, UTIL.C:34");
        k.Equal("player square x after the forward arrow", _xFwd >> 8, _tileXFwd, "the player object follows: uw1 PHYSICS.C:241-249");
        k.Equal("player square y after the forward arrow", _yFwd >> 8, _tileYFwd, "the player object follows: uw1 PHYSICS.C:241-249");
        k.Equal("Shift+X: x moved", -0x40 * Dx[_dir], _xBack - _xFwd, "a quarter tile back: uw1 PHYSICS.C:214-217, PLAYER.C:178");
        k.Equal("Shift+X: y moved", -0x40 * Dy[_dir], _yBack - _yFwd, "a quarter tile back: uw1 PHYSICS.C:214-217, PLAYER.C:178");
        return k.Result;
    }
}
