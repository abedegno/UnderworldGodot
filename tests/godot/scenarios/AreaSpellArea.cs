using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Which squares and objects an area effect visits. The original's gronk_area centres the area
/// dist squares ahead and passes process_area the corner radius squares before the centre with
/// w = h = radius * 2 + 1 (uw1/src/combat/SPELLS.C:487-512, uw2/src/combat/SPELLS.C:674-699).
/// process_area (uw1 SPELLS.C:416-483, uw2 SPELLS.C:602-670):
/// - clips the area to the map, x with x0 + w &gt;= 64 and y with y0 + h &gt; 64 (uw1 SPELLS.C:439-446);
/// - visits x0..x0 + w and y0..y0 + h;
/// - for type 0x40, calls fn on an open square when rand() % (w * h + 3) &lt; count, ending when count
///   reaches 0, and makes up to five passes while count is left (uw1 SPELLS.C:463-468, 482);
/// - for type 0x80, calls fn on every object in UW1 (uw1 SPELLS.C:471-473), and in UW2 first on
///   every open square, drawing rand() all the same, then on every object (uw2 SPELLS.C:649-663);
/// - for type 0 calls fn on every critter but the source, for 0xC0 on every object; an object call
///   ends the area when count drops to 0 or below (uw1 SPELLS.C:476, uw2 SPELLS.C:663).
///
/// The scenario calls the port's CallBacks.RunCodeOnTargetsAroundObject and RunCodeOnTargetsInArea
/// directly with a recording callback, and compares the visits, in order, with a model of the
/// original run on the same map and the same rand() seed. The model skips animation objects
/// (major class 7) as the port deliberately does (src/utility/CallBacks.cs), which the original does not.
/// Cases:
/// - radius-2 objects (0xC0), with a critter on the row 3 after the centre, inside the area, and one
///   on the row 4 after, outside it: the area's height (finding 1);
/// - random squares (0x40) with count 0x7F and a callback that never succeeds: every open square,
///   five passes; and with count 3 and a callback that succeeds: the random test (finding 2);
/// - type 0x80 on a small patch: squares as well only in UW2 (finding 3);
/// - type 0 with count 0 and a callback that succeeds: only the first critter;
/// - random squares on an area reaching the bottom edge, y0 + h = 64 and 66: the y clip (finding 4).
///   The port's old bound (&gt;= 64) gives the same result, because at y0 + h = 64 the clip subtracts 0,
///   so this case checks the clip but cannot tell the two bounds apart.
///
/// Both games.
/// </summary>
public sealed class AreaSpellArea : Scenario
{
    public override string Name => "gronk-area";
    public override string Cites => "underworld-exhumed uw1/src/combat/SPELLS.C:416-483 (process_area), 487-512 (gronk_area); uw2/src/combat/SPELLS.C:602-670, 674-699";

    string _problem;
    int _cx, _cy;
    readonly List<string> _fails = new();
    readonly List<string> _notes = new();

    sealed class Case
    {
        public string What;
        public bool AroundPlayer;   // through RunCodeOnTargetsAroundObject (player, dist 0, radius R)
        public int R;
        public int X0, Y0, W, H;    // else RunCodeOnTargetsInArea with these
        public int Count, Src, Type;
        public bool Succeeds;
        public uint Seed;
        public int MinModelVisits = 1;
        public string Cite;
    }

    readonly List<Case> _cases = new();

    public override Task Setup(ScenarioContext c)
    {
        var p = playerdat.playerObject;
        _cx = p.npc_xhome; _cy = p.npc_yhome;
        // the critters go up to 4 rows after the player and one column on; areas clip at the top and sides
        if (_cx < 2 || _cx > 61 || _cy > 59) { _problem = $"the player's square ({_cx},{_cy}) is too near the map's edge"; return Task.CompletedTask; }
        int id = BatchB.FindCritter(i => true);
        if (id < 0) { _problem = "no critter type"; return Task.CompletedTask; }
        var inside = BatchB.SpawnCritter(id, _cx, _cy + 2 + 1);
        var outside = BatchB.SpawnCritter(id, _cx, _cy + 2 + 2);
        var second = BatchB.SpawnCritter(id, _cx + 1, _cy + 2 + 1);
        if (inside == null || outside == null || second == null) { _problem = "the critters could not be made"; return Task.CompletedTask; }

        _cases.Add(new Case { What = "every object within radius 2 (0xC0)", AroundPlayer = true, R = 2, Count = 0x7F, Src = 1, Type = 0xC0, Succeeds = false, Seed = 1,
            MinModelVisits = 2, Cite = "uw1 SPELLS.C:509, uw2 SPELLS.C:696 (h = radius * 2 + 1)" });
        _cases.Add(new Case { What = "random squares within radius 2, count 0x7F, none succeed (0x40)", AroundPlayer = true, R = 2, Count = 0x7F, Src = 1, Type = 0x40, Succeeds = false, Seed = 2,
            MinModelVisits = 6, Cite = "uw1 SPELLS.C:465, 482; uw2 SPELLS.C:651, 669" });
        _cases.Add(new Case { What = "random squares within radius 3, count 3, all succeed (0x40)", AroundPlayer = true, R = 3, Count = 3, Src = 1, Type = 0x40, Succeeds = true, Seed = 3,
            Cite = "uw1 SPELLS.C:465-466, uw2 SPELLS.C:651-652" });
        _cases.Add(new Case { What = "squares and objects of a 3 by 3 patch (0x80)", X0 = _cx - 1, Y0 = _cy + 1, W = 2, H = 2, Count = 0x7F, Src = 0, Type = 0x80, Succeeds = false, Seed = 4,
            MinModelVisits = 2, Cite = "uw1 SPELLS.C:463-476, uw2 SPELLS.C:649-663" });
        _cases.Add(new Case { What = "critters, count 0, the first succeeds (0)", X0 = _cx - 2, Y0 = _cy - 2, W = 5, H = 5, Count = 0, Src = 1, Type = 0, Succeeds = true, Seed = 5,
            Cite = "uw1 SPELLS.C:476, uw2 SPELLS.C:663 (--count <= 0)" });
        _cases.Add(new Case { What = "random squares to the bottom edge, y0 + h = 64 (0x40)", X0 = 1, Y0 = 60, W = 62, H = 4, Count = 0x7F, Src = 0, Type = 0x40, Succeeds = false, Seed = 6,
            MinModelVisits = 0, Cite = "uw1 SPELLS.C:444, uw2 SPELLS.C:630" });
        _cases.Add(new Case { What = "random squares past the bottom edge, y0 + h = 66 (0x40)", X0 = 1, Y0 = 61, W = 62, H = 5, Count = 0x7F, Src = 0, Type = 0x40, Succeeds = false, Seed = 7,
            MinModelVisits = 0, Cite = "uw1 SPELLS.C:444-445, uw2 SPELLS.C:630-631" });
        return Task.CompletedTask;
    }

    static string Square(int x, int y) => $"({x},{y})";
    static string Object(int x, int y, uwObject o) => $"({x},{y})#{o.index}";

    /// <summary>process_area, uw1/src/combat/SPELLS.C:416-483, uw2/src/combat/SPELLS.C:602-670.</summary>
    static List<string> Model(bool uw2, int count, int src, int type, int x0, int y0, int w, int h, bool succeeds, BatchB.DosRand r)
    {
        var v = new List<string>();
        var map = UWTileMap.current_tilemap;
        count = (sbyte)count;   // char count
        if (x0 >= 64 || x0 + w < 0 || y0 >= 64 || y0 + h < 0) return v;
        if (x0 < 0) { w -= -x0; x0 = 0; } else if (x0 + w >= 64) w -= x0 + w - 64;
        if (y0 < 0) { h -= -y0; y0 = 0; } else if (y0 + h > 64) h -= y0 + h - 64;
        if (w <= 0 || h <= 0) return v;
        int tries = 0;
        do
        {
            for (int x = x0; x <= x0 + w; x++)
                for (int y = y0; y <= y0 + h; y++)
                {
                    if (x < 0 || x >= 64 || y < 0 || y >= 64) continue;
                    var tile = map.Tiles[x, y];
                    if (type == 0x40 || (uw2 && type == 0x80))
                    {
                        if (tile.tileType > 0 && (r.Rand() % (w * h + 3) < count || type == 0x80))
                        {
                            v.Add(Square(x, y));
                            if (succeeds && --count == 0) return v;
                        }
                    }
                    if (type == 0x40) continue;
                    for (int i = tile.indexObjectList; i != 0; )
                    {
                        var o = map.LevelObjects[i];
                        i = o.next;
                        if (o.majorclass == 7) continue;   // the port's own choice, kept in the model
                        if (type == 0x80 || (type == 0 && o.majorclass == 1 && o.index != src) || type == 0xC0)
                        {
                            v.Add(Object(x, y, o));
                            if (succeeds && --count <= 0) return v;
                        }
                    }
                }
        } while (type == 0x40 && count > 0 && tries++ < 4);
        return v;
    }

    public override Task Act(ScenarioContext c)
    {
        if (_problem != null) return Task.CompletedTask;
        bool uw2 = c.Game == "uw2";
        var saved = Rng.Source;
        try
        {
            foreach (var k in _cases)
            {
                int x0 = k.AroundPlayer ? _cx - k.R : k.X0, y0 = k.AroundPlayer ? _cy - k.R : k.Y0;
                int w = k.AroundPlayer ? k.R * 2 + 1 : k.W, h = k.AroundPlayer ? k.R * 2 + 1 : k.H;
                var expected = Model(uw2, k.Count, k.Src, k.Type, x0, y0, w, h, k.Succeeds, new BatchB.DosRand(k.Seed));
                var actual = new List<string>();
                CallBacks.AreaEffectCallBack record = (x, y, obj, tile, srcIndex) =>
                {
                    actual.Add(obj == null ? Square(x, y) : Object(x, y, obj));
                    return k.Succeeds;
                };
                Rng.Source = new BorlandRandSource(k.Seed);
                if (k.AroundPlayer)
                    CallBacks.RunCodeOnTargetsAroundObject(record, 1, k.Count, k.Type, 0, k.R);
                else
                    CallBacks.RunCodeOnTargetsInArea(record, k.Count, k.Src, k.Type, k.X0, k.Y0, k.W, k.H);
                _notes.Add($"{k.What}: DOS {expected.Count} visits, port {actual.Count}");
                if (expected.Count < k.MinModelVisits)
                {
                    _problem = $"{k.What}: the model visits only {expected.Count}, too few for a test";
                    return Task.CompletedTask;
                }
                int first = 0;
                while (first < expected.Count && first < actual.Count && expected[first] == actual[first]) first++;
                if (first < expected.Count || first < actual.Count)
                {
                    string e = first < expected.Count ? expected[first] : "end";
                    string a = first < actual.Count ? actual[first] : "end";
                    _fails.Add($"{k.What}: port visits {actual.Count}, DOS {expected.Count}; first difference at visit {first}, port {a}, DOS {e} ({k.Cite})");
                }
            }
        }
        finally { Rng.Source = saved; }
        c.Note($"player at ({_cx},{_cy}); " + string.Join("; ", _notes));
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_fails.Count == 0) return null;
        return "area visits differ from process_area: " + string.Join("; ", _fails);
    }
}
