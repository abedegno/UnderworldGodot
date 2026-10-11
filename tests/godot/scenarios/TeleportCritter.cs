using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Moving a critter to a square (UW2). The original's teleport_critter
/// (uw2/src/critter/CRITTIME.C:374-410), used by conversations, SCD events and the castle
/// schedule: a critter already on (x, y) stays as it is and the call succeeds (:384-385); a flier
/// goes half way up, z = ((height &lt;&lt; 3) + 0x80) &gt;&gt; 1, anything else on the floor, height &lt;&lt; 3
/// (:387-390); the fine position comes from set_gridx_and_y_based_on_tile_type
/// (CRITTIME.C:93-122): 4, 4 on an open or sloped square, the open corner of a diagonal (type 2
/// 6, 1; 3 1, 1; 4 6, 6; 5 6, 1), and a solid square fails (:391-392); can_place must say the
/// critter fits there, else it fails (:393-394); and it fails if it cannot be taken out of its old
/// square (:395-396). UW1 has no teleport_critter (uw1 CRITTIME.C:22).
///
/// The port's npc.moveNPCToTile (src/npc/npcutil.cs:132-167 before this fix) refuses only a solid square, always
/// puts the critter at fine 3, 3 on the floor, and has no fit test and no early return.
///
/// The scenario makes critters on level 1 away from the player and calls npc.moveNPCToTile to:
/// a diagonal square of each type (an open square in the clear made diagonal for the test, as
/// the castle has almost none), an open square with another critter
/// standing in its middle (refused, nothing changes), an open square for a flier, the square the
/// critter is already on (succeeds, fine position and height unchanged), and a solid square.
/// Type 5's 6, 1 lies in the wall half of that diagonal, so whether it fits rests on can_place:
/// the scenario only checks the move is whole either way (placed at 6, 1, or refused and unchanged).
/// A slope is not tested: it takes the default 4, 4 like the open squares here, and whether the
/// original's can_place accepts a critter on the floor of a slope is not settled by this file.
/// No destination has a solid neighbour, and the object chains of each destination and its eight
/// neighbours are set aside during the test, so nothing but the tested cause can refuse a placement.
/// </summary>
public sealed class TeleportCritter : Scenario
{
    public override string Name => "teleport-critter";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/critter/CRITTIME.C:93-122, 374-410";

    string _problem;
    readonly Checks _k = new();
    int _walker = -1, _flier = -1;
    readonly HashSet<(int, int)> _used = new();
    readonly List<(int x, int y, int head)> _cleared = new();

    /// <summary>
    /// A square of type t (with flat open neighbours if asked) with no solid neighbour, away from
    /// the player and from squares already used. The castle is too full for a clear square, so the
    /// object chains of the square and its eight neighbours are set aside (Restore puts them back),
    /// and nothing but the tested cause can refuse a placement.
    /// </summary>
    (int x, int y) Find(int type, bool needFlatOpenNeighbours = false)
    {
        var p = playerdat.playerObject;
        for (int x = 3; x < 61; x++)
        for (int y = 3; y < 61; y++)
        {
            var t = UWTileMap.current_tilemap.Tiles[x, y];
            if (t.tileType != type) continue;
            if (System.Math.Abs(x - p.tileX) < 4 && System.Math.Abs(y - p.tileY) < 4) continue;
            bool ok = true;
            foreach (var (ux, uy) in _used) if (System.Math.Abs(ux - x) < 3 && System.Math.Abs(uy - y) < 3) ok = false;
            for (int dx = -1; dx <= 1 && ok; dx++)
            for (int dy = -1; dy <= 1 && ok; dy++)
            {
                var n = UWTileMap.current_tilemap.Tiles[x + dx, y + dy];
                if (n.tileType == UWTileMap.TILE_SOLID) ok = false;
                if (needFlatOpenNeighbours && (n.tileType != UWTileMap.TILE_OPEN || n.floorHeight != t.floorHeight)) ok = false;
                if (n.indexObjectList == 1 || (p.tileX == x + dx && p.tileY == y + dy)) ok = false;
            }
            if (!ok) continue;
            _used.Add((x, y));
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

    /// <summary>Puts the set-aside object chains back, after whatever the scenario added to each square.</summary>
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

    /// <summary>A critter on an open square of its own.</summary>
    uwObject Spawn(int item)
    {
        var (x, y) = Find(UWTileMap.TILE_OPEN);
        if (x < 0) return null;
        var o = BatchB.SpawnCritter(item, x, y);
        if (o == null) return null;
        o.quality = (short)x; o.owner = (short)y;
        o.npc_goal = 0; o.npc_attitude = 2;
        return o;
    }

    public override Task Setup(ScenarioContext c)
    {
        // The smallest critters that walk and fly, so a placement fails only for the tested cause.
        int bestW = 999, bestF = 999;
        for (int id = 0x40; id < 0x7F; id++)
        {
            if (critterObjectDat.avghit(id) <= 0) continue;
            int r = commonObjDat.radius(id), h = commonObjDat.height(id);
            if (r == 0 || h == 0 || h >= 0x40) continue;
            int size = r * 0x100 + h;
            if (critterObjectDat.isFlier(id)) { if (size < bestF) { bestF = size; _flier = id; } }
            else if (size < bestW) { bestW = size; _walker = id; }
        }
        if (_walker < 0 || _flier < 0) _problem = $"no small walking ({_walker}) or flying ({_flier}) critter";
        return Task.CompletedTask;
    }

    void Moved(string what, uwObject o, int x, int y, bool result, int fx, int fy, int z, string cite)
    {
        _k.True(result, $"{what}: move to ({x}, {y}) expected to succeed, it failed ({cite})");
        _k.Equal($"{what}: square x", x, o.npc_xhome, cite);
        _k.Equal($"{what}: square y", y, o.npc_yhome, cite);
        _k.Equal($"{what}: fine x", fx, o.xpos, cite);
        _k.Equal($"{what}: fine y", fy, o.ypos, cite);
        _k.Equal($"{what}: z", z, o.zpos, cite);
        var objs = UWTileMap.current_tilemap.LevelObjects;
        bool listed = false;
        for (int i = UWTileMap.current_tilemap.Tiles[x, y].indexObjectList, n = 0; i != 0 && n < 1024; i = objs[i].next, n++)
            if (i == o.index) listed = true;
        _k.True(listed, $"{what}: critter not in the object list of ({x}, {y}) ({cite})");
    }

    void Unmoved(string what, uwObject o, int x0, int y0, int fx0, int fy0, int z0, bool result, string cite)
    {
        _k.True(!result, $"{what}: move expected to fail, it succeeded ({cite})");
        _k.Equal($"{what}: square x", x0, o.npc_xhome, cite);
        _k.Equal($"{what}: square y", y0, o.npc_yhome, cite);
        _k.Equal($"{what}: fine x", fx0, o.xpos, cite);
        _k.Equal($"{what}: fine y", fy0, o.ypos, cite);
        _k.Equal($"{what}: z", z0, o.zpos, cite);
    }

    public override Task Act(ScenarioContext c)
    {
        try { Run(c); }
        finally { Restore(); }
        return Task.CompletedTask;
    }

    void Run(ScenarioContext c)
    {
        if (_problem != null) return;
        int tested = 0;

        // Diagonals: the open corner, CRITTIME.C:97-117. Type 5 gets 6, 1 like type 2.
        var corner = new Dictionary<int, (int, int)> { [2] = (6, 1), [3] = (1, 1), [4] = (6, 6), [5] = (6, 1) };
        foreach (var (type, (fx, fy)) in corner)
        {
            // Castle levels have almost no diagonals, so an open square in the clear is made one
            // for the test and put back afterwards.
            var (x, y) = Find(UWTileMap.TILE_OPEN, true);
            var o = Spawn(_walker);
            if (x < 0 || o == null) { _problem = $"no square or critter for diagonal type {type}"; return; }
            var tile = UWTileMap.current_tilemap.Tiles[x, y];
            tile.tileType = (short)type;
            int x0 = o.npc_xhome, y0 = o.npc_yhome, fx0 = o.xpos, fy0 = o.ypos, z0 = o.zpos;
            bool r = npc.moveNPCToTile(o, x, y);
            if (type != UWTileMap.TILE_DIAG_NW)
                Moved($"diagonal type {type}", o, x, y, r, fx, fy, tile.floorHeight << 3, "uw2 CRITTIME.C:93-122, 387-402");
            else if (r)// 6, 1 is in the closed half of this diagonal, so can_place decides; either way it must be whole
                Moved($"diagonal type {type}", o, x, y, r, fx, fy, tile.floorHeight << 3, "uw2 CRITTIME.C:93-122, 387-402");
            else
                Unmoved($"diagonal type {type}, refused", o, x0, y0, fx0, fy0, z0, r, "uw2 CRITTIME.C:393-394");
            tile.tileType = UWTileMap.TILE_OPEN;
            tested++;
        }
        if (tested == 0) { _problem = "no clear diagonal square on this level"; return; }

        // A flier: half way up, CRITTIME.C:387-390.
        {
            var (x, y) = Find(UWTileMap.TILE_OPEN, true);
            var o = Spawn(_flier);
            if (x < 0 || o == null) { _problem = $"no square ({x}, {y}) or critter ({o != null}) for the flier"; return; }
            int h = UWTileMap.current_tilemap.Tiles[x, y].floorHeight;
            int z = ((h << 3) + 0x80) >> 1;
            if (z + commonObjDat.height(_flier) > 0x7F) { _problem = $"flier would not fit under the original's 0x7F limit at height {h}"; return; }
            bool r = npc.moveNPCToTile(o, x, y);
            Moved("flier", o, x, y, r, 4, 4, z, "uw2 CRITTIME.C:387-402");
        }

        // A square with another critter standing in its middle: can_place refuses, CRITTIME.C:393-394.
        uwObject spare;
        {
            var (x, y) = Find(UWTileMap.TILE_OPEN, true);
            var o = Spawn(_walker);
            if (x < 0 || o == null) { _problem = "no square or critter for the blocked case"; return; }
            var blocker = BatchB.SpawnCritter(_walker, x, y);
            if (blocker == null) { _problem = "could not make the blocking critter"; return; }
            blocker.xpos = 4; blocker.ypos = 4;
            int x0 = o.npc_xhome, y0 = o.npc_yhome, fx0 = o.xpos, fy0 = o.ypos, z0 = o.zpos;
            bool r = npc.moveNPCToTile(o, x, y);
            Unmoved("square taken by another critter", o, x0, y0, fx0, fy0, z0, r, "uw2 CRITTIME.C:393-394");
            spare = o;
        }

        // The critter the blocked square refused, still on its own square, serves for the last two.
        // A solid square: refused, CRITTIME.C:95-96, 391-392.
        {
            var o = spare;
            int sx = -1, sy = -1;
            for (int x = 1; x < 63 && sx < 0; x++)
            for (int y = 1; y < 63 && sx < 0; y++)
                if (UWTileMap.current_tilemap.Tiles[x, y].tileType == UWTileMap.TILE_SOLID) { sx = x; sy = y; }
            if (sx < 0) { _problem = "no solid square"; return; }
            int x0 = o.npc_xhome, y0 = o.npc_yhome, fx0 = o.xpos, fy0 = o.ypos, z0 = o.zpos;
            bool r = npc.moveNPCToTile(o, sx, sy);
            Unmoved("solid square", o, x0, y0, fx0, fy0, z0, r, "uw2 CRITTIME.C:95-96, 391-392");
        }

        // Already on the square: succeeds and nothing changes, CRITTIME.C:384-385.
        {
            var o = spare;
            o.xpos = 2; o.ypos = 5; o.zpos = (short)(o.zpos + 8);
            int x0 = o.npc_xhome, y0 = o.npc_yhome, z0 = o.zpos;
            bool r = npc.moveNPCToTile(o, x0, y0);
            _k.True(r, "already on the square: expected to succeed (uw2 CRITTIME.C:384-385)");
            _k.Equal("already on the square: fine x", 2, o.xpos, "uw2 CRITTIME.C:384-385");
            _k.Equal("already on the square: fine y", 5, o.ypos, "uw2 CRITTIME.C:384-385");
            _k.Equal("already on the square: z", z0, o.zpos, "uw2 CRITTIME.C:384-385");
        }
        c.Note($"walker 0x{_walker:X}, flier 0x{_flier:X}, {tested} diagonal types");
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        return _k.Result;
    }
}
