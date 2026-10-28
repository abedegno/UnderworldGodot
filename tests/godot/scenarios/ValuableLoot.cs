using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// A critter's valuable loot (generate_treasure), drawn through the Rng seam and with the original's 8-bit
/// arithmetic.
///
/// The original (uw1/src/obj/TREASURE.C:93-134; uw2/src/obj/TREASURE.C:89-129): with chance treasure_rate in 16
/// (rand() % 16 >= rate gives nothing) a type is rolled, rand() % (40 - 3l) - (33 - 3l) in UW1, l the dungeon level,
/// and rand() % (37 - 3w) - (30 - 3w) in UW2, w = (level - 1) / 8, the world; below 0 it becomes 0, and in UW2 type 1
/// too. The item is 0xA0 + type and its value (ComObjData, 0 made 1) is stretched in a char: from 12 up value * 8 - 68,
/// from 8 value * 4 - 20, from 4 value * 2 - 4. When four times treasure_prob is below the value, one item comes at
/// the chance rand() % value below 4 * prob; otherwise the quantity is rollem(4, ((4 * prob) / value) << 1) >> 2, and
/// nothing comes below 1. From 25 up the stretched char goes negative, so the chance test is skipped and rollem, given
/// no sides, returns 4: such an item comes singly (Exhumed uw1/docs/FINDINGS.md, "Valuable gems skip the treasure
/// chance and always come singly"; rollem: uw1/src/sys/UTIL.C:72-80).
///
/// The scenario makes a critter of each kind whose treasure_rate is not 0 and, for 400 seeds each on two levels,
/// installs a BorlandRandSource with the seed, calls npc.ValuableObjectLoot and compares the item and quantity with
/// the rule above worked on a second BorlandRandSource with the same seed. It also counts how often the overflowing
/// types came and checks they always came singly.
/// </summary>
public sealed class ValuableLoot : Scenario
{
    const int Seeds = 400;

    public override string Name => "valuable-loot";
    public override string Cites => "underworld-exhumed uw1/src/obj/TREASURE.C:93-134, uw1/src/sys/UTIL.C:72-80; uw2/src/obj/TREASURE.C:89-129";

    string _problem;
    int _runs, _drops, _mismatches, _overflowDrops, _overflowNotSingle;
    readonly List<string> _firstMismatches = new();
    readonly List<int> _kinds = new();

    public override Task Setup(ScenarioContext c)
    {
        for (int id = 0x40; id < 0x7F; id++)
            if (critterObjectDat.valuable_loot_probability(id) > 0) _kinds.Add(id);
        if (_kinds.Count == 0) _problem = "no critter kind with a treasure rate";
        return Task.CompletedTask;
    }

    // the original's generate_treasure, on its own Turbo C++ rand()
    static (int item, int qty) Original(uint seed, int prob, int rate, int level, bool uw2)
    {
        var r = new BorlandRandSource(seed);
        if (r.Rand() % 16 >= rate) return (-1, 0);
        sbyte type;
        if (uw2)
        {
            int w = (level - 1) / 8 * 3;
            type = (sbyte)(r.Rand() % (37 - w) - (30 - w));
        }
        else
            type = (sbyte)(r.Rand() % (40 - level * 3) - (33 - level * 3));
        if (type < 0) type = 0;
        if (uw2 && type == 1) type = 0;
        sbyte value = (sbyte)(byte)commonObjDat.monetaryvalue(0xA0 + type);
        if (value == 0) value = 1;
        if (value >= 12) value = (sbyte)(value * 8 - 68);
        else if (value >= 8) value = (sbyte)(value * 4 - 20);
        else if (value >= 4) value = (sbyte)(value * 2 - 4);
        sbyte qty;
        if ((prob << 2) < value)
        {
            if (r.Rand() % value >= (prob << 2)) return (-1, 0);
            qty = 1;
        }
        else
        {
            sbyte dice = (sbyte)(((prob << 2) / value) << 1);
            int total = 4;
            if (dice > 0)
                for (int i = 0; i < 4; i++) total += (int)((long)r.Rand() * dice / 0x8000);
            qty = (sbyte)(total >> 2);
        }
        if (qty < 1) return (-1, 0);
        return (0xA0 + type, qty);
    }

    static bool Overflows(int item)
    {
        int v = (byte)commonObjDat.monetaryvalue(item);
        return v >= 12 && (sbyte)(v * 8 - 68) < 0;
    }

    public override async Task Act(ScenarioContext c)
    {
        if (_problem != null) return;
        bool uw2 = c.Game == "uw2";
        var keepSource = Rng.Source;
        int keepLevel = playerdat.dungeon_level;
        var critter = BatchA.SpawnCritter(_kinds[0], 1, 0);
        if (critter == null) { _problem = "no critter could be made"; return; }
        int keepId = critter.item_id; short keepLink = critter.link;
        int[] levels = uw2 ? new[] { 1, 17 } : new[] { 1, 6 };
        var map = UWTileMap.current_tilemap;
        try
        {
            foreach (int level in levels)
                foreach (int kind in _kinds)
                    for (uint seed = 1; seed <= Seeds; seed++)
                    {
                        playerdat.dungeon_level = level;
                        critter.item_id = kind;
                        critter.link = 0;
                        Rng.Source = new BorlandRandSource(seed);
                        npc.ValuableObjectLoot(critter);
                        int item = -1, qty = 0;
                        if (critter.link != 0)
                        {
                            var o = map.LevelObjects[critter.link];
                            item = o.item_id; qty = o.link;
                            ObjectFreeLists.ReleaseFreeObject(o);
                        }
                        critter.link = 0;
                        var want = Original(seed, critterObjectDat.valuable_multipleprobability(kind), critterObjectDat.valuable_loot_probability(kind), level, uw2);
                        _runs++;
                        if (item >= 0) _drops++;
                        if (item >= 0 && Overflows(item)) { _overflowDrops++; if (qty != 1) _overflowNotSingle++; }
                        if (item != want.item || qty != want.qty)
                        {
                            _mismatches++;
                            if (_firstMismatches.Count < 4)
                                _firstMismatches.Add($"critter 0x{kind:X} level {level} seed {seed}: got {(item < 0 ? "nothing" : $"{qty} of 0x{item:X}")}, expected {(want.item < 0 ? "nothing" : $"{want.qty} of 0x{want.item:X}")}");
                        }
                    }
        }
        finally
        {
            Rng.Source = keepSource;
            playerdat.dungeon_level = keepLevel;
            critter.item_id = keepId;
            critter.link = keepLink;
        }
        c.Note($"{_kinds.Count} critter kinds, {_runs} rolls, {_drops} drops, {_overflowDrops} of an overflowing type (0xA0 + type with value from 25 up), {_mismatches} differing from the original");
        await c.Frames(1);
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        var k = new Checks();
        string cite = c.Game == "uw1" ? "generate_treasure: uw1 TREASURE.C:93-134" : "generate_treasure: uw2 TREASURE.C:89-129";
        k.True(_mismatches == 0, $"{_mismatches} of {_runs} seeded loot rolls differ from the original's ({string.Join("; ", _firstMismatches)}) ({cite})");
        k.True(_overflowDrops > 0, $"no roll gave a type whose stretched value overflows a char ({cite})");
        k.True(_overflowNotSingle == 0, $"{_overflowNotSingle} of {_overflowDrops} drops of an overflowing type came more than one (rollem(4, 0) >> 2 is 1: uw1 UTIL.C:72-80)");
        return k.Result;
    }
}
