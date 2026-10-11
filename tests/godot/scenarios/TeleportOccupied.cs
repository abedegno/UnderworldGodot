using System;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// A teleport within the level does not land the player inside a critter (UW1 and UW2). The
/// original's do_teleport, for the current level (level 0 or PlayerLevel) and a square other
/// than x or y 0x3F, moves the destination to the first square of a breadth-first search
/// outwards from it, within the 9 by 9 box round it, where the object fits at the square's centre
/// (find_good_x_and_y with can_place at x * 8 + 3, y * 8 + 3) (uw1/src/event/WORLDEV.C:76-168,
/// 175-190; uw2/src/event/WORLDEV.C:65-157, 180-205).
///
/// The port queues the requested square and puts the player there with no fit test
/// (src/utility/teleportation.cs:169, InitialisePlayerOnLevelOrPositionChange; UW1's TeleportUW1,
/// :394).
///
/// The scenario finds an empty open square 2 to 10 squares from the player with an empty open
/// neighbour at the same floor height (so there is somewhere to fit), puts a critter (item 0x40) at its centre, and teleports the player
/// there through Teleportation.Teleport(0, x, y, 0), the call a teleport trap makes. The player must
/// end on another square of the 9 by 9 box.
///
/// Before that it queues a teleport callback (CodeToRunOnTeleport, which the port uses for jail,
/// resurrection at the gem and gate travel) and teleports to a solid square of this level, where
/// find_good_x_and_y finds nothing, so do_teleport fails and moves no one (uw1 WORLDEV.C:183-190,
/// uw2 WORLDEV.C:199-205, 228-229). The failed teleport's callback must not run later, on the
/// successful teleport.
/// </summary>
public sealed class TeleportOccupied : Scenario
{
    const int Critter = 0x40;
    const ulong TeleportMs = 10000;

    public override string Name => "teleport-occupied-square";
    public override string Cites => "underworld-exhumed uw1/src/event/WORLDEV.C:76-168, 175-190; uw2/src/event/WORLDEV.C:65-157, 180-205";

    string _problem;
    int _sx, _sy, _dx = -1, _dy = -1, _ex, _ey, _bx = -1, _by = -1;
    bool _moved, _staleRan, _blockedFits, _blockedMoved;
    int _blockedResult;

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        _sx = playerdat.playerObject.tileX; _sy = playerdat.playerObject.tileY;
        bool Open(int x, int y, int h) => UWTileMap.ValidTile(x, y) && map.Tiles[x, y].tileType == UWTileMap.TILE_OPEN && map.Tiles[x, y].floorHeight == h;
        for (int r = 2; r <= 10 && _dx < 0; r++)
            for (int y = _sy - r; y <= _sy + r && _dx < 0; y++)
                for (int x = _sx - r; x <= _sx + r && _dx < 0; x++)
                {
                    if (Math.Max(Math.Abs(x - _sx), Math.Abs(y - _sy)) != r || !UWTileMap.ValidTile(x, y)) continue;
                    int h = map.Tiles[x, y].floorHeight;
                    bool Free(int i, int j) => Open(x + i, y + j, h) && map.Tiles[x + i, y + j].indexObjectList == 0;
                    if (Free(0, 0) && (Free(1, 0) || Free(-1, 0) || Free(0, 1) || Free(0, -1))) { _dx = x; _dy = y; }
                }
        if (_dx < 0)
        {
            int open = 0, empty = 0;
            for (int y = _sy - 10; y <= _sy + 10; y++) for (int x = _sx - 10; x <= _sx + 10; x++)
                if (UWTileMap.ValidTile(x, y) && map.Tiles[x, y].tileType == UWTileMap.TILE_OPEN) { open++; if (map.Tiles[x, y].indexObjectList == 0) empty++; }
            c.Note($"open squares within 10: {open}, of them empty: {empty}");
        }
        if (_dx < 0) { _problem = $"no empty open square with an empty open neighbour 2 to 10 squares from the player at ({_sx}, {_sy})"; return Task.CompletedTask; }
        var crit = ObjectCreator.spawnObjectInTile(Critter, _dx, _dy, 3, 3, (short)(map.Tiles[_dx, _dy].floorHeight << 3), ObjectFreeLists.ObjectListType.MobileList);
        if (crit == null) { _problem = "no critter could be made"; return Task.CompletedTask; }
        for (int y = 1; y <= 0x3E && _bx < 0; y++)
            for (int x = 1; x <= 0x3E && _bx < 0; x++)
                if (map.Tiles[x, y].tileType == UWTileMap.TILE_SOLID) { _bx = x; _by = y; }
        if (_bx < 0) { _problem = "no solid square on the level"; return Task.CompletedTask; }
        _blockedFits = Teleportation.FindGoodXAndY(playerdat.playerObject, _bx, _by, out _, out _);
        crit.npc_goal = 7;   // stand
        crit.npc_attitude = 3;
        c.Note($"player at ({_sx}, {_sy}); critter 0x{crit.item_id:X} at the centre of ({_dx}, {_dy})");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        // A teleport with a callback queued that finds no square.
        Teleportation.CodeToRunOnTeleport = () => _staleRan = true;
        _blockedResult = Teleportation.Teleport(character: 0, tileX: _bx, tileY: _by, newLevel: 0, HeadingHeightFlag: 0);
        await c.Frames(2);
        _blockedMoved = playerdat.playerObject.tileX != _sx || playerdat.playerObject.tileY != _sy;
        c.Note($"teleport to solid ({_bx}, {_by}) returned {_blockedResult}, moved {_blockedMoved}, callback run yet {_staleRan}");
        Teleportation.Teleport(character: 0, tileX: _dx, tileY: _dy, newLevel: 0, HeadingHeightFlag: 0);
        ulong deadline = Time.GetTicksMsec() + TeleportMs;
        while (Time.GetTicksMsec() < deadline)
        {
            await c.Frames(1);
            if (playerdat.playerObject.tileX != _sx || playerdat.playerObject.tileY != _sy) { _moved = true; break; }
        }
        await c.Frames(1);
        _ex = playerdat.playerObject.tileX; _ey = playerdat.playerObject.tileY;
        c.Note($"player now at ({_ex}, {_ey})");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_moved) return $"precondition: the teleport did not move the player within {TeleportMs / 1000} s";
        string cite = c.Game == "uw1" ? "find_good_x_and_y: uw1 WORLDEV.C:76-168, 183-190" : "find_good_x_and_y: uw2 WORLDEV.C:65-157, 199-205";
        if (_blockedFits) return $"precondition: find_good_x_and_y found room round the solid square ({_bx}, {_by})";
        if (_blockedMoved) return "precondition: the teleport to a solid square moved the player";
        var k = new Checks();
        k.Equal("result of a teleport that finds no square", 2, _blockedResult, cite);
        k.True(!_staleRan, $"the callback queued for a teleport that failed ran later (do_teleport moves no one and returns 2: {(c.Game == "uw1" ? "uw1 WORLDEV.C:183-190" : "uw2 WORLDEV.C:199-205, 228-229")})");
        k.True(_ex != _dx || _ey != _dy, $"teleport onto a square with a critter at its centre expected to land on another square, landed on it at ({_dx}, {_dy}) ({cite})");
        k.True(Math.Abs(_ex - _dx) <= 4 && Math.Abs(_ey - _dy) <= 4, $"landing square ({_ex}, {_ey}) expected within the 9 by 9 box round ({_dx}, {_dy}) ({cite})");
        return k.Result;
    }
}
