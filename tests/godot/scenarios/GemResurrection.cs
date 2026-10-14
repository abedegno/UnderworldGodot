using System.Reflection;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Coming back at the blackrock gem (UW2). A death outside Britannia (level above 8) makes the original's
/// player_is_dead queue a move to the stand-in square 0x3F on level 5 with npp_func = do_gem, and carry it out at
/// once with new_player_pos (uw2/src/game/SKILLS.C:849-868). new_player_pos changes level and runs do_gem, which
/// draws xoff = rand() % 5 + 1, yoff = rand() % 5 + 1 and moves the square 0x1C, 0x28 by move_along(rand(), 8);
/// if that square is open it leaves a blood stain (item 0xDF) on it at the floor's height, with its fine x set to
/// yoff and its fine y to xoff (SKILLS.C:663-711). Then new_player_pos searches for room round that square
/// (uw2 UWEDIT.C:480-486). With no room it fails, and player_is_dead ends the game with real_death(1), which plays
/// the death cutscene 0x103 and goes back to the start menu (SKILLS.C:864-865, UWEDIT.C:408-439).
///
/// Before this fix the port's death path (src/player/playerdatdeath.cs) sent the player to the fixed square 28, 37,
/// left the blood stain where the player died, and its callback ResurrectAtBlackrockGem set the hit points to the
/// maximum where do_gem sets them 2 to 4 below it (SKILLS.C:686).
///
/// A. The death path: the player is moved to level 9 (with the stand-in square and an empty callback, which parks
///    him), then playerdat.PlayerDeathUW2 runs and the queued move is carried out with
///    Teleportation.HandleTeleportation. He is on level 5 within the search box of a square move_along can give,
///    with 2 to 4 hit points below the maximum.
/// B. do_gem itself (ResurrectAtBlackrockGem, the callback, called directly on level 5) with a known seed: the
///    player lands on the square new_player_pos's search gives round do_gem's square, and the blood stain is on it.
///    move_along is computed with the port's motion.GetCoordinateInDirection, which is the original's formula.
/// C. As B with do_gem's square made solid, so there is no room even after clearing: the death cutscene 0x103
///    starts and the game goes back to the main menu.
/// </summary>
public sealed class GemResurrection : Scenario
{
    const int BloodStain = 0xDF;
    const int GemLevel = 5, OutsideLevel = 9;

    public override string Name => "gem-resurrection";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/game/SKILLS.C:663-711, 849-868; uw2/src/game/UWEDIT.C:408-439, 480-486";

    string _problem;
    int _aLevel, _aHp, _aMaxHp, _aCutscene;
    (int x, int y) _aEnd;
    uint _bSeed, _cSeed;
    int _bXoff, _bYoff;
    (int x, int y) _bSquare, _bExpected, _bEnd, _cSquare, _cStart, _cEnd;
    bool _bStain;
    int _bStainX, _bStainY, _bStainZ, _bFloorZ, _bCutscene, _cCutscene;
    bool _cMenu;

    static UWTileMap Map => UWTileMap.current_tilemap;
    static (int, int) Here => (playerdat.playerObject.tileX, playerdat.playerObject.tileY);

    static readonly MethodInfo DeathUW2 = typeof(playerdat).GetMethod("PlayerDeathUW2", BindingFlags.NonPublic | BindingFlags.Static);
    static readonly MethodInfo Gem = typeof(playerdat).GetMethod("ResurrectAtBlackrockGem", BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>do_gem's draws from seed (SKILLS.C:680-682): xoff, yoff, and the square after move_along(rand(), 8).</summary>
    static (int xoff, int yoff, int x, int y) GemSquare(uint seed)
    {
        var r = new BorlandRandSource(seed);
        int xoff = r.Rand() % 5 + 1;
        int yoff = r.Rand() % 5 + 1;
        int x = 0x1C, y = 0x28;
        motion.GetCoordinateInDirection(r.Rand(), 8, ref x, ref y);
        return (xoff, yoff, x, y);
    }

    public override Task Setup(ScenarioContext c)
    {
        if (DeathUW2 == null || Gem == null) _problem = "playerdat.PlayerDeathUW2 or ResurrectAtBlackrockGem not found";
        else if (playerdat.IsFightingInPit || playerdat.DreamingInVoid) _problem = "the player is fighting in the pits or dreaming";
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        // A: the death path from outside Britannia.
        Teleportation.CodeToRunOnTeleport = () => { };
        Teleportation.Teleport(0, 0x3F, 0x3F, OutsideLevel, 0);
        Teleportation.HandleTeleportation();
        if (playerdat.dungeon_level != OutsideLevel || playerdat.CurrentWorld == 0)
        {
            _problem = $"the move to level {OutsideLevel} left the player on level {playerdat.dungeon_level}, world {playerdat.CurrentWorld}";
            return;
        }
        DeathCutscene.Forget();
        DeathUW2.Invoke(null, null);
        Teleportation.HandleTeleportation();
        _aLevel = playerdat.dungeon_level;
        _aEnd = Here;
        _aHp = playerdat.play_hp;
        _aMaxHp = playerdat.max_hp;
        _aCutscene = DeathCutscene.Current;
        c.Note($"A: died on level {OutsideLevel}; now level {_aLevel} at {_aEnd} with {_aHp} of {_aMaxHp} hp, cutscene {_aCutscene:X}");
        playerdat.play_hp = _aMaxHp;
        await c.Frames(2);
        if (_aLevel != GemLevel) return;

        // B: do_gem with a known seed, onto an open square with room.
        for (uint s = 1; s < 200 && _bSeed == 0; s++)
        {
            var g = GemSquare(s);
            if (Map.Tiles[g.x, g.y].tileType == UWTileMap.TILE_OPEN && (g.x, g.y) != Here
                && Teleportation.FindGoodXAndY(playerdat.playerObject, g.x, g.y, out int ex, out int ey))
            {
                _bSeed = s; _bXoff = g.xoff; _bYoff = g.yoff; _bSquare = (g.x, g.y); _bExpected = (ex, ey);
            }
        }
        if (_bSeed == 0) { _problem = "no seed gives do_gem an open square with room (B)"; return; }
        int stainsBefore = Stains(_bSquare, out _);
        Rng.Source = new BorlandRandSource(_bSeed);
        DeathCutscene.Forget();
        Gem.Invoke(null, null);
        _bEnd = Here;
        _bCutscene = DeathCutscene.Current;
        _bStain = Stains(_bSquare, out var stain) > stainsBefore;
        if (stain != null) { _bStainX = stain.xpos; _bStainY = stain.ypos; _bStainZ = stain.zpos; }
        _bFloorZ = Map.Tiles[_bSquare.x, _bSquare.y].floorHeight << 3;
        c.Note($"B: seed {_bSeed}: xoff {_bXoff}, yoff {_bYoff}, square {_bSquare}, search gives {_bExpected}; player at {_bEnd}, new stain {_bStain} at fine ({_bStainX}, {_bStainY}) z {_bStainZ}, cutscene {_bCutscene:X}");
        await c.Frames(2);

        // C: do_gem's square made solid, so no room.
        _cStart = Here;
        for (uint s = _bSeed + 1; s < 400 && _cSeed == 0; s++)
        {
            var g = GemSquare(s);
            if ((g.x, g.y) != Here) { _cSeed = s; _cSquare = (g.x, g.y); }
        }
        var tile = Map.Tiles[_cSquare.x, _cSquare.y];
        var oldType = tile.tileType;
        tile.tileType = UWTileMap.TILE_SOLID;
        Rng.Source = new BorlandRandSource(_cSeed);
        DeathCutscene.Forget();
        Gem.Invoke(null, null);
        _cEnd = Here;
        _cCutscene = DeathCutscene.Current;
        tile.tileType = oldType;
        c.Note($"C: seed {_cSeed}: square {_cSquare} made solid; player now at {_cEnd}, cutscene {_cCutscene:X}");
        _cMenu = await DeathCutscene.StopAndReachMenu(c);
    }

    /// <summary>The number of blood stains in a square's object list, and the last one.</summary>
    static int Stains((int x, int y) sq, out uwObject last)
    {
        int n = 0;
        last = null;
        int i = Map.Tiles[sq.x, sq.y].indexObjectList;
        while (i != 0)
        {
            var o = Map.LevelObjects[i];
            if (o.item_id == BloodStain) { n++; last = o; }
            i = o.next;
        }
        return n;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_aMaxHp <= 8) return $"precondition: maximum hit points {_aMaxHp}, at most 8, where the full amount is given";
        var k = new Checks();
        k.Equal("A: level after a death outside Britannia", GemLevel, _aLevel, "do_teleport(ThePlayer, 0x3F, 0x3F, 5): uw2 SKILLS.C:853");
        // move_along(.., 8) moves at most 8 in each axis, and the search box reaches at most 5 beyond its start.
        k.True(System.Math.Abs(_aEnd.x - 0x1C) <= 13 && System.Math.Abs(_aEnd.y - 0x28) <= 13, $"A: the player expected within reach of (0x1C, 0x28) moved by move_along(rand(), 8), is at {_aEnd} (uw2 SKILLS.C:680-685)");
        k.Within("A: hit points after coming back at the gem", _aMaxHp - 4, _aMaxHp - 2, _aHp, "avghit - 2 - rand() * 3L / 0x8000L: uw2 SKILLS.C:686");
        k.True(_aCutscene != 0x103, "A: the death cutscene 0x103 started although there was room (uw2 SKILLS.C:858-863)");
        if (_aLevel != GemLevel) return k.Result;
        k.True(_bEnd == _bExpected, $"B: the player expected on {_bExpected}, the square new_player_pos's search gives round do_gem's {_bSquare}, is at {_bEnd} (uw2 SKILLS.C:680-685, UWEDIT.C:480-486)");
        k.True(_bStain, $"B: a blood stain expected on do_gem's open square {_bSquare} (uw2 SKILLS.C:697-709)");
        if (_bStain)
        {
            k.Equal("B: the blood stain's fine x (do_gem sets it from yoff)", _bYoff, _bStainX, "SET_FINEX(stain, yoff): uw2 SKILLS.C:705");
            k.Equal("B: the blood stain's fine y (do_gem sets it from xoff)", _bXoff, _bStainY, "SET_FINEY(stain, xoff): uw2 SKILLS.C:706");
            k.Equal("B: the blood stain's height", _bFloorZ, _bStainZ, "z = tile->height << 3: uw2 SKILLS.C:701, 704");
        }
        k.True(_bCutscene != 0x103, "B: the death cutscene 0x103 started although there was room (uw2 SKILLS.C:858-863)");
        k.Equal("C: cutscene started when there is no room round do_gem's square", 0x103, _cCutscene, "real_death(1): uw2 SKILLS.C:864-865, UWEDIT.C:421");
        k.True(_cEnd == _cStart, $"C: the player expected not moved from {_cStart} when there is no room, is at {_cEnd} (uw2 UWEDIT.C:480-486)");
        k.True(_cMenu, "C: the game expected back at the main menu after the death cutscene (real_death(1): uw2 UWEDIT.C:431-435)");
        return k.Result;
    }
}
