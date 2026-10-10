using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Guard: walking onto a move trigger whose teleport trap leads to another level takes the player
/// to that level and square.
///
/// The original: when the player's physics put him in a new square, the move triggers among the
/// objects he now touches are set off (UseTrigger with kind 0, uw1/src/motion/PHYSICS.C:240-277);
/// a teleport trap (0x181) then calls do_teleport with its quality, owner and z as square x, y and
/// level (uw1/src/event/TRIGGER.C:212-217, uw2 TRIGGER.C:309-323), which for another level only
/// records the move (uw1/src/event/WORLDEV.C:175-200), and the 3D view's new_player_pos carries it
/// out: ChangeLevel, then the player is set up on the free square nearest the target, the target
/// itself when it is free (uw1/src/game/UWEDIT.C:446-480).
///
/// The scenario finds, among the level's live objects, the move triggers (0x1A0) on open squares
/// whose link is a teleport trap to another level, and walks the player in to the nearest from an
/// open square of the same floor height beside it: moved there with the game's teleport, facing
/// it, holding the run key (W). A walk that does not reach the trigger is followed by the next
/// entry. UW1 level 1 has such stairs (to level 2).
/// </summary>
public sealed class GuardLevelChange : Scenario
{
    const int MoveTrigger = 0x1A0, TeleportTrap = 0x181;
    const ulong WalkMs = 5000;
    const int WalkFrames = 300;   // 5 s at --fixed-fps 60
    static readonly short[] Yaw = { 0, 16384, -32768, -16384 };
    static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { 1, 0, -1, 0 };

    public override string Name => "guard-level-change";
    public override string Cites => "underworld-exhumed uw1/src/motion/PHYSICS.C:240-277; uw1/src/event/TRIGGER.C:212-217; uw1/src/event/WORLDEV.C:175-200; uw1/src/game/UWEDIT.C:446-480; uw2/src/event/TRIGGER.C:309-323";

    string _problem;
    int _level0, _levelAfter, _xAfter, _yAfter, _tries;
    bool _changed;
    (int tx, int ty, int fx, int fy, int dir, int level, int dx, int dy) _used;
    readonly List<(int tx, int ty, int fx, int fy, int dir, int level, int dx, int dy, int dist)> _entries = new();

    public override Task Setup(ScenarioContext c)
    {
        var map = UWTileMap.current_tilemap;
        var p = playerdat.playerObject;
        _level0 = playerdat.dungeon_level;
        for (int y = 1; y < 63; y++)
            for (int x = 1; x < 63; x++)
            {
                if (map.Tiles[x, y].tileType == UWTileMap.TILE_SOLID) continue;
                for (int i = map.Tiles[x, y].indexObjectList, g = 0; i != 0 && g < 1024; g++)
                {
                    var o = map.LevelObjects[i];
                    if (o == null) break;
                    var trap = o.item_id == MoveTrigger && o.link > 0 ? map.LevelObjects[o.link] : null;
                    if (trap != null && trap.item_id == TeleportTrap && trap.zpos != 0 && trap.zpos != _level0)
                    {
                        // Each open square of this one's floor height beside it, to walk in from.
                        for (int d = 0; d < 4; d++)
                        {
                            int fx = x - Dx[d], fy = y - Dy[d];
                            if (!UWTileMap.ValidTile(fx, fy)) continue;
                            var ft = map.Tiles[fx, fy];
                            if (ft.tileType != UWTileMap.TILE_OPEN || ft.floorHeight != map.Tiles[x, y].floorHeight) continue;
                            // Squares holding a trap or trigger of their own go last.
                            _entries.Add((x, y, fx, fy, d, trap.zpos, trap.quality, trap.owner, (x - p.tileX) * (x - p.tileX) + (y - p.tileY) * (y - p.tileY) + (HasTrigger(fx, fy) ? 1 << 20 : 0)));
                        }
                    }
                    i = o.next;
                }
            }
        _entries.Sort((a, b) => a.dist.CompareTo(b.dist));
        if (_entries.Count == 0) _problem = "no move trigger with a teleport trap to another level, with an open square beside it, on this level";
        return Task.CompletedTask;
    }

    static bool HasTrigger(int x, int y)
    {
        var map = UWTileMap.current_tilemap;
        for (int i = map.Tiles[x, y].indexObjectList, g = 0; i != 0 && g < 1024; g++)
        {
            var o = map.LevelObjects[i];
            if (o == null) break;
            if (o.item_id >= 0x180 && o.item_id <= 0x1BF) return true;
            i = o.next;
        }
        return false;
    }

    /// <summary>
    /// Walks in to each entry in turn, nearest first: moved beside the trigger's square with the
    /// game's teleport, facing it, with the run key held for a few seconds. A walk that does not
    /// reach the trigger (a diagonal wall in the way, say) leaves the level as it was, and the next
    /// entry is tried; a walk that sets it off ends the search.
    /// </summary>
    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        foreach (var e in _entries)
        {
            if (_tries >= 6) break;
            _tries++;
            if (!await Guards.TeleportHere(c, e.fx, e.fy)) { c.Note($"the move to ({e.fx}, {e.fy}) did not happen"); continue; }
            if (playerdat.dungeon_level != _level0) { _problem = "the level changed before a walk"; return; }
            playerdat.PlayerCameraYaw_dseg_8294 = Yaw[e.dir];
            await c.Frames(5);
            Guards.Key(Key.W, true);
            ulong deadline = Time.GetTicksMsec() + WalkMs;
            // Bounded by frames, not real time, so a slow host walks just as far.
            for (int f = 0; f < WalkFrames && playerdat.dungeon_level == _level0 && !uimanager.WaitingForFade; f++)
                await c.Frames(1);
            Guards.Key(Key.W, false);
            int stable = 0;
            while (Time.GetTicksMsec() < deadline + 10000 && stable < 20)
            {
                await c.Frames(1);
                stable = (!uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
            }
            c.Note($"walked {"NESW"[e.dir]} from ({e.fx}, {e.fy}) onto the move trigger at ({e.tx}, {e.ty}) (to level {e.level} ({e.dx}, {e.dy})): now level {playerdat.dungeon_level} at ({playerdat.playerObject.tileX}, {playerdat.playerObject.tileY})");
            if (playerdat.dungeon_level != _level0)
            {
                _changed = true;
                _used = (e.tx, e.ty, e.fx, e.fy, e.dir, e.level, e.dx, e.dy);
                break;
            }
        }
        _levelAfter = playerdat.dungeon_level;
        _xAfter = playerdat.playerObject.tileX; _yAfter = playerdat.playerObject.tileY;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        k.True(_changed, $"walking onto a move trigger expected to leave level {_level0}; {_tries} walks did not (PHYSICS.C:270-276, TRIGGER.C:212-217)");
        if (!_changed) return k.Result;
        k.Equal("level after the move trigger", _used.level, _levelAfter, "the teleport trap's z: uw1 TRIGGER.C:215, WORLDEV.C:192-196, UWEDIT.C:459-463");
        k.Equal("square x after the move trigger", _used.dx, _xAfter, "the trap's quality, the free target square: uw1 TRIGGER.C:213, UWEDIT.C:466-474");
        k.Equal("square y after the move trigger", _used.dy, _yAfter, "the trap's owner: uw1 TRIGGER.C:214, UWEDIT.C:466-474");
        return k.Result;
    }
}
