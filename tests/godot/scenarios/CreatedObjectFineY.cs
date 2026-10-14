using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Where a created object sits inside its square. The original's creat_spell works out a point x, y
/// in fine units, 9 ahead of the caster (12 for Summon Monster in UW1) along its heading plus
/// rand() % 27 - 13, puts the object on square x >> 3, y >> 3, and sets its fine x from x and its
/// fine y from y: SET_FINEX_UNSIGNED(obj, x & 7), SET_FINEY(obj, y), which keeps y & 7
/// (uw1/src/combat/SPELLS.C:586-589, 617-618; uw2/src/combat/SPELLS2.C:487-491, 526-527; SET_FINEY in
/// uw1/src/include/object.h:231, uw2/src/include/object.h:207).
///
/// The scenario faces the player along a heading with open floor ahead, at fine x 1 and fine y 6 in
/// its square, and casts Create Food (class 8 minor 1) through SpellCasting.CastClass8_Summoning for a
/// few seeds. Each food item that appears must sit at a point (square * 8 + fine) that creat_spell
/// could have reached with one of the 27 headings.
///
/// The port once set the fine y from x (src/magic/spellcasting_class_8.cs).
/// </summary>
public sealed class CreatedObjectFineY : Scenario
{
    const int FirstFood = 0xB0;   // FIRST_FOOD, items 0xB0..0xB6 (uw1 SPELLS.C:603, uw2 SPELLS2.C:506; uw1/src/include/items.h:130)
    const int Distance = 9;
    static readonly uint[] Seeds = { 3, 31, 314, 2718, 4242 };

    public override string Name => "created-object-fine-y";
    public override string Cites => "underworld-exhumed uw1/src/combat/SPELLS.C:586-589, 617-618 (creat_spell); uw2/src/combat/SPELLS2.C:487-491, 526-527";

    string _problem;
    int _heading;
    readonly List<(int x, int y, int fx, int fy, bool reachable)> _made = new();

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        p.xpos = 1;
        p.ypos = 6;
        int x0 = (p.npc_xhome << 3) + p.xpos, y0 = (p.npc_yhome << 3) + p.ypos;
        for (int h = 1; h < 8; h++)   // heading 0 would take the turn below zero
        {
            bool open = true;
            foreach (int turn in new[] { -13, 0, 13 })
            {
                int x = x0, y = y0;
                motion.GetCoordinateInDirection((h << 5) + turn, Distance, ref x, ref y);
                int tx = x >> 3, ty = y >> 3;
                if (!UWTileMap.ValidTile(tx, ty) || UWTileMap.current_tilemap.Tiles[tx, ty].tileType != UWTileMap.TILE_OPEN) open = false;
            }
            if (open) { _heading = h; return Task.CompletedTask; }
        }
        _problem = "no heading from the start position faces open floor";
        return Task.CompletedTask;
    }

    static HashSet<int> Food()
    {
        var set = new HashSet<int>();
        var map = UWTileMap.current_tilemap;
        for (int x = 0; x < 64; x++)
            for (int y = 0; y < 64; y++)
                for (int i = map.Tiles[x, y].indexObjectList, guard = 0; i != 0 && guard < 1024; guard++)
                {
                    var o = map.LevelObjects[i];
                    if (o == null) break;
                    if (o.item_id >= FirstFood && o.item_id <= FirstFood + 6) set.Add(i);
                    i = o.next;
                }
        return set;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        var p = playerdat.playerObject;
        foreach (uint seed in Seeds)
        {
            p.heading = (short)_heading;   // the camera can turn the player between frames
            p.npc_heading = 0;
            p.xpos = 1;
            p.ypos = 6;
            int x0 = (p.npc_xhome << 3) + p.xpos, y0 = (p.npc_yhome << 3) + p.ypos;
            var reach = new HashSet<(int, int)>();
            for (int turn = -13; turn <= 13; turn++)   // rand() % 0x1B - 0xD
            {
                int x = x0, y = y0;
                motion.GetCoordinateInDirection(((_heading << 5) + turn) % 0xFF, Distance, ref x, ref y);
                reach.Add((x, y));
            }
            var before = Food();
            BatchB.Seed(seed);
            SpellCasting.CastClass8_Summoning(1, p);
            foreach (int i in Food().Except(before))
            {
                var o = UWTileMap.current_tilemap.LevelObjects[i];
                int px = (o.tileX << 3) + o.xpos, py = (o.tileY << 3) + o.ypos;
                _made.Add((o.tileX, o.tileY, o.xpos, o.ypos, reach.Contains((px, py))));
                c.Note($"seed {seed}: item 0x{o.item_id:X} on ({o.tileX},{o.tileY}) at fine ({o.xpos},{o.ypos}); reachable points {string.Join(" ", reach.Select(r => $"({r.Item1},{r.Item2})"))}");
            }
        }
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_made.Count == 0) return $"precondition: {Seeds.Length} casts of Create Food made nothing";
        string cite = c.Game == "uw1" ? "uw1 SPELLS.C:617-618, SET_FINEY(obj, y)" : "uw2 SPELLS2.C:526-527, SET_FINEY(obj, y)";
        var k = new Checks();
        foreach (var m in _made)
            k.True(m.reachable, $"food on ({m.x},{m.y}) at fine ({m.fx},{m.fy}) is not at a point creat_spell reaches ({cite})");
        return k.Result;
    }
}
