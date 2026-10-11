using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A critter goes home while time passes. The original's up_crit (uw1/src/critter/CRITTIME.C:132-193,
/// uw2/src/critter/CRITTIME.C:130-193), run for every active critter by
/// update_all_critters_whilst_player_snoozes (sleep, level re-entry; uw1 CRITTIME.C:230-242, uw2
/// CRITTIME.C:232-244): a critter below its average hit points that may heal gets
/// hp = (hp + avghit) / 2 (uw1 :160-161, uw2 :159-160); it turns a random way, rand() % 8, always in
/// UW1 (uw1 :159) and in UW2 unless waiting to talk, goal 10 (uw2 :157-158); then, if its home
/// square (quality, owner) is not the square it stands on, it moves there when it fits
/// (uw1 :172-192, uw2 :171-192).
///
/// The port's ReturnNPCToHomeTile (src/npc/npc.cs:504-558 before this fix) heals hp += avghit / 2 (:526), skips the
/// turn at goal 10 in both games (:517), and moves the critter to the square it already stands on
/// (npc_xhome, npc_yhome are the current square; :545-551), so it never goes home.
///
/// The scenario makes a critter on one open square with its home on another, below its average
/// hit points, and calls npc.ReturnNPCToHomeTile (the port's up_crit); then a second critter
/// standing at home with goal 10, to see whether it draws its turn. The home square has no solid
/// neighbour, and the object chains of it and its neighbours are set aside during the call, so
/// nothing but the rule under test decides the move.
/// </summary>
public sealed class CritterGoesHome : Scenario
{
    public override string Name => "critter-goes-home";
    public override string Cites => "underworld-exhumed uw1/src/critter/CRITTIME.C:132-193; uw2/src/critter/CRITTIME.C:130-193";

    string _problem;
    readonly Checks _k = new();
    readonly HashSet<(int, int)> _used = new();
    readonly List<(int x, int y, int head)> _cleared = new();

    /// <summary>An open square with open neighbours at its height, away from the player and squares already used; clear sets its chains aside.</summary>
    (int x, int y) Find(bool clear)
    {
        var p = playerdat.playerObject;
        for (int x = 3; x < 61; x++)
        for (int y = 3; y < 61; y++)
        {
            var t = UWTileMap.current_tilemap.Tiles[x, y];
            if (t.tileType != UWTileMap.TILE_OPEN) continue;
            if (System.Math.Abs(x - p.tileX) < 4 && System.Math.Abs(y - p.tileY) < 4) continue;
            bool ok = true;
            foreach (var (ux, uy) in _used) if (System.Math.Abs(ux - x) < 3 && System.Math.Abs(uy - y) < 3) ok = false;
            for (int dx = -1; dx <= 1 && ok; dx++)
            for (int dy = -1; dy <= 1 && ok; dy++)
            {
                var n = UWTileMap.current_tilemap.Tiles[x + dx, y + dy];
                if (n.tileType != UWTileMap.TILE_OPEN || n.floorHeight != t.floorHeight) ok = false;
            }
            if (!ok) continue;
            _used.Add((x, y));
            if (clear)
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var n = UWTileMap.current_tilemap.Tiles[x + dx, y + dy];
                    _cleared.Add((x + dx, y + dy, n.indexObjectList));
                    n.indexObjectList = 0;
                }
            return (x, y);
        }
        return (-1, -1);
    }

    /// <summary>Puts the set-aside chains back after whatever is in each square now.</summary>
    void Restore()
    {
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

    static bool InChain(int x, int y, int index)
    {
        var objs = UWTileMap.current_tilemap.LevelObjects;
        for (int i = UWTileMap.current_tilemap.Tiles[x, y].indexObjectList, n = 0; i != 0 && n < 1024; i = objs[i].next, n++)
            if (i == index) return true;
        return false;
    }

    /// <summary>The smallest walking critter with an average of at least 4 hit points.</summary>
    static int Walker()
    {
        int best = -1, bestSize = 999;
        for (int id = 0x40; id < 0x7F; id++)
        {
            if (critterObjectDat.avghit(id) < 4 || critterObjectDat.isFlier(id)) continue;
            int r = commonObjDat.radius(id), h = commonObjDat.height(id);
            if (r == 0 || h == 0 || h >= 0x40) continue;
            if (r * 0x100 + h < bestSize) { bestSize = r * 0x100 + h; best = id; }
        }
        return best;
    }

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    public override Task Act(ScenarioContext c)
    {
        try { Run(c); }
        finally { Restore(); }
        return Task.CompletedTask;
    }

    void Run(ScenarioContext c)
    {
        int item = Walker();
        if (item < 0) { _problem = "no small walking critter"; return; }
        int avghit = critterObjectDat.avghit(item);
        var counts = new int[64];
        string game = c.Game;

        // A critter away from home, below its average hit points.
        {
            var (sx, sy) = Find(false);
            var (hx, hy) = Find(true);
            if (sx < 0 || hx < 0) { _problem = "no squares for the critter and its home"; return; }
            var o = BatchB.SpawnCritter(item, sx, sy);
            if (o == null) { _problem = "could not make a critter"; return; }
            o.quality = (short)hx; o.owner = (short)hy;
            o.npc_goal = 0; o.npc_attitude = 2;
            o.SpawnedCritter_0XD_Bit8 = 0; o.UnkBit_0XD_Bit9_StopHPRegen = 0;
            int hp0 = avghit - 2;   // (hp + avghit) / 2 and hp + avghit / 2 differ for any avghit of 4 or more
            o.npc_hp = (byte)hp0;
            npc.ReturnNPCToHomeTile(o, ref counts);
            string cite = game == "uw1" ? "uw1 CRITTIME.C:172-192" : "uw2 CRITTIME.C:171-192";
            _k.Equal("critter's square x after up_crit", hx, o.npc_xhome, cite);
            _k.Equal("critter's square y after up_crit", hy, o.npc_yhome, cite);
            _k.True(InChain(hx, hy, o.index), $"critter not in its home square's object list ({cite})");
            _k.True(!InChain(sx, sy, o.index), $"critter still in its old square's object list ({cite})");
            _k.Equal("hit points after up_crit", (hp0 + avghit) / 2, o.npc_hp, game == "uw1" ? "uw1 CRITTIME.C:160-161" : "uw2 CRITTIME.C:159-160");
            c.Note($"critter 0x{item:X}, avghit {avghit}, from ({sx}, {sy}) home ({hx}, {hy})");
        }

        // A critter at home waiting to talk (goal 10): UW1 turns it anyway, UW2 does not.
        {
            var (x, y) = Find(false);
            if (x < 0) { _problem = "no square for the waiting critter"; return; }
            var o = BatchB.SpawnCritter(item, x, y);
            if (o == null) { _problem = "could not make the waiting critter"; return; }
            o.quality = (short)x; o.owner = (short)y;
            o.npc_goal = 10; o.npc_attitude = 2; o.npc_hp = (byte)avghit;
            o.SpawnedCritter_0XD_Bit8 = 0;
            if (Rng.Source is not BorlandRandSource src) { _problem = "Rng.Source is not a BorlandRandSource"; return; }
            uint before = src.Seed;
            npc.ReturnNPCToHomeTile(o, ref counts);
            bool drew = ((BorlandRandSource)Rng.Source).Seed != before;
            if (game == "uw1")
                _k.True(drew, "UW1 critter waiting to talk expected to turn a random way, nothing drawn (uw1 CRITTIME.C:159)");
            else
                _k.True(!drew, "UW2 critter waiting to talk expected to keep its heading, a draw was made (uw2 CRITTIME.C:157-158)");
        }
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        return _k.Result;
    }
}
