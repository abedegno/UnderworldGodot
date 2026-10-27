using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Coming back at the silver tree (UW1). With a tree planted, the original's player_is_dead queues a move to
/// the stand-in square 0x3F on the tree's level with npp_func = do_resurrect, and carries it out at once with
/// new_player_pos (uw1/src/game/SKILLS.C:861-878). new_player_pos changes level and runs do_resurrect, which sets
/// the hit points 2 to 4 below the maximum (rand() * 3L / 0x8000L, SKILLS.C:794) and the square to the tree's
/// (moveto, SKILLS.C:769-784, 790-806); then it searches for room round that square, plainly and then clearing
/// (uw1 UWEDIT.C:466-471). With no room it fails, and player_is_dead ends the game with real_death(1), which plays
/// the death cutscene 0x103 and goes back to the start menu (SKILLS.C:874-875, UWEDIT.C:393-424).
///
/// Before this fix the port's TeleportToSilverTree (src/player/playerdatdeath.cs) set the hit points to the maximum
/// and queued a second teleport to the tree, with do_teleport's plain search only; with no room that teleport was
/// dropped and the player was left alive on the parking square 32, 32.
///
/// The scenario plants a tree (item 0x1CA) on an empty open square of level 1 and sets the tree's level, then calls
/// playerdat.ResurrectAtSilverTree (the callback of the cutscene 0x102 the port plays on death) and carries out the
/// queued move with Teleportation.HandleTeleportation.
/// A. The player lands on the square new_player_pos's search gives round the tree, with 2 to 4 hit points below the
///    maximum, and the game goes on.
/// B. The tree's square is made solid, so there is no room even after clearing: the death cutscene 0x103 starts and
///    the game goes back to the main menu.
/// </summary>
public sealed class SilverTreeResurrection : Scenario
{
    const int SilverTree = 0x1CA;

    public override string Name => "silver-tree-resurrection";
    public override string[] Games => new[] { "uw1" };
    public override string Cites => "underworld-exhumed uw1/src/game/SKILLS.C:769-806, 861-878; uw1/src/game/UWEDIT.C:393-424, 466-471";

    string _problem;
    (int x, int y) _tree, _expected, _aEnd, _bStart, _bEnd;
    int _aHp, _aMaxHp, _aCutscene, _bCutscene;
    bool _bMenu;

    static UWTileMap Map => UWTileMap.current_tilemap;
    static (int, int) Here => (playerdat.playerObject.tileX, playerdat.playerObject.tileY);

    public override Task Setup(ScenarioContext c)
    {
        _tree = DeathCutscene.FindEmptySquare();
        if (_tree.x < 0) { _problem = "no empty open square with four empty open neighbours"; return Task.CompletedTask; }
        var tree = ObjectCreator.spawnObjectInTile(SilverTree, _tree.x, _tree.y, 3, 3, (short)(Map.Tiles[_tree.x, _tree.y].floorHeight << 3));
        if (tree == null) { _problem = "no silver tree could be made"; return Task.CompletedTask; }
        playerdat.SilverTreeDungeon = playerdat.dungeon_level;
        c.Note($"silver tree planted at {_tree} on level {playerdat.dungeon_level}, player at {Here}");
        return Task.CompletedTask;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        // A: room round the tree.
        if (!Teleportation.FindGoodXAndY(playerdat.playerObject, _tree.x, _tree.y, out int ex, out int ey))
        {
            _problem = "find_good_x_and_y found no room round the tree (A)";
            return;
        }
        _expected = (ex, ey);
        DeathCutscene.Forget();
        playerdat.ResurrectAtSilverTree();
        Teleportation.HandleTeleportation();
        _aEnd = Here;
        _aHp = playerdat.play_hp;
        _aMaxHp = playerdat.max_hp;
        _aCutscene = DeathCutscene.Current;
        c.Note($"A: tree at {_tree}, search gives {_expected}; player now at {_aEnd} with {_aHp} of {_aMaxHp} hp, cutscene {_aCutscene:X}");
        playerdat.play_hp = _aMaxHp;
        await c.Frames(2);

        // B: no room even after clearing.
        _bStart = Here;
        var tile = Map.Tiles[_tree.x, _tree.y];
        var oldType = tile.tileType;
        tile.tileType = UWTileMap.TILE_SOLID;
        DeathCutscene.Forget();
        playerdat.ResurrectAtSilverTree();
        Teleportation.HandleTeleportation();
        _bEnd = Here;
        _bCutscene = DeathCutscene.Current;
        tile.tileType = oldType;
        c.Note($"B: tree's square made solid; player now at {_bEnd}, cutscene {_bCutscene:X}");
        _bMenu = await DeathCutscene.StopAndReachMenu(c);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_aMaxHp <= 8) return $"precondition: maximum hit points {_aMaxHp}, at most 8, where the full amount is given";
        var k = new Checks();
        k.True(_aEnd == _expected, $"A: the player expected on {_expected}, the square new_player_pos's search gives round the tree at {_tree}, is at {_aEnd} (do_resurrect and new_player_pos: uw1 SKILLS.C:790-806, UWEDIT.C:466-471)");
        k.Within("A: hit points after coming back at the tree", _aMaxHp - 4, _aMaxHp - 2, _aHp, "avghit - 2 - rand() * 3L / 0x8000L: uw1 SKILLS.C:794");
        k.True(_aCutscene != 0x103, "A: the death cutscene 0x103 started although there was room round the tree (uw1 SKILLS.C:868-873)");
        k.Equal("B: cutscene started when there is no room round the tree", 0x103, _bCutscene, "real_death(1): uw1 SKILLS.C:874-875, UWEDIT.C:406");
        k.True(_bMenu, "B: the game expected back at the main menu after the death cutscene (real_death(1): uw1 UWEDIT.C:416-419)");
        return k.Result;
    }
}

/// <summary>Helpers for the scenarios of a death that ends the game (real_death(1)).</summary>
static class DeathCutscene
{
    /// <summary>Wall-clock limit for the death cutscene to stop. Its waits are real time.</summary>
    const ulong StopMs = 20000;

    static readonly FieldInfo CutsceneNo = typeof(cutsplayer).GetField("currentCutsceneNo", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingFieldException(nameof(cutsplayer), "currentCutsceneNo");

    /// <summary>The number of the cutscene last started (cutsplayer.PlayCutscene sets it at once).</summary>
    public static int Current => (int)CutsceneNo.GetValue(null);

    /// <summary>Clears the record of the cutscene last started, so Current shows whether one starts after this.</summary>
    public static void Forget() => CutsceneNo.SetValue(null, -1);

    /// <summary>
    /// If a cutscene was started, stops it and waits for its callback to reach the main menu, as the runner does with
    /// the intro. Returns whether the game is at the main menu.
    /// </summary>
    public static async Task<bool> StopAndReachMenu(ScenarioContext c)
    {
        if (Current == -1) return false;
        await c.Frames(2);
        cutsplayer.StopCutscene();
        ulong deadline = Time.GetTicksMsec() + StopMs;
        while (cutsplayer.IsPlaying || uimanager.CurrentGameMode != uimanager.GameModes.MAIN)
        {
            if (Time.GetTicksMsec() > deadline) return false;
            await c.Frames(1);
        }
        return true;
    }

    /// <summary>The nearest empty open square at least 2 squares from the player whose four neighbours are empty open squares at
    /// its height.</summary>
    public static (int x, int y) FindEmptySquare()
    {
        var map = UWTileMap.current_tilemap;
        int sx = playerdat.playerObject.tileX, sy = playerdat.playerObject.tileY;
        bool Free(int x, int y, int h) => UWTileMap.ValidTile(x, y) && map.Tiles[x, y].tileType == UWTileMap.TILE_OPEN
            && map.Tiles[x, y].floorHeight == h && map.Tiles[x, y].indexObjectList == 0;
        for (int r = 2; r <= 63; r++)
            for (int y = sy - r; y <= sy + r; y++)
                for (int x = sx - r; x <= sx + r; x++)
                {
                    if (Math.Max(Math.Abs(x - sx), Math.Abs(y - sy)) != r || !UWTileMap.ValidTile(x, y)) continue;
                    int h = map.Tiles[x, y].floorHeight;
                    if (Free(x, y, h) && Free(x + 1, y, h) && Free(x - 1, y, h) && Free(x, y + 1, h) && Free(x, y - 1, h)) return (x, y);
                }
        return (-1, -1);
    }
}
