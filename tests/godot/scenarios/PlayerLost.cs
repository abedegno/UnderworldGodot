using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// UW2's lost player. A teleport trap with fine x bit 2 set, fired by the player, loses him: if he
/// was not lost already, player->map_scrap becomes the level he is on, and player->automap is
/// cleared (uw2/src/event/TRIGGER.C:316-321). While it is clear the view does not write what it
/// sees into his map (uw2 3d/GRIDDB.C:640), the map screen shows the level of map_scrap
/// (uw2/src/ui/AUTOMAP.C:108, 128-131) with no marker for him (AUTOMAP.C:795), and leaving the map
/// screen sets map_scrap to the level shown (AUTOMAP.C:252). player_newsq, run on entering a square
/// by walking (uw2/src/motion/PHYSICS.C:574) and on arriving by a teleport (uw2/src/game/UWEDIT.C:503),
/// sets player->automap again when the square's map byte has a tile type of 1 to 9 in its low
/// nibble (PHYSICS.C:631-638; 0 is never seen and 0xA and up known but not seen, AUTOMAP.C:6-13).
/// UW1 has none of it (uw1 PHYSICS.C:389, uw1 TRIGGER.C:212-217).
///
/// Before this fix the port never lost the player (src/traps/a_teleport_trap.cs read fine x bits 0
/// and 1 only), had a TODO where the player enters a square (src/physics/motion_player.cs), found
/// the way again on any square with a nonzero map byte from PlayerStatusUpdate, opened the map on
/// the current level and drew the marker.
///
/// The parts, in order:
/// W. Lost, the player runs forward (W held) onto a square whose map byte is 0x0A, then onto one
///    whose byte is 0x01: still lost on the first, found on the second.
/// A. Found, a lost-bit teleport trap (item 0x181, fine x 4) to an unmapped square: lost straight
///    after the trap, map_scrap the level, and still lost on arrival.
/// B. Lost, the same trap to a mapped square (byte 0x01): map_scrap unchanged by the trap, found on
///    arrival.
/// C. Found, a teleport trap with fine x 0 to an unmapped square: not lost.
/// M. Lost, map_scrap another level: the map scroll opens the map on that level, the map of the
///    player's level is drawn differently found and lost (the marker), and closing the map sets
///    map_scrap to the level shown.
/// Teleports are carried out with Teleportation.HandleTeleportation, called directly.
/// </summary>
public sealed class PlayerLost : Scenario
{
    const int TeleportTrap = 0x181;
    static readonly short[] Yaw = { 0, 16384, -32768, -16384 };   // as GuardWalk
    static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };

    public override string Name => "player-lost";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/event/TRIGGER.C:316-321; uw2/src/motion/PHYSICS.C:574, 631-638; uw2/src/game/UWEDIT.C:503; uw2/src/ui/AUTOMAP.C:6-13, 108, 128-131, 252, 795";

    string _problem;
    int _level, _dir = -1;
    (int x, int y) _s, _w1, _w2, _t1, _t2;
    bool _wReached1, _wReached2, _wLostOn1, _wLostOn2;
    bool _aLostAfterTrap, _aLostOnArrival, _bLostAfterTrap, _bLostOnArrival, _cLost;
    int _aScrap, _bScrap, _mShown, _mScrapAfterClose, _mScrapLevel;
    (int, int) _aEnd, _bEnd;
    bool _mMarkerFound, _mMarkerLost;

    static UWTileMap Map => UWTileMap.current_tilemap;
    static (int x, int y) Here => (playerdat.playerObject.tileX, playerdat.playerObject.tileY);
    static byte[] MapBytes => automap.automaps[playerdat.dungeon_level - 1].buffer;

    static bool OpenEmpty(int x, int y, int h) => UWTileMap.ValidTile(x, y) && Map.Tiles[x, y].tileType == UWTileMap.TILE_OPEN
        && Map.Tiles[x, y].floorHeight == h && Map.Tiles[x, y].indexObjectList == 0;

    /// <summary>The nearest empty open square, not one of avoid, whose four neighbours are empty open squares at its height.</summary>
    static (int x, int y) FindSquare(params (int x, int y)[] avoid)
    {
        var (sx, sy) = Here;
        for (int r = 3; r <= 63; r++)
            for (int y = sy - r; y <= sy + r; y++)
                for (int x = sx - r; x <= sx + r; x++)
                {
                    if (Math.Max(Math.Abs(x - sx), Math.Abs(y - sy)) != r || !UWTileMap.ValidTile(x, y)) continue;
                    if (Array.Exists(avoid, a => Math.Abs(a.x - x) <= 1 && Math.Abs(a.y - y) <= 1)) continue;
                    int h = Map.Tiles[x, y].floorHeight;
                    if (OpenEmpty(x, y, h) && OpenEmpty(x + 1, y, h) && OpenEmpty(x - 1, y, h) && OpenEmpty(x, y + 1, h) && OpenEmpty(x, y - 1, h)) return (x, y);
                }
        return (-1, -1);
    }

    /// <summary>A cardinal direction from (x, y) with three empty open squares at its height ahead, or -1.</summary>
    static int RunFrom(int x, int y)
    {
        int h = Map.Tiles[x, y].floorHeight;
        for (int d = 0; d < 4; d++)
            if (OpenEmpty(x + Dx[d], y + Dy[d], h) && OpenEmpty(x + 2 * Dx[d], y + 2 * Dy[d], h) && OpenEmpty(x + 3 * Dx[d], y + 3 * Dy[d], h)) return d;
        return -1;
    }

    uwObject Trap(int xpos, (int x, int y) to)
    {
        int slot = ObjectCreator.PrepareNewObject(TeleportTrap);
        if (slot == 0) { _problem ??= "no teleport trap could be made"; return null; }
        var t = Map.LevelObjects[slot];
        t.link = 0; t.next = 0; t.is_quant = 0;
        t.tileX = Here.x; t.tileY = Here.y;
        t.xpos = (short)xpos; t.ypos = 0; t.zpos = 0; t.heading = 0;
        t.quality = (short)to.x; t.owner = (short)to.y;
        return t;
    }

    static void Fire(uwObject t) =>
        trap.ActivateTrap(character: 1, trapObj: t, ObjectUsed: null, triggerX: Here.x, triggerY: Here.y, objList: Map.LevelObjects);

    static int At((int x, int y) p) => (p.y << 6) + p.x;

    /// <summary>Whether every opaque pixel of the marker (uimanager's cross, drawn at the player's square, 4 pixels a square) is on the map image.
    /// The map's floor colours are picked at random, so two renders cannot be compared whole.</summary>
    static bool MarkerDrawn(Image img)
    {
        var cross = uimanager.instance.cross.GetImage();
        int n = 0, match = 0;
        for (int y = 0; y < cross.GetHeight(); y++)
            for (int x = 0; x < cross.GetWidth(); x++)
            {
                var cc = cross.GetPixel(x, y);
                if (cc.A8 < 255) continue;
                n++;
                int ox = Here.x * 4 + x, oy = Here.y * 4 + y;
                if (ox < img.GetWidth() && oy < img.GetHeight() && img.GetPixel(ox, oy) == cc) match++;
            }
        return n > 0 && match == n;
    }

    public override async Task Setup(ScenarioContext c)
    {
        await Guards.CameraOnPlayer(c);
        _level = playerdat.dungeon_level;
        var p = playerdat.playerObject;
        _dir = RunFrom(p.tileX, p.tileY);
        if (_dir < 0)
        {// the nearest empty open square with a run, as GuardWalk does
            int best = int.MaxValue, sx = -1, sy = -1;
            for (int y = 1; y < 63; y++)
                for (int x = 1; x < 63; x++)
                {
                    int dd = (x - p.tileX) * (x - p.tileX) + (y - p.tileY) * (y - p.tileY);
                    if (dd < best && OpenEmpty(x, y, Map.Tiles[x, y].floorHeight) && RunFrom(x, y) >= 0) { best = dd; sx = x; sy = y; }
                }
            if (sx < 0 || !await Guards.TeleportHere(c, sx, sy)) { _problem = "no square with a run of three open squares ahead"; return; }
            _dir = RunFrom(p.tileX, p.tileY);
            if (_dir < 0) { _problem = $"no run ahead after the move to ({sx}, {sy})"; return; }
        }
        _s = Here;
        _w1 = (_s.x + Dx[_dir], _s.y + Dy[_dir]);
        _w2 = (_s.x + 2 * Dx[_dir], _s.y + 2 * Dy[_dir]);
        _t1 = FindSquare(_s, _w1, _w2, (_s.x + 3 * Dx[_dir], _s.y + 3 * Dy[_dir]));
        _t2 = FindSquare(_s, _w1, _w2, (_s.x + 3 * Dx[_dir], _s.y + 3 * Dy[_dir]), _t1);
        if (_t1.x < 0 || _t2.x < 0) { _problem = "no two empty squares with empty neighbours to teleport to"; return; }
        playerdat.PlayerCameraYaw_dseg_8294 = Yaw[_dir];
        await c.Frames(5);
        c.Note($"level {_level}: running {"NESW"[_dir]} from {_s} over {_w1} and {_w2}; teleport squares {_t1} and {_t2}");
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;

        // W: walking while lost.
        playerdat.AutomapEnabled = false;
        MapBytes[At(_w1)] = 0x0A;
        MapBytes[At(_w2)] = 0x01;
        Guards.Key(Key.W, true);
        for (int f = 0; f < 300 && !_wReached2; f++)
        {
            await c.Frames(1);
            if (!_wReached1 && Here == _w1) { _wReached1 = true; _wLostOn1 = !playerdat.AutomapEnabled; }
            if (!_wReached2 && Here == _w2) { _wReached2 = true; _wLostOn2 = !playerdat.AutomapEnabled; }
        }
        Guards.Key(Key.W, false);
        await c.Frames(30);
        c.Note($"W: reached {_w1} {_wReached1} (lost {_wLostOn1}), {_w2} {_wReached2} (lost {_wLostOn2})");

        // A: a lost-bit trap to an unmapped square.
        playerdat.AutomapEnabled = true;
        playerdat.MapScrap = 0x33;
        MapBytes[At(_t1)] = 0;
        var ta = Trap(4, _t1);
        if (ta == null) return;
        Fire(ta);
        _aLostAfterTrap = !playerdat.AutomapEnabled;
        _aScrap = playerdat.MapScrap;
        MapBytes[At(_t1)] = 0;
        Teleportation.HandleTeleportation();
        _aEnd = Here;
        _aLostOnArrival = !playerdat.AutomapEnabled;
        c.Note($"A: after the trap lost {_aLostAfterTrap}, map_scrap {_aScrap}; arrived at {_aEnd}, lost {_aLostOnArrival}");
        await c.Frames(5);

        // B: the same trap while lost, to a mapped square.
        playerdat.AutomapEnabled = false;
        playerdat.MapScrap = 0x22;
        MapBytes[At(_t2)] = 0x01;
        var tb = Trap(4, _t2);
        if (tb == null) return;
        Fire(tb);
        _bLostAfterTrap = !playerdat.AutomapEnabled;
        _bScrap = playerdat.MapScrap;
        Teleportation.HandleTeleportation();
        _bEnd = Here;
        _bLostOnArrival = !playerdat.AutomapEnabled;
        c.Note($"B: after the trap lost {_bLostAfterTrap}, map_scrap {_bScrap}; arrived at {_bEnd}, lost {_bLostOnArrival}");
        await c.Frames(5);

        // C: a trap without the lost bit.
        playerdat.AutomapEnabled = true;
        MapBytes[At(_t1)] = 0;
        var tc = Trap(0, _t1);
        if (tc == null) return;
        Fire(tc);
        Teleportation.HandleTeleportation();
        _cLost = !playerdat.AutomapEnabled;
        c.Note($"C: arrived at {Here}, lost {_cLost}");
        await c.Frames(5);

        // M: the map screen.
        int block = playerdat.dungeon_level - 1;
        playerdat.AutomapEnabled = true;
        _mMarkerFound = MarkerDrawn(AutomapRender.MapImage(block).GetImage());
        playerdat.AutomapEnabled = false;
        _mMarkerLost = MarkerDrawn(AutomapRender.MapImage(block).GetImage());
        _mScrapLevel = _level == 2 ? 3 : 2;
        playerdat.MapScrap = _mScrapLevel;
        map.Use(playerdat.playerObject, false);
        _mShown = automap.currentworld * 8 + automap.currentautomap + 1;
        await c.Frames(2);
        playerdat.MapScrap = 0x44;
        var close = typeof(uimanager).GetMethod("CloseAutomap", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("uimanager", "CloseAutomap");
        close.Invoke(uimanager.instance, new object[] { new InputEventMouseButton { Pressed = true, ButtonIndex = MouseButton.Left } });
        _mScrapAfterClose = playerdat.MapScrap;
        playerdat.AutomapEnabled = true;
        c.Note($"M: map opened on level {_mShown} (map_scrap {_mScrapLevel}), marker found {_mMarkerFound}, lost {_mMarkerLost}, map_scrap after closing {_mScrapAfterClose}, mode {uimanager.CurrentGameMode}");
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_wReached1 || !_wReached2) return $"precondition: the run did not reach {_w1} and {_w2}";
        if (!_mMarkerFound) return "precondition: the map of the player's level, found, does not show his marker where the test looks";
        if (_aEnd != _t1 || _bEnd != _t2) return $"precondition: the teleports landed at {_aEnd} and {_bEnd}, not {_t1} and {_t2}";
        var k = new Checks();
        k.True(_wLostOn1, "W: entering a square whose map byte is 0x0A (known, not seen) expected the player still lost (player_newsq: uw2 PHYSICS.C:631-638)");
        k.True(!_wLostOn2, "W: entering a square whose map byte is 0x01 expected the player found (player_newsq: uw2 PHYSICS.C:574, 631-638)");
        k.True(_aLostAfterTrap, "A: a teleport trap with fine x bit 2 fired by the player expected him lost (uw2 TRIGGER.C:316-321)");
        k.Equal("A: map_scrap after the trap", _level, _aScrap, "uw2 TRIGGER.C:318-319");
        k.True(_aLostOnArrival, "A: arriving on an unmapped square expected the player still lost (uw2 UWEDIT.C:503, PHYSICS.C:631-638)");
        k.True(_bLostAfterTrap, "B: the trap expected the player lost (uw2 TRIGGER.C:320)");
        k.Equal("B: map_scrap after the trap on a player already lost", 0x22, _bScrap, "uw2 TRIGGER.C:318");
        k.True(!_bLostOnArrival, "B: arriving on a mapped square expected the player found (uw2 UWEDIT.C:503, PHYSICS.C:631-638)");
        k.True(!_cLost, "C: a teleport trap with fine x bit 2 clear expected the player not lost (uw2 TRIGGER.C:316-317)");
        k.Equal("M: level the map opens on for a lost player", _mScrapLevel, _mShown, "uw2 AUTOMAP.C:108, 128-131");
        k.True(!_mMarkerLost, "M: the map of the player's level expected drawn without his marker when lost (uw2 AUTOMAP.C:795)");
        k.Equal("M: map_scrap after closing the map", _mScrapLevel, _mScrapAfterClose, "uw2 AUTOMAP.C:252");
        return k.Result;
    }
}
