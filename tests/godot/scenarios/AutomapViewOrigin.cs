using System;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// The automap is marked from the camera's square, not the player's (UW1 and UW2). The original's setup_vars takes
/// the eye from get_eye (the player's head, or the roaming camera campos when the view is detached) and makes the eye's
/// square the grid's origin, mapptr (uw1/src/3d/VIEW3D.C:448-459, uw2/src/3d/VIEW3D.C:443-454; get_eye: uw1
/// src/motion/PLAYMOVE.C:604-666, uw2 PLAYMOVE.C:643-705). init_grid starts the vision grid there (uw1 VIEW3D.C:540-556),
/// and subprocess walks the automap from it, from the farthest row to the eye, then the row just behind the eye
/// (uw1/src/3d/GRIDDB.C:315-362, uw2 GRIDDB.C). Every square the walk passes that is not seen gets its unexplored code
/// whether the automap switch is on or not; a seen one gets its full code only with the switch on (uw1 GRIDDB.C:561-566,
/// 660-661).
///
/// The port built the grid and walked the automap from the player's square in every view
/// (src/utility/visionlos.cs:246-247, 333), so Roaming Sight and a camera trap marked the squares around the player.
///
/// The scenario picks an open square at least 24 squares from the player, faces it away from the player, empties the level's
/// automap, detaches the view to a camera there (CameraReference null and the DoCamera values, as Roaming Sight and
/// a_do_trap_camera do) and runs one view update (playerdat.PositionPlayerCamera), once with the automap switch on
/// (Roaming Sight) and once with it off (a camera trap, crystal_ball, turns it off while it shows its view: uw1 TRIGGER.C:505, PLAYER.C:448-466; uw2 TRIGGER.C:779, PLAYER.C:463-482).
/// It counts the marked squares within 2 of the camera and within 2 of the player.
/// </summary>
public sealed class AutomapViewOrigin : Scenario
{
    public override string Name => "automap-view-origin";
    public override string Cites => "underworld-exhumed uw1/src/3d/VIEW3D.C:448-459, 540-556; uw2/src/3d/VIEW3D.C:443-454; uw1/src/3d/GRIDDB.C:315-362, 561-566, 660-661; uw1/src/motion/PLAYMOVE.C:604-666";

    string _problem;
    int _camX, _camY, _plX, _plY;
    short _heading;
    readonly int[] _nearCamera = new int[2], _nearPlayer = new int[2];
    readonly bool[] _cameraSquare = new bool[2];

    static int Marked(byte[] map, int cx, int cy)
    {
        int n = 0;
        for (int y = cy - 2; y <= cy + 2; y++)
            for (int x = cx - 2; x <= cx + 2; x++)
                if (x >= 0 && x < 64 && y >= 0 && y < 64 && map[y * 64 + x] != 0) n++;
        return n;
    }

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        if (p == null) { _problem = "no player object"; return Task.CompletedTask; }
        _plX = p.tileX; _plY = p.tileY;
        var tiles = UWTileMap.current_tilemap.Tiles;
        int best = -1;
        for (int y = 3; y < 61; y++)
            for (int x = 3; x < 61; x++)
            {
                int d = Math.Max(Math.Abs(x - _plX), Math.Abs(y - _plY));
                if (d < 24 || tiles[x, y].tileType == UWTileMap.TILE_SOLID) continue;
                if (d > best) { best = d; _camX = x; _camY = y; }
            }
        if (best < 0) { _problem = $"no open square 24 squares from the player at ({_plX}, {_plY})"; return Task.CompletedTask; }
        // of the four ways, the camera faces the one that points most directly away from the player
        long bestAway = long.MinValue;
        foreach (short h in new short[] { 0, 0x4000, -0x8000, -0x4000 })
        {
            short vx = 0, vy = 0;
            motion.SomethingProjectileHeading_seg021_22FD_EAE((ushort)h, ref vx, ref vy);
            long away = (long)vx * (_camX - _plX) + (long)vy * (_camY - _plY);
            if (away > bestAway) { bestAway = away; _heading = h; }
        }
        return Task.CompletedTask;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var map = automap.automaps[playerdat.dungeon_level - 1].buffer;
        var keep = (byte[])map.Clone();
        bool keepSwitch = playerdat.AutomapEnabled;
        var keepRef = playerdat.CameraReference;
        short kx = playerdat.DoCameraX, ky = playerdat.DoCameraY, kz = playerdat.DoCameraZ, kh = playerdat.DoCameraH, kp = playerdat.DoCameraPitch, kr = playerdat.DoCameraRoll;
        try
        {
            for (int pass = 0; pass < 2; pass++)
            {
                Array.Clear(map);
                playerdat.AutomapEnabled = pass == 0;
                playerdat.CameraReference = null;
                playerdat.DoCameraX = (short)((_camX << 8) + 0x80);
                playerdat.DoCameraY = (short)((_camY << 8) + 0x80);
                playerdat.DoCameraZ = 0x458;   // SetCameraViewValues(0), Roaming Sight's height
                playerdat.DoCameraH = _heading;
                playerdat.DoCameraPitch = 0;
                playerdat.DoCameraRoll = 0;
                playerdat.PositionPlayerCamera();
                _nearCamera[pass] = Marked(map, _camX, _camY);
                _nearPlayer[pass] = Marked(map, _plX, _plY);
                _cameraSquare[pass] = map[_camY * 64 + _camX] != 0;
                c.Note($"automap {(pass == 0 ? "on (Roaming Sight)" : "off (camera trap)")}: camera at ({_camX}, {_camY}) heading 0x{(ushort)_heading:X}, player at ({_plX}, {_plY}); marked within 2 of the camera {_nearCamera[pass]}, of the player {_nearPlayer[pass]}; camera square {(_cameraSquare[pass] ? "marked" : "not marked")}");
            }
        }
        finally
        {
            Array.Copy(keep, map, map.Length);
            playerdat.AutomapEnabled = keepSwitch;
            playerdat.DoCameraX = kx; playerdat.DoCameraY = ky; playerdat.DoCameraZ = kz; playerdat.DoCameraH = kh; playerdat.DoCameraPitch = kp; playerdat.DoCameraRoll = kr;
            playerdat.CameraReference = keepRef;
            playerdat.PositionPlayerCamera();
        }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        string[] what = { "with the automap on (Roaming Sight)", "with the automap off (camera trap)" };
        for (int pass = 0; pass < 2; pass++)
        {
            k.True(_nearCamera[pass] > 0, $"{what[pass]}: expected squares around the camera marked, none were (the grid's origin is the eye's square, mapptr: VIEW3D.C setup_vars; GRIDDB.C subprocess)");
            k.Equal($"{what[pass]}: squares marked around the player, 24 or more squares behind the camera", 0, _nearPlayer[pass], "mapptr is the eye's square: uw1 VIEW3D.C:456-459, uw2 VIEW3D.C:451-454");
        }
        k.True(_cameraSquare[0], "with the automap on: expected the camera's own square marked (subprocess's middle column of the eye's row, uw1 GRIDDB.C:353-355)");
        return k.Result;
    }
}
