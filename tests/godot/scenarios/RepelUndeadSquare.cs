using System.Collections.Generic;
using System.Threading.Tasks;

namespace Underworld.Scenarios;

/// <summary>
/// Repel Undead's handler for one square or object (UW2). sp_repel_undead returns 1 at once for
/// any caster but the player (uw2/src/combat/SPELLS.C:555-556). For the player on a square with no
/// object (target 0) it calls rand() % 3 and, when that is 0, puts effect 7 for 4 frames on the
/// square at height caster z + 20 (put_effect with no object: fine x and y 3 from CreateObj), and
/// returns 0 (SPELLS.C:557-560; uw2/src/obj/EFFECT.C:349-372; uw2/src/map/MAPADDR.C:29-48).
///
/// The port's RepelUndead returned false for a square with nothing drawn or put, and ran its
/// object branch for any caster (src/magic/spellcasting_class_6.cs).
///
/// The scenario calls SpellCasting.RepelUndead nine times for the player (index 1) on the player's
/// own square with no target, replaying the draws on a copy of the port's BorlandRandSource, and
/// counts the effects (item 0x1C7, 0x1C0 + 7) added to the square and their height. Then it calls
/// it once with a caster index other than the player's and checks it returns true and draws nothing.
/// </summary>
public sealed class RepelUndeadSquare : Scenario
{
    const int Calls = 9;
    const int Effect = 448 + 7;

    public override string Name => "repel-undead-square";
    public override string[] Games => new[] { "uw2" };
    public override string Cites => "underworld-exhumed uw2/src/combat/SPELLS.C:547-560; uw2/src/obj/EFFECT.C:349-372";
    public override uint Seed => 37;

    readonly List<string> _fails = new();
    int _expectedEffects, _effects;
    string _problem;

    public override Task Setup(ScenarioContext c) => Task.CompletedTask;

    static List<int> EffectsIn(int x, int y)
    {
        var found = new List<int>();
        var objs = UWTileMap.current_tilemap.LevelObjects;
        int i = UWTileMap.current_tilemap.Tiles[x, y].indexObjectList;
        for (int guard = 0; i != 0 && guard < 1024; guard++)
        {
            if (objs[i].item_id == Effect) found.Add(i);
            i = objs[i].next;
        }
        return found;
    }

    public override Task Act(ScenarioContext c)
    {
        if (Rng.Source is not BorlandRandSource src) { _problem = "Rng.Source is not a BorlandRandSource"; return Task.CompletedTask; }
        var p = playerdat.playerObject;
        int x = p.tileX, y = p.tileY;
        var tile = UWTileMap.current_tilemap.Tiles[x, y];
        var puts = new bool[Calls];
        var replay = new BorlandRandSource(src.Seed);
        for (int n = 0; n < Calls; n++)
        {
            puts[n] = replay.Rand() % 3 == 0;   // SPELLS.C:558
            if (puts[n]) _expectedEffects++;
        }
        var copy = new BorlandRandSource(src.Seed);
        for (int n = 0; n < Calls; n++)
        {
            bool put = puts[n];
            copy.Rand();
            var before = EffectsIn(x, y);
            bool ret = SpellCasting.RepelUndead(x, y, null, tile, 1);
            var after = EffectsIn(x, y);
            if (ret) _fails.Add($"call {n + 1}: returned true, expected false (SPELLS.C:560)");
            if (after.Count != before.Count + (put ? 1 : 0))
                _fails.Add($"call {n + 1}: {after.Count - before.Count} effects added, expected {(put ? 1 : 0)} (SPELLS.C:558-559)");
            foreach (int i in after)
            {
                if (before.Contains(i)) continue;
                _effects++;
                var e = UWTileMap.current_tilemap.LevelObjects[i];
                if (e.zpos != p.zpos + 20 || e.xpos != 3 || e.ypos != 3)
                    _fails.Add($"call {n + 1}: effect at ({e.xpos}, {e.ypos}, {e.zpos}), expected (3, 3, {p.zpos + 20}) (EFFECT.C:362-363, MAPADDR.C:45-46)");
            }
            if (src.Seed != copy.Seed) { _fails.Add($"call {n + 1}: the port's draws differ from rand() % 3 once per call (SPELLS.C:558)"); break; }
        }
        c.Note($"{_effects} effects put in {Calls} calls, the original's draws give {_expectedEffects}");

        uint seed = src.Seed;
        bool other = SpellCasting.RepelUndead(x, y, null, tile, 2);
        if (!other) _fails.Add("a caster other than the player: returned false, expected true (SPELLS.C:555-556)");
        if (src.Seed != seed) _fails.Add("a caster other than the player: rand() was called (SPELLS.C:555-556)");
        return Task.CompletedTask;
    }

    public override string Assert(ScenarioContext c)
    {
        if (_problem != null) return "precondition: " + _problem;
        if (_expectedEffects == 0) return "precondition: the seed gives no effect in nine draws";
        return _fails.Count == 0 ? null : string.Join("; ", _fails);
    }
}
