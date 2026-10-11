using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Underworld.Scenarios;

/// <summary>
/// Where a hostile critter lands when it finds the sleeping player. wander_that_monster
/// (uw1/src/critter/CRITTIME.C:303-367, uw2/src/critter/CRITTIME.C:305-369), run by
/// wandering_monster_check through gronk_area while the player sleeps: half the time a hostile
/// critter within sqrt(3) times its range, with a flood_path to the player at least two squares
/// long, is moved to pathsq[pathlen - 2] (uw1 :344-345, uw2 :346-347), two squares short of the
/// player. flood_path's path runs from pathsq[0], the critter's square, to pathsq[pathlen], the
/// target's (uw1 PATHFIND.C:721-723, uw2 PATHFIND.C:865-867), so pathlen - 1 is the square next
/// to the player and pathlen - 2 the one before it.
///
/// The port's sleep.NpcHuntsSleepingPlayer (src/World/sleep.cs) took finalpath[length - 1], the
/// square next to the player. Its StorePath has the same layout as flood_path (finalpath[0] the
/// start, finalpath[length] the target, src/npc/pathfind.cs StorePath).
///
/// The port also passed the heights to the path search unshifted, where the original passes
/// OBJ_Z &gt;&gt; 3 for the critter and the player (uw1 CRITTIME.C:331-332, uw2 CRITTIME.C:333-334),
/// so above floor height 0 no path was found and no critter moved at all. And the path search
/// reads npc.SpecialMotionHandler, which only the critter AI sets, as only AI.C sets the
/// original's tp_act; before any AI had run it was null and the search threw, where the
/// original's zeroed tp_act reads noclimb and w6 as 0 from DS:4 and DS:6. The scenario runs the
/// ambush with the handler the port now starts with, MotionHandler.TpActAtStartUp.
///
/// The scenario finds a straight run of 5 open squares at one floor height (on the first level
/// from the start one that has one), with no solid square beside it, takes the player to one end
/// (Teleportation.Teleport), puts a small hostile walker with the range to reach the player at its
/// far end, makes the 50:50 draw come out "yes", and calls sleep.NpcHuntsSleepingPlayer. The run
/// and the squares beside it are raised to floor height 7 for the test, the player with them, so
/// the heights are not 0, where Z and Z &gt;&gt; 3 agree. Their object chains (but the player's) are
/// set aside, so nothing but the path decides where the critter goes; all of it is put back
/// afterwards. The scenario first runs the port's path search itself as the original calls it,
/// heights as Z &gt;&gt; 3, and requires the straight path, so the expected square is the run's
/// square two short of the player.
/// </summary>
public sealed class SleepAmbush : Scenario
{
    public override string Name => "sleep-ambush-square";
    public override string Cites => "underworld-exhumed uw1/src/critter/CRITTIME.C:303-367, PATHFIND.C:721-723; uw2/src/critter/CRITTIME.C:305-369, PATHFIND.C:865-867";

    string _problem;
    readonly Checks _k = new();
    readonly List<(int x, int y, int head)> _cleared = new();
    readonly List<(int x, int y, short h)> _lowered = new();
    int _playerZ = -1;
    const short Height = 7;
    int _walker = -1, _level, _px, _py, _dx, _dy, _n;

    /// <summary>Always 1 for Next(2), so the 50:50 draw says yes.</summary>
    sealed class Yes : IRandSource
    {
        public int Rand() => 1;
        public int Next(int max) => max > 1 ? 1 : 0;
    }

    void SetAside(int x, int y)
    {
        var p = playerdat.playerObject;
        if (x == p.tileX && y == p.tileY) return;
        foreach (var c in _cleared) if (c.x == x && c.y == y) return;
        var t = UWTileMap.current_tilemap.Tiles[x, y];
        _cleared.Add((x, y, t.indexObjectList));
        t.indexObjectList = 0;
    }

    void Restore()
    {
        foreach (var (x, y, h) in _lowered) UWTileMap.current_tilemap.Tiles[x, y].floorHeight = h;
        _lowered.Clear();
        if (_playerZ >= 0) { playerdat.playerObject.zpos = (short)_playerZ; _playerZ = -1; }
        var objs = UWTileMap.current_tilemap.LevelObjects;
        foreach (var (x, y, head) in _cleared)
        {
            if (head == 0) continue;
            var tile = UWTileMap.current_tilemap.Tiles[x, y];
            if (tile.indexObjectList == 0) { tile.indexObjectList = (short)head; continue; }
            int i = tile.indexObjectList;
            while (objs[i].next != 0) i = objs[i].next;
            objs[i].next = (short)head;
        }
        _cleared.Clear();
    }

    public override async Task Setup(ScenarioContext c)
    {
        // The smallest walker with a range of at least 4, so it fits and the range test passes
        // for a run of 5 squares (range * range * 3 >= distance squared).
        int best = 999;
        for (int id = 0x40; id < 0x7F; id++)
        {
            if (critterObjectDat.avghit(id) <= 0 || critterObjectDat.isFlier(id)) continue;
            if (critterObjectDat.TravelRange(id) < 4) continue;
            int r = commonObjDat.radius(id), h = commonObjDat.height(id);
            if (r == 0 || h == 0 || h >= 0x40) continue;
            int size = r * 0x100 + h;
            if (size < best) { best = size; _walker = id; }
        }
        if (_walker < 0) { _problem = "no small walking critter with a range of 4"; return; }

        // The first level from the start one with a straight run of 5 open squares; the player is
        // taken to one end of it.
        int start = playerdat.dungeon_level;
        for (int level = start; level < start + 5 && _n == 0; level++)
        {
            if (level != playerdat.dungeon_level && !await TeleportTo(c, playerdat.playerObject.tileX, playerdat.playerObject.tileY, level))
                continue;
            for (int x = 1; x < 63 && _n == 0; x++)
            for (int y = 1; y < 63 && _n == 0; y++)
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (Run(x, y, dx, dy, 5)) { _level = level; _px = x; _py = y; _dx = dx; _dy = dy; _n = 5; break; }
        }
        if (_n == 0) { _problem = $"no straight open run of 5 squares on levels {start} to {start + 4}"; return; }
        if (!await TeleportTo(c, _px, _py, _level)) _problem = $"the teleport to ({_px}, {_py}) on level {_level} did not happen";
    }

    static async Task<bool> TeleportTo(ScenarioContext c, int x, int y, int level)
    {
        var p = playerdat.playerObject;
        if (playerdat.dungeon_level == level && p.tileX == x && p.tileY == y) return true;
        Teleportation.Teleport(character: 0, tileX: x, tileY: y, newLevel: level == playerdat.dungeon_level ? 0 : level, HeadingHeightFlag: 0);
        ulong deadline = Time.GetTicksMsec() + 20000;
        int stable = 0;
        while (Time.GetTicksMsec() < deadline && stable < 10)
        {
            await c.Frames(1);
            bool there = playerdat.dungeon_level == level && playerdat.playerObject.tileX == x && playerdat.playerObject.tileY == y;
            stable = (there && !uimanager.WaitingForFade && uimanager.InGame) ? stable + 1 : 0;
        }
        return stable >= 10;
    }

    /// <summary>A straight run of n open squares from (px, py), all at its floor height, with no solid square beside it.</summary>
    static bool Run(int px, int py, int dx, int dy, int n)
    {
        int h = UWTileMap.current_tilemap.Tiles[px, py].floorHeight;
        for (int i = 0; i <= n; i++)
        {
            int x = px + dx * i, y = py + dy * i;
            for (int ex = -1; ex <= 1; ex++)
            for (int ey = -1; ey <= 1; ey++)
            {
                int tx = x + ex, ty = y + ey;
                if (tx < 1 || ty < 1 || tx > 62 || ty > 62) return false;
                var t = UWTileMap.current_tilemap.Tiles[tx, ty];
                if (t.tileType == UWTileMap.TILE_SOLID) return false;
                if (ex == 0 && ey == 0 && (t.tileType != UWTileMap.TILE_OPEN || t.floorHeight != h)) return false;
            }
        }
        return true;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var previous = Rng.Source;
        var handler = npc.SpecialMotionHandler;
        try { Go(c); }
        finally { Restore(); Rng.Source = previous; npc.SpecialMotionHandler = handler; }
        return Task.CompletedTask;
    }

    void Go(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        int dx = _dx, dy = _dy, n = _n;
        if (p.tileX != _px || p.tileY != _py) { _problem = $"player at ({p.tileX}, {p.tileY}), not ({_px}, {_py})"; return; }

        // The run and the squares beside it go to floor height 7 for the test, and the player with
        // them: above 0, so Z and Z >> 3 differ (see the summary).
        for (int i = 0; i <= n; i++)
            for (int ex = -1; ex <= 1; ex++)
                for (int ey = -1; ey <= 1; ey++)
                {
                    int x = p.tileX + dx * i + ex, y = p.tileY + dy * i + ey;
                    SetAside(x, y);
                    var t = UWTileMap.current_tilemap.Tiles[x, y];
                    _lowered.Add((x, y, t.floorHeight));
                    t.floorHeight = Height;
                }
        _playerZ = p.zpos;
        p.zpos = Height << 3;

        int sx = p.tileX + dx * n, sy = p.tileY + dy * n;
        var o = BatchB.SpawnCritter(_walker, sx, sy);
        if (o == null) { _problem = "could not make the critter"; return; }
        o.npc_attitude = 0;
        o.npc_goal = 0;

        // The motion handler the path search reads is the one the critter AI last chose; here the one
        // a fresh process starts with, before any AI has run (the original's tp_act, zeroed _BSS, reads
        // its fields at near null; uw2 PATHFIND.C:77, uw1 PATHFIND.C:83). The Setup's frames ran the AI.
        npc.SpecialMotionHandler = MotionHandler.TpActAtStartUp;

        // The path as the original asks for it (heights Z >> 3, uw1 CRITTIME.C:331-332, uw2 CRITTIME.C:333-334): it must be the straight run.
        if (!Pathfind.PathFindBetweenTiles(critter: o, currTileX_arg0: sx, currTileY_arg2: sy, CurrFloorHeight_arg4: o.zpos >> 3,
                TargetTileX_arg6: p.tileX, TargetTileY_arg8: p.tileY, TargetFloorHeight_argA: p.zpos >> 3, LikelyRangeArgC: 0))
        { _problem = $"no path from ({sx}, {sy}) to the player at ({p.tileX}, {p.tileY})"; return; }
        int len2 = Pathfind.MaybePathIndexOrLength_dseg_67d6_225A;
        var path = new List<(int, int)>();
        for (int i = 0; i <= len2; i++) path.Add((FinalPath56.finalpath[i].X0, FinalPath56.finalpath[i].Y1));
        c.Note($"level {_level}, walker 0x{_walker:X}, player ({p.tileX}, {p.tileY}), critter ({sx}, {sy}), path length {len2}: {string.Join(" ", path)}");
        if (len2 != n) { _problem = $"path length {len2}, expected the straight run's {n}"; return; }
        for (int i = 0; i <= n; i++)
            if (path[i] != (sx - dx * i, sy - dy * i)) { _problem = $"path square {i} is {path[i]}, not on the straight run"; return; }

        Rng.Source = new Yes();
        var tile = UWTileMap.current_tilemap.Tiles[sx, sy];
        bool moved = sleep.NpcHuntsSleepingPlayer(sx, sy, o, tile, 0);

        // pathsq[pathlen - 2]: two squares short of the player along the run.
        int ex2 = p.tileX + dx * 2, ey2 = p.tileY + dy * 2;
        string cite = c.Game == "uw1" ? "uw1 CRITTIME.C:344-345, PATHFIND.C:721-723" : "uw2 CRITTIME.C:346-347, PATHFIND.C:865-867";
        string pathCite = c.Game == "uw1" ? "uw1 CRITTIME.C:331-332" : "uw2 CRITTIME.C:333-334";
        _k.True(moved, $"hostile critter in range expected to be moved to the sleeping player, path heights Z >> 3 ({pathCite})");
        _k.Equal("ambushing critter's square x (two short of the player)", ex2, o.npc_xhome, cite);
        _k.Equal("ambushing critter's square y (two short of the player)", ey2, o.npc_yhome, cite);
        _k.Equal("ambushing critter's tile x", ex2, o.tileX, cite);
        _k.Equal("ambushing critter's tile y", ey2, o.tileY, cite);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        return _k.Result;
    }
}
