using System;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Carrying out a player move searches for room again, then clears room, then kills (UW1 and
/// UW2). The original's new_player_pos, which carries out the move queued by do_teleport on the
/// next pass of the main loop, changes level if it must, runs npp_func, and then calls
/// find_good_x_and_y on the queued square, first plainly and then with clear set, which deletes
/// (Obj_Punt, so only what is not tenacious) the first object with a height, or the first mobile
/// object, on each square it searches. If both fail the player's hit points become 0 and he is not
/// moved (uw1/src/game/UWEDIT.C:450-484, uw2/src/game/UWEDIT.C:466-507; find_good_x_and_y uw1
/// WORLDEV.C:76-168, uw2 WORLDEV.C:65-157; Obj_Punt and chkTenacious uw1 OBJECTS.C:136-170, 306-321).
///
/// Before this fix the port put the player on the queued square with no search (src/utility/teleportation.cs,
/// HandleTeleportation and InitialisePlayerOnLevelOrPositionChange).
///
/// Each part queues a teleport with Teleportation.Teleport to an empty square, so do_teleport's
/// own search finds that square, then changes the square before the move is carried out
/// (Teleportation.HandleTeleportation, called directly):
/// A. a critter (item 0x40, tenacity 8) on the square: the player lands on another square nearby.
/// B. a critter on the square and its four neighbours made solid: the plain search finds nothing,
///    the second search deletes the critter, and the player lands on the square.
/// C. as B but the critter is tenacious (its doordir bit set, chkTenacious): nothing fits, the
///    player stays where he was with 0 hit points.
/// D. a teleport to another level, onto a solid square of the map's edge (x 0x20, y 0): the player
///    has 0 hit points.
/// Hit points are put back after each part, before any frame passes, so the player does not die.
/// </summary>
public sealed class NewPlayerPos : Scenario
{
    const int Critter = 0x40;
    const int EdgeX = 0x20, EdgeY = 0;

    public override string Name => "new-player-pos";
    public override string Cites => "underworld-exhumed uw1/src/game/UWEDIT.C:450-484; uw2/src/game/UWEDIT.C:466-507; uw1 WORLDEV.C:76-168; uw2 WORLDEV.C:65-157; uw1 OBJECTS.C:136-170, 306-321";

    string _problem;
    (int x, int y) _a, _aEnd, _b, _bEnd, _c, _cStart, _cEnd;
    bool _bCritterGone, _cCritterKept, _dEdgeSolid;
    int _bHp, _cHp, _dHp, _dLevel, _level0;

    static UWTileMap Map => UWTileMap.current_tilemap;
    static (int, int) Here => (playerdat.playerObject.tileX, playerdat.playerObject.tileY);

    /// <summary>The nearest empty open square at least 2 squares from the player whose four neighbours are open squares at its
    /// height, and, when emptyNeighbours is set, empty too.</summary>
    static (int x, int y) FindSquare(bool emptyNeighbours)
    {
        var (sx, sy) = Here;
        bool Open(int x, int y, int h) => UWTileMap.ValidTile(x, y) && Map.Tiles[x, y].tileType == UWTileMap.TILE_OPEN
            && Map.Tiles[x, y].floorHeight == h;
        bool Free(int x, int y, int h) => Open(x, y, h) && Map.Tiles[x, y].indexObjectList == 0;
        bool Side(int x, int y, int h) => emptyNeighbours ? Free(x, y, h) : Open(x, y, h);
        for (int r = 2; r <= 63; r++)
            for (int y = sy - r; y <= sy + r; y++)
                for (int x = sx - r; x <= sx + r; x++)
                {
                    if (Math.Max(Math.Abs(x - sx), Math.Abs(y - sy)) != r || !UWTileMap.ValidTile(x, y)) continue;
                    int h = Map.Tiles[x, y].floorHeight;
                    if (Free(x, y, h) && Side(x + 1, y, h) && Side(x - 1, y, h) && Side(x, y + 1, h) && Side(x, y - 1, h)) return (x, y);
                }
        return (-1, -1);
    }

    static uwObject PutCritter(int x, int y) =>
        ObjectCreator.spawnObjectInTile(Critter, x, y, 3, 3, (short)(Map.Tiles[x, y].floorHeight << 3), ObjectFreeLists.ObjectListType.MobileList);

    static readonly int[] Nx = { 1, -1, 0, 0 }, Ny = { 0, 0, 1, -1 };

    static short[] CloseNeighbours(int x, int y)
    {
        var old = new short[4];
        for (int d = 0; d < 4; d++) { old[d] = Map.Tiles[x + Nx[d], y + Ny[d]].tileType; Map.Tiles[x + Nx[d], y + Ny[d]].tileType = UWTileMap.TILE_SOLID; }
        return old;
    }

    static void OpenNeighbours(int x, int y, short[] old)
    {
        for (int d = 0; d < 4; d++) Map.Tiles[x + Nx[d], y + Ny[d]].tileType = old[d];
    }

    public override Task Setup(ScenarioContext c)
    {
        _level0 = playerdat.dungeon_level;
        c.Note($"player at {Here} on level {_level0}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        int hp = playerdat.play_hp;
        // A: a critter arrives on the square between the queueing and the move.
        _a = FindSquare(emptyNeighbours: true);
        if (_a.x < 0) { _problem = "no empty open square with four empty open neighbours (A)"; return; }
        Teleportation.Teleport(0, _a.x, _a.y, 0, 0);
        if (PutCritter(_a.x, _a.y) == null) { _problem = "no critter could be made (A)"; return; }
        Teleportation.HandleTeleportation();
        _aEnd = Here;
        playerdat.play_hp = hp;
        c.Note($"A: queued ({_a.x}, {_a.y}), critter put there, player now at {_aEnd}");
        await c.Frames(2);

        // B: a critter on the square and no other square to reach.
        _b = FindSquare(emptyNeighbours: false);
        if (_b.x < 0) { _problem = "no empty open square with four open neighbours (B)"; return; }
        Teleportation.Teleport(0, _b.x, _b.y, 0, 0);
        var critB = PutCritter(_b.x, _b.y);
        if (critB == null) { _problem = "no critter could be made (B)"; return; }
        int critBIndex = critB.index;
        var oldB = CloseNeighbours(_b.x, _b.y);
        if (Teleportation.FindGoodXAndY(playerdat.playerObject, _b.x, _b.y, out _, out _))
        {
            OpenNeighbours(_b.x, _b.y, oldB);
            _problem = "find_good_x_and_y found room round the closed-in square with the critter (B)";
            return;
        }
        Teleportation.HandleTeleportation();
        OpenNeighbours(_b.x, _b.y, oldB);
        _bEnd = Here;
        _bHp = playerdat.play_hp;
        playerdat.play_hp = hp;
        _bCritterGone = Map.LevelObjects[critBIndex].item_id != Critter || Map.LevelObjects[critBIndex].tileX != _b.x || Map.LevelObjects[critBIndex].tileY != _b.y;
        c.Note($"B: queued ({_b.x}, {_b.y}), critter put there and its neighbours closed, player now at {_bEnd} with {_bHp} hp, critter gone {_bCritterGone}");
        await c.Frames(2);

        // C: as B with a tenacious critter.
        _cStart = Here;
        _c = FindSquare(emptyNeighbours: false);
        if (_c.x < 0) { _problem = "no empty open square with four open neighbours (C)"; return; }
        Teleportation.Teleport(0, _c.x, _c.y, 0, 0);
        var critC = PutCritter(_c.x, _c.y);
        if (critC == null) { _problem = "no critter could be made (C)"; return; }
        critC.doordir = 1;
        int critCIndex = critC.index;
        var oldC = CloseNeighbours(_c.x, _c.y);
        Teleportation.HandleTeleportation();
        OpenNeighbours(_c.x, _c.y, oldC);
        _cEnd = Here;
        _cHp = playerdat.play_hp;
        playerdat.play_hp = hp;
        _cCritterKept = Map.LevelObjects[critCIndex].item_id == Critter && Map.LevelObjects[critCIndex].tileX == _c.x && Map.LevelObjects[critCIndex].tileY == _c.y;
        critC.doordir = 0;
        c.Note($"C: queued ({_c.x}, {_c.y}), tenacious critter put there and its neighbours closed, player now at {_cEnd} with {_cHp} hp, critter kept {_cCritterKept}");
        await c.Frames(2);

        // D: a level change onto a solid square.
        int level = _level0 == 2 ? 3 : 2;
        Teleportation.Teleport(0, EdgeX, EdgeY, level, 0);
        Teleportation.HandleTeleportation();
        _dLevel = playerdat.dungeon_level;
        _dHp = playerdat.play_hp;
        playerdat.play_hp = hp;
        _dEdgeSolid = Map.Tiles[EdgeX, EdgeY].tileType == UWTileMap.TILE_SOLID;
        c.Note($"D: teleport to level {level} ({EdgeX}, {EdgeY}): now level {_dLevel} at {Here} with {_dHp} hp (square solid {_dEdgeSolid})");
        await c.Frames(2);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (!_dEdgeSolid) return $"precondition: ({EdgeX}, {EdgeY}) of the new level is not solid";
        if (_dLevel == _level0) return "precondition: the level change (D) did not happen";
        string npp = c.Game == "uw1" ? "new_player_pos: uw1 UWEDIT.C:466-471" : "new_player_pos: uw2 UWEDIT.C:480-486";
        var k = new Checks();
        k.True(_aEnd != _a, $"A: a critter on the queued square ({_a.x}, {_a.y}) when the move is carried out expected the player on another square, landed on it ({npp})");
        k.True(Math.Abs(_aEnd.x - _a.x) <= 4 && Math.Abs(_aEnd.y - _a.y) <= 4, $"A: landing square {_aEnd} expected within the box round ({_a.x}, {_a.y}) ({npp})");
        k.True(_bCritterGone, $"B: the critter on the closed-in square ({_b.x}, {_b.y}) expected deleted by the second search ({npp}, find_good_x_and_y with clear)");
        k.True(_bEnd == _b, $"B: the player expected on the cleared square ({_b.x}, {_b.y}), is at {_bEnd} ({npp})");
        k.True(_bHp != 0, $"B: the player expected alive after a move the second search found room for ({npp})");
        k.Equal("C: hit points after a move with no room even after clearing", 0, _cHp, npp);
        k.True(_cEnd == _cStart, $"C: the player expected not moved from {_cStart} when there is no room, is at {_cEnd} ({npp})");
        k.True(_cCritterKept, $"C: the tenacious critter expected kept ({npp}, Obj_Punt and chkTenacious)");
        k.Equal("D: hit points after a level change onto a solid square", 0, _dHp, npp);
        return k.Result;
    }
}
